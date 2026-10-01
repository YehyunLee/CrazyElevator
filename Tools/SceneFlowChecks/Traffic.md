# Elevator traffic

Open `Assets/Zoe scene/Scenes/Zoe.unity` and press Play. The second elevator starts on track 3, three floors ahead. Its **Stop Every Floors** and **Pickup Duration** Inspector settings control the frequency and length of passenger stops; longer intervals leave more room for collision testing. Two passengers wait on each side's landing. At each stop, the NPC unloads one rider (if occupied) before boarding two waiting passengers. Dropped-off riders stay on the landing; collected passengers do not respawn when floor rows wrap or the player bounces backwards.

The camera follows the player across tracks and outward bends while retaining the saved view angle and zoom. An NPC marker displays its track and distance ahead/behind, points at it in view, and moves to the screen edge when it leaves view. On **Main Camera**, tune **Player Camera Follow → Follow Smooth Time** and **Player Screen Position** to adjust tracking.

NPC speed varies by ±10% after boarding or unloading. Player speed varies by ±10% after returning from a passenger visit. Each change is relative to base speed, so repeated stops do not compound the variation.

Select **NPC — Passenger Elevator** in the Hierarchy to tune its starting lane, speed, speed variation, stop interval, and boarding duration in **Npc Elevator Controller**. The lane field uses 0–2 for tracks 1–3. Select **PLAYER — Elevator** to tune **Elevator Collision Slowdown**:

- Hitting an NPC at a passenger stop bounces the player backwards at 6 units/second for 0.6 seconds. Actual descent progress reverses and cannot go above the start. The stopped NPC stays on its track.
- Hitting a moving NPC pushes it onto an adjacent track over 0.4 seconds and reduces player speed to 60% for three seconds. The NPC continues on its new track. The knock chooses an equal-depth or inner track and stays within the three track bounds.
- Continuous contact does not repeatedly trigger collisions. A later collision can apply a new effect. Pause freezes movement and effect timers. The HUD distinguishes bounce and knock outcomes.

Lane changes are allowed only when the source is equally or farther outward than the destination, measured at the player's current height. In this scene, more negative Z is farther from the building. Flat tracks permit switching both ways; the outward section of the S-bend permits leaving for an inner track but blocks entering it from an inner track. The check uses the player's current interpolated track position during a switch.

Floor visits retain NPC distance, lane (including an unfinished knock), speed, boarding progress, passenger count, served-floor history, and remaining collision effects. Restart resets them.

The existing scene has the NPC and references saved. **Crazy Elevator → Traffic → Add or Refresh NPC Elevator** can reconnect the setup if needed; it preserves the camera and records Undo.

## Verification

- `ElevatorTrafficSetup.RunChecks()` checks boarding, depth restrictions, collision separation and swept contact, debuff expiry, and checkpoint restoration in an isolated preview scene. The setup watcher runs it when explicitly requested through `Temp/TrafficChecks/setup.request`.
- Creating `Temp/TrafficChecks/play.request` while Zoe is open and stopped runs live collision, boarding, HUD, pause, lane restrictions, checkpoint, and restart checks. Results: `Temp/TrafficChecks/play-result.txt`.
- **Crazy Elevator → Check Floor Visit Round Trips** checks actual Zoe/AtLevel scene transitions. Results: `Temp/SceneFlowChecks/play-result.json`.
- Creating `Temp/PlayerViewChecks/apply.request` while Zoe is open and stopped saves the follow camera/marker references and checks live lane following, outward-bend framing, off-screen arrows, ahead/behind labels, and marker bounds. Results: `Temp/PlayerViewChecks/result.txt`. The traffic Play fixture temporarily uses four-floor, 2.5-second stops without changing the saved NPC settings.
- See `README.md` for the standalone session tests and full C# compilation command.
