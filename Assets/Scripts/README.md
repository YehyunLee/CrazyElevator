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

Create passenger assets: **Crazy Elevator → Create Default Passenger Data**. In a `PassengerData` asset, set **Theme** to Office, Candy, or Underwater to limit where that type waits; **Any** keeps generic types available everywhere. Destinations may still cross between worlds. Clown groups double elevator speed. By default, the first elderly passenger halves it; each additional elderly passenger applies x0.85, down to a minimum x0.30. One clown and one elderly cancel the passenger-speed modifier. Mina & Baby makes every onboard passenger's patience drain x1.6; boarding a grandma with them calms the baby and reduces that drain to x0.5 while both remain onboard. Each landing keeps the finite group first shown in the world. Boarding removes that exact rider from the group. In 1v1, both elevators compete for those same riders; pickups do not create replacements.

Passenger destinations are independent of the elevator's direction. Regular riders favor trips of 1–3 floors, with occasional 4–6-floor detours in either direction; interview riders stay within three floors to suit their shorter patience. Bosses and handymen can request any other available floor. Requests stay at or below their origin world's top floor so they are deliverable when that world opens. Progression increases passenger variety and group size at landings that have not yet been revealed.

At a stop, choose the left/front/right door with A/W/D (or left/up/right on the stick), then press C/controller A to open it. The selected doorway has a gold frame and an arrow with the open-button hint above it; open doors turn teal. Clicking a physical doorway opens it directly. Passengers line up directly outside a doorway and board through the nearest entrance. A passenger at an outside front-side corner can use either adjoining door, once in total. Boarding follows the chosen doorway while capacity and adjacent cabin tiles are available; the player does not select or place waiting passengers. E/controller Y closes the doors and starts travel. Player 2 uses the arrow keys, period to open/stop, slash to close, comma to kick, semicolon to repair, and right Shift to boost. Drop-offs stay automatic; down selects an onboard passenger for the existing kick action.

The cabin and its camera ride inside the same exterior-car transform used for driving. Opening a door looks onto the real world landing. The exterior camera cuts away the cabin back wall; it renders the same doors and onboard passengers as the cabin camera. Each waiting rider has one world-anchored model and destination number, reused when either elevator boards that rider. Both views use the same revealed group, so a landing showing two Grandmas and one courier can supply at most those two Grandmas and one courier across both elevators.

In the local 1v1 lobby, ready Player 1 with C and Player 2 with period, or click their READY buttons, then Start. A single keyboard supports both players; two gamepads and mixed keyboard/gamepad seats are also supported. The global GameManager remains bound to seat 0, while each elevator retains its own passenger manager and score. Either player's controller can pause and resume the match.
