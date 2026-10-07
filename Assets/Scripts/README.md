# Crazy Elevator scripts

One Main scene. Liam's singleton managers own the public design.
`ElevatorManager` + `PassengerManager` sit on each elevator (×2 in 1v1).

## Scene ownership (required)

Put each global manager on its **own named GameObject** in `Main.unity` and attach the script in the Inspector. Do **not** recreate them with runtime `new GameObject` / `AddComponent`.

| Hierarchy object | Script(s) |
| --- | --- |
| `GameManager` | `GameManager` |
| `InputManager` | `InputManager` |
| `SfxManager` | `SfxManager` |
| `MusicManager` | `MusicManager` |
| `MenuManager` | `MenuManager` |
| `Player Elevator` | `ElevatorManager` + `PassengerManager` (+ `ElevatorMatch`) |

NPC elevator may be spawned for 1v1; keep its managers inspectable.

## Layout

```text
InputManager / SfxManager / MusicManager / MenuManager / GameManager   (named scene objects)
Player Elevator
  ElevatorManager          ← movement, floors, travel, shift
  PassengerManager         ← board / kick / waiting
  ElevatorMatch            ← 1v1 only (+ NpcElevator)
Shared
  ElevatorRound + Rider, ElevatorScene, PassengerData, view widgets
```

## Folders

| Folder | What |
| --- | --- |
| `Managers/Singletons/` | Liam managers. ElevatorManager is split into `.cs` / `.Passengers.cs` / `.UI.cs` / `.Api.cs` only to keep the file readable. |
| `Match/` | `ElevatorMatch.cs`, `NpcElevator.cs` |
| `Shared/` | Rules + prefab components + PassengerData |
| `Editor/` | Optional editor tools |

## Liam mapping

| Box | Script |
| --- | --- |
| GameManager | `GameManager.cs` |
| MenuManager | `MenuManager.cs` |
| InputManager | `InputManager.cs` |
| SfxManager | `SfxManager.cs` |
| MusicManager | `MusicManager.cs` |
| ElevatorManager | `ElevatorManager*.cs` (per elevator, not DDOL) |
| PassengerManager | `PassengerManager.cs` (per elevator) |
| ControlElevatorView / PassengerView | `GameView` via `GameManager.GoToView` |

Create passenger assets: **Crazy Elevator → Create Default Passenger Data**. In a `PassengerData` asset, set **Theme** to Office, Candy, or Underwater to limit where that type waits; **Any** keeps generic types available everywhere. Destinations may still cross between worlds. Clown groups double elevator speed and elderly passengers halve it while aboard; one of each applies, canceling the passenger-speed modifier. In 1v1, boarding a rider adds a same-type replacement to the shared origin-floor queue after a one-second arrival delay.
