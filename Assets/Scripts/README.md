# Crazy Elevator scripts

One Main scene. Liam's singleton managers own the public design.
`ElevatorManager` + `PassengerManager` sit on each elevator (×2 in 1v1).

On the `single-dimension` branch, each round lasts **60 seconds** in one world: **office -> candy -> underwater -> office**, repeating until someone wins three rounds. Higher delivery score wins the round; ties award no win and the next round still changes worlds. `GameManager` owns the round number and match wins. Both elevators reset their score, passengers, position, and clock together. Results offer **Next Round**, or **Play Again** after a match winner. Solo practice uses the same one-minute world rotation.

Every world displays floors **1–8**, including the HUD, cabin indicator, world signs, passenger destinations, and teleport gates. `ElevatorRound.FloorNumber` converts the authored floor indices (0–23) for display; movement and delivery still use those original indices. Passenger destination generators stay inside the passenger's origin world and never request the origin floor.

Before every round, including Office and rematches, a world preview introduces its characters and mechanics using the existing 3D models. Solo waits for confirmation; in a match every human player must be ready, while CPU seats never block. The briefing holds the timer, passenger patience, boarding, and countdown. Once everyone is ready, the shared countdown runs before the round starts. Tutorial practice hands off to this same briefing for the first real round.

After the basic steps, each human practices a real collision in the shaft: hold up and boost into the staged rival. Contact and knockback complete the step; a timer or confirmation button cannot skip it. Only the practicing driver controls their car during that turn. Short prompts and the animated controller show the action; the Bouncer's character introduction explains its stronger hit. Both cars recoil, and passenger count adds no collision weight or stability. Kick practice highlights a real onboard rider, supplies one if automatic drop-off emptied the cabin, and requires an actual kick before advancing. All staging and passenger replenishment are tutorial-only; the real round starts with fresh cars and passengers.

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

Three prototype passengers interact with the shaft automatically while aboard. The office Hunter repeatedly stuns nearby monsters for three seconds without a cooldown. The underwater Diver blocks every tornado hit. The candy Bouncer takes two spaces and increases the other elevator's vertical collision rebound on every qualifying collision. Their feedback appears in the shaft view. Each uses its own editable prefab under `Assets/Static/ElevatorPersonas/Prefabs/`, combining an art-team model with role-specific accessories. A PassengerData prefab can override the scene's assigned art. The default roster introduces these types at selected floors; a custom catalog must include them explicitly.

The first time a passenger ability interacts with the world or another passenger, a short cinematic uses the passenger and hazard/elevator prefabs to show what happened. Hunter plays when stunning a monster, Diver when blocking a tornado, Grandma when slowing the elevator, the Trio when its candy trail slows the rival, Grandma and Baby when Grandma calms the Baby, and Handyman when repairing rust. Bouncer plays on the shared screen when its collision hits the other elevator. Ordinary boarding and the Baby's patience drain do not trigger a cutaway. Each introduction plays once per human player's pane (or once on the shared screen for Bouncer). Both elevators, CPU decisions, and the round clock pause during each introduction. A rematch resets the introductions. The shared preview renders at 1920x1080.

Halfway through each live round, one passenger in the shared landing queue becomes VIP. A shared cinematic cuts to the real shaft, zooms to the VIP at their actual floor and stop, and circles them while shading the rest of the scene. Both elevators and the round clock pause; both seats see the pickup floor, side, destination, and +250 on-time bonus. Only one elevator can board that rider, and the CPU considers the VIP when choosing a stop. A nearby rival indicator shows direction and floor, and close passenger claims give both seats feedback. The Trio automatically leaves short-lived candy in the shaft while travelling; the other car is slowed if it hits the marked trail. At 40 seconds remaining one existing shared monster or tornado surges for eight seconds, with a brief announcement in each visible pane. These match interactions are disabled during tutorial practice and reset each round.

Each live world has a shared moving shaft event. Beginning after 18 seconds, the next event waits for an elevator to travel and appears ahead of its route; later events are spaced at least 22 seconds apart. Office sends a security scan that briefly restricts speed, Candy sends syrup that sticks to and slows the car for several seconds, and Underwater sends a current that pushes cars along a floor rail and adds rust exposure. The first warning lasts three seconds; later warnings last two. Both cars can be hit independently. A Diver blocks the current, and an onboard Handyman prevents its added rust. The colored models are editable prefabs under `Assets/Static/ShaftHazards/Prefabs/`; warnings and reactions stay in the shaft view and tutorial practice excludes the events.

The Bouncer's stronger collision can knock a docked elevator back into travel after forcing its doors closed. Its stronger recoil briefly preserves the impulse instead of immediately clamping it to normal travel speed; hits on floor rails push the other elevator sideways. World bounds still limit displacement.

Passenger destinations are independent of the elevator's direction. Regular riders favor trips of 1–3 floors, with occasional 4–6-floor detours in either direction; interview riders stay within three floors to suit their shorter patience. Bosses and handymen can request any other available floor. Requests stay inside their origin world. All of that world's passenger types are available from the start, with up to three parties per landing queue. Movement, side tracks, collisions, and teleport gates stay inside the same eight-floor world.

At a stop, A/W/D immediately opens the left/front/right door (or use left/up/right on the stick or D-pad). Open any combination of doors; opening another leaves the first one open. Direction hints appear by all three doors, and each open door frame turns teal. Clicking a physical doorway also opens it directly. Passengers line up directly outside a doorway and board through the nearest entrance. A passenger at an outside front-side corner can use either adjoining door, once in total. Boarding follows an open doorway while capacity and adjacent cabin tiles are available; the player does not select or place waiting passengers. C/controller A closes all doors and starts travel, or stops at a floor while moving. Player 2 uses the arrow keys to open doors, period to stop or close, comma to kick, semicolon to repair, and right Shift to boost. Drop-offs stay automatic; down selects an onboard passenger for the I/controller B kick action.

The real three-door cabin is mounted in the saved scene at `ElevatorScene > Exterior office building > Gameplay elevator > Elevator cutaway`. Its `Cabin camera` and lights are children of that cabin in Edit Mode and Play Mode. The legacy exterior shell is inactive. Gameplay moves the complete elevator without rebuilding its art or camera hierarchy. Waiting queues use the cabin's authored mount, rotation, and scale. Opening a door looks onto the real world landing. Each waiting rider has one world-anchored model and destination number, reused when either elevator boards that rider. Both views use the same revealed group, so a landing showing two Grandmas and one courier can supply at most those two Grandmas and one courier across both elevators.

Visible groups begin with a front-door passenger, then alternate which side joins first across landings; groups of three use all entrances. Passenger counts and unlock times are unchanged. Centre-stop front queues align with the actual shaft fronts, rather than the gap between cars, and retain their lane when another passenger boards. The office wings in `OfficeBand.prefab` meet behind the central shafts, so the cabin looks into the real office instead of a separate miniature lobby.

Select `Cabin camera` to edit its Transform, field of view, clipping planes, and Culling Mask directly. On the `ElevatorScene` component, **Auto Frame Cabin** defaults to off, so gameplay preserves manual camera edits in solo and split screen. Enable it only when automatic floor/doorway fitting is wanted; **Cabin Camera Shake** optionally adds motion in that mode. The `Elevator tracks` layer is excluded from the cabin camera. Its far clip is 30 to include the office beyond the doors; the current landing floor covers the floors beneath it. The exterior camera still shows the tracks and building. Make permanent adjustments with Play Mode stopped and save Main. Play Mode edits are available for preview and follow Unity's normal temporary-edit behavior.

In the local 1v1 lobby, ready Player 1 with C and Player 2 with period, or click their READY buttons. The launch begins automatically when all human players are ready; CPU seats are ready automatically. A single keyboard supports both players; two gamepads and mixed keyboard/gamepad seats are also supported. The global GameManager remains bound to seat 0, while each elevator retains its own passenger manager and score. Either player's controller can pause and resume the match.
