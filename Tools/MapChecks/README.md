# Map data checks

From the Unity project folder, with .NET SDK 8 installed:

```powershell
dotnet run --project Tools/MapChecks/MapChecks.csproj
```

This console harness compiles the actual `Assets/Map/Scripts/MapState.cs` and `MapSections.cs`. It requires no Unity Editor, third-party packages, or package downloads. Build outputs go into Unity's ignored `Temp/MapChecks` folder. These checks cover grouping, boarding/delivery, replacement, movement snapshots, score ownership, validation, notifications, and read-only access. Dedicated 100-floor scenarios check all floors 1-100, reject floor 101, follow a passenger from floor 1 to floor 100, and preserve elevator movement at floor 99.5. Section checks verify Cotton Candy on floors 1-50, Water on floors 51-100, and rejection of floors outside the building.

`DeadlineSeconds` is an absolute time supplied by the gameplay system, measured against the same clock used by the map display. Passenger systems own expiry and scoring. The map holds the latest reported status and points until another snapshot arrives.

Section passenger counts include waiting and riding passengers. Waiting passengers use their current floor; riding passengers stay grouped under their source floor. Delivered and expired passengers are excluded. Checks cover the 50/51 boundary, boarding, trip completion/expiry, replacement without double-counting, and invalid arguments.

To compile all map scripts, including scene tools in `Assets/Map/Editor`, against the project's installed Unity assemblies:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/MapChecks/CompileUnity.ps1
```

This additional check reads reference paths and compiler defines from Unity's generated `Assembly-CSharp.csproj` and reads extra references from `Assembly-CSharp-Editor.csproj` when available. It does not edit either file or Unity settings. Unity must have generated the project files, and the local Editor and imported library assemblies must still exist. The check builds into `Temp/MapChecks/UnityCompile`; it checks C# compatibility, not visual layout or Play mode behavior.
