# Crazy Elevator scripts

One Main scene. Liam's singleton managers own the public design.
`ElevatorManager` + `PassengerManager` sit on each elevator (×2 in 1v1).

On the `single-dimension` branch, each round lasts **60 seconds** in one world: **office -> candy -> underwater -> office**, repeating until someone wins three rounds. Higher delivery score wins the round; ties award no win and the next round still changes worlds. `GameManager` owns the round number and match wins. Both elevators reset their score, passengers, position, and clock together. Results offer **Next Round**, or **Play Again** after a match winner. Solo practice uses the same one-minute world rotation.

Every world displays floors **1–8**, including the HUD, cabin indicator, world signs, passenger destinations, and teleport gates. `ElevatorRound.FloorNumber` converts the authored floor indices (0–23) for display; movement and delivery still use those original indices. Passenger destination generators stay inside the passenger's origin world and never request the origin floor.

Before every round, including Office and rematches, a world preview introduces its characters and mechanics using the existing 3D models. Solo waits for confirmation; in a match every human player must be ready, while CPU seats never block. The briefing holds the timer, passenger patience, boarding, and countdown. Once everyone is ready, the shared countdown runs before the round starts. Tutorial practice hands off to this same briefing for the first real round.

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

Create passenger assets: **Crazy Elevator → Create Default Passenger Data**. In a `PassengerData` asset, set **Theme** to Office, Candy, or Underwater to limit where that type waits; **Any** keeps generic types available everywhere. Passengers and destinations stay inside the active world for the whole round. Clown groups double elevator speed. By default, the first elderly passenger halves it; each additional elderly passenger applies x0.85, down to a minimum x0.30. Mina & Baby makes every onboard passenger's patience drain x1.6; boarding a grandma with them calms the baby and reduces that drain to x0.5 while both remain onboard. Each landing keeps the finite group first shown in the world. Boarding removes that exact rider from the group. In 1v1, both elevators compete for those same riders; pickups do not create replacements.

Three prototype passengers interact with the shaft automatically while aboard. The office Hunter stuns a nearby monster for three seconds, with a five-second cooldown per elevator. The underwater Diver blocks one tornado hit per passenger. The candy Bouncer takes two spaces and increases the other elevator's vertical collision rebound. Their feedback appears in the shaft view. Prototype art reuses the assigned Interview, Handyman, and Boss passenger prefabs respectively; a PassengerData prefab overrides the reuse. The default roster introduces these types at selected floors; a custom catalog must include them explicitly.

Passenger destinations are independent of the elevator's direction. Regular riders favor trips of 1–3 floors, with occasional 4–6-floor detours in either direction; interview riders stay within three floors to suit their shorter patience. Bosses and handymen can request any other available floor. Requests stay inside their origin world. All of that world's passenger types are available from the start, with up to three parties per landing queue. Movement, side tracks, collisions, and teleport gates stay inside the same eight-floor world.

At a stop, A/W/D immediately opens the left/front/right door (or use left/up/right on the stick or D-pad). Direction hints appear by all three doors, and the open door frame turns teal. Clicking a physical doorway also opens it directly. Passengers line up directly outside a doorway and board through the nearest entrance. A passenger at an outside front-side corner can use either adjoining door, once in total. Boarding follows the chosen doorway while capacity and adjacent cabin tiles are available; the player does not select or place waiting passengers. E/controller Y closes the doors and starts travel. Player 2 uses the arrow keys to open doors, period to stop, slash to close, comma to kick, semicolon to repair, and right Shift to boost. Drop-offs stay automatic; down selects an onboard passenger for the existing kick action.

The real three-door cabin is mounted in the saved scene at `ElevatorScene > Exterior office building > Gameplay elevator > Elevator cutaway`. Its `Cabin camera` and lights are children of that cabin in Edit Mode and Play Mode. The legacy exterior shell is inactive. Gameplay moves the complete elevator without rebuilding its art or camera hierarchy. Waiting queues use the cabin's authored mount, rotation, and scale. Opening a door looks onto the real world landing. Each waiting rider has one world-anchored model and destination number, reused when either elevator boards that rider. Both views use the same revealed group, so a landing showing two Grandmas and one courier can supply at most those two Grandmas and one courier across both elevators.

Visible groups begin with a front-door passenger, then alternate which side joins first across landings; groups of three use all entrances. Passenger counts and unlock times are unchanged. Centre-stop front queues align with the actual shaft fronts, rather than the gap between cars, and retain their lane when another passenger boards. The office wings in `OfficeBand.prefab` meet behind the central shafts, so the cabin looks into the real office instead of a separate miniature lobby.

Select `Cabin camera` to edit its Transform, field of view, clipping planes, and Culling Mask directly. On the `ElevatorScene` component, **Auto Frame Cabin** defaults to off, so gameplay preserves manual camera edits in solo and split screen. Enable it only when automatic floor/doorway fitting is wanted; **Cabin Camera Shake** optionally adds motion in that mode. The `Elevator tracks` layer is excluded from the cabin camera. Its far clip is 30 to include the office beyond the doors; the current landing floor covers the floors beneath it. The exterior camera still shows the tracks and building. Make permanent adjustments with Play Mode stopped and save Main. Play Mode edits are available for preview and follow Unity's normal temporary-edit behavior.

In the local 1v1 lobby, ready Player 1 with C and Player 2 with period, or click their READY buttons, then Start. A single keyboard supports both players; two gamepads and mixed keyboard/gamepad seats are also supported. The global GameManager remains bound to seat 0, while each elevator retains its own passenger manager and score. Either player's controller can pause and resume the match.
