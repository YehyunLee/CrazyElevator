# Cotton Candy World

Open `Assets/Scenes/CottonCandyWorld.unity` and press Play.

The scene translates `Reference/CottonCandyWorld.png` (the supplied artwork) into an editable 3D landscape: lavender sky, vanilla clouds, striped mint hills, cotton-candy trees and a peach cottage. The new winged elevator, numbered platforms, cottage, tree and cloud prefabs live here alongside their materials and landing-ring mesh. Existing source assets and scenes are not modified.

## Flight controls

| Action | Gamepad | Keyboard |
| --- | --- | --- |
| Left / right / up / down | Left stick or D-pad | WASD or arrows |
| Faster movement | Right shoulder | Shift |
| Leave a stop | South button (A / Cross) | E |
| Pause / resume | Start | Space |
| Restart | Select | R |

The elevator moves manually in the XY plane at a fixed depth (Z = 0). Up/down changes its height; left/right changes its horizontal position. Release the controls to hold still. The scenery and stops remain stationary. Stops 1–4 sit at heights 10, 28, 46 and 64, so higher stop indices are always above lower ones. You can visit the stops in either direction.

Move into the center of a golden ring to dock. Press the departure button, then move away to continue; waiting after departure does not dock you again. Movement stays within X = -12 to 12 and Y = 4 to 70. This is a standalone scenic elevator, with no passenger loading or changes to the descent game's state.

The camera follows the elevator's full horizontal and vertical displacement in `LateUpdate`, with smoothing and a fixed starting offset. Its angle stays steady. Adjust `cameraFollowSpeed` on the Cotton Candy World controller (default 5) for a slower or faster response. Position the camera before Play to change the offset.

The 2.5D presentation uses a gentle three-quarter perspective camera (9 degrees down, 12 degrees from the side, 40-degree field of view). Background scenery sits at three depths for natural parallax as the camera follows. Lavender distance fog, cool shaded sides and warm highlights separate the 3D candy shapes. The elevator and docking points still share the same XY movement plane. **Tools > Crazy Elevator > Apply 2.5D Cotton Candy Visuals** updates only the presentation and saves a scene backup first.

## Edit the scene

- Runtime behavior: `Assets/Scripts/CottonCandyFlight.cs`.
- Stop data: `Assets/Scripts/CottonCandyStop.cs`. `stopIndex` uses positive indices 1, 2, 3, 4, etc.
- To add a stop, duplicate a Stop object under Vertical Candy Landscape, assign the next index, edit its sign text and position, and add it to the flight controller's `stops` array. Keep its docking point on Z = 0 and within the controller's horizontal and altitude limits. Place larger stop indices higher in Y; extend the altitude limit when adding another floor.
- Scene generation: `Assets/Scripts/Editor/CottonCandySceneBuilder.cs` and menu **Tools > Crazy Elevator > Build Cotton Candy World**. Rebuilding replaces this generated scene and generated assets; edit the builder to preserve layout changes across rebuilds. Importing the scripts alone never rebuilds the scene.
- The scene is intentionally independent of the existing build list and level transitions. Open it directly for Play testing; add it to a build profile when integrating it into the main game.

The builder renders a preview and checks idle hold, four-way movement, bounds, boost, fixed depth, stationary scenery, smooth camera follow, pause, ascending stop indices, visits to all stops upward and downward, swept docking at high speed, departure and reset. Output is in `Temp/CottonCandyTasks`. **Tools > Crazy Elevator > Apply Vertical Cotton Candy Layout** applies the vertical layout to the existing scene and saves a backup there before changing it.
