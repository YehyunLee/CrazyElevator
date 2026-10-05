# Extended elevator interior

Open `Assets/Scenes/Main.unity` and press Play. This scene supports both single-player and 1v1. Gameplay scripts are in `Assets/Scripts/Gameplay`, and the editable cabin/persona prefabs are in this folder.

World order outside the cabin: **0–3 office, 4–7 candy, 8–11 underwater**. Tune `Candy Starts At Floor` / `Underwater Starts At Floor` on the player elevator. The cabin host stays friendly in office + candy (soft glove only in candy), and goes rusty underwater.

| Action | Controller (button position) | Keyboard |
| --- | --- | --- |
| Start / continue menus | Bottom face button or Start | Enter |
| Highlight a passenger inside | Left stick or D-pad | Hover the mouse, or WASD / arrow keys |
| Board / unload the highlighted passenger | Bottom face button | E or Space |
| Close doors and begin travel | Top face button | C |
| Set travel direction outside | Stick / D-pad up or down | W / S or up/down arrows |
| Build speed in that direction | Hold left shoulder (L / LB) + up/down | Hold Shift + W / S or up/down arrows |
| Stop at a nearby floor | Bottom face button | E or Space |
| Hold door for rider bonus | Hold right face button | Hold H |
| Repair underwater mechanism | Hold RB for 2 seconds | Hold R for 2 seconds |
| Pause / resume | Start | Escape |

Inside, hovering highlights one passenger in gold. Moving the stick changes the highlighted passenger by screen direction; release between selections. Confirm moves that exact passenger in or out. Mouse buttons do nothing: there is no dragging, clicking to board, or clicking to dismiss a passenger. Passengers are placed in available cabin spaces automatically.

Close the doors with C / the top face button. You see them close from inside; once shut, the view switches to the building and the car begins moving. Up/down sets a direction, and releasing the stick keeps the car travelling. Confirm while the nearby floor marker is green to dock there. Between floors, stop input is ignored with a reminder. The car remains outside while docking and opening the doors, then returns to the interior. Shaft limits hold the car with doors closed until you confirm or reverse. Departure waits for any passenger hand animation to finish.

Controller labels use button positions, since Switch and Xbox letter layouts differ. Bottom means B on Switch / A on Xbox; top means X on Switch / Y on Xbox.

Boost adds acceleration continuously while held. Release it to coast at the accumulated speed; push the opposite direction to brake and then reverse. Holding boost makes this quicker. Horizontal or neutral stick input does not add acceleration. Docking clears momentum. Candy starts at 0.85 floors/second, accelerates at 1.1 floors/second squared, and caps at 2.4 floors/second. These values are editable on the scene's game component.

The three starfish above the doors and above the outside car show impairment: colored stars count the current severity; grey stars are inactive. Water begins at one starfish, reaches two after 10 seconds of exposure and three after 20 seconds. At one starfish, cruise speed and speed cap are 58% of candy, and acceleration is 45%; at three, these fall to about 32% and 23%. Candy has no movement penalty and reduces stored exposure over time.

Casey, the **HANDYMAN / FIX** passenger, wears a yellow hard hat and carries tools. Boarding this passenger clears both starfish meters and restores the exact candy cruise speed, acceleration and speed cap for as long as a handyman remains aboard. Rust stops accumulating during that time. Unloading the last handyman restores any remaining underwater impairment. Waiting handymen do not repair the elevator.

Office keeps a friendly face without a helping glove. The candy glove gently carries passengers along a curved path, with a smile and slow idle movement. Underwater uses a rusted mechanical hand with a brief wind-up and a fast push, plus a scowling face and occasional cough/shudder. Holding R / right shoulder for two seconds reduces rust to one starfish and suppresses rust growth and coughing for 20 seconds. A handyman aboard also suppresses coughing and restores full performance. Neither changes the underwater personality or the original scoring rules.

The building camera follows the car during travel. The supplied image is preserved in `Reference/CottonAndWater.png`; the two interior backdrop materials select its candy and water panels using UV rectangles. The backdrop is flat, while passengers, hands, and cabin remain 3D. Each world wrap (`Office world wrap`, candy, underwater) paints the **outside hall**: front through the doorway plus left/right building flanks in the lobby (not the cabin interior walls). Office uses `Reference/OfficeHall.png`; candy and water crop panels from `Reference/CottonAndWater.png` with a light mood tint.

Edit the hand meshes and face in `Prefabs/ElevatorPersonaRig.prefab`. Runtime scripts live in `Assets/Scripts/Gameplay`, and `Assets/Scripts/ElevatorPersonaRig.cs`. The Main scene uses `Prefabs/ExtendedElevatorScene.prefab` and `Prefabs/ButtonlessCabin.prefab`.

### Illustrated 3D passenger prefabs

The active passenger art is exported as normal editable prefabs in `Assets/Prefabs/Resources/`, with the Atlantis handyman in `Prefabs/Passenger-HANDYMAN.prefab`. The office workers, candy guests, granny, clown trio, Atlantis variants, dolphin, and clean round/square bases are reproducible from `Assets/Scripts/Editor/PassengerPrefabBuilder.cs`.

Use **Tools > Crazy Elevator > Rebuild Illustrated Passenger Prefabs** after changing the builder. It preserves the gameplay kinds, colliders, destination/speech references, and handyman tool names, then rewires `ExtendedElevatorScene.prefab`. Use **Open Illustrated Passenger Preview** to inspect the exported lineup together; the showcase and extra concept prefabs live in `Prefabs/PassengerConcepts/`.

The passengers and opaque cabin/world surfaces use the shared **Storybook Surface** shader: three broad lighting bands, no specular reflections, and a subtle printed-paper pattern on the set. Bloom is disabled so bright colours stay crisp instead of looking plastic. Use **Tools > Crazy Elevator > Apply Matte Storybook Style** to apply the same treatment to newly added Lit materials; existing authored storybook patterns are preserved.

### Typography

Menus, HUD text, 1v1 overlays, elevator signs, passenger destination badges, speech bubbles, and patience labels use **Lemon Milk**. The Regular and Bold font files live in `Assets/Resources/Fonts/` and are applied through `Assets/Scripts/Shared/GameTypography.cs`, including labels created at runtime or rebuilt by the passenger prefab tool. Keep `LemonMilk-LICENSE.txt` with the font assets when sharing or packaging the project.

To rerun integration checks, open `Assets/Scenes/Main.unity` and select **Tools > Crazy Elevator > Check Extended Interior (Play Mode)**. This starts Play Mode and tests hover/highlighting, inert mouse clicks/drags, stick selection, selected-passenger confirmation, safe departure, continuous travel, manual stops, camera transitions, shaft limits, world transitions, repair, scoring, cumulative boost, braking/reversal, frame-rate independence, underwater performance, both starfish meters and handyman boarding/unloading. Results are written to `Temp/ExtendedInterior/result.txt`. It does not save or modify any scene.
