[CmdletBinding()]
param(
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) {
    $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ProjectRoot = (Resolve-Path (Join-Path $scriptDirectory '../..')).Path
}
$projectPath = Join-Path $ProjectRoot 'Assembly-CSharp.csproj'
if (-not (Test-Path -LiteralPath $projectPath)) {
    throw 'Open this Unity project and regenerate its C# project files before running the compile check.'
}

# Reuse the local Editor's actual reference paths without changing its generated project.
[xml]$unityProject = Get-Content -LiteralPath $projectPath -Raw
$referenceNodes = @($unityProject.SelectNodes('//Reference/HintPath'))
$editorProjectPath = Join-Path $ProjectRoot 'Assembly-CSharp-Editor.csproj'
if (Test-Path -LiteralPath $editorProjectPath) {
    [xml]$editorProject = Get-Content -LiteralPath $editorProjectPath -Raw
    $referenceNodes += $editorProject.SelectNodes('//Reference/HintPath')
}
$referencePaths = @($referenceNodes |
    ForEach-Object {
        $referencePath = $_.InnerText
        if (-not [IO.Path]::IsPathRooted($referencePath)) {
            $referencePath = Join-Path $ProjectRoot $referencePath
        }
        $referenceName = [IO.Path]::GetFileName($referencePath)
        if ($referenceName -ne 'Assembly-CSharp.dll' -and $referenceName -ne 'Assembly-CSharp-Editor.dll' -and
            (Test-Path -LiteralPath $referencePath)) {
            (Resolve-Path -LiteralPath $referencePath).Path
        }
    })
# Package project references (for example Input System) resolve to imported assemblies.
$scriptAssembliesDirectory = Join-Path $ProjectRoot 'Library/ScriptAssemblies'
if (Test-Path -LiteralPath $scriptAssembliesDirectory) {
    $referencePaths += Get-ChildItem -LiteralPath $scriptAssembliesDirectory -Filter '*.dll' |
        Where-Object { $_.Name -notlike 'Assembly-CSharp*' } |
        Select-Object -ExpandProperty FullName
}
# Runtime and Editor projects can list different facade paths for the same assembly.
# Prefer the runtime project's first reference for each assembly filename.
$seenReferenceNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$referencePaths = @($referencePaths | Where-Object {
    $seenReferenceNames.Add([IO.Path]::GetFileName($_))
})
if ($referencePaths.Count -eq 0) {
    throw 'The generated C# project has no available Unity reference assemblies.'
}

$sdkCandidates = @(dotnet --list-sdks | ForEach-Object {
    if ($_ -match '^([^\s]+)\s+\[([^\]]+)\]$') {
        [PSCustomObject]@{
            Version = [Version]($Matches[1] -split '-')[0]
            Compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
        }
    }
} | Sort-Object Version -Descending)
if ($sdkCandidates.Count -eq 0) {
    throw 'Install a .NET SDK to run the Unity C# compile check.'
}
$compilerPath = $sdkCandidates[0].Compiler
$sourcePaths = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets/Map/Scripts') -Filter '*.cs' |
    Select-Object -ExpandProperty FullName)
# The prototype's GUI checks whether the detailed map is open.
$prototypeSource = Join-Path $ProjectRoot 'Assets/CrazyElevatorPrototype.cs'
if (Test-Path -LiteralPath $prototypeSource) { $sourcePaths += $prototypeSource }
$editorSourceDirectory = Join-Path $ProjectRoot 'Assets/Map/Editor'
if (Test-Path -LiteralPath $editorSourceDirectory) {
    $sourcePaths += Get-ChildItem -LiteralPath $editorSourceDirectory -Filter '*.cs' |
        Select-Object -ExpandProperty FullName
}
if ($sourcePaths.Count -eq 0) {
    throw 'No map scripts were found.'
}

$outputDirectory = Join-Path $ProjectRoot 'Temp/MapChecks/UnityCompile'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$assemblyPath = Join-Path $outputDirectory 'CrazyElevator.Map.CompileCheck.dll'
$responsePath = Join-Path $outputDirectory 'compile.rsp'
$defineNode = $unityProject.SelectSingleNode('//DefineConstants')
$compilerArguments = @(
    '-nostdlib+'
    '-target:library'
    '-langversion:9.0'
    '-warn:4'
    '-warnaserror+'
    # Unity's imported packages may reference netstandard 2.0 while this project targets 2.1.
    '-nowarn:1701'
    ('-out:"' + $assemblyPath + '"')
)
if ($null -ne $defineNode -and $defineNode.InnerText) {
    $compilerArguments += '-define:' + $defineNode.InnerText
}
$compilerArguments += $referencePaths | ForEach-Object { '-reference:"' + $_ + '"' }
$compilerArguments += $sourcePaths | ForEach-Object { '"' + $_ + '"' }
[IO.File]::WriteAllLines($responsePath, $compilerArguments, [Text.UTF8Encoding]::new($false))

& dotnet $compilerPath '-noconfig' ('@' + $responsePath)
if ($LASTEXITCODE -ne 0) {
    throw 'Map scripts failed compilation against the local Unity assemblies.'
}
Write-Output ('Compiled ' + $sourcePaths.Count + ' map scripts against the local Unity assemblies.')
Write-Output ('Output: ' + $assemblyPath)
