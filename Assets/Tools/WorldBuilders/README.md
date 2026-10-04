# World builders

Gameplay uses **exported prefabs only**. These Editor scripts rebuild those prefabs
when art needs another pass. They never run in Play Mode.

## Quick rebuild (Unity open, Play Mode stopped)

| Goal | Menu |
| --- | --- |
| Rebuild doors-open lobby (office / candy / water) | **Crazy Elevator > World / Rebuild Lobby View (3D)** |
| Rebuild travel shaft from band prefabs | **Crazy Elevator > World / Rebuild Shaft Map (3D bands)** |
| Only recolor landings / ground | **Crazy Elevator > World / Reapply Exterior Band Materials** |

Optional request file (Unity focused): write one word into `Temp/WorldBandTasks/request.txt`

- `lobby` → rebuild lobby
- `shaft` → rebuild shaft

## What gets written

| Output | Path |
| --- | --- |
| Lobby art on cabin persona | `Assets/Static/ElevatorPersonas/Prefabs/ElevatorPersonaRig.prefab` (`Lobby Worlds`) |
| Standalone lobby copy | `Assets/Static/WorldBands/Prefabs/LobbyView.prefab` |
| Shaft band sources | `OfficeBand.prefab`, `CandyBand.prefab`, `WaterBand.prefab` |
| Travel exterior | `Assets/Prefabs/Resources/ExteriorWorld.prefab` (`Shaft World Bands`) |

## Iteration loop

1. Edit band prefabs / materials under `Assets/Static/WorldBands/`.
2. Run the matching menu above.
3. Press Play in `Main.unity` — `ElevatorPersonaRig` / `ElevatorManager` only enable and tint; they do not build meshes.

## Runtime wiring (no new managers)

- `ElevatorPersonaRig` — serialized `officeBackdrop` / `candyBackdrop` / `waterBackdrop` → lobby roots
- `ElevatorManager` — `WorldBand` from floor; cabin/hall floor tint + travel sky

## Archived Plastic recovery

| File | Notes |
| --- | --- |
| `Archived/CottonCandySceneBuilder.cs.txt` | Plastic `cs:42`. `.txt` so it does not compile. |
