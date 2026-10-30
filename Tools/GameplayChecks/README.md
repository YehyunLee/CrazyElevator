# Gameplay regression checks

Run the actual `ElevatorRound.cs`, `PassengerData.cs`, and `SessionTypes.cs` with .NET 8, without Unity or package downloads:

```powershell
dotnet run --project Tools/GameplayChecks/GameplayChecks.csproj
```

The harness checks matching queue identities and per-door counts at every floor and stop, front-first groups with alternating side entrances, stable captured door/slot assignments, shared boarding exclusivity, independently owned cabin patience and delivery scores, finite shared passenger counts, the two-Grandmas-and-one-courier preview limit, and no hidden backfill after boarding, rejection, timeout, progression, or revisits. It also checks capacity, the one-minute deadline, the office/candy/underwater order, default and catalog rosters confined to the active dimension, reachable passenger destinations, immediate availability of that world's passenger mechanics, rejection of arrivals outside the active world, and fresh shared rosters and clocks across six rounds. Match-state checks cover first-to-three wins, ties awarding no wins, repeated result calls awarding once, dimensions cycling back to office, championship locking, either seat winning, and Play Again resetting the series. `UnityDataStubs.cs` substitutes only Unity's asset/Inspector types and integer math; no gameplay rule is reimplemented. It does not verify rendering, cameras, lobby navigation, audio, or device input.

The small GameplayChecks project is included in version control; Unity-generated projects stay ignored.

Floor-number checks cover 1–8 in each of the three worlds. Destination checks sample every passenger kind at every origin floor, including special random destinations and default/catalog rosters, to verify requests stay inside the origin world and skip the origin floor.

For the round flow in Main, use **Tools > Crazy Elevator > Check Single-Dimension Match** (batch entry point: `ExtendedInteriorChecks.RequestSingleDimensionMatch`). It checks both human seats, fresh shared rosters and 60-second clocks, Office/Candy/Underwater rotation, countdown and pause, travel and teleport bounds, tied rounds, first-to-three results, and rematches. It writes `Temp/ExtendedInterior/result.txt` and does not save scene changes. This check requires a working Unity runtime.

Compile all runtime and editor C# sources against the installed Unity assemblies, independently of the Editor license:

```powershell
./Tools/GameplayChecks/CompileUnity.ps1
```

This reads Unity-generated project references, uses the installed .NET SDK, and writes assemblies and response files under `Temp/GameplayCompile`. It does not regenerate project files or modify the scene. The local imported Unity/package assemblies must exist. Existing warnings remain visible.

In the Unity Editor, **Tools > Crazy Elevator > Check Unified Cabin and Local Lobby** first checks the saved Main hierarchy before entering Play Mode: `Gameplay elevator` owns the cabin, the cabin owns its camera, and the three legacy shell roots are inactive. It also checks that automatic framing and camera shake default to off. In Play Mode, the check edits the cabin camera's local pose, field of view, far clipping plane, and culling mask, then verifies that runtime updates, camera-motion helpers, and both split-screen seat setups preserve those edits. It then runs the physical cabin/camera, track clearance, shared passenger model, unchanged destination, and two-human ready-flow regressions. It can also be requested with `-executeMethod ExtendedInteriorChecks.RequestUnifiedCabin` in batch mode. Results are written to `Temp/ExtendedInterior/result.txt`. A working Unity Editor license/runtime is required; runtime checks could not execute in the restricted host, where Editor startup aborts before scene load.

**Tools > Crazy Elevator > Check Camera Smoothing** repeats the saved-hierarchy and manual-camera preservation checks, then temporarily enables automatic framing to check floor and doorway visibility at square, wide, and split-screen aspect ratios. It also checks stable framing while a crowded landing boards, smooth camera motion at 30/120 fps, pause behavior, reversible inside/travel handoffs, and viewport bounds within a split-screen seat. The tests restore the camera properties and framing settings afterward. The batch entry point is `ExtendedInteriorChecks.RequestCameraSmoothing`, with results in `Temp/ExtendedInterior/result.txt`. These editor checks require a working Unity runtime; local C# compilation verifies their source here, while live camera rendering remains unavailable in the restricted host.

For manual camera adjustments, leave **Auto Frame Cabin** and **Cabin Camera Shake** off on `ElevatorScene`, then edit the cabin Camera component's Transform and Lens settings directly. Viewport placement and aspect ratio still follow the selected solo or split-screen layout.
