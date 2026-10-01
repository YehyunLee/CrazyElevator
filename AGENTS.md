# Crazy Elevator agent rules

The playable game lives in `Assets/Scenes/Main.unity`. Use that one scene for both solo and 1v1. `CottonCandyWorld.unity` is an art preview, not a second game flow. Keep the tech demo playable after each change.

## Ownership

- Follow Liam's manager diagram, documented in `Assets/Scripts/README.md`. `GameManager` owns the round/session, `MenuManager` owns menus, `InputManager` translates controls, `SfxManager` and `MusicManager` own audio. Each elevator owns its `ElevatorManager` and `PassengerManager`; `ElevatorMatch` coordinates the second elevator.
- Put each global manager on its own named GameObject in Main and attach its script in the Inspector. Put `ElevatorManager` and `PassengerManager` on the player elevator controller. The NPC may be instantiated for 1v1, but its manager components should remain easy to find and inspect. Do not hide required global managers behind runtime `new GameObject` or `AddComponent` calls.
- Keep responsibilities clear, public references serialized, and code readable to a beginner. Prefer small methods and concise comments explaining why. Avoid god classes, but do not create a new script for every tiny helper; extend the existing responsible component first.
- Gameplay and art must be separable. Build cabin, shaft, passengers, and world scenery from editable GameObjects, prefabs, meshes, materials, or ProBuilder objects. Scripts may move or animate them; do not generate detailed 3D geometry in gameplay code. This lets artists replace the visuals without rewriting rules. Existing editable geometry lives in `Assets/Static/ElevatorPersonas/Prefabs/`.
- World order is office (floors 0–3), candy (4–7), underwater (8–11). Keep the starting world friendly to new players; avoid changing the three-minute delivery-score win condition without team agreement.
- Keep HUD and help text short and readable over gameplay. Prefer a focused feedback cue to another permanent box or panel. Do not trade away usable cabin space for decoration.
- Use one folder level below a feature folder when possible. Keep Unity `.meta` files and GUID references intact when moving assets.

## Before finishing

- Check solo and 1v1, world order (office, candy, underwater), boarding, kicking, travel, timer, and audio after relevant changes. Verify compilation and inspect the Game view when editing layout.
- Preserve teammates' local edits. Do not check in or push unless the user asks.
