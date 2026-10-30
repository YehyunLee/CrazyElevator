using UnityEngine;
using UnityEngine.Serialization;
using CrazyElevator.Managers;
using CrazyElevator.Shared;
using Game = CrazyElevator.Managers.ElevatorManager;

namespace CrazyElevator.Match
{
    // One shared match; each elevator keeps its own controller and score.
    [DefaultExecutionOrder(-100)]
    public sealed partial class ElevatorMatch : MonoBehaviour
    {
        // The authored solo shaft sits at the centre of the exterior building.
        public const float ShaftCenterX = 9f;
        public Game player;
        [Min(3f)] public float shaftSpacing = 4f;
        [Header("Two-track collisions")]
        [Range(.15f, .8f)] public float trackSwitchSeconds = .32f;
        [FormerlySerializedAs("verticalBlockDistance")]
        [Tooltip("Extra world-space breathing room around the visible elevator cars.")]
        [Range(0f, .75f)] public float collisionPadding = .15f;
        [FormerlySerializedAs("collisionSpeedRetained")]
        [Tooltip("How much of the closing speed becomes a short rebound after a vertical hit.")]
        [Range(.15f, 1.1f)] public float collisionBounceStrength = .55f;
        [Range(.4f, 2.5f)] public float collisionSlowSeconds = 1.35f;
        public Game Rival { get; private set; }
        public bool Running { get; private set; }
        public bool Paused { get; private set; }
        bool humanRival;
        bool humanPlayer;
        public bool SplitScreen => humanPlayer && humanRival;
        public bool IsVisibleSeat(int seat) => SplitScreen
            || seat == (humanPlayer || !humanRival ? 0 : 1);
        public bool Finished => Running && player != null && Rival != null
            && player.ShiftFinished && Rival.ShiftFinished;
        public int CollisionCount => collisionCount;
        ElevatorScene rivalView;
        Transform rivalShaft;
        readonly Transform[] soloRails = new Transform[2];
        readonly int[] targetTrack = { 0, 1 };
        readonly float[] trackPosition = { 0f, 1f };
        float collisionCooldown;
        float collisionFlash;
        int collisionCount;
        static readonly float[] JunctionFloorPositions = { 5.5f, 12.5f, 19.5f };
        readonly int[] expressTrack = { 0, 1, 0 };
        readonly int[] usedJunctionMask = { 0, 0 };
        readonly float[] expressFlash = { 0f, 0f };
        const float ResultRevealInputAt = 5.7f;
        float resultRevealStartedAt = -1f;
        AudioClip collisionClip;
        ParticleSystem collisionParticles;
        Material collisionParticleMaterial;
        bool dimensionRevealActive;
        int dimensionRevealIndex = -1;
        float dimensionRevealTime;
        float dimensionRevealExitTime = -1f;
        readonly bool[] dimensionRevealReady = new bool[2];
        readonly bool[] dimensionRevealShown = new bool[ElevatorRound.Dimensions];
        public bool DimensionRevealActive => dimensionRevealActive;

        void Awake()
        {
            // The same scene and gameplay controller serve both menu choices.
            // This manager only activates for the 1v1 selection.
            if (!MenuManager.ConsumeDuelRequest())
            {
                enabled = false;
                return;
            }
            humanPlayer = !MenuManager.ConsumePlayerOneCpuRequest();
            humanRival = MenuManager.ConsumePlayerTwoRequest();

            if (player == null || player.sceneView == null)
            {
                Debug.LogError("Assign the player elevator to Elevator Match.", this);
                enabled = false;
                return;
            }
            player.ConfigureMatch(this, 0, !humanPlayer);
            if (!humanPlayer)
            {
                var npc = player.GetComponent<NpcElevator>();
                if (npc == null) npc = player.gameObject.AddComponent<NpcElevator>();
                npc.game = player;
            }
            CreateRival();
            collisionClip = CreateCollisionClip();
            collisionParticles = CreateCollisionParticles(player.sceneView.exteriorCar.parent);
        }

        // Reuse authored prefabs, not a second copy of the whole environment.
        void CreateRival()
        {
            var source = player.sceneView;
            rivalView = Instantiate(source);
            rivalView.name = humanRival ? "Player 2 elevator scene" : "NPC elevator scene";
            Transform duplicateWorld = rivalView.exteriorCar.parent;
            rivalView.exteriorCar.SetParent(source.exteriorCar.parent, false);
            rivalView.exteriorCar.name = humanRival ? "Player 2 exterior car" : "NPC exterior car";
            Destroy(duplicateWorld.gameObject);

            // The saved prefab already mounts the cabin and camera on this car.

            foreach (var listener in rivalView.GetComponentsInChildren<AudioListener>(true))
            { listener.enabled = false; Destroy(listener); }
            foreach (var listener in rivalView.exteriorCar.GetComponentsInChildren<AudioListener>(true))
            { listener.enabled = false; Destroy(listener); }
            if (rivalView.clearCamera != null) rivalView.clearCamera.gameObject.SetActive(false);

            // Keep the solo shaft centred, with two equally spaced tracks in 1v1.
            rivalShaft = new GameObject("Duel guide rails").transform;
            rivalShaft.SetParent(source.exteriorCar.parent, false);
            string[] railNames = { "Left guide rail", "Right guide rail" };
            for (int i = 0; i < railNames.Length; i++)
            {
                var rail = source.exteriorCar.parent.Find(railNames[i]);
                if (rail == null) continue;
                soloRails[i] = rail;
                for (int track = 0; track < 2; track++)
                {
                    var copy = Instantiate(rail, rivalShaft, false);
                    copy.name = "Track " + (track + 1) + " " + railNames[i];
                    copy.localPosition += Vector3.right * ((track - .5f) * shaftSpacing);
                }
                rail.gameObject.SetActive(false);
            }

            // Assign references before Awake runs on the rival controller.
            var actor = new GameObject(humanRival ? "Player 2 elevator controller" : "NPC elevator controller");
            actor.SetActive(false);
            Rival = actor.AddComponent<Game>();
            actor.AddComponent<PassengerManager>().Bind(Rival);
            Rival.sceneView = rivalView;
            player.CopyMatchSettingsTo(Rival);
            Rival.ConfigureMatch(this, 1, !humanRival);
            if (!humanRival) actor.AddComponent<NpcElevator>().game = Rival;
            actor.SetActive(true);
            ConfigureCameras();
        }

        void Update()
        {
            if (!enabled || player == null) return;
            float dt = Time.unscaledDeltaTime;
            if (dimensionRevealActive)
            {
                UpdateDimensionReveal(dt);
                return;
            }
            if (Paused) return;
            collisionCooldown = Mathf.Max(0f, collisionCooldown - dt);
            collisionFlash = Mathf.Max(0f, collisionFlash - dt);
            expressFlash[0] = Mathf.Max(0f, expressFlash[0] - dt);
            expressFlash[1] = Mathf.Max(0f, expressFlash[1] - dt);
            float switchSpeed = 1f / Mathf.Max(.05f, trackSwitchSeconds);
            float playerTrackBefore = trackPosition[0];
            float rivalTrackBefore = trackPosition[1];
            trackPosition[0] = Mathf.MoveTowards(trackPosition[0], targetTrack[0], switchSpeed * dt);
            trackPosition[1] = Mathf.MoveTowards(trackPosition[1], targetTrack[1], switchSpeed * dt);
            bool playerWasSwitching = !Mathf.Approximately(playerTrackBefore, targetTrack[0]);
            bool rivalWasSwitching = !Mathf.Approximately(rivalTrackBefore, targetTrack[1]);
            // Do not spend the collision cooldown while both cars are merely
            // sharing a floor during their boarding/door transition. The
            // impact must be caused by a car that is actually travelling or
            // changing tracks; the other car may be stopped and valid target.
            bool playerTravelling = player.MovingInShaft
                && Mathf.Abs(player.TravelVelocity) > .05f;
            bool rivalTravelling = Rival != null && Rival.MovingInShaft
                && Mathf.Abs(Rival.TravelVelocity) > .05f;
            if (Running && !Paused && (playerTravelling || rivalTravelling
                    || playerWasSwitching || rivalWasSwitching)
                && CarsOverlap(player, Rival))
            {
                trackPosition[0] = playerTrackBefore;
                trackPosition[1] = rivalTrackBefore;
                // A failed lane change must return to the track it came from.
                // With two tracks that is the opposite of the requested target;
                // using the current interpolation point would choose the wrong
                // track when the cars meet just past the midpoint.
                if (playerWasSwitching) targetTrack[0] = targetTrack[0] == 0 ? 1 : 0;
                if (rivalWasSwitching) targetTrack[1] = targetTrack[1] == 0 ? 1 : 0;
                TriggerCollision(player, Rival);
            }

            // The CPU reads the same visible junction sign as a player and
            // moves toward the express track shortly before reaching it.
            if (Running && !Paused && Rival != null && Rival.IsNpc
                && TryGetUpcomingJunction(Rival, out int npcJunction, out float npcDistance)
                && npcDistance < 1.15f)
            {
                int desired = expressTrack[npcJunction];
                int current = targetTrack[SeatOf(Rival)];
                if (desired != current) RequestTrack(Rival, desired > current ? 1 : -1);
            }

            if (Finished && resultRevealStartedAt < 0f)
            {
                GameManager.Instance?.RecordRoundResult(player.Points, Rival.Points);
                resultRevealStartedAt = Time.unscaledTime;
                player.PlayEndingDrum();
            }

            if (player.IntroPlaying || player.CountdownPlaying
                || Rival.CountdownPlaying || MenuManager.IsOpen) return;
            var input = InputManager.Instance;
            if (input == null) return;

            // The lobby already confirms the matchup, so begin immediately
            // after the intro instead of blocking on another tutorial popup.
            if (!Running)
            {
                StartMatch();
                return;
            }
            if (Finished && ResultRevealTime >= ResultRevealInputAt)
            {
                InputManager.SeatInput playerOne = input.ReadForSeat(0, true);
                InputManager.SeatInput playerTwo = input.ReadForSeat(1, true);
                if (playerOne.StartPressed || playerOne.ConfirmPressed
                    || playerTwo.StartPressed || playerTwo.ConfirmPressed)
                    ContinueMatch();
            }
            // Pause is owned by GameManager so InputManager only fires once.
        }

        // A rematch starts a fresh first-to-three series in the office.
        public void StartMatch()
        {
            GameManager.Instance?.ResetMatchProgress();
            BeginRound();
        }

        // A tied round advances the world without awarding either player a win.
        public void ContinueMatch()
        {
            if (!Finished) return;
            var session = GameManager.Instance;
            session?.RecordRoundResult(player.Points, Rival.Points);
            if (session == null || session.MatchFinished)
            {
                StartMatch();
                return;
            }
            session.AdvanceRound();
            BeginRound();
        }

        // Both cars receive a fresh clock, passenger pool and starting position.
        void BeginRound()
        {
            bool replayCountdown = Running;
            Paused = false;
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            dimensionRevealExitTime = -1f;
            for (int i = 0; i < dimensionRevealShown.Length; i++) dimensionRevealShown[i] = false;
            resultRevealStartedAt = -1f;
            targetTrack[0] = 0; targetTrack[1] = 1;
            trackPosition[0] = 0; trackPosition[1] = 1;
            collisionCooldown = collisionFlash = 0;
            collisionCount = 0;
            int firstExpressTrack = Random.Range(0, 2);
            expressTrack[0] = firstExpressTrack;
            expressTrack[1] = 1 - firstExpressTrack;
            expressTrack[2] = Random.Range(0, 2);
            usedJunctionMask[0] = usedJunctionMask[1] = 0;
            expressFlash[0] = expressFlash[1] = 0f;
            player.StartMatchShift(countdown: replayCountdown);
            Rival.StartMatchShift(player, countdown: replayCountdown);
            player.GetComponent<NpcElevator>()?.ResetDecisions();
            Rival.GetComponent<NpcElevator>()?.ResetDecisions();
            Running = true;
            Paused = false;
        }

        void TryBeginDimensionReveal()
        {
            if (!Running || player == null || dimensionRevealActive) return;
            float elapsed = ElevatorRound.Duration - player.SecondsLeft;
            for (int index = 1; index < ElevatorRound.Dimensions; index++)
            {
                if (dimensionRevealShown[index]
                    || elapsed < ElevatorRound.DimensionUnlockAt(index)) continue;
                dimensionRevealShown[index] = true;
                dimensionRevealIndex = index;
                dimensionRevealTime = 0f;
                dimensionRevealExitTime = -1f;
                dimensionRevealReady[0] = dimensionRevealReady[1] = false;
                dimensionRevealActive = true;
                player.BeginDimensionReveal(index);
                Rival?.BeginDimensionReveal(index);
                return;
            }
        }

        // The card stays up until every human player confirms; the CPU never blocks it.
        void UpdateDimensionReveal(float dt)
        {
            dimensionRevealTime += Mathf.Max(0f, dt);
            if (dimensionRevealExitTime < 0f)
            {
                ReadDimensionRevealConfirms();
                if (DimensionRevealEveryoneReady()) dimensionRevealExitTime = 0f;
                return;
            }
            dimensionRevealExitTime += Mathf.Max(0f, dt);
            if (dimensionRevealExitTime < Game.DimensionRevealFadeSeconds) return;
            player.FinishDimensionReveal();
            Rival?.FinishDimensionReveal();
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            dimensionRevealExitTime = -1f;
        }

        bool DimensionRevealSeatHuman(int seat) => seat == 0 ? humanPlayer : humanRival && Rival != null;

        void ReadDimensionRevealConfirms()
        {
            var input = InputManager.Instance;
            if (input == null || dimensionRevealTime < Game.DimensionRevealConfirmDelay) return;
            for (int seat = 0; seat < 2; seat++)
            {
                if (!DimensionRevealSeatHuman(seat) || dimensionRevealReady[seat]) continue;
                if (!input.ReadForSeat(seat, true).ConfirmPressed) continue;
                dimensionRevealReady[seat] = true;
                player.PlayClick();
            }
        }

        bool DimensionRevealEveryoneReady()
        {
            if (!DimensionRevealSeatHuman(0) && !DimensionRevealSeatHuman(1))
                return dimensionRevealTime >= Game.DimensionRevealSeconds;
            return (!DimensionRevealSeatHuman(0) || dimensionRevealReady[0])
                && (!DimensionRevealSeatHuman(1) || dimensionRevealReady[1]);
        }

        float ResultRevealTime => resultRevealStartedAt < 0f
            ? 0f : Time.unscaledTime - resultRevealStartedAt;

        public void TogglePause()
        {
            Paused = !Paused;
            player.SetMatchPaused(Paused);
            Rival.SetMatchPaused(Paused);
            // Tutorial pauses are managed by the tutorial cards themselves.
            if (MenuManager.TutorialActive) return;
            if (Paused)
            {
                MenuManager.OpenPause();
                GameManager.Instance?.GoToView(GameView.Pause);
            }
            else
            {
                MenuManager.ClosePause();
                GameManager.Instance?.SyncViewFromPlayer();
            }
        }

        void ConfigureCameras()
        {
            SetCameraRect(player, 0);
            SetCameraRect(Rival, 1);
            if (player.sceneView.clearCamera != null)
            {
                player.sceneView.clearCamera.gameObject.SetActive(IsVisibleSeat(0));
                player.sceneView.clearCamera.rect = CameraRect(0);
            }
            if (Rival.sceneView.clearCamera != null)
            {
                Rival.sceneView.clearCamera.gameObject.SetActive(IsVisibleSeat(1));
                Rival.sceneView.clearCamera.rect = CameraRect(1);
            }
        }

        void SetCameraRect(Game game, int seat)
        {
            Rect rect = CameraRect(seat);
            game.sceneView.cabinCamera.rect = rect;
            game.sceneView.exteriorCamera.rect = rect;
            game.sceneView.cabinCamera.enabled = IsVisibleSeat(seat);
            game.sceneView.exteriorCamera.enabled = IsVisibleSeat(seat);
            game.sceneView.cabinCamera.targetTexture = null;
            game.sceneView.exteriorCamera.targetTexture = null;
            float aspect = Mathf.Max(.1f, Screen.width * rect.width
                / Mathf.Max(1f, Screen.height * rect.height));
            game.sceneView.cabinCamera.aspect = aspect;
        }

        // Both duel tracks straddle the authored solo shaft at equal distances.
        public float ShaftX(int seat) => ShaftCenterX + (seat - .5f) * shaftSpacing;

        // Lane blend used while switching vertical shafts (not ground-track travel).
        public float LaneX(Game actor)
        {
            int seat = SeatOf(actor);
            return ShaftX(0) + Mathf.SmoothStep(0f, 1f, trackPosition[seat]) * shaftSpacing;
        }

        public float ShaftX(Game actor)
        {
            if (actor == null) return ShaftCenterX;
            // Cars own continuous lateral position so ground tracks can move
            // horizontally instead of only flipping between two vertical lanes.
            return actor.RouteLateralX;
        }

        public void SyncTrackFromLateral(Game actor, float lateralX)
        {
            if (actor == null) return;
            int seat = SeatOf(actor);
            int nearest = Mathf.Abs(lateralX - ShaftX(0)) <= Mathf.Abs(lateralX - ShaftX(1)) ? 0 : 1;
            targetTrack[seat] = nearest;
            trackPosition[seat] = nearest;
        }

        public int TrackNumber(Game actor) => targetTrack[SeatOf(actor)] + 1;

        public int JunctionCount => JunctionFloorPositions.Length;
        public int JunctionSignature => expressTrack[0] | expressTrack[1] << 1 | expressTrack[2] << 2;
        public float JunctionFloor(int index) => JunctionFloorPositions[Mathf.Clamp(index, 0, JunctionFloorPositions.Length - 1)];
        public int ExpressTrack(int index) => expressTrack[Mathf.Clamp(index, 0, expressTrack.Length - 1)];
        public float ExpressFlash(Game actor) => expressFlash[SeatOf(actor)];

        public bool TryGetUpcomingJunction(Game actor, out int junction, out float distance)
        {
            junction = -1;
            distance = float.MaxValue;
            if (!Running || actor == null || !actor.MovingInShaft) return false;
            float direction = Mathf.Sign(actor.TravelVelocity);
            if (Mathf.Abs(direction) < .5f) return false;
            int seat = SeatOf(actor);
            for (int i = 0; i < JunctionFloorPositions.Length; i++)
            {
                if ((usedJunctionMask[seat] & 1 << i) != 0) continue;
                float delta = JunctionFloorPositions[i] - actor.CurrentFloor;
                if (delta * direction <= 0f) continue;
                float candidate = Mathf.Abs(delta);
                if (candidate >= distance || candidate > 1.6f) continue;
                junction = i;
                distance = candidate;
            }
            return junction >= 0;
        }

        // Crossing the highlighted lane gives a readable reward without
        // punishing the safe route. Each junction can trigger once per car.
        public void EvaluateJunction(Game actor, float before, float after)
        {
            if (!Running || actor == null || Mathf.Approximately(before, after)) return;
            int seat = SeatOf(actor);
            bool movingUp = after > before;
            for (int i = 0; i < JunctionFloorPositions.Length; i++)
            {
                int bit = 1 << i;
                if ((usedJunctionMask[seat] & bit) != 0) continue;
                float gate = JunctionFloorPositions[i];
                bool crossed = movingUp ? before < gate && after >= gate : before > gate && after <= gate;
                if (!crossed) continue;
                usedJunctionMask[seat] |= bit;
                if (targetTrack[seat] != expressTrack[i]) continue;
                // Teleports are the visible gates on the floor rails (ElevatorManager.TeleportGates).
                actor.ApplyExpressJunction();
                expressFlash[seat] = 1.15f;
            }
        }

        public void RequestTrack(Game actor, int direction)
        {
            if (!Running || Paused || actor == null || !actor.MovingInShaft || direction == 0) return;
            int seat = SeatOf(actor);
            int requested = Mathf.Clamp(targetTrack[seat] + (direction > 0 ? 1 : -1), 0, 1);
            if (requested == targetTrack[seat]) return;

            targetTrack[seat] = requested;
        }

        // Prevent cars in the same track from passing through each other. A
        // player can escape the block with A/D; the NPC automatically tries the
        // free track after it reaches a blocker.
        public float ConstrainTravel(Game actor, float before, float candidate, ref float velocity)
        {
            if (!Running || actor == null) return candidate;
            Game other = Other(actor);
            if (other == null || !HorizontallyOverlaps(actor, other)) return candidate;

            // At a shaft boundary the clamped candidate can equal `before`
            // even though the car is still pushing toward the other car.
            // Keep the signed velocity so a stopped boarding elevator at that
            // floor can still be rammed and knocked away.
            float direction = Mathf.Sign(candidate - before);
            if (direction == 0f) direction = Mathf.Sign(velocity);
            if (direction == 0) return candidate;
            // Opening/boarding cars are in the cabin view, but their physical
            // collision position is the docked passenger floor, not a stale
            // travel interpolation value.
            float otherFloor = other.CollisionFloor;
            bool approaching = direction > 0 ? before <= otherFloor : before >= otherFloor;
            if (!approaching) return candidate;

            float contactDistance = VerticalContactDistance(first: actor, second: other);
            float boundary = otherFloor - direction * contactDistance;
            bool blocked = direction > 0 ? candidate >= boundary : candidate <= boundary;
            if (!blocked) return candidate;

            float closingSpeed = Mathf.Abs(velocity - other.TravelVelocity);
            candidate = Mathf.Clamp(boundary, actor.ActiveMinFloor, actor.ActiveMaxFloor);
            velocity = 0f;
            TriggerVerticalCollision(actor, other, direction, closingSpeed);
            if (actor.IsNpc)
                RequestTrack(actor, targetTrack[SeatOf(actor)] == 0 ? 1 : -1);
            return candidate;
        }

        bool HorizontallyOverlaps(Game first, Game second)
        {
            float halfWidths = first.ExteriorCollisionHalfSize.x + second.ExteriorCollisionHalfSize.x;
            return Mathf.Abs(ShaftX(first) - ShaftX(second)) <= halfWidths + collisionPadding;
        }

        float VerticalContactDistance(Game first, Game second)
        {
            float worldDistance = first.ExteriorCollisionHalfSize.y
                + second.ExteriorCollisionHalfSize.y + collisionPadding;
            return worldDistance / Game.ShaftFloorHeight;
        }

        bool CarsOverlap(Game first, Game second)
        {
            if (first == null || second == null || !HorizontallyOverlaps(first, second)) return false;
            float verticalDistance = Mathf.Abs(first.CollisionFloor - second.CollisionFloor)
                * Game.ShaftFloorHeight;
            float combinedHalfHeights = first.ExteriorCollisionHalfSize.y
                + second.ExteriorCollisionHalfSize.y + collisionPadding;
            return verticalDistance <= combinedHalfHeights;
        }

        // A lane-change impact can also knock a stopped car away from its
        // floor. Use the moving car's direction so boarding is not a safe
        // shield against a collision.
        void TriggerCollision(Game first, Game second)
        {
            float direction = 0f;
            if (first != null && Mathf.Abs(first.TravelVelocity) > .05f)
                direction = Mathf.Sign(first.TravelVelocity);
            else if (second != null && Mathf.Abs(second.TravelVelocity) > .05f)
                direction = -Mathf.Sign(second.TravelVelocity);
            float closingSpeed = first != null && second != null
                ? Mathf.Abs(first.TravelVelocity - second.TravelVelocity) : 0f;
            PlayCollision(first, second, direction, closingSpeed,
                TutorialCollisionTarget(first, second));
        }

        void TriggerVerticalCollision(Game first, Game second, float direction, float closingSpeed)
            // ConstrainTravel calls this with the moving elevator first and
            // the elevator it hit second. Keep that target stationary during
            // tutorials so a collision cannot create tutorial momentum.
            => PlayCollision(first, second, direction, closingSpeed,
                MenuManager.TutorialActive ? second : null);

        void PlayCollision(Game first, Game second, float verticalDirection,
            float closingSpeed, Game tutorialTarget = null)
        {
            if (collisionCooldown > 0f) return;
            collisionCooldown = .48f;
            collisionFlash = .72f;
            collisionCount++;
            // A tutorial collision can still show feedback and stop the
            // incoming elevator, but it must not move the elevator that was
            // hit. Otherwise a passenger-view tutorial card can unexpectedly
            // become an elevator-view card and a stopped car gains momentum.
            if (verticalDirection != 0f)
            {
                float rebound = Mathf.Clamp(closingSpeed * collisionBounceStrength, 1.05f, 2.4f);
                if (first != tutorialTarget && !TutorialInputLocked(first))
                    ApplyCollisionResponse(first, -verticalDirection * rebound);
                if (second != tutorialTarget && !TutorialInputLocked(second))
                    ApplyCollisionResponse(second, verticalDirection * rebound);
            }
            else
            {
                // A lane-change bump still jolts both cars, but does not reverse their vertical travel.
                if (first != tutorialTarget && !TutorialInputLocked(first))
                    first?.ApplyMatchCollision(collisionSlowSeconds, collisionBounceStrength);
                if (second != tutorialTarget && !TutorialInputLocked(second))
                    second?.ApplyMatchCollision(collisionSlowSeconds, collisionBounceStrength);
            }
            if (first != null && second != null)
                EmitCollisionSparks((first.ShaftWorldPosition + second.ShaftWorldPosition) * .5f);
            SfxManager.Instance?.PlaySfx(collisionClip, 1f);
        }

        static Game TutorialCollisionTarget(Game first, Game second)
        {
            if (!MenuManager.TutorialActive || first == null || second == null)
                return null;
            if (first.TutorialPassengerViewActive != second.TutorialPassengerViewActive)
                return first.TutorialPassengerViewActive ? first : second;

            bool firstMoving = first.MovingInShaft && Mathf.Abs(first.TravelVelocity) > .05f;
            bool secondMoving = second.MovingInShaft && Mathf.Abs(second.TravelVelocity) > .05f;
            if (firstMoving != secondMoving)
                return firstMoving ? second : first;
            if (first.InPassengerStop != second.InPassengerStop)
                return first.InPassengerStop ? first : second;
            return null;
        }

        static bool TutorialInputLocked(Game actor)
            => actor != null && InputManager.Instance != null
                && InputManager.Instance.IsTutorialInputLocked(actor.Seat);

        static bool TutorialStartingFloorStationary(Game actor)
            => actor != null && MenuManager.TutorialActive
                && actor.DiscreteFloor == 0
                && Mathf.Abs(actor.TravelVelocity) <= .05f;

        void ApplyCollisionResponse(Game actor, float reboundVelocity)
        {
            if (actor == null) return;
            // The opening tutorial position is a stationary staging point. It
            // can still be hit, but must not inherit a rebound or momentum.
            if (TutorialStartingFloorStationary(actor))
                actor.ApplyMatchCollision(collisionSlowSeconds, 1f);
            else actor.ApplyMatchBounce(collisionSlowSeconds, reboundVelocity);
        }

        ParticleSystem CreateCollisionParticles(Transform parent)
        {
            var visual = new GameObject("Collision sparks");
            visual.SetActive(false);
            visual.layer = 31;
            visual.transform.SetParent(parent, false);
            var particles = visual.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = .45f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.22f, .48f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .18f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color32(255, 226, 72, 255), new Color32(255, 104, 28, 255));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            particleRenderer.velocityScale = .2f;
            particleRenderer.lengthScale = 2.8f;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                collisionParticleMaterial = new Material(shader) { name = "Runtime collision sparks" };
                particleRenderer.sharedMaterial = collisionParticleMaterial;
            }
            visual.SetActive(true);
            return particles;
        }

        void EmitCollisionSparks(Vector3 position)
        {
            if (collisionParticles == null) return;
            for (int i = 0; i < 28; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(3.5f, 8.5f);
                var spark = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed,
                    startLifetime = Random.Range(.22f, .48f),
                    startSize = Random.Range(.07f, .17f),
                    startColor = Color.Lerp(
                        new Color32(255, 226, 72, 255),
                        new Color32(255, 104, 28, 255), Random.value)
                };
                collisionParticles.Emit(spark, 1);
            }
        }

        int SeatOf(Game actor) => actor == Rival ? 1 : 0;
        Game Other(Game actor) => actor == Rival ? player : Rival;

        static AudioClip CreateCollisionClip()
        {
            const int rate = 22050;
            const float seconds = .34f;
            var data = new float[Mathf.CeilToInt(rate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 11f);
                float thud = Mathf.Sin(2f * Mathf.PI * (115f - 55f * t) * t) * .62f;
                float metal = Mathf.Sin(2f * Mathf.PI * 760f * t) * Mathf.Exp(-t * 19f) * .19f;
                float noise = (Mathf.Repeat(Mathf.Sin(i * 78.233f) * 43758.5453f, 1f) * 2f - 1f) * .16f;
                data[i] = Mathf.Clamp((thud + metal + noise) * envelope, -.9f, .9f);
            }
            var clip = AudioClip.Create("Elevator track collision", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDestroy()
        {
            foreach (var rail in soloRails)
                if (rail != null) rail.gameObject.SetActive(true);
            if (collisionClip != null) Destroy(collisionClip);
            if (collisionParticles != null) Destroy(collisionParticles.gameObject);
            if (collisionParticleMaterial != null) Destroy(collisionParticleMaterial);
            if (resultGlowTexture != null) Destroy(resultGlowTexture);
            if (Rival != null) Destroy(Rival.gameObject);
            if (rivalView != null && rivalView.exteriorCar != null) Destroy(rivalView.exteriorCar.gameObject);
            if (rivalShaft != null) Destroy(rivalShaft.gameObject);
            if (rivalView != null) Destroy(rivalView.gameObject);
        }
    }
}

// --- MatchScreens ---
namespace CrazyElevator.Match
{
    public sealed partial class ElevatorMatch
    {
        // Only two human players need separate camera panes.
        public Rect ViewRect(int seat)
        {
            if (!SplitScreen) return new Rect(0, 0, Screen.width, Screen.height);
            float paneWidth = Screen.width * .5f;
            return new Rect(seat * paneWidth + (seat == 1 ? 2 : 0), 0,
                Mathf.Max(1, paneWidth - 2), Mathf.Max(1, Screen.height));
        }

        public Rect CameraRect(int seat)
        {
            return SplitScreen ? new Rect(seat * .5f, 0, .5f, 1) : new Rect(0, 0, 1, 1);
        }
    }
}

// --- MatchHUD ---
namespace CrazyElevator.Match
{
    public sealed partial class ElevatorMatch
    {
        static readonly Color Teal = new Color32(75, 226, 202, 255);
        static readonly Color Coral = new Color32(255, 124, 104, 255);
        static readonly Color Gold = new Color32(255, 205, 82, 255);
        static readonly Color Ink = new Color32(25, 31, 46, 255);
        [System.NonSerialized] GUIStyle heading, text, score, centred, tileCaption, tileValue, collisionWord,
            resultTitle, resultName, resultPoints, resultWinner, roundStatus;
        [System.NonSerialized] Texture2D resultGlowTexture;

        void OnGUI()
        {
            if (player == null || Rival == null) return;
            if (player.CountdownPlaying || Rival.CountdownPlaying) return;
            if (MenuManager.FullScreenOverlayActive) return;
            var previousMatrix = GUI.matrix;
            var previousColour = GUI.color;
            var previousContent = GUI.contentColor;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GameTypography.ApplyToSkin(GUI.skin);
            MatchStyles();
            if (player.IntroPlaying)
            {
                player.DrawMatchIntroGUI();
                DrawSessionFade(player.SessionFadeAlpha);
                GUI.matrix = previousMatrix;
                GUI.color = previousColour;
                GUI.contentColor = previousContent;
                return;
            }
            if (dimensionRevealActive)
            {
                DrawDimensionReveal();
                GUI.matrix = previousMatrix;
                GUI.color = previousColour;
                GUI.contentColor = previousContent;
                return;
            }
            if (IsVisibleSeat(0)) DrawSeat(player, 0, Coral);
            if (IsVisibleSeat(1)) DrawSeat(Rival, 1, Teal);
            if (SplitScreen) DrawSplitDivider();
            DrawCollisionFeedback();
            if (Finished) DrawResultReveal();
            else if (Paused && !MenuManager.TutorialActive) DrawPauseOverlay();
            DrawSessionFade(player.SessionFadeAlpha);
            GUI.matrix = previousMatrix;
            GUI.color = previousColour;
            GUI.contentColor = previousContent;
        }

        void DrawSessionFade(float alpha)
        {
            if (alpha <= .001f) return;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void MatchStyles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            GameTypography.Apply(text);
            text.normal.textColor = Color.white;
            heading = new GUIStyle(text) { fontSize = 18, fontStyle = FontStyle.Bold };
            GameTypography.Apply(heading, true);
            score = new GUIStyle(heading) { fontSize = 25 };
            GameTypography.Apply(score, true);
            centred = new GUIStyle(heading) { alignment = TextAnchor.MiddleCenter };
            GameTypography.Apply(centred, true);
            roundStatus = new GUIStyle(centred) { fontSize = 13, wordWrap = false };
            GameTypography.Apply(roundStatus, true);
            tileCaption = new GUIStyle(text) { fontSize = 11, fontStyle = FontStyle.Bold };
            GameTypography.Apply(tileCaption, true);
            tileCaption.normal.textColor = new Color(1f, 1f, 1f, .72f);
            tileValue = new GUIStyle(score) { fontSize = 22 };
            GameTypography.Apply(tileValue, true);
            tileValue.normal.textColor = Color.white;
            collisionWord = new GUIStyle(centred) { fontSize = 24, clipping = TextClipping.Overflow };
            GameTypography.Apply(collisionWord, true);
            collisionWord.normal.textColor = Color.white;
            resultTitle = new GUIStyle(centred) { fontSize = 30 };
            GameTypography.Apply(resultTitle, true);
            resultTitle.normal.textColor = Color.white;
            resultName = new GUIStyle(centred) { fontSize = 17 };
            GameTypography.Apply(resultName, true);
            resultName.normal.textColor = Color.white;
            resultPoints = new GUIStyle(centred) { fontSize = 58 };
            GameTypography.Apply(resultPoints, true);
            resultPoints.normal.textColor = Color.white;
            resultWinner = new GUIStyle(centred) { fontSize = 38, clipping = TextClipping.Overflow };
            GameTypography.Apply(resultWinner, true);
            resultWinner.normal.textColor = Color.white;
        }

        static void Fill(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawDimensionReveal()
        {
            float contentAlpha = Game.DimensionRevealAlpha(dimensionRevealTime, dimensionRevealExitTime);
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), Color.black);
            if (contentAlpha <= .001f) return;

            WorldBand world = Game.DimensionWorld(dimensionRevealIndex);
            Color accent = world == WorldBand.Candy ? new Color32(236, 118, 188, 255)
                : new Color32(48, 196, 210, 255);
            float width = Mathf.Min(Screen.width - 72f, 1120f);
            float height = Mathf.Min(Screen.height - 96f, 720f);
            Rect card = new Rect((Screen.width - width) * .5f,
                (Screen.height - height) * .5f, width, height);
            Fill(new Rect(card.x + 12f, card.y + 14f, card.width, card.height),
                new Color(0f, 0f, 0f, .42f * contentAlpha));
            Fill(card, new Color(Ink.r, Ink.g, Ink.b, contentAlpha));
            Fill(new Rect(card.x, card.y, card.width, 6f),
                new Color(accent.r, accent.g, accent.b, contentAlpha));

            Texture preview = player != null ? player.DimensionRevealTexture : null;
            if (preview != null)
            {
                Rect image = new Rect(card.x + 26f, card.y + 18f,
                    card.width - 52f, card.height * .29f);
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, contentAlpha);
                GUI.DrawTexture(image, preview, ScaleMode.ScaleToFit, true);
                GUI.color = previous;
            }

            Color previousText = heading.normal.textColor;
            int previousHeadingSize = heading.fontSize;
            TextAnchor previousAlign = heading.alignment;
            heading.alignment = TextAnchor.MiddleCenter;
            heading.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * .04f, 32f, 58f));
            heading.normal.textColor = new Color(1f, 1f, 1f, contentAlpha);
            GUI.Label(new Rect(card.x + 24f, card.y + card.height * .35f,
                card.width - 48f, 56f), world == WorldBand.Candy ? "COTTON CANDY" : "UNDERWATER", heading);

            string[] names;
            string[] lines;
            if (world == WorldBand.Candy)
            {
                names = new[] { "MOM + BABY", "THE BOSS", "THE TRIO" };
                lines = new[]
                {
                    "Crying drains patience. Grandma calms the baby.",
                    "On-time delivery pays a bonus.",
                    "Three spaces. Speeds the elevator up."
                };
            }
            else
            {
                names = new[] { "HANDYMAN", "RUST", "REPAIR" };
                lines = new[]
                {
                    "Clears rust while aboard.",
                    "Slows the elevator over time.",
                    "Hold to patch it back up."
                };
            }

            float rowTop = card.y + card.height * .46f;
            float rowWidth = (card.width - 80f) / 3f;
            int nameSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * .02f, 18f, 28f));
            Color previousBody = text.normal.textColor;
            TextAnchor previousBodyAlign = text.alignment;
            int previousBodySize = text.fontSize;
            bool previousWrap = text.wordWrap;
            text.alignment = TextAnchor.UpperCenter;
            text.wordWrap = true;
            for (int i = 0; i < 3; i++)
            {
                Rect row = new Rect(card.x + 28f + i * (rowWidth + 12f), rowTop, rowWidth, card.height * .40f);
                Fill(row, new Color(accent.r, accent.g, accent.b, .22f * contentAlpha));
                heading.fontSize = nameSize;
                heading.normal.textColor = new Color(1f, 1f, 1f, contentAlpha);
                GUI.Label(new Rect(row.x + 8f, row.y + 12f, row.width - 16f, 34f), names[i], heading);
                text.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * .014f, 14f, 20f));
                text.normal.textColor = new Color(1f, 1f, 1f, .9f * contentAlpha);
                GUI.Label(new Rect(row.x + 12f, row.y + 48f, row.width - 24f, 70f), lines[i], text);
                player?.DrawDimensionPassenger(new Rect(row.x, row.y + 120f, row.width, row.height - 124f),
                    i, contentAlpha);
            }
            float rowBottom = rowTop + card.height * .40f;
            DrawDimensionRevealPrompt(new Rect(card.x, rowBottom + 4f, card.width, card.yMax - rowBottom - 8f),
                nameSize, contentAlpha);

            heading.fontSize = previousHeadingSize;
            heading.normal.textColor = previousText;
            heading.alignment = previousAlign;
            text.fontSize = previousBodySize;
            text.normal.textColor = previousBody;
            text.alignment = previousBodyAlign;
            text.wordWrap = previousWrap;
        }

        void DrawDimensionRevealPrompt(Rect line, int size, float contentAlpha)
        {
            if (dimensionRevealExitTime >= 0f) return;
            float blink = Game.DimensionPromptAlpha(dimensionRevealTime);
            if (blink <= 0f) return;
            heading.fontSize = size;
            if (SplitScreen)
            {
                Rect left = new Rect(line.x, line.y, line.width * .5f, line.height);
                Rect right = new Rect(line.center.x, line.y, line.width * .5f, line.height);
                DrawSeatReady(left, 0, "P1  PRESS C / A", Coral, contentAlpha, blink);
                DrawSeatReady(right, 1, "P2  PRESS . / A", Teal, contentAlpha, blink);
                return;
            }
            if (!DimensionRevealSeatHuman(0) && !DimensionRevealSeatHuman(1)) return;
            string prompt = DimensionRevealSeatHuman(0) ? "PRESS C / A TO CONTINUE" : "PRESS . / A TO CONTINUE";
            heading.normal.textColor = new Color(1f, .82f, .24f, contentAlpha * blink);
            GUI.Label(line, prompt, heading);
        }

        void DrawSeatReady(Rect area, int seat, string prompt, Color colour, float contentAlpha, float blink)
        {
            bool ready = dimensionRevealReady[seat];
            heading.normal.textColor = new Color(colour.r, colour.g, colour.b, contentAlpha * (ready ? 1f : blink));
            GUI.Label(area, ready ? "P" + (seat + 1) + "  READY!" : prompt, heading);
        }

        void DrawSplitDivider()
        {
            // Keep the split obvious while either cabin is on screen; hide it
            // when both players are looking at their full shaft-travel views.
            if (player.Traveling && Rival.Traveling) return;

            const float width = 12f;
            float x = Screen.width * .5f;
            Fill(new Rect(x - width * .5f, 0, width, Screen.height), Ink);
            Fill(new Rect(x - width * .5f, 0, 2f, Screen.height), Teal);
            Fill(new Rect(x + width * .5f - 2f, 0, 2f, Screen.height), Coral);
        }

        void DrawSeat(Game actor, int seat, Color colour)
        {
            Rect view = ViewRect(seat);
            actor.DrawMatchViewGUI();
            DrawPlayerScreenBorder(view, colour);
            int seconds = Mathf.CeilToInt(player.SecondsLeft);
            string clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            float barWidth = Mathf.Max(1f, Mathf.Min(view.width - 16f,
                SplitScreen ? view.width : 760f));
            var bar = new Rect(view.x + 8f, view.y + 8f, barWidth, 58f);
            Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 26f), Color.white);
            Fill(new Rect(bar.x, bar.y, bar.width, bar.height + 22f),
                new Color(9f / 255f, 11f / 255f, 16f / 255f, .96f));
            float tileWidth = bar.width / 3f;
            DrawSeatTile(new Rect(bar.x, bar.y, tileWidth, bar.height), colour,
                (actor.IsNpc ? "CPU " : "PLAYER ") + (seat + 1) + " SCORE", actor.Points.ToString());
            DrawSeatTile(new Rect(bar.x + tileWidth, bar.y, tileWidth, bar.height),
                colour, "FLOOR", Mathf.RoundToInt(actor.CurrentFloor).ToString());
            DrawSeatTile(new Rect(bar.x + tileWidth * 2f, bar.y, tileWidth, bar.height),
                new Color32(112, 183, 255, 255), "TIME", clock);
            var session = GameManager.Instance;
            if (session != null)
                GUI.Label(new Rect(bar.x, bar.yMax, bar.width, 22f),
                    "ROUND " + session.RoundNumber + " / " + session.ActiveDimensionName
                    + "  ·  WINS " + session.PlayerOneWins + " - " + session.PlayerTwoWins
                    + "  ·  FIRST TO " + GameManager.RoundsToWin, roundStatus);
        }

        void DrawPlayerScreenBorder(Rect view, Color playerColour)
        {
            const float thickness = 6f;
            Color border = new Color(playerColour.r, playerColour.g, playerColour.b, .42f);

            Fill(new Rect(view.x, view.y, view.width, thickness), border);
            Fill(new Rect(view.x, view.yMax - thickness, view.width, thickness), border);
            Fill(new Rect(view.x, view.y + thickness, thickness, Mathf.Max(0f, view.height - thickness * 2f)), border);
            Fill(new Rect(view.xMax - thickness, view.y + thickness, thickness,
                Mathf.Max(0f, view.height - thickness * 2f)), border);
        }

        void DrawSeatTile(Rect area, Color accent, string caption, string value)
        {
            Fill(new Rect(area.x, area.yMax - 5f, area.width, 5f), accent);
            Fill(new Rect(area.x + 5f, area.y + 7f, 5f, area.height - 19f), accent);
            GUI.Label(new Rect(area.x + 16f, area.y + 3f, area.width - 18f, 18f), caption, tileCaption);
            GUI.Label(new Rect(area.x + 16f, area.y + 19f, area.width - 18f, 30f), value, tileValue);
        }

        void DrawCollisionFeedback()
        {
            if (collisionFlash <= 0f) return;
            float alpha = Mathf.Clamp01(collisionFlash / .62f);
            Color flash = new Color(1f, .35f, .18f, alpha * .9f);
            const float edge = 7f;
            Fill(new Rect(0, 0, Screen.width, edge), flash);
            Fill(new Rect(0, Screen.height - edge, Screen.width, edge), flash);
            Fill(new Rect(0, 0, edge, Screen.height), flash);
            Fill(new Rect(Screen.width - edge, 0, edge, Screen.height), flash);
            float width = Mathf.Min(430f, Screen.width - 24f);
            Rect banner = new Rect((Screen.width - width) * .5f, 102f, width, 44f);
            Fill(banner, new Color(.04f, .05f, .08f, alpha * .96f));
            Fill(new Rect(banner.x, banner.yMax - 5f, banner.width, 5f), flash);
            GUI.Label(banner, "BOUNCE!  SWITCH TRACKS", collisionWord);
        }

        void DrawResultReveal()
        {
            float time = ResultRevealTime;
            float curtain = Reveal01(time / .85f);
            float panelWidth = Screen.width * .5f * curtain;
            Fill(new Rect(0, 0, panelWidth, Screen.height), Color.black);
            Fill(new Rect(Screen.width - panelWidth, 0, panelWidth, Screen.height), Color.black);
            if (time < .85f) return;

            Fill(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            float light = Reveal01((time - 1.35f) / .8f);
            if (light > 0f)
            {
                EnsureResultGlow();
                float glowWidth = Mathf.Min(Screen.width * .82f, 960f);
                float glowHeight = Mathf.Min(Screen.height * .9f, 760f);
                Rect glow = new Rect((Screen.width - glowWidth) * .5f,
                    Screen.height * .04f, glowWidth, glowHeight);
                Color previous = GUI.color;
                GUI.color = new Color(1f, .82f, .48f, .74f * light);
                GUI.DrawTexture(glow, resultGlowTexture, ScaleMode.StretchToFill, true);
                GUI.color = previous;
            }

            float titleIn = Reveal01((time - 2.05f) / .45f);
            var session = GameManager.Instance;
            bool matchFinished = session != null && session.MatchFinished;
            DrawRevealLabel(new Rect(0, Screen.height * .18f, Screen.width, 46f),
                session == null ? "ROUND SCORES" : "ROUND " + session.RoundNumber + " / "
                    + session.ActiveDimensionName, resultTitle, titleIn, Color.white);

            string rivalName = Rival.IsNpc ? "CPU" : "PLAYER 2";
            float cardWidth = Mathf.Min(290f, Screen.width * .32f);
            float gap = Mathf.Min(28f, Screen.width * .035f);
            float cardsWidth = cardWidth * 2f + gap;
            float cardY = Screen.height * .34f;
            Rect playerCard = new Rect((Screen.width - cardsWidth) * .5f, cardY, cardWidth, 142f);
            Rect rivalCard = new Rect(playerCard.xMax + gap, cardY, cardWidth, 142f);
            DrawRevealScoreCard(playerCard, player.IsNpc ? "CPU" : "PLAYER 1", player.Points, Teal,
                Reveal01((time - 2.65f) / .75f));
            DrawRevealScoreCard(rivalCard, rivalName, Rival.Points, Coral,
                Reveal01((time - 3.55f) / .75f));

            bool draw = player.Points == Rival.Points;
            bool playerWon = player.Points > Rival.Points;
            string playerName = player.IsNpc ? "CPU 1" : "PLAYER 1";
            string winnerName = playerWon ? playerName : rivalName;
            string winner = draw ? "DRAW / NO ROUND WIN"
                : winnerName + (matchFinished ? " WINS THE MATCH!" : " WINS THE ROUND!");
            Color winnerColour = draw ? Gold : playerWon ? Teal : Coral;
            float winnerIn = Reveal01((time - 4.55f) / .6f);
            DrawRevealLabel(new Rect(0, cardY + 168f, Screen.width, 56f),
                winner, resultWinner, winnerIn, winnerColour);
            if (session != null)
                DrawRevealLabel(new Rect(0, cardY + 218f, Screen.width, 28f),
                    "ROUND WINS " + session.PlayerOneWins + " - " + session.PlayerTwoWins
                    + "  /  FIRST TO " + GameManager.RoundsToWin
                    + (matchFinished ? "" : "  /  NEXT: " + session.NextDimensionName),
                    resultName, winnerIn, Color.white);

            if (time < ResultRevealInputAt) return;
            float buttonWidth = Mathf.Min(440f, Screen.width - 40f);
            Rect button = new Rect((Screen.width - buttonWidth) * .5f,
                Mathf.Min(Screen.height - 62f, cardY + 260f), buttonWidth, 40f);
            Fill(button, Teal);
            string prompt = matchFinished ? "PLAY AGAIN / ENTER" : "NEXT ROUND / ENTER";
            if (GUI.Button(button, prompt, centred)) ContinueMatch();
        }

        void DrawRevealScoreCard(Rect area, string name, int points, Color accent, float reveal)
        {
            if (reveal <= 0f) return;
            area.y += Mathf.Lerp(26f, 0f, reveal);
            Fill(area, new Color(Ink.r, Ink.g, Ink.b, .96f * reveal));
            Fill(new Rect(area.x, area.y, area.width, 5f),
                new Color(accent.r, accent.g, accent.b, reveal));
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, reveal);
            GUI.Label(new Rect(area.x + 10f, area.y + 15f, area.width - 20f, 30f), name, resultName);
            int shownPoints = Mathf.RoundToInt(Mathf.Lerp(0f, points, reveal));
            GUI.Label(new Rect(area.x + 10f, area.y + 48f, area.width - 20f, 78f),
                shownPoints.ToString(), resultPoints);
            GUI.color = previous;
        }

        void DrawRevealLabel(Rect area, string value, GUIStyle style, float reveal, Color colour)
        {
            if (reveal <= 0f) return;
            area.y += Mathf.Lerp(14f, 0f, reveal);
            Color previous = GUI.color;
            GUI.color = new Color(colour.r, colour.g, colour.b, colour.a * reveal);
            GUI.Label(area, value, style);
            GUI.color = previous;
        }

        static float Reveal01(float value)
            => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));

        void EnsureResultGlow()
        {
            if (resultGlowTexture != null) return;
            const int size = 128;
            resultGlowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Result spotlight glow",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f;
                float dy = (y + .5f) / size * 2f - 1f;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.7f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            resultGlowTexture.SetPixels(pixels);
            resultGlowTexture.Apply(false, true);
        }

        void DrawPauseOverlay()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, .78f));
            float width = Mathf.Min(620, Screen.width - 32), height = Mathf.Min(310, Screen.height - 32);
            var box = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            Fill(box, Ink);
            GUI.Label(new Rect(box.x + 20, box.y + 20, width - 40, 40), "BOTH ELEVATORS PAUSED", centred);
            GUI.Label(new Rect(box.x + 24, box.y + 72, width - 48, height - 130),
                "Both elevators are paused.\n\nResume when you're ready.", text);
            var lobbyButton = new Rect(box.x + 24, box.yMax - 94, (width - 60) * .5f, 34);
            var menuButton = new Rect(lobbyButton.xMax + 12, lobbyButton.y, lobbyButton.width, 34);
            if (MenuManager.PauseSelection == 1)
                Fill(new Rect(lobbyButton.x - 3, lobbyButton.y - 3, lobbyButton.width + 6, lobbyButton.height + 6), Color.white);
            if (MenuManager.PauseSelection == 2)
                Fill(new Rect(menuButton.x - 3, menuButton.y - 3, menuButton.width + 6, menuButton.height + 6), Color.white);
            Fill(lobbyButton, Coral);
            Fill(menuButton, Gold);
            if (GUI.Button(lobbyButton, "BACK TO LOBBY")) MenuManager.ReturnToLobby();
            if (GUI.Button(menuButton, "MAIN MENU")) MenuManager.ReturnToMainMenu();
            var resumeButton = new Rect(box.x + 24, box.yMax - 52, width - 48, 34);
            if (MenuManager.PauseSelection == 0)
                Fill(new Rect(resumeButton.x - 3, resumeButton.y - 3, resumeButton.width + 6, resumeButton.height + 6), Color.white);
            Fill(resumeButton, Teal);
            if (GUI.Button(resumeButton, "RESUME BOTH")) TogglePause();
        }
    }
}
