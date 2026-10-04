using CrazyElevator.Shared;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ElevatorMatchType = CrazyElevator.Match.ElevatorMatch;
using MatchController = CrazyElevator.Match.ElevatorMatch;

// --- from CrazyElevatorGame.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    public sealed partial class ElevatorManager : MonoBehaviour
    {
        [Header("Editable scene objects")]
        public ElevatorScene sceneView;
        // The phase controls input, animation, and whether the round clock runs.
        enum Phase { Intro, Welcome, Tutorial, Boarding, Closing, Closed, Moving, Docking, Opening, Results }
        static readonly Color Ink = new Color32(43, 48, 78, 255);
        static readonly Color Teal = new Color32(75, 226, 202, 255);
        static readonly Color Cream = new Color32(255, 250, 234, 255);
        static readonly Color Coral = new Color32(255, 124, 104, 255);
        static readonly Color Sky = new Color32(112, 183, 255, 255);
        static readonly Color Gold = new Color32(255, 205, 82, 255);
        static readonly Color[] Palette =
        {
            new Color32(91, 168, 255, 255), Coral, Gold,
            new Color32(177, 120, 255, 255), new Color32(112, 204, 139, 255), new Color32(255, 154, 79, 255)
        };
        static readonly string[] FloorNames =
        {
            "LOBBY", "MAIL ROOM", "GARDENS", "OFFICES", "STUDIO", "CAFETERIA",
            "LIBRARY", "OBSERVATORY", "SKY LOUNGE", "ARCADE", "ROOFTOP", "DREAM DECK"
        };
        const float IntroRevealStart = 3.05f;
        const float IntroRevealDuration = 4.2f;
        ElevatorRound round;
        Phase phase;
        float phaseTime, introTime, doors = 1;
        bool paused, tutorialSeen, stampPlayed;
        string notice = "Welcome aboard. Your shift starts when you are ready.";

        // Bind editable scene objects, then start audio and a fresh round.
        void Awake()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            Time.timeScale = 1f;
            EnsureManagers();
            round = CreateExtendedRound();
            if (!BindScene()) { enabled = false; return; }
            InitializeExtendedInterior();
            InitializeAudio();
            phase = Phase.Intro;
            introTime = 0;
            doors = 0;
            SetDoors(doors);
            SyncFigures(0);
        }

        // Advance gameplay each frame; menus and pause stop the clock.
        void Update()
        {
            if (MenuManager.IsOpen) return;
            if (Match != null && ((!Match.Running && phase != Phase.Intro) || Match.Paused)) return;
            var input = InputManager.Instance;
            bool menu = phase == Phase.Welcome || phase == Phase.Tutorial || phase == Phase.Results;
            if (extendedInterior && input != null)
            {
                if (menu && input.StartPressed) { StartOrContinue(); return; }
            }
            if (phase == Phase.Intro)
            {
                // Update is gated while the mode menu is open, so the intro
                // starts when the player chooses a mode rather than behind it.
                introTime += Time.unscaledDeltaTime;
                if (!stampPlayed && introTime >= 1.12f) { stampPlayed = true; Play(stamp); }
                float opening = Mathf.Clamp01((introTime - IntroRevealStart) / IntroRevealDuration);
                SetDoors(EaseOut(opening));
                if (introTime >= IntroRevealStart + IntroRevealDuration + .15f) { phase = Phase.Welcome; introTime = 0; SetDoors(1); }
                return;
            }
            // Pause / mute are owned by GameManager + MusicManager for the player seat.
            if (IsNpc && input != null && input.PausePressed && phase != Phase.Welcome && phase != Phase.Results)
                paused = !paused;
            if (input != null && input.StartPressed && menu) StartOrContinue();
            if (paused || phase == Phase.Welcome || phase == Phase.Tutorial || phase == Phase.Results) return;
            float dt = Time.deltaTime;
            // Drain waiting patience only while stopped for boarding.
            round.Tick(dt, phase == Phase.Boarding);
            // End before accepting another drop-off, even if an exit animation
            // is still running. The score is locked when the clock reaches zero.
            if (round.Finished)
            {
                phase = Phase.Results;
                SelectRider(null);
                ClearInteriorTransfers(); ClearKicks(); exiting.Clear();
                Play(chime);
                ResetCameraMotion();
                session?.NotifyVictory();
                return;
            }
            // Process the player's action before advancing door/travel animation.
            if (!IsNpc)
            {
                UpdatePassengerSelection();
                HandleInteriorController(dt);
            }
            if (phase == Phase.Moving || phase == Phase.Docking) AdvanceBuildingTravel(dt);
            else
            {
                phaseTime += dt;
                if (phase == Phase.Closing)
                {
                    doors = 1 - Mathf.Clamp01(phaseTime / .65f);
                    if (phaseTime >= .65f) { doors = 0; BeginBuildingTravel(); }
                }
                else if (phase == Phase.Opening)
                {
                    doors = Mathf.Clamp01(phaseTime / .65f);
                    if (phaseTime >= .65f) { phase = Phase.Boarding; phaseTime = 0; destination = -1; }
                }
            }
            SetDoors(doors);
            SyncFigures(dt);
            if (phase != Phase.Moving) AccumulateRust(dt);
            UpdateRideMotion(dt);
            UpdateInteriorPersona(dt);
        }

        // Show the tutorial once, then begin a fresh shift.
        void StartOrContinue()
        {
            if (phase == Phase.Welcome && !tutorialSeen)
            {
                tutorialSeen = true; phase = Phase.Tutorial; phaseTime = 0; Play(chime);
                return;
            }
            Restart();
        }

        // Clear old rider state and reset the shift.
        void Restart()
        {
            CancelPassengerDrag();
            SelectRider(null);
            ClearInteriorTransfers();
            ClearKicks();
            ClearPatienceBars();
            ClearSpeechBubbles();
            foreach (var f in figures.Values) Destroy(f.gameObject); figures.Clear();
            bubbles.Clear(); destinationTags.Clear(); cabinPositions.Clear(); exiting.Clear(); exitStarts.Clear(); riderHits.Clear();
            selectedRider = null;
            round = CreateExtendedRound(); phase = Phase.Boarding; paused = false; phaseTime = 0; doors = 1;
            leftDoor.gameObject.SetActive(false); rightDoor.gameObject.SetActive(false);
            floorSign.text = "0"; floorSign.transform.localPosition = floorSignHome; floorSign.characterSize = .04f;
            arrivalImpact = 0; ResetCameraMotion(); selectedFloor = -1; destination = -1;
            ResetBuildingTravel();
            RefreshFloorButtons(); SetControlStatus("READY"); notice = "Highlight a passenger, then confirm. C / top button closes the doors.";
            SyncFigures(0); UpdateInteriorPersona(0); Play(chime);
        }

    }
}

// --- from SceneBindings.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    public sealed partial class ElevatorManager
    {
        // Read the scene's Inspector references; never regenerate its geometry.
        bool BindScene()
        {
            if (sceneView == null || sceneView.cabin == null || sceneView.cabinCamera == null
                || sceneView.leftDoor == null || sceneView.rightDoor == null
                || sceneView.floorDisplay == null || sceneView.exteriorCamera == null
                || sceneView.exteriorCar == null || sceneView.passingLights == null)
            {
                Debug.LogError("The Main scene needs its Elevator Scene references assigned in the Inspector.", this);
                return false;
            }
            stage = sceneView.cabin;
            eye = sceneView.cabinCamera;
            leftDoor = sceneView.leftDoor; rightDoor = sceneView.rightDoor;
            scenery = sceneView.passingLights;
            floorSign = sceneView.floorDisplay;
            floorSignHome = floorSign.transform.localPosition;
            cameraHome = eye.transform.position;
            cameraHomeRotation = eye.transform.rotation;
            if (keepDoorwayClear && cameraLift > 0)
            {
                eye.transform.position += eye.transform.up * cameraLift;
                cameraHome = eye.transform.position;
            }
            exteriorCamera = sceneView.exteriorCamera;
            exteriorCar = sceneView.exteriorCar;
            eye.gameObject.SetActive(true); eye.enabled = true;
            eye.targetTexture = null;
            exteriorCamera.enabled = false;
            if (sceneView.clearCamera != null) sceneView.clearCamera.enabled = true;
            if (sceneView.lights != null)
                foreach (var light in sceneView.lights) if (light != null) light.enabled = true;

            // The action is Inspector data, independent of the button's appearance.
            foreach (var button in stage.GetComponentsInChildren<ElevatorButton>(true))
            {
                var collider = button.GetComponent<Collider>();
                if (button.action == ElevatorButton.Action.Open) openButtons.Add(collider);
                else if (button.action == ElevatorButton.Action.Close) closeButtons.Add(collider);
                else
                {
                    floorButtons[collider] = button.floor;
                    floorButtonVisuals[button.floor] = button.highlight;
                }
            }
            foreach (var display in sceneView.controlDisplays)
                if (display != null) controlStatuses.Add(display);
            RefreshFloorButtons();
            return true;
        }
    }
}

// --- from BuildingTravel.cs ---
namespace CrazyElevator.Managers
{
    public sealed partial class ElevatorManager
    {
        [Header("Building travel")]
        [Range(.2f, 2f)] public float floorsPerSecond = .85f;
        [Min(.1f)] public float boostAcceleration = 1.1f;
        [Min(.2f)] public float maximumTravelSpeed = 2.4f;
        [Range(.1f, .4f)] public float stopWindow = .24f;
        float travelFloor, dockingStart;
        int travelDirection = 1;
        float travelVelocity, boostAxis;
        bool boostHeld;
        readonly HashSet<int> passedFloors = new HashSet<int>();
        bool BuildingView => phase == Phase.Moving || phase == Phase.Docking || phase == Phase.Opening;
        int NearbyFloor => Mathf.Clamp(Mathf.RoundToInt(travelFloor), 0, ElevatorRound.Floors - 1);
        bool CanStopAtFloor => phase == Phase.Moving && Mathf.Abs(travelFloor - NearbyFloor) <= stopWindow;

        void CloseAndTravel()
        {
            if (phase != Phase.Boarding || PersonaBusy) return;
            SelectRider(null);
            travelFloor = round.Floor;
            if (round.Floor == 0) travelDirection = 1;
            if (round.Floor == ElevatorRound.Floors - 1) travelDirection = -1;
            destination = -1; phase = Phase.Closing; phaseTime = 0;
            notice = "Doors closing. Choose up/down outside, then confirm near a floor to stop.";
            Play(click);
        }
        void BeginBuildingTravel()
        {
            passedFloors.Clear(); passedFloors.Add(round.Floor);
            round.LeaveFloor();
            travelFloor = round.Floor;
            travelVelocity = travelDirection * CruiseSpeed;
            boostAxis = 0; boostHeld = false;
            phase = Phase.Moving; phaseTime = 0; destination = -1;
            notice = "Hold Shift / left shoulder with up/down to build speed. Confirm near a floor to stop.";
        }
        void RequestFloorStop()
        {
            if (!CanStopAtFloor) { notice = "Between floors. Confirm when the floor marker turns green."; return; }
            destination = NearbyFloor; dockingStart = travelFloor;
            travelVelocity = 0; boostAxis = 0; boostHeld = false;
            phase = Phase.Docking; phaseTime = 0;
            notice = "Stopping at floor " + destination + "."; Play(click);
        }
        void AdvanceBuildingTravel(float dt)
        {
            if (dt <= 0) return;
            if (phase == Phase.Docking)
            {
                phaseTime += dt;
                travelFloor = Mathf.Lerp(dockingStart, destination, Mathf.SmoothStep(0, 1, phaseTime / .45f));
                if (phaseTime >= .45f)
                {
                    travelFloor = destination;
                    int waiting = round.Arrive(destination);
                    floorSign.text = destination.ToString();
                    phase = Phase.Opening; phaseTime = 0; Play(ding);
                    notice = waiting > 0 ? "Highlight a passenger and confirm to help them out." : "Highlight a waiting passenger and confirm to welcome them.";
                    UpdateInteriorPersona(0);
                }
                return;
            }
            if (phase != Phase.Moving) return;
            // Small integration steps make acceleration independent of frame rate and respect world boundaries.
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120));
            float step = dt / steps;
            for (int i = 0; i < steps; i++) AdvanceTravelStep(step);
            floorSign.text = NearbyFloor.ToString();
        }

        void AdvanceTravelStep(float dt)
        {
            AccumulateRust(dt);
            float before = travelFloor;
            float oldVelocity = Mathf.Clamp(travelVelocity, -SpeedLimit, SpeedLimit);
            if (boostHeld && Mathf.Abs(boostAxis) > .25f)
                travelVelocity = Mathf.Clamp(oldVelocity + boostAxis * CurrentAcceleration * dt, -SpeedLimit, SpeedLimit);
            else if (oldVelocity * travelDirection < CruiseSpeed)
                travelVelocity = Mathf.MoveTowards(oldVelocity, travelDirection * CruiseSpeed, CurrentAcceleration * .7f * dt);
            else travelVelocity = oldVelocity; // Releasing boost preserves the accumulated momentum.
            travelFloor = Mathf.Clamp(before + (oldVelocity + travelVelocity) * .5f * dt, 0, ElevatorRound.Floors - 1);
            if (travelFloor <= 0 && travelVelocity < 0 || travelFloor >= ElevatorRound.Floors - 1 && travelVelocity > 0) travelVelocity = 0;
            int motionDirection = travelFloor >= before ? 1 : -1;
            // A skipped floor counts only after leaving its stopping window; docking there never incurs a missed-stop penalty.
            for (int floor = 0; floor < ElevatorRound.Floors; floor++)
            {
                float edge = floor + motionDirection * stopWindow;
                bool crossed = motionDirection > 0 ? before <= edge && travelFloor > edge : before >= edge && travelFloor < edge;
                if (!crossed || !passedFloors.Add(floor)) continue;
                int dockedFloor = round.Floor;
                round.Floor = floor; round.LeaveFloor(); round.Floor = dockedFloor;
            }
            if (Mathf.Approximately(before, travelFloor))
                notice = "End of shaft. Confirm to stop here, or choose the opposite direction.";
        }
        void ResetBuildingTravel()
        {
            travelFloor = round.Floor; travelDirection = 1; destination = -1; passedFloors.Clear();
            travelVelocity = boostAxis = rustExposure = 0; boostHeld = false;
            SelectRider(null); controllerSelection = false; lastSelectionDirection = Vector2.zero;
        }
    }
}

// --- from ElevatorMovement.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    // Door timing, floor selection, travel and camera movement.
    public sealed partial class ElevatorManager
    {
        // State owned by this part of the prototype.
        float travelDuration;
        const float LowestFloorDoorTime = 8f;
        const float HighestFloorDoorTime = 3.2f;
        float arrivalImpact;
        Vector3 cameraHome;
        Quaternion cameraHomeRotation;
        int destination, origin;
        int selectedFloor = -1;

        // Slow an animation near its end.
        float EaseOut(float value)
        {
            value = Mathf.Clamp01(value);
            return 1 - Mathf.Pow(1 - value, 3);
        }

        // Slide both doors; 0 is shut and 1 is open.
        void SetDoors(float openness)
        {
            doors = Mathf.Clamp01(openness);
            if (leftDoor == null || rightDoor == null) return;
            leftDoor.localPosition = sceneView.leftDoorClosed + Vector3.left * (doors * sceneView.doorTravel);
            rightDoor.localPosition = sceneView.rightDoorClosed + Vector3.right * (doors * sceneView.doorTravel);
            leftDoor.gameObject.SetActive(doors < .98f); rightDoor.gameObject.SetActive(doors < .98f);
        }

        // Show each passing floor with a small pulse.
        void AnimateFloorIndicator()
        {
            int distance = Mathf.Abs(destination - origin);
            if (distance == 0) return;
            float progress = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, travelDuration));
            float exactStep = progress * distance;
            int completedStep = Mathf.Min(distance, Mathf.FloorToInt(exactStep + .001f));
            int direction = destination > origin ? 1 : -1;
            int shownFloor = origin + direction * completedStep;
            float betweenFloors = exactStep - completedStep;
            float pulse = Mathf.Sin(Mathf.Clamp01(betweenFloors) * Mathf.PI);
            floorSign.text = shownFloor.ToString();
            floorSign.transform.localPosition = floorSignHome + Vector3.up * (.055f * pulse);
            floorSign.characterSize = .04f + .006f * pulse;
        }

        // Apply travel vibration or an arrival bump.
        void UpdateRideMotion(float dt)
        {
            if (eye == null) return;
            if (phase == Phase.Moving)
            {
                float progress = Mathf.Clamp01(phaseTime / Mathf.Max(.01f, travelDuration));
                float ramp = Mathf.Clamp01(Mathf.Min(progress / .12f, (1 - progress) / .12f));
                float strength = (destination > origin ? .024f : .016f) * ramp;
                float clock = Time.unscaledTime;
                Vector3 localShake = new Vector3(Mathf.Sin(clock * 51f), Mathf.Sin(clock * 67f) * .75f, 0) * strength;
                eye.transform.position = cameraHome + cameraHomeRotation * localShake;
                eye.transform.rotation = cameraHomeRotation * Quaternion.Euler(Mathf.Sin(clock * 39f) * strength * 18f, 0, Mathf.Sin(clock * 43f) * strength * 12f);
                return;
            }
            if (arrivalImpact > 0)
            {
                arrivalImpact = Mathf.Max(0, arrivalImpact - dt * 2.8f);
                float kick = Mathf.Sin((1 - arrivalImpact) * Mathf.PI * 4f) * arrivalImpact;
                eye.transform.position = cameraHome + cameraHomeRotation * new Vector3(0, kick * .075f, 0);
                eye.transform.rotation = cameraHomeRotation * Quaternion.Euler(kick * 1.1f, 0, 0);
                return;
            }
            ResetCameraMotion();
        }

        // Restore the camera's resting pose.
        void ResetCameraMotion()
        {
            if (eye == null) return;
            eye.transform.position = cameraHome;
            eye.transform.rotation = cameraHomeRotation;
        }

        // Update both control-panel messages.
        void SetControlStatus(string text)
        {
            foreach (var status in controlStatuses) if (status != null) status.text = text;
        }

        // Highlight the selected and current floors.
        void RefreshFloorButtons()
        {
            foreach (var pair in floorButtonVisuals)
            {
                Color color = pair.Key == selectedFloor ? Gold : pair.Key == round.Floor ? Cream : Teal;
                if (pair.Value != null && pair.Value.material != null) pair.Value.material.color = color;
            }
        }

        // Close manually without choosing a stop.
        void CloseWithoutDestination()
        {
            if (phase != Phase.Boarding) return;
            selectedFloor = -1; destination = -1; RefreshFloorButtons();
            phase = Phase.Closing; phaseTime = 0; SetControlStatus("CLOSING"); Play(click);
        }

        // Prefer onboard destinations, otherwise choose a nearby floor.
        int AutomaticNextFloor()
        {
            var riderStops = new List<int>();
            foreach (var rider in round.Riders)
            {
                if (!rider.Boarded || rider.Resolved || rider.Destination == round.Floor) continue;
                if (!riderStops.Contains(rider.Destination)) riderStops.Add(rider.Destination);
            }
            if (riderStops.Count > 0)
                return riderStops[Random.Range(0, riderStops.Count)];

            // With an empty car, stay local rather than jumping across the
            // whole building. The small random window makes an unattended
            // elevator feel alive while keeping the next queue reachable.
            var nearby = new List<int>();
            for (int distance = 1; distance <= 3; distance++)
            {
                int above = round.Floor + distance;
                int below = round.Floor - distance;
                if (above < ElevatorRound.Floors) nearby.Add(above);
                if (below >= 0) nearby.Add(below);
            }
            return nearby.Count > 0 ? nearby[Random.Range(0, nearby.Count)] : (round.Floor + 1) % ElevatorRound.Floors;
        }

        // Choose a stop when boarding time runs out.
        void AutoDepartAfterTimeout()
        {
            if (phase != Phase.Boarding) return;
            int next = AutomaticNextFloor();
            selectedFloor = next; destination = next; RefreshFloorButtons();
            phase = Phase.Closing; phaseTime = 0;
            SetControlStatus("AUTO " + next.ToString()); Play(click);
            notice = "Doors timed out. Continuing to " + FloorNames[next] + ".";
        }

        // Higher floors give less boarding time.
        float DoorHoldDurationAtCurrentFloor()
        {
            if (round == null || ElevatorRound.Floors <= 1) return LowestFloorDoorTime;
            float floorProgress = Mathf.Clamp01(round.Floor / (float)(ElevatorRound.Floors - 1));
            return Mathf.Lerp(LowestFloorDoorTime, HighestFloorDoorTime, floorProgress);
        }

        // Apply missed-stop penalties, then begin travel.
        void StartMoving()
        {
            if (destination < 0 || destination == round.Floor) return;
            origin = round.Floor;
            int missed = round.LeaveFloor();
            travelDuration = 1.1f + Mathf.Abs(destination - origin) * .8f;
            phase = Phase.Moving; phaseTime = 0;
            SetControlStatus("GOING " + destination.ToString());
            notice = missed > 0 ? "You passed someone's floor. Their mood dropped." : "Next stop: " + FloorNames[destination] + ".";
        }

        // Accept floor choices only while stopped or closing.
        void DepartToFloor(int floor)
        {
            if (paused || floor < 0 || floor >= ElevatorRound.Floors) return;
            if (floor == round.Floor)
            {
                selectedFloor = -1; destination = -1; RefreshFloorButtons(); SetControlStatus("CURRENT FLOOR");
                return;
            }
            if (phase != Phase.Boarding && phase != Phase.Closing && phase != Phase.Closed) return;
            selectedFloor = floor; destination = floor; RefreshFloorButtons();
            SetControlStatus("GOING " + destination.ToString()); Play(click);
            if (phase == Phase.Closed) StartMoving();
            else if (phase == Phase.Boarding) { phase = Phase.Closing; phaseTime = 0; }
        }

    }
}

// --- from ExtendedElevatorInterior.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    // Optional authored cabin controls used by the Main gameplay scene.
    public sealed partial class ElevatorManager
    {
        [Header("Extended interior")]
        public bool extendedInterior;
        public ElevatorPersonaRig personaPrefab;
        [Range(1, 11)] public int candyStartsAtFloor = 4;
        [Range(1, 11)] public int underwaterStartsAtFloor = 8;
        ElevatorPersonaRig persona;
        float repairProgress;
        float repairedFor;
        int appliedWorld = -1;
        readonly Dictionary<Rider, InteriorTransfer> boardingTransfers = new Dictionary<Rider, InteriorTransfer>();

        sealed class InteriorTransfer
        {
            public Vector3 start, target, scale, targetScale;
            public Quaternion rotation;
            public float time;
            public bool gentle;
        }

        WorldBand BandForFloor(float floor)
        {
            if (floor >= underwaterStartsAtFloor) return WorldBand.Water;
            if (floor >= candyStartsAtFloor) return WorldBand.Candy;
            return WorldBand.Office;
        }

        // Cabin persona stays gentle in office + candy; only water goes rusty.
        bool FriendlyInterior => BandForFloor(round != null ? round.Floor : 0) != WorldBand.Water;
        bool PersonaBusy => boardingTransfers.Count > 0 || exiting.Count > 0;
        float PersonaExitDuration => FriendlyInterior ? 1.35f : .78f;
        static readonly Vector3[] ClearDoorwayCabinSpots =
        {
            // The camera is at positive Z looking toward the doors, so the
            // smallest Z row is visually the back row. Complete each row before
            // trying a position closer to the camera. The 1.12-unit spacing
            // also prevents larger parties from prematurely forcing a new row.
            new Vector3(-1.68f, .12f, .32f), new Vector3(-.56f, .12f, .32f),
            new Vector3(.56f, .12f, .32f), new Vector3(1.68f, .12f, .32f),

            new Vector3(-1.68f, .12f, 1.48f), new Vector3(-.56f, .12f, 1.48f),
            new Vector3(.56f, .12f, 1.48f), new Vector3(1.68f, .12f, 1.48f),

            new Vector3(-1.68f, .12f, 2.64f), new Vector3(-.56f, .12f, 2.64f),
            new Vector3(.56f, .12f, 2.64f), new Vector3(1.68f, .12f, 2.64f)
        };

        void InitializeExtendedInterior()
        {
            if (!extendedInterior) return;
            // Hide the entire hardware panel, including its labels and colliders.
            foreach (Transform part in stage.GetComponentsInChildren<Transform>(true))
                if (part.name == "Wall control panel") part.gameObject.SetActive(false);
            foreach (var button in stage.GetComponentsInChildren<ElevatorButton>(true)) button.gameObject.SetActive(false);
            openButtons.Clear(); closeButtons.Clear(); floorButtons.Clear(); floorButtonVisuals.Clear(); controlStatuses.Clear();
            if (!personaPrefab) { Debug.LogError("Assign the interior persona prefab.", this); return; }
            persona = Instantiate(personaPrefab, stage, false);
            persona.name = "Elevator personality";
            InitializeImpairmentIndicators();
            foreach (Transform part in stage.GetComponentsInChildren<Transform>(true))
                if (part.name == "Hall back wall" || part.name == "Hall wall seam") part.gameObject.SetActive(false);
            floorSign.transform.localPosition = new Vector3(1.75f, 2.72f, .2f);
            floorSignHome = floorSign.transform.localPosition;
            UpdateInteriorPersona(0);
        }

        void UpdateInteriorPersona(float dt)
        {
            if (!extendedInterior || !persona) return;
            repairedFor = Mathf.Max(0, repairedFor - dt);
            WorldBand band = BandForFloor(round.Floor);
            persona.SetWorld(band, repairedFor > 0 || HasHandyman);
            UpdateImpairmentIndicators();
            int world = (int)band;
            if (appliedWorld != world)
            {
                appliedWorld = world;
                var tint = new MaterialPropertyBlock();
                Color wall = band == WorldBand.Office ? new Color(.82f, .79f, .72f)
                    : band == WorldBand.Candy ? new Color(.90f, .72f, .84f)
                    : new Color(.18f, .34f, .36f);
                // Lobby + cabin floors follow the world theme (navy carpet / pastel / deep sand).
                Color floorTint = band == WorldBand.Office ? new Color(.14f, .20f, .38f)
                    : band == WorldBand.Candy ? new Color(.93f, .78f, .88f)
                    : new Color(.10f, .30f, .34f);
                Color ceilingTint = band == WorldBand.Office ? new Color(.92f, .90f, .84f)
                    : band == WorldBand.Candy ? new Color(.95f, .88f, .96f)
                    : new Color(.12f, .40f, .48f);
                foreach (var surface in stage.GetComponentsInChildren<Renderer>(true))
                {
                    string part = surface.name;
                    Color color = wall;
                    if (part == "Cabin floor" || part == "Hall floor"
                        || part.StartsWith("Floor seam") || part.StartsWith("Hall tile")
                        || part.StartsWith("Threshold")) color = floorTint;
                    else if (part == "Hall ceiling" || part == "Hall upper band"
                        || part.StartsWith("Ceiling")) color = ceilingTint;
                    else if (part != "Right wall" && part != "Left wall" && !part.StartsWith("Sliding door")
                        && part != "Back wall" && !part.StartsWith("Back metal")) continue;
                    surface.GetPropertyBlock(tint);
                    tint.SetColor("_BaseColor", color); tint.SetColor("_Color", color);
                    surface.SetPropertyBlock(tint);
                }
                Color lamp = band == WorldBand.Office ? new Color(1f, .96f, .88f)
                    : band == WorldBand.Candy ? new Color(1f, .88f, .94f)
                    : new Color(.45f, .82f, .95f);
                foreach (var lampLight in sceneView.lights ?? System.Array.Empty<Light>())
                {
                    if (!lampLight) continue;
                    lampLight.color = lamp;
                    lampLight.intensity = band == WorldBand.Water ? 0.85f
                        : band == WorldBand.Candy ? 1.15f : 1.05f;
                }
            }
            persona.Tick(dt, phase == Phase.Moving, PersonaBusy);
        }

        void HandleInteriorController(float dt)
        {
            var input = InputManager.Instance;
            var mouse = Mouse.current;
            Vector2 mouseGui = mouse == null ? Vector2.zero
                : new Vector2(mouse.position.ReadValue().x, Screen.height - mouse.position.ReadValue().y);
            if (Match != null)
            {
                Rect view = Match.ViewRect(Seat);
                mouseGui.x -= view.x;
                mouseGui.y -= view.y;
            }
            GetMouseControlRects(out var mouseUp, out var mouseDown, out var mouseStop,
                out var mouseClose, out var mouseHold, out var mouseRepair);
            bool confirm = input != null && input.ConfirmPressed;
            if (phase == Phase.Moving)
            {
                float vertical = input != null ? input.Move.y : 0f;
                bool mouseUpHeld = mouse != null && mouse.leftButton.isPressed && mouseUp.Contains(mouseGui);
                bool mouseDownHeld = mouse != null && mouse.leftButton.isPressed && mouseDown.Contains(mouseGui);
                if (mouseUpHeld || mouseDownHeld) vertical = mouseUpHeld ? 1 : -1;
                bool boost = (input != null && input.BoostHeld) || mouseUpHeld || mouseDownHeld;
                SetTravelInput(vertical, boost);
                bool mouseStopPressed = mouse != null && mouse.leftButton.wasPressedThisFrame && mouseStop.Contains(mouseGui);
                if (confirm || mouseStopPressed) RequestFloorStop();
                return;
            }
            if (phase != Phase.Boarding || PersonaBusy) return;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && mouseClose.Contains(mouseGui))
            { CloseAndTravel(); return; }
            if (confirm) { ConfirmPassenger(); return; }
            if (input != null && input.CloseDoorsPressed) { CloseAndTravel(); return; }
            if (input != null && input.KickoutPressed) { KickoutSelectedPublic(); return; }
            bool hold = (input != null && input.HoldDoorHeld)
                || mouse != null && mouse.leftButton.isPressed && mouseHold.Contains(mouseGui);
            if (hold) { round.HoldDoor(dt); notice = "Holding the door for passengers."; }
            bool repair = (input != null && input.RepairHeld)
                || mouse != null && mouse.leftButton.isPressed && mouseRepair.Contains(mouseGui);
            if (!FriendlyInterior && repairedFor <= 0 && repair)
            {
                repairProgress += dt;
                notice = "Repairing rusty mechanism... " + Mathf.RoundToInt(repairProgress / 2f * 100) + "%";
                if (repairProgress >= 2f)
                {
                    repairedFor = 20f; repairProgress = 0; rustExposure = 0; Play(ding);
                    notice = "Rust reduced to one starfish. A handyman onboard restores full performance.";
                }
            }
            else repairProgress = 0;
        }

        void BoardPassengerWithPersona(Rider rider)
        {
            if (!CanSelect(rider) || rider.Boarded || !figures.TryGetValue(rider, out var figure)) return;
            if (!round.HasCapacityFor(rider))
            {
                int available = Mathf.Max(0, ElevatorRound.Capacity - round.Load);
                string unit = rider.Space == 1 ? " space" : " spaces";
                SetControlStatus("OVERLOADED");
                notice = "ELEVATOR OVERLOADED! " + rider.Name + " needs " + rider.Space + unit + "; only " + available + " left.";
                Play(buzz);
                return;
            }
            if (keepDoorwayClear)
            {
                foreach (Vector3 spot in ClearDoorwayCabinSpots)
                {
                    if (!IsInsideCabin(rider, spot) || !CabinPlacementClear(rider, spot)) continue;
                    if (!round.Board(rider)) return;
                    cabinPositions[rider] = spot;
                    BeginPersonaBoard(rider, figure.localPosition, spot);
                    notice = FriendlyInterior ? "Come on in. I'll give you a hand!" : "In you go! Cough... salt water in my gears again.";
                    return;
                }
                SetControlStatus("OVERLOADED");
                notice = "ELEVATOR OVERLOADED! This passenger needs " + rider.Space + " adjacent spaces. Unload someone first.";
                Play(buzz);
                return;
            }
            for (float z = 1f; z <= 3f; z += .7f)
            for (float x = -1.4f; x <= 1.41f; x += .7f)
            {
                Vector3 spot = new Vector3(x, .12f, z);
                if (!IsInsideCabin(rider, spot) || !CabinPlacementClear(rider, spot)) continue;
                if (!round.Board(rider)) return;
                cabinPositions[rider] = spot;
                BeginPersonaBoard(rider, figure.localPosition, spot);
                notice = FriendlyInterior ? "Come on in. I'll give you a hand!" : "In you go! Cough... salt water in my gears again.";
                return;
            }
            SetControlStatus("OVERLOADED");
            notice = "ELEVATOR OVERLOADED! No clear space for this passenger. Unload someone first.";
            Play(buzz);
        }

        void BeginPersonaBoard(Rider rider, Vector3 start, Vector3 target)
        {
            if (!extendedInterior || !figures.TryGetValue(rider, out var figure)) return;
            float depthScale = keepDoorwayClear
                ? Mathf.Lerp(.72f, .92f, Mathf.InverseLerp(.32f, 2.64f, target.z))
                : 1f;
            boardingTransfers[rider] = new InteriorTransfer { start = start, target = target, scale = figure.localScale,
                targetScale = figure.localScale * depthScale,
                rotation = figure.localRotation, gentle = FriendlyInterior };
            figure.localPosition = start;
            Play(FriendlyInterior ? chime : buzz);
        }

        bool AnimatePersonaBoard(Rider rider, Transform figure, float dt)
        {
            if (!boardingTransfers.TryGetValue(rider, out var transfer)) return false;
            transfer.time += dt / (transfer.gentle ? 1.35f : .78f);
            ElevatorPersonaRig.PassengerPose(transfer.start, transfer.target, transfer.time, transfer.gentle,
                out Vector3 position, out float lean, out float squash);
            figure.localPosition = position;
            figure.localRotation = Quaternion.Euler(lean, 0, 0);
            Vector3 baseScale = Vector3.Lerp(transfer.scale, transfer.targetScale,
                Mathf.SmoothStep(0, 1, Mathf.Clamp01(transfer.time)));
            figure.localScale = Vector3.Scale(baseScale,
                new Vector3(1f / Mathf.Sqrt(squash), squash, 1f / Mathf.Sqrt(squash)));
            if (persona) persona.GuidePassenger(position, transfer.time, transfer.gentle, true);
            if (transfer.time >= 1)
            {
                figure.localPosition = transfer.target; figure.localScale = transfer.targetScale;
                figure.localRotation = Quaternion.identity;
                boardingTransfers.Remove(rider);
                if (persona) persona.RestHands();
            }
            return true;
        }

        void ClearInteriorTransfers()
        {
            foreach (var item in boardingTransfers)
                if (figures.TryGetValue(item.Key, out var figure))
                    figure.localScale = item.Key.Boarded ? item.Value.targetScale : item.Value.scale;
            boardingTransfers.Clear();
            repairProgress = repairedFor = 0;
            if (persona) persona.RestHands();
        }

        void DrawInteriorHUD()
        {
            if (!extendedInterior) return;
            float hudScale = Mathf.Max(1, Screen.height / 900f);
            float screenHeight = Screen.height / hudScale;
            float width = Mathf.Min(680, Screen.width / hudScale - 24);
            if (!compactTopHud && BuildingView)
            {
                Panel(new Rect(12, 42, 550, 153), new Color(Ink.r, Ink.g, Ink.b, .92f));
                string status = phase == Phase.Opening ? "DOORS OPENING" : phase == Phase.Docking ? "DOCKING AT FLOOR " + destination
                    : (travelVelocity >= 0 ? "GOING UP" : "GOING DOWN") + "  /  " + travelFloor.ToString("0.0");
                Label(new Rect(24, 49, 500, 30), status, large);
                Label(new Rect(24, 84, 500, 28), CanStopAtFloor ? "STOP READY - FLOOR " + NearbyFloor + "  /  click STOP or press E" : "Hold UP / DOWN or use keys   |   Click STOP near a floor", body);
                Label(new Rect(24, 112, 500, 24), WorldName(travelFloor), small);
                Label(new Rect(24, 137, 525, 24), Mathf.Abs(travelVelocity).ToString("0.00") + " floors/s  |  " + (boostHeld && boostAxis != 0 ? "ACCELERATING" : "COASTING"), body);
                Label(new Rect(24, 164, 525, 24), ImpairmentDescription, small);
            }
            else if (!compactTopHud)
            {
                Panel(new Rect(12, 42, Mathf.Min(width, 500), 97), new Color(Ink.r, Ink.g, Ink.b, .88f));
                Label(new Rect(24, 49, 444, 25), WorldStatusLine(round.Floor), body);
                Label(new Rect(24, 77, 444, 24), "FLOOR " + round.Floor + "  |  Drag riders to board, move, or eject", small);
                Label(new Rect(24, 106, 476, 24), ImpairmentDescription, small);
            }
            Panel(new Rect(12, screenHeight - 54, width, 42), new Color(Ink.r, Ink.g, Ink.b, .88f));
            string prompt = selectedRider == null
                ? (BuildingView ? "E / STOP near a floor   |   Shift+↑↓ to build speed" : "Drag riders to board or kick   |   C close & travel")
                : selectedRider.Name + " → F" + selectedRider.Destination + (selectedRider.Boarded ? "  ·  drag out to kick" : "  ·  drag in to board");
            Label(new Rect(24, screenHeight - 48, width - 24, 30), BuildingView ? WorldName(travelFloor) + "   ·   " + prompt : prompt, body);
        }
    }
}

// --- from UnderwaterImpairment.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    public sealed partial class ElevatorManager
    {
        [Header("Rust and handyman")]
        public StarfishImpairmentView impairmentGaugePrefab;
        [Min(1)] public float secondsPerRustLevel = 10f;
        [Range(.1f, .9f)] public float underwaterSpeedMultiplier = .58f;
        [Range(.1f, .9f)] public float underwaterAccelerationMultiplier = .45f;
        float rustExposure;
        StarfishImpairmentView interiorImpairment, exteriorImpairment;
        float MovementFloor => BuildingView ? travelFloor : round.Floor;
        bool InWater => MovementFloor >= underwaterStartsAtFloor;
        bool HasHandyman
        {
            get
            {
                if (round == null) return false;
                foreach (var rider in round.Riders)
                    if (rider.HasFeature(PassengerFeature.ClearsRust) && rider.Boarded && !rider.Resolved) return true;
                return false;
            }
        }
        int ImpairmentLevel => !InWater || HasHandyman ? 0 : 1 + Mathf.Clamp(Mathf.FloorToInt(rustExposure / secondsPerRustLevel), 0, 2);
        float SpeedMultiplier => ImpairmentLevel == 0 ? 1 : underwaterSpeedMultiplier * Mathf.Lerp(1, .55f, (ImpairmentLevel - 1) * .5f);
        float AccelerationMultiplier => ImpairmentLevel == 0 ? 1 : underwaterAccelerationMultiplier * Mathf.Lerp(1, .5f, (ImpairmentLevel - 1) * .5f);
        float CruiseSpeed => floorsPerSecond * SpeedMultiplier;
        float CurrentAcceleration => boostAcceleration * AccelerationMultiplier;
        float SpeedLimit => maximumTravelSpeed * SpeedMultiplier;

        [Header("Passenger data")]
        public PassengerCatalog passengerCatalog;

        ElevatorRound CreateExtendedRound()
        {
            var result = passengerCatalog != null && passengerCatalog.types != null && passengerCatalog.types.Length > 0
                ? ElevatorRound.FromCatalog(passengerCatalog)
                : new ElevatorRound();
            result.LateDeliveriesScore = !zeroScoreWhenMad;
            for (int floor = 0; floor < ElevatorRound.Floors; floor++)
            {
                int first = result.Riders.FindIndex(rider => rider.Origin == floor);
                if (first < 0) continue;
                PassengerData handyman = passengerCatalog != null ? passengerCatalog.FindKind("HANDYMAN") : null;
                Rider fixer = handyman != null
                    ? handyman.CreateRider(floor, (floor + 3) % ElevatorRound.Floors)
                    : new Rider
                    {
                        Name = "Casey", Kind = "HANDYMAN", Badge = "FIX", Color = 4,
                        Request = "Rust-free while I'm aboard!", Origin = floor, Destination = (floor + 3) % ElevatorRound.Floors,
                        Space = 1, Patience = 80, Remaining = 80, Bonus = 110
                    };
                result.Riders.Insert(first + 2, fixer);
            }
            float patienceScale = Mathf.Max(1f, patienceDurationMultiplier);
            foreach (var rider in result.Riders)
            {
                rider.Patience *= patienceScale;
                rider.Remaining = rider.Patience;
            }
            return result;
        }

        void InitializeImpairmentIndicators()
        {
            if (!impairmentGaugePrefab) { Debug.LogError("Assign the starfish impairment gauge.", this); return; }
            interiorImpairment = Instantiate(impairmentGaugePrefab, stage, false);
            interiorImpairment.name = "Interior starfish rust meter";
            interiorImpairment.transform.localPosition = new Vector3(0, 2.31f, .48f);
            interiorImpairment.transform.localScale = Vector3.one * .85f;
            exteriorImpairment = Instantiate(impairmentGaugePrefab, exteriorCar, false);
            exteriorImpairment.name = "Exterior starfish rust meter";
            exteriorImpairment.transform.localPosition = new Vector3(0, 1.65f, -1.05f);
            exteriorImpairment.transform.localRotation = Quaternion.Euler(0, 180, 0);
            exteriorImpairment.transform.localScale = Vector3.one * 1.8f;
            foreach (Transform part in exteriorImpairment.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = ExteriorLayer;
        }

        void AccumulateRust(float dt)
        {
            if (!InWater) rustExposure = Mathf.Max(0, rustExposure - dt * 2);
            else if (!HasHandyman && repairedFor <= 0) rustExposure = Mathf.Min(secondsPerRustLevel * 2, rustExposure + dt);
        }
        void UpdateImpairmentIndicators()
        {
            int severity = ImpairmentLevel;
            if (interiorImpairment) interiorImpairment.SetSeverity(severity);
            if (exteriorImpairment) exteriorImpairment.SetSeverity(severity);
        }
        string ImpairmentDescription => HasHandyman ? "HANDYMAN ABOARD - full speed / no impairment"
            : ImpairmentLevel == 0 ? "NO IMPAIRMENT" : "RUST " + ImpairmentLevel + "/3 - " + (ImpairmentLevel == 1 ? "sluggish" : ImpairmentLevel == 2 ? "worn gears" : "severely impaired");
    }
}

// --- from TravelView.cs ---
namespace CrazyElevator.Managers
{
    public sealed partial class ElevatorManager
    {
        const int ExteriorLayer = 31;
        const float FloorHeight = 3.3f;
        Camera exteriorCamera;
        Transform exteriorCar;
        bool showingTravelView;
        string WorldName(float floor)
        {
            switch (BandForFloor(floor))
            {
                case WorldBand.Office: return "OFFICE";
                case WorldBand.Candy: return "CANDY";
                default: return "UNDERWATER";
            }
        }

        string WorldStatusLine(float floor)
        {
            switch (BandForFloor(floor))
            {
                case WorldBand.Office: return "OFFICE  /  floors 0–3";
                case WorldBand.Candy: return "CANDY  /  floors 4–7";
                default: return "UNDERWATER  /  " + (HasHandyman ? "handyman on duty" : repairedFor > 0 ? "patched up" : "rusty & needs repair");
            }
        }

        Color WorldAccent(float floor)
        {
            switch (BandForFloor(floor))
            {
                case WorldBand.Office: return new Color32(98, 132, 178, 255);
                case WorldBand.Candy: return new Color32(236, 118, 188, 255);
                default: return new Color32(48, 196, 210, 255);
            }
        }

        Color WorldSky(float floor)
        {
            // Soft blend near band edges so travel reads as climbing through layers.
            float candy = candyStartsAtFloor;
            float water = underwaterStartsAtFloor;
            Color office = new Color32(176, 198, 224, 255);
            Color candySky = new Color32(220, 168, 236, 255);
            Color waterSky = new Color32(6, 58, 92, 255);
            if (floor < candy - 0.5f) return office;
            if (floor < candy + 0.5f)
                return Color.Lerp(office, candySky, Mathf.InverseLerp(candy - 0.5f, candy + 0.5f, floor));
            if (floor < water - 0.5f) return candySky;
            if (floor < water + 0.5f)
                return Color.Lerp(candySky, waterSky, Mathf.InverseLerp(water - 0.5f, water + 0.5f, floor));
            return waterSky;
        }

        // The cabin closes first. The building stays visible until docking and door opening finish.
        void LateUpdate()
        {
            if (!exteriorCamera || round == null || !eye) return;
            showingTravelView = BuildingView;
            exteriorCamera.enabled = showingTravelView;
            eye.enabled = !showingTravelView;
            Rect cameraRect = Match != null ? ElevatorMatchType.CameraRect(Seat) : new Rect(0, 0, 1, 1);
            eye.targetTexture = null;
            exteriorCamera.targetTexture = null;
            eye.rect = exteriorCamera.rect = cameraRect;
            exteriorCar.localPosition = new Vector3(Match != null ? Match.ShaftX(Seat) : 9,
                travelFloor * FloorHeight + 1.4f, -.8f);
            float cameraX = Match != null ? Match.ShaftX(0) + Match.shaftSpacing * .5f : 6f;
            exteriorCamera.transform.position = new Vector3(cameraX, travelFloor * FloorHeight + 4, -44);
            exteriorCamera.transform.rotation = Quaternion.Euler(5, 0, 0);
            if (Match != null)
                exteriorCamera.orthographicSize = Mathf.Max(9.5f, (Match.shaftSpacing + 3f) / exteriorCamera.aspect);
            exteriorCamera.backgroundColor = WorldSky(travelFloor);
        }

        void DrawTravelView()
        {
            if (!BuildingView || !exteriorCamera) return;
            int floor = NearbyFloor;
            Vector3 point = exteriorCamera.WorldToScreenPoint(exteriorCar.parent.TransformPoint(new Vector3(9, floor * FloorHeight + 1.4f, -.8f)));
            if (point.z <= 0) return;
            float size = Mathf.Max(1, Screen.height / 900f);
            float x = point.x, y = Screen.height - point.y;
            Color accent = CanStopAtFloor ? Teal : WorldAccent(floor);
            Panel(new Rect(x - 70 * size, y + 44 * size, 140 * size, 3 * size), accent);
            var markerStyle = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(15 * size) };
            Panel(new Rect(x - 78 * size, y + 49 * size, 156 * size, 28 * size), new Color(Ink.r, Ink.g, Ink.b, .92f));
            Label(new Rect(x - 76 * size, y + 50 * size, 152 * size, 26 * size),
                CanStopAtFloor ? "F" + floor + "  ·  STOP" : "F" + floor + "  ·  " + WorldName(floor), markerStyle);
        }

        void ReleaseCabinPreview() { if (eye) eye.targetTexture = null; }
    }
}

// --- from RuntimeEffects.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    // Scene handles and small temporary effects. Static art lives in prefabs.
    public sealed partial class ElevatorManager
    {
        // State owned by this part of the prototype.
        Camera eye;
        Transform stage, leftDoor, rightDoor, scenery;
        TextMesh floorSign;
        readonly Dictionary<Collider, int> floorButtons = new Dictionary<Collider, int>();
        readonly Dictionary<int, Renderer> floorButtonVisuals = new Dictionary<int, Renderer>();
        readonly HashSet<Collider> openButtons = new HashSet<Collider>();
        readonly HashSet<Collider> closeButtons = new HashSet<Collider>();
        readonly List<TextMesh> controlStatuses = new List<TextMesh>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        Vector3 floorSignHome = new Vector3(0, 2.72f, -.18f);

        // Reuse one generated material per color.
        Material Mat(Color color)
        {
            if (materials.TryGetValue(color, out var existing)) return existing;
            // Runtime-generated meshes do not have a serialized renderer reference.
            // Use the Resources material so the player build keeps the URP shader;
            // Shader.Find alone can return null after player shader stripping.
            var template = Resources.Load<Material>("GameJam2Lit");
            var mat = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .32f);
            materials.Add(color, mat); return mat;
        }

        // Create a primitive relative to its parent.
        Transform Shape(string name, PrimitiveType kind, Vector3 position, Vector3 size, Color color, Transform parent)
        {
            var g = GameObject.CreatePrimitive(kind); g.name = name;
            g.transform.SetParent(parent, false); g.transform.localPosition = position; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = Mat(color);
            return g.transform;
        }
        // Create centered world-space text.
        TextMesh Sign(string text, Vector3 position, float size, Color color)
        {
            var g = new GameObject("Sign • " + text); g.transform.SetParent(stage, false); g.transform.localPosition = position;
            var t = g.AddComponent<TextMesh>(); t.text = text; t.fontSize = 64; t.characterSize = size;
            t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center; t.color = color;
            // TextMesh fronts face negative Z, toward the observation camera.
            return t;
        }

    }
}

// --- from GameAudio.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    // Local clip generation + routing through SfxManager / MusicManager.
    public sealed partial class ElevatorManager
    {
        public AudioClip backgroundMusic;
        AudioClip chime, ding, click, buzz, groove, stamp, kickWhoosh;
        bool generatedGroove;

        void InitializeAudio()
        {
            chime = Tone(660, .28f);
            ding = DingTone();
            click = Tone(420, .08f);
            buzz = Tone(130, .18f);
            stamp = StampTone();
            kickWhoosh = KickWhoosh();

            if (IsNpc) return;

            groove = backgroundMusic;
            if (groove == null)
            {
                groove = Groove();
                generatedGroove = true;
                Debug.LogWarning("Background music is not assigned; using the built-in placeholder groove.");
            }
            MusicManager.Instance?.PlayMusic(groove);
        }

        AudioClip Tone(float frequency, float seconds)
        {
            const int rate = 22050; var data = new float[(int)(rate * seconds)];
            for (int i = 0; i < data.Length; i++) { float t = i / (float)rate; data[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) * Mathf.Sin(Mathf.PI * i / data.Length) * .5f; }
            var clip = AudioClip.Create("Elevator tone", data.Length, 1, rate, false); clip.SetData(data, 0); return clip;
        }

        AudioClip DingTone()
        {
            const int rate = 22050; const float seconds = 1.05f;
            var data = new float[Mathf.CeilToInt(rate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float attack = Mathf.Clamp01(t / .008f);
                float decay = Mathf.Exp(-t * 4.6f);
                float bell = Mathf.Sin(2 * Mathf.PI * 880f * t) * .46f
                           + Mathf.Sin(2 * Mathf.PI * 1320f * t) * .22f
                           + Mathf.Sin(2 * Mathf.PI * 1760f * t) * .10f;
                data[i] = bell * attack * decay;
            }
            var clip = AudioClip.Create("Elevator arrival ding", data.Length, 1, rate, false);
            clip.SetData(data, 0); return clip;
        }

        AudioClip Groove()
        {
            const int rate = 22050; const float bpm = 116; const int beats = 16;
            float beatLength = 60f / bpm, seconds = beats * beatLength;
            var data = new float[Mathf.CeilToInt(rate * seconds)];
            float[] bass = { 130.81f, 164.81f, 196f, 174.61f };
            float[] hook = { 523.25f, 659.25f, 783.99f, 659.25f, 587.33f, 698.46f, 880f, 698.46f };
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate, beat = t / beatLength, half = t % (beatLength * .5f);
                int beatIndex = Mathf.FloorToInt(beat) % beats, bar = beatIndex / 4;
                float beatPhase = t % beatLength, kick = Mathf.Exp(-beatPhase * 18f) * Mathf.Sin(2 * Mathf.PI * (84 - beatPhase * 35) * t) * .20f;
                float bassPhase = t % beatLength, bassLine = Mathf.Sin(2 * Mathf.PI * bass[bar] * t) * Mathf.Exp(-bassPhase * 3.8f) * .12f;
                float clapPhase = beatPhase;
                float noise = Mathf.Repeat(Mathf.Sin(i * 12.9898f) * 43758.5453f, 1f) * 2f - 1f;
                float clap = Mathf.Abs(noise) * Mathf.Exp(-clapPhase * 32f) * ((beatIndex % 4 == 1 || beatIndex % 4 == 3) ? .055f : 0);
                int hookIndex = Mathf.FloorToInt(t / (beatLength * .5f)) % hook.Length;
                float hookPhase = half, melody = Mathf.Sin(2 * Mathf.PI * hook[hookIndex] * t) * Mathf.Exp(-hookPhase * 8f) * .055f;
                float pad = Mathf.Sin(2 * Mathf.PI * bass[bar] * 2f * t) * .018f + Mathf.Sin(2 * Mathf.PI * bass[bar] * 3f * t) * .012f;
                float edge = Mathf.Min(1f, Mathf.Min(t / .035f, (seconds - t) / .035f));
                data[i] = Mathf.Clamp((kick + bassLine + clap + melody + pad) * edge, -.45f, .45f);
            }
            var clip = AudioClip.Create("Crazy Elevator Groove", data.Length, 1, rate, false); clip.SetData(data, 0); return clip;
        }

        AudioClip StampTone()
        {
            const int rate = 22050; const float seconds = .24f;
            var data = new float[Mathf.CeilToInt(rate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float thump = Mathf.Sin(2 * Mathf.PI * (145 - 90 * t) * t) * Mathf.Exp(-t * 20f) * .55f;
                float noise = (Mathf.Repeat(Mathf.Sin(i * 91.7f) * 43758.5453f, 1f) * 2f - 1f) * Mathf.Exp(-t * 38f) * .22f;
                data[i] = Mathf.Clamp(thump + noise, -.8f, .8f);
            }
            var clip = AudioClip.Create("Crazy stamp", data.Length, 1, rate, false);
            clip.SetData(data, 0); return clip;
        }

        AudioClip KickWhoosh()
        {
            const int rate = 22050;
            const float duration = .22f;
            var data = new float[Mathf.CeilToInt(rate * duration)];
            float phase = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)data.Length;
                phase += 2 * Mathf.PI * Mathf.Lerp(1100, 220, t) / rate;
                float envelope = Mathf.Sin(t * Mathf.PI);
                data[i] = Mathf.Sin(phase) * envelope * .5f;
            }
            var clip = AudioClip.Create("Passenger kick whoosh", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip sound)
        {
            if (IsNpc || sound == null) return;
            if (SfxManager.Instance != null) SfxManager.Instance.PlaySfx(sound);
        }

        void OnDestroy()
        {
            ReleaseCabinPreview();
            foreach (var m in materials.Values) Destroy(m);
            if (chime) Destroy(chime); if (ding) Destroy(ding); if (click) Destroy(click); if (buzz) Destroy(buzz);
            if (generatedGroove && groove) Destroy(groove);
            if (stamp) Destroy(stamp);
            if (kickWhoosh) Destroy(kickWhoosh);
            if (!IsNpc) MusicManager.Instance?.StopMusic();
        }
    }
}

// --- from MatchIntegration.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;

    // Match-only hooks. Solo and NPC elevators still run this same controller.
    public sealed partial class ElevatorManager
    {
        public MatchController Match { get; private set; }
        public int Seat { get; private set; }
        public bool IsNpc { get; private set; }
        public bool IntroPlaying => phase == Phase.Intro;
        public bool ShiftFinished => round != null && round.Finished;
        public int Points => round != null ? round.Score : 0;
        public int PassengerLoad => round != null ? round.Load : 0;
        public int HappyDeliveries => round != null ? round.Happy : 0;
        public float SecondsLeft => round != null ? round.TimeLeft : ElevatorRound.Duration;
        public float CurrentFloor => BuildingView ? travelFloor : round.Floor;
        public bool Traveling => phase == Phase.Moving || phase == Phase.Docking || phase == Phase.Opening;
        public bool AtStop => phase == Phase.Boarding && !paused && !ShiftFinished;
        public bool BusyWithPassenger => PersonaBusy;
        public bool IsBoarding => phase == Phase.Boarding && !paused && !ShiftFinished && !PersonaBusy;

        int npcTargetFloor = -1;
        float ViewWidth => Match != null ? Match.ViewRect(Seat).width : Screen.width;
        float ViewHeight => Match != null ? Match.ViewRect(Seat).height : Screen.height;

        public void ConfigureMatch(MatchController match, int seat, bool npc = false)
        {
            Match = match;
            Seat = seat;
            IsNpc = npc;
        }

        // Mirror Inspector tuning so both sides share the same passenger rules.
        public void CopyMatchSettingsTo(ElevatorManager target)
        {
            target.passengerCatalog = passengerCatalog;
            target.extendedInterior = extendedInterior;
            target.personaPrefab = personaPrefab;
            target.candyStartsAtFloor = candyStartsAtFloor;
            target.underwaterStartsAtFloor = underwaterStartsAtFloor;
            target.passengerPatienceBarPrefab = passengerPatienceBarPrefab;
            target.zeroScoreWhenMad = zeroScoreWhenMad;
            target.patienceDurationMultiplier = patienceDurationMultiplier;
            target.compactTopHud = compactTopHud;
            target.passengerSpeechBubblePrefab = passengerSpeechBubblePrefab;
            target.speechDuration = speechDuration;
            target.keepDoorwayClear = keepDoorwayClear;
            target.cameraLift = cameraLift;
            target.impairmentGaugePrefab = impairmentGaugePrefab;
            target.secondsPerRustLevel = secondsPerRustLevel;
            target.underwaterSpeedMultiplier = underwaterSpeedMultiplier;
            target.underwaterAccelerationMultiplier = underwaterAccelerationMultiplier;
            target.floorsPerSecond = floorsPerSecond;
            target.boostAcceleration = boostAcceleration;
            target.maximumTravelSpeed = maximumTravelSpeed;
            target.stopWindow = stopWindow;
            target.backgroundMusic = null;
        }

        public void StartMatchShift()
        {
            npcTargetFloor = -1;
            Restart();
        }

        public void SetMatchPaused(bool value) => paused = value;

        // NPC uses the same passenger placement, scoring, and kick animation.
        public bool NpcDeliver()
        {
            if (!IsBoarding) return false;
            foreach (Rider rider in round.Riders)
            {
                if (!rider.Boarded || rider.Resolved || rider.Destination != round.Floor) continue;
                if (!figures.TryGetValue(rider, out Transform figure)) continue;
                cabinPositions.Remove(rider);
                QueueRiderExit(rider, figure.localPosition);
                return true;
            }
            return false;
        }

        public bool NpcBoard()
        {
            if (!IsBoarding) return false;
            SyncFigures(0);
            foreach (Rider rider in round.Riders)
            {
                if (rider.Boarded || rider.Resolved || !round.IsOffered(rider) || rider.Arrival > 0) continue;
                if (!CanSelect(rider)) continue;
                SelectRider(rider);
                BoardPassengerWithPersona(rider);
                SelectRider(null);
                return rider.Boarded;
            }
            return false;
        }

        // Pick the nearest useful destination, or a floor with waiting riders.
        public void NpcChooseFloor()
        {
            if (!IsBoarding) return;
            int target = FindNpcTarget();
            if (target < 0 || target == round.Floor) target = (round.Floor + 1) % ElevatorRound.Floors;
            npcTargetFloor = target;
            travelDirection = target > round.Floor ? 1 : -1;
            CloseAndTravel();
        }

        public void NpcDriveToStop()
        {
            if (phase != Phase.Moving || npcTargetFloor < 0) return;
            boostAxis = travelDirection;
            boostHeld = true;
            if (CanStopAtFloor && NearbyFloor == npcTargetFloor)
            {
                boostAxis = 0;
                boostHeld = false;
                RequestFloorStop();
            }
        }

        int FindNpcTarget()
        {
            int best = -1;
            int bestDistance = int.MaxValue;
            foreach (Rider rider in round.Riders)
            {
                if (rider.Resolved) continue;
                int floor = rider.Boarded ? rider.Destination : rider.Origin;
                if (!rider.Boarded && !round.IsOffered(rider) && rider.Arrival > 0) continue;
                int distance = Mathf.Abs(floor - round.Floor);
                if (distance == 0) continue;
                if (distance < bestDistance)
                {
                    best = floor;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
