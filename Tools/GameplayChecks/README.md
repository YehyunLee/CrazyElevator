# Gameplay regression checks

Run the actual `ElevatorRound.cs` and `PassengerData.cs` with .NET 8, without Unity or package downloads:

```powershell
dotnet run --project Tools/GameplayChecks/GameplayChecks.csproj
```

The harness checks matching queue identities and per-door counts at every floor and stop, shared boarding exclusivity, independently owned cabin patience and delivery scores, finite shared passenger counts, the two-Grandmas-and-one-courier preview limit, and no hidden backfill after boarding, rejection, timeout, progression, or revisits, capacity, the three-minute deadline, and office/candy/underwater progression. `UnityDataStubs.cs` substitutes only Unity's asset/Inspector types and integer math; no gameplay rule is reimplemented. It does not verify rendering, cameras, lobby navigation, audio, or device input.

Compile all runtime and editor C# sources against the installed Unity assemblies, independently of the Editor license:

```powershell
./Tools/GameplayChecks/CompileUnity.ps1
```

This reads Unity-generated project references, uses the installed .NET SDK, and writes assemblies and response files under `Temp/GameplayCompile`. It does not regenerate project files or modify the scene. The local imported Unity/package assemblies must exist. Existing warnings remain visible.

In the Unity Editor, **Tools > Crazy Elevator > Check Unified Cabin and Local Lobby** runs the physical cabin/camera, shared passenger model, unchanged destination, and two-human ready-flow regression against Main. It can also be requested with `-executeMethod ExtendedInteriorChecks.RequestUnifiedCabin` in batch mode. The result is written to `Temp/ExtendedInterior/result.txt`. This requires a working Unity Editor license/runtime; it was added and compiled but could not execute in the restricted host, where Editor startup aborts before scene load.

**Tools > Crazy Elevator > Check Camera Smoothing** checks stable framing while a crowded landing boards, release of the old framing at a new landing, smooth camera motion at 30/120 fps, pause behavior, reversible inside/travel handoffs, and viewport bounds within a split-screen seat. It also boards a visible passenger in Main and checks that the settled cabin framing stays put. The batch entry point is `ExtendedInteriorChecks.RequestCameraSmoothing`, with results in `Temp/ExtendedInterior/result.txt`. These editor checks require a working Unity runtime; local C# compilation verifies their source here, while live camera rendering remains unavailable in the restricted host.