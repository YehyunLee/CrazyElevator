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

Create passenger assets: **Crazy Elevator → Create Default Passenger Data**. In a `PassengerData` asset, set **Theme** to Office, Candy, or Underwater to limit where that type waits; **Any** keeps generic types available everywhere. Destinations may still cross between worlds. Clown groups double elevator speed. By default, the first elderly passenger halves it; each additional elderly passenger applies x0.85, down to a minimum x0.30. One clown and one elderly cancel the passenger-speed modifier. Mina & Baby makes every onboard passenger's patience drain x1.6; boarding a grandma with them calms the baby and reduces that drain to x0.5 while both remain onboard. In 1v1, boarding a rider adds a same-type replacement to the shared origin-floor queue after a one-second arrival delay.

Passenger destinations are independent of the elevator's direction. Regular riders favor trips of 1–3 floors, with occasional 4–6-floor detours in either direction; interview riders stay within three floors to suit their shorter patience. Bosses and handymen can request any other available floor. Initial requests stay at or below their origin world's top floor so they are deliverable when that world opens; shared-queue replacements can also request newly unlocked worlds.

Keyboard passenger selection uses WASD or arrow keys. `C` confirms the highlighted passenger and closes the doors only when no passenger is selected; `X` (keyboard X or the controller X/left-face button) picks up a waiting passenger. While holding one, use the stick or directional keys to move between cabin tiles, then press `X` again to place it. Moving back out through the front row and pressing `X` restores the original waiting spot without removing the passenger from the queue; controller Y remains the dedicated close-door button.
