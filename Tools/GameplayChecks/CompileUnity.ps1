[CmdletBinding()]
param([string]$ProjectRoot)

$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path }
$taskDotnet = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
if (-not $taskDotnet) { $taskDotnet = Join-Path $env:ProgramFiles 'dotnet/dotnet.exe' }
if (-not (Test-Path -LiteralPath $taskDotnet)) { throw 'A local .NET SDK is required.' }
$taskSdks = @(& $taskDotnet --list-sdks | ForEach-Object {
    if ($_ -match '^([^\s]+)\s+\[([^\]]+)\]$') {
        [PSCustomObject]@{
            Version = [Version]($Matches[1] -split '-')[0]
            Compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn/bincore/csc.dll'
        }
    }
} | Sort-Object Version -Descending)
if ($taskSdks.Count -eq 0) { throw 'A local .NET SDK is required.' }

[xml]$taskRuntimeProject = Get-Content -LiteralPath (Join-Path $ProjectRoot 'Assembly-CSharp.csproj') -Raw
[xml]$taskEditorProject = Get-Content -LiteralPath (Join-Path $ProjectRoot 'Assembly-CSharp-Editor.csproj') -Raw
$taskReferenceNodes = @($taskRuntimeProject.SelectNodes('//Reference/HintPath'))
$taskReferenceNodes += $taskEditorProject.SelectNodes('//Reference/HintPath')
$taskReferences = @($taskReferenceNodes | ForEach-Object {
    $taskReference = $_.InnerText
    if (-not [IO.Path]::IsPathRooted($taskReference)) { $taskReference = Join-Path $ProjectRoot $taskReference }
    if ((Test-Path -LiteralPath $taskReference) -and [IO.Path]::GetFileName($taskReference) -notlike 'Assembly-CSharp*') {
        (Resolve-Path -LiteralPath $taskReference).Path
    }
})
$taskReferences += Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Library/ScriptAssemblies') -Filter '*.dll' |
    Where-Object Name -NotLike 'Assembly-CSharp*' | Select-Object -ExpandProperty FullName
$taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$taskReferences = @($taskReferences | Where-Object { $taskSeen.Add([IO.Path]::GetFileName($_)) })
if ($taskReferences.Count -eq 0) { throw 'No local Unity reference assemblies were found.' }

$taskOutput = Join-Path $ProjectRoot 'Temp/GameplayCompile'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskRuntimeAssembly = Join-Path $taskOutput 'Assembly-CSharp.dll'
$taskSources = @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets') -Recurse -Filter '*.cs')
foreach ($taskStage in @('Runtime', 'Editor')) {
    $taskEditorStage = $taskStage -eq 'Editor'
    $taskStageSources = @($taskSources | Where-Object { ($_.FullName -match '[\\/]Editor[\\/]') -eq $taskEditorStage })
    if ($taskStageSources.Count -eq 0) { continue }
    $taskAssembly = if ($taskEditorStage) { Join-Path $taskOutput 'Assembly-CSharp-Editor.dll' } else { $taskRuntimeAssembly }
    $taskArguments = @('-nostdlib+', '-target:library', '-langversion:9.0', '-warn:4', '-nowarn:1701', ('-out:"' + $taskAssembly + '"'))
    $taskArguments += '-define:' + $taskRuntimeProject.SelectSingleNode('//DefineConstants').InnerText
    $taskArguments += $taskReferences | ForEach-Object { '-reference:"' + $_ + '"' }
    if ($taskEditorStage) { $taskArguments += '-reference:"' + $taskRuntimeAssembly + '"' }
    $taskArguments += $taskStageSources | ForEach-Object { '"' + $_.FullName + '"' }
    $taskResponse = Join-Path $taskOutput ($taskStage + '.rsp')
    [IO.File]::WriteAllLines($taskResponse, $taskArguments, [Text.UTF8Encoding]::new($false))
    & $taskDotnet $taskSdks[0].Compiler '-noconfig' ('@' + $taskResponse)
    if ($LASTEXITCODE -ne 0) { throw ($taskStage + ' C# compilation failed.') }
    Write-Output ('PASS: ' + $taskStageSources.Count + ' ' + $taskStage.ToLowerInvariant() + ' sources compiled against the installed Unity assemblies.')
}
