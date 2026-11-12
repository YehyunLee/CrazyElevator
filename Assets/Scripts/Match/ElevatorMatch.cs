using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using CrazyElevator.Managers;
using CrazyElevator.Shared;
using Game = CrazyElevator.Managers.ElevatorManager;
using SharedRider = CrazyElevator.Shared.Rider;

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
        [Header("Elevator trim materials")]
        [SerializeField] Material playerOneCarTrim;
        [SerializeField] Material playerTwoCarTrim;
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
        bool collisionContactActive;
        bool bouncerCollision;
        const int BumpRewardPoints = 50;
        SharedRider matchVip;
        bool hazardEventStarted;
        bool vipRevealStarted;
        float eventMessageTime;
        string eventMessage;
        readonly float[] candyCooldown = { 5f, 5f };
        readonly List<CandyTrail> candyTrails = new List<CandyTrail>();
        sealed class CandyTrail
        {
            public int Seat;
            public Vector3 Position;
            public float Life;
        }
        readonly string[] contestMessages = new string[2];
        readonly float[] contestMessageTimes = new float[2];
        int collisionCount;
        Game tutorialCollisionAttacker, tutorialCollisionTarget;
        bool tutorialCollisionCompleted;
        float tutorialCollisionStartFloor;
        static readonly float[] JunctionFloorPositions = { 5.5f, 12.5f, 19.5f };
        readonly int[] expressTrack = { 0, 1, 0 };
        readonly int[] usedJunctionMask = { 0, 0 };
        readonly float[] expressFlash = { 0f, 0f };
        const float ResultRevealInputAt = 5.7f;
        float resultRevealStartedAt = -1f;
        int resultActionSelection;
        bool resultActionMoveHeld;
        float resultActionMoveRepeatTimer;
        AudioClip collisionClip;
        ParticleSystem collisionParticles;
        Material collisionParticleMaterial;
        bool dimensionRevealActive;
        int dimensionRevealIndex = -1;
        float dimensionRevealTime;
        public bool DimensionRevealActive => dimensionRevealActive;

        void Awake()
        {
            // Main also hosts the title screen; the match activates after both
            // local players confirm the lobby.
            if (!MenuManager.ConsumeDuelRequest())
            {
                enabled = false;
                return;
            }
            // Every playable match has two human seats. Old menu flags must
            // never silently turn a disconnected player into a CPU.
            MenuManager.ConsumePlayerOneCpuRequest();
            MenuManager.ConsumePlayerTwoRequest();
            humanPlayer = humanRival = true;

            if (player == null || player.sceneView == null)
            {
                Debug.LogError("Assign the player elevator to Elevator Match.", this);
                enabled = false;
                return;
            }
            player.ConfigureMatch(this, 0, false);
            CreateRival();
            collisionClip = CreateCollisionClip();
            collisionParticles = CreateCollisionParticles(player.sceneView.exteriorCar.parent);
        }

        // Reuse authored prefabs, not a second copy of the whole environment.
        void CreateRival()
        {
            var source = player.sceneView;
            rivalView = Instantiate(source);
            rivalView.name = "Player 2 elevator scene";
            Transform duplicateWorld = rivalView.exteriorCar.parent;
            rivalView.exteriorCar.SetParent(source.exteriorCar.parent, false);
            rivalView.exteriorCar.name = "Player 2 exterior car";
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
            var actor = new GameObject("Player 2 elevator controller");
            actor.SetActive(false);
            Rival = actor.AddComponent<Game>();
            actor.AddComponent<PassengerManager>().Bind(Rival);
            Rival.sceneView = rivalView;
            player.CopyMatchSettingsTo(Rival);
            Rival.ConfigureMatch(this, 1, false);
            actor.SetActive(true);
            ApplyCarTrim(source, playerOneCarTrim);
            ApplyCarTrim(rivalView, playerTwoCarTrim);
            ConfigureCameras();
        }

        // The cabin prefab supplies these editable frame renderers. Each match
        // car gets its own authored material; no cabin geometry is duplicated in code.
        static void ApplyCarTrim(ElevatorScene view, Material material)
        {
            if (view == null || material == null) return;
            SetFrameMaterial(view.frontDoorFrame, material);
            SetFrameMaterial(view.leftDoorFrame, material);
            SetFrameMaterial(view.rightDoorFrame, material);
        }

        static void SetFrameMaterial(Renderer[] frame, Material material)
        {
            if (frame == null) return;
            foreach (Renderer part in frame)
                if (part != null) part.sharedMaterial = material;
        }

        void Update()
        {
            if (!enabled || player == null) return;
            if (Paused || MenuManager.IsOpen) return;
            float dt = Time.unscaledDeltaTime;
            if (dimensionRevealActive)
            {
                UpdateDimensionReveal(dt);
                return;
            }
            if (MenuManager.RoundPassengerIntroductionActive) return;
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
                && (Mathf.Abs(player.TravelVelocity) > .05f || Mathf.Abs(player.LateralTravelVelocity) > .05f);
            bool rivalTravelling = Rival != null && Rival.MovingInShaft
                && (Mathf.Abs(Rival.TravelVelocity) > .05f || Mathf.Abs(Rival.LateralTravelVelocity) > .05f);
            bool carsOverlap = CarsOverlap(player, Rival);
            if (!carsOverlap) collisionContactActive = false;
            if (Running && !Paused && (playerTravelling || rivalTravelling
                    || playerWasSwitching || rivalWasSwitching)
                && carsOverlap)
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

            if (Finished && resultRevealStartedAt < 0f)
            {
                GameManager.Instance?.RecordRoundResult(player.Points, Rival.Points);
                resultRevealStartedAt = Time.unscaledTime;
                resultActionSelection = 0;
                resultActionMoveHeld = false;
                resultActionMoveRepeatTimer = 0f;
                player.PlayEndingDrum();
            }

            if (player.IntroPlaying || Rival.IntroPlaying || player.CountdownPlaying
                || Rival.CountdownPlaying || MenuManager.IsOpen) return;
            if (Running && !Finished && !MenuManager.TutorialActive)
            {
                UpdateRivalInteractions(Time.deltaTime);
            }
            var input = InputManager.Instance;
            if (input == null) return;

            // The round's world briefing precedes its shared countdown.
            if (!Running)
            {
                StartMatch();
                return;
            }
            if (Finished && ResultRevealTime >= ResultRevealInputAt)
            {
                InputManager.SeatInput playerOne = input.ReadForSeat(0, true);
                InputManager.SeatInput playerTwo = input.ReadForSeat(1, true);
                GameManager session = GameManager.Instance;
                if (session != null && session.MatchFinished)
                {
                    float horizontal = Mathf.Abs(playerTwo.Move.x) > Mathf.Abs(playerOne.Move.x)
                        ? playerTwo.Move.x : playerOne.Move.x;
                    UpdateResultActionNavigation(horizontal);
                    if (playerOne.StartPressed || playerOne.ConfirmPressed
                        || playerTwo.StartPressed || playerTwo.ConfirmPressed)
                        ActivateResultAction(resultActionSelection);
                }
                else if (playerOne.StartPressed || playerOne.ConfirmPressed
                    || playerTwo.StartPressed || playerTwo.ConfirmPressed)
                {
                    ContinueMatch();
                }
            }
            // Pause is owned by GameManager so InputManager only fires once.
        }

        // A rematch starts a fresh best-of-three series with a new dimension order.
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
            EndTutorialCollisionPractice();
            bool practice = MenuManager.TutorialActive;
            Paused = false;
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            resultRevealStartedAt = -1f;
            resultActionSelection = 0;
            resultActionMoveHeld = false;
            resultActionMoveRepeatTimer = 0f;
            targetTrack[0] = 0; targetTrack[1] = 1;
            trackPosition[0] = 0; trackPosition[1] = 1;
            collisionCooldown = collisionFlash = 0;
            collisionContactActive = false;
            bouncerCollision = false;
            collisionCount = 0;
            int firstExpressTrack = Random.Range(0, 2);
            expressTrack[0] = firstExpressTrack;
            expressTrack[1] = 1 - firstExpressTrack;
            expressTrack[2] = Random.Range(0, 2);
            usedJunctionMask[0] = usedJunctionMask[1] = 0;
            expressFlash[0] = expressFlash[1] = 0f;
            player.StartMatchShift(countdown: !practice);
            Rival.StartMatchShift(player, countdown: !practice);
            // Promote one waiting passenger to VIP halfway through the live shift.
            matchVip = null;
            vipRevealStarted = false;
            hazardEventStarted = false;
            eventMessageTime = 0f;
            eventMessage = null;
            candyTrails.Clear();
            candyCooldown[0] = candyCooldown[1] = 5f;
            contestMessageTimes[0] = contestMessageTimes[1] = 0f;
            player.ResetSharedHazardEvent();
            player.GetComponent<NpcElevator>()?.ResetDecisions();
            Rival.GetComponent<NpcElevator>()?.ResetDecisions();
            Running = true;
            Paused = false;
            if (!practice) BeginRoundIntroduction();
        }

        // Boarding is reported only after the shared rider was claimed successfully.
        public void PassengerBoarded(Game actor, SharedRider rider)
        {
            if (!Running || MenuManager.TutorialActive || actor == null || rider == null) return;
            // Regular boarding already has immediate per-car feedback. Reserve
            // the shared rivalry banner for VIPs to avoid repeating "ON BOARD!".
            if (!rider.IsVip) return;
            int winner = SeatOf(actor), loser = 1 - winner;
            contestMessages[winner] = "VIP ON BOARD!";
            contestMessages[loser] = "RIVAL GOT THE VIP!";
            contestMessageTimes[winner] = contestMessageTimes[loser] = 2.5f;
        }

        void UpdateRivalInteractions(float dt)
        {
            eventMessageTime = Mathf.Max(0f, eventMessageTime - dt);
            for (int seat = 0; seat < 2; seat++)
            {
                contestMessageTimes[seat] = Mathf.Max(0f, contestMessageTimes[seat] - dt);
                candyCooldown[seat] = Mathf.Max(0f, candyCooldown[seat] - dt);
                Game actor = seat == 0 ? player : Rival;
                if (actor.HasCandyTrail && actor.MovingInShaft && candyCooldown[seat] <= 0f)
                {
                    candyTrails.Add(new CandyTrail { Seat = seat, Position = actor.ShaftWorldPosition, Life = 5f });
                    actor.ShowPassengerAbility("CANDY!", actor.ShaftWorldPosition + Vector3.up * 3f);
                    candyCooldown[seat] = 7f;
                }
            }
            for (int i = candyTrails.Count - 1; i >= 0; i--)
            {
                CandyTrail trail = candyTrails[i];
                trail.Life -= dt;
                Game target = trail.Seat == 0 ? Rival : player;
                Vector3 delta = target.ShaftWorldPosition - trail.Position;
                // Give the rival half a second to see the candy before it can trigger.
                if (trail.Life < 4.5f && target.MovingInShaft
                    && Mathf.Abs(delta.x) < 3f && Mathf.Abs(delta.y) < 3f)
                {
                    target.ApplyCandyTrail();
                    contestMessages[1 - trail.Seat] = "STICKY CANDY!";
                    contestMessages[trail.Seat] = "RIVAL SLOWED!";
                    contestMessageTimes[0] = contestMessageTimes[1] = 2f;
                    GameManager.Instance?.RequestAbilityIntro(trail.Seat == 0 ? player : Rival, "GROUP");
                    candyTrails.RemoveAt(i);
                }
                else if (trail.Life <= 0f) candyTrails.RemoveAt(i);
            }
            if (!vipRevealStarted && player.SecondsLeft <= ElevatorRound.Duration * .5f)
            {
                SharedRider arrivingVip = player.ChooseMatchVip();
                if (arrivingVip != null)
                {
                    matchVip = arrivingVip;
                    vipRevealStarted = true;
                    GameManager.Instance?.RequestVipIntro(player, matchVip);
                }
            }
            if (!hazardEventStarted && player.SecondsLeft <= 40f)
            {
                hazardEventStarted = true;
                eventMessage = player.StartSharedHazardEvent();
                if (!string.IsNullOrEmpty(eventMessage)) eventMessageTime = 3.5f;
            }
        }

        void BeginRoundIntroduction()
        {
            dimensionRevealIndex = GameManager.Instance != null ? GameManager.Instance.ActiveDimension : 0;
            dimensionRevealTime = 0f;
            dimensionRevealActive = true;
            player.BeginDimensionReveal(dimensionRevealIndex);
            Rival?.BeginDimensionReveal(dimensionRevealIndex);
        }

        // Either human can confirm the shared world preview.
        void UpdateDimensionReveal(float dt)
        {
            if (Paused || MenuManager.IsOpen) return;
            dimensionRevealTime += Mathf.Max(0f, dt);
            if (dimensionRevealTime < Game.DimensionRevealConfirmDelay
                || !(player.DimensionRevealConfirmed || Rival != null && Rival.DimensionRevealConfirmed)) return;
            player.FinishDimensionReveal();
            Rival?.FinishDimensionReveal();
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            if (!MenuManager.BeginRoundPassengerIntroduction(player, Rival))
            {
                player.BeginRoundCountdown();
                Rival?.BeginRoundCountdown();
            }
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

        public bool BeginTutorialCollisionPractice(int attackerSeat)
        {
            EndTutorialCollisionPractice();
            if (!MenuManager.TutorialActive || !Running || player == null || Rival == null
                || attackerSeat < 0 || attackerSeat > 1) return false;
            Game attacker = attackerSeat == 0 ? player : Rival;
            Game target = attackerSeat == 0 ? Rival : player;
            if (attacker.IsNpc) return false;
            tutorialCollisionAttacker = attacker;
            tutorialCollisionTarget = target;
            tutorialCollisionCompleted = false;
            tutorialCollisionStartFloor = attacker.ActiveMinFloor + 2f;
            targetTrack[0] = targetTrack[1] = 0;
            trackPosition[0] = trackPosition[1] = 0f;
            collisionCooldown = collisionFlash = 0f;
            if (!attacker.StageTutorialCollision(tutorialCollisionStartFloor)
                || !target.StageTutorialCollision(attacker.ActiveMinFloor + 3f))
            {
                EndTutorialCollisionPractice();
                return false;
            }
            return true;
        }

        public bool IsTutorialCollisionParticipant(Game actor)
            => MenuManager.TutorialActive && tutorialCollisionAttacker != null
                && (actor == tutorialCollisionAttacker || actor == tutorialCollisionTarget);

        public bool TutorialCollisionTravelAllowed(Game actor)
        {
            if (!IsTutorialCollisionParticipant(actor) || tutorialCollisionCompleted) return true;
            return actor == tutorialCollisionAttacker && actor.TravelInputActive;
        }

        public bool TutorialCollisionPracticeCompleted(int seat)
            => IsTutorialCollisionParticipant(tutorialCollisionAttacker)
                && tutorialCollisionAttacker.Seat == seat && tutorialCollisionCompleted;

        public void EndTutorialCollisionPractice()
        {
            tutorialCollisionAttacker?.StopTutorialCollisionMotion();
            tutorialCollisionTarget?.StopTutorialCollisionMotion();
            tutorialCollisionAttacker = tutorialCollisionTarget = null;
            tutorialCollisionCompleted = false;
            tutorialCollisionStartFloor = 0f;
        }

        // Prevent cars in the same track from passing through each other. A
        // player can escape the block with A/D; the NPC automatically tries the
        // free track after it reaches a blocker.
        public float ConstrainTravel(Game actor, float before, float candidate, ref float velocity)
        {
            if (!Running || actor == null) return candidate;
            if (IsTutorialCollisionParticipant(actor) && actor == tutorialCollisionAttacker
                && !tutorialCollisionCompleted && candidate < tutorialCollisionStartFloor)
            {
                velocity = Mathf.Max(0f, velocity);
                candidate = tutorialCollisionStartFloor;
            }
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

        // Both cars share the same rider. A pickup is a close call only when
        // the other car has already committed to this rider's landing.
        public void NotifyPassengerBoarded(Game actor, CrazyElevator.Shared.Rider rider)
        {
            if (!Running || Paused || MenuManager.TutorialActive || actor == null || rider == null
                || (actor != player && actor != Rival) || rider.BoardedBySeat != actor.Seat)
                return;
            Game other = actor == player ? Rival : player;
            if (other == null || !other.ApproachingPickup(rider)) return;

            actor.ShowActionFeedback("STOLEN!", true, true);
            other.ShowActionFeedback("TOO LATE!", false, true);
        }

        void PlayCollision(Game first, Game second, float verticalDirection,
            float closingSpeed, Game tutorialTarget = null)
        {
            if (collisionCooldown > 0f || collisionContactActive) return;
            bool practicePair = IsTutorialCollisionParticipant(first)
                && IsTutorialCollisionParticipant(second);
            Game bumpWinner = MenuManager.TutorialActive || practicePair
                ? null : ResolveBumpWinner(first, second, verticalDirection, closingSpeed);
            collisionContactActive = true;
            bool practiceHit = practicePair && !tutorialCollisionCompleted
                && verticalDirection != 0f && closingSpeed > .05f
                && tutorialCollisionAttacker.TravelInputActive;
            if (practicePair)
            {
                // This is the one tutorial step where the learner must move the
                // target. The other practice steps keep their collision protection.
                tutorialTarget = null;
                if (practiceHit) tutorialCollisionCompleted = true;
            }
            collisionCooldown = .48f;
            collisionFlash = .72f;
            collisionCount++;
            bool firstBoost = (practicePair || !MenuManager.TutorialActive) && first != null && first.HasBouncer;
            bool secondBoost = (practicePair || !MenuManager.TutorialActive) && second != null && second.HasBouncer;
            bouncerCollision = firstBoost || secondBoost;
            // A tutorial collision can still show feedback and stop the
            // incoming elevator, but it must not move the elevator that was
            // hit. Otherwise a passenger-view tutorial card can unexpectedly
            // become an elevator-view card and a stopped car gains momentum.
            if (verticalDirection != 0f)
            {
                float rebound = Mathf.Clamp(closingSpeed * collisionBounceStrength, 1.05f, 2.4f);
                if (first != tutorialTarget && (practicePair || !TutorialInputLocked(first)))
                    ApplyCollisionResponse(first, -verticalDirection * rebound * (secondBoost ? 1.5f : 1f), secondBoost);
                if (second != tutorialTarget && (practicePair || !TutorialInputLocked(second)))
                    ApplyCollisionResponse(second, verticalDirection * rebound * (firstBoost ? 1.5f : 1f), firstBoost);
                if (firstBoost) first.ShowPassengerAbility("BUMP!", first.ShaftWorldPosition + Vector3.up * 3f);
                if (secondBoost) second.ShowPassengerAbility("BUMP!", second.ShaftWorldPosition + Vector3.up * 3f);
            }
            else
            {
                // A lane-change bump still jolts both cars, but does not reverse their vertical travel.
                float separation = ShaftX(second) - ShaftX(first);
                int direction = separation > .01f ? 1 : separation < -.01f ? -1
                    : first != null && Mathf.Abs(first.LateralTravelVelocity) > .05f
                        ? (first.LateralTravelVelocity > 0f ? 1 : -1) : 1;
                if (first != tutorialTarget && (practicePair || !TutorialInputLocked(first)))
                {
                    if (secondBoost) first?.ApplyMatchLateralBounce(collisionSlowSeconds, -direction);
                    else first?.ApplyMatchCollision(collisionSlowSeconds, collisionBounceStrength);
                }
                if (second != tutorialTarget && (practicePair || !TutorialInputLocked(second)))
                {
                    if (firstBoost) second?.ApplyMatchLateralBounce(collisionSlowSeconds, direction);
                    else second?.ApplyMatchCollision(collisionSlowSeconds, collisionBounceStrength);
                }
                if (firstBoost) first.ShowPassengerAbility("BUMP!", first.ShaftWorldPosition + Vector3.up * 3f);
                if (secondBoost) second.ShowPassengerAbility("BUMP!", second.ShaftWorldPosition + Vector3.up * 3f);
            }
            if (first != null && second != null)
                EmitCollisionSparks((first.ShaftWorldPosition + second.ShaftWorldPosition) * .5f);
            SfxManager.Instance?.PlaySfx(collisionClip, 1f);
            if (bouncerCollision)
            {
                first?.PulseMatchImpact(true);
                second?.PulseMatchImpact(true);
                if (firstBoost && secondBoost)
                {
                    first.ShowActionFeedback("BOUNCER CLASH!", true, true);
                    second.ShowActionFeedback("BOUNCER CLASH!", true, true);
                }
                else if (firstBoost)
                {
                    first.ShowActionFeedback("BOUNCER HIT!", true, true);
                    second?.ShowActionFeedback("HIT BY BOUNCER!", false, true);
                }
                else
                {
                    second.ShowActionFeedback("BOUNCER HIT!", true, true);
                    first?.ShowActionFeedback("HIT BY BOUNCER!", false, true);
                }
            }

            if (bumpWinner != null)
            {
                Vector3 impactPoint = (first.ShaftWorldPosition + second.ShaftWorldPosition) * .5f;
                if (bumpWinner.AwardBumpPoints(BumpRewardPoints, impactPoint))
                {
                    bumpWinner.ShowActionFeedback("+" + BumpRewardPoints + " BUMP!", true, true);
                    Game bumped = bumpWinner == first ? second : first;
                    bumped?.ShowActionFeedback("BUMPED!", false);
                }
            }
        }

        static Game ResolveBumpWinner(Game first, Game second, float verticalDirection,
            float closingSpeed)
        {
            if (first == null || second == null) return null;
            bool firstHasBouncer = first.HasBouncer;
            bool secondHasBouncer = second.HasBouncer;
            if (firstHasBouncer != secondHasBouncer)
                return firstHasBouncer ? first : second;
            if (firstHasBouncer) return null;

            // Vertical contact is reported with the advancing car first. For
            // a lateral impact, reward the car carrying more impact momentum;
            // equal-force clashes have no clear winner.
            if (Mathf.Abs(verticalDirection) > .01f && closingSpeed > .05f) return first;
            float firstImpact = Mathf.Abs(first.TravelVelocity) + Mathf.Abs(first.LateralTravelVelocity);
            float secondImpact = Mathf.Abs(second.TravelVelocity) + Mathf.Abs(second.LateralTravelVelocity);
            if (firstImpact - secondImpact > .12f) return first;
            if (secondImpact - firstImpact > .12f) return second;
            return null;
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

        void ApplyCollisionResponse(Game actor, float reboundVelocity, bool strong)
        {
            if (actor == null) return;
            // The opening tutorial position is a stationary staging point. It
            // can still be hit, but must not inherit a rebound or momentum.
            if (TutorialStartingFloorStationary(actor))
                actor.ApplyMatchCollision(collisionSlowSeconds, 1f);
            else actor.ApplyMatchBounce(collisionSlowSeconds, reboundVelocity, strong);
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
        static readonly string[] VipNavigationArrows = { "←", "↖", "↑", "↗", "→", "↘", "↓", "↙" };
        [System.NonSerialized] GUIStyle heading, text, score, centred, tileCaption, tileValue, collisionWord,
            resultTitle, resultName, resultPoints, resultWinner, roundStatus, sharedClock,
            resultSmall, resultWorld, resultAction, resultKey, resultWinBadge;
        [System.NonSerialized] Texture2D resultGlowTexture;

        void OnGUI()
        {
            if (player == null || Rival == null) return;
            if (!dimensionRevealActive && !Paused && (player.CountdownPlaying || Rival.CountdownPlaying)) return;
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
                player.DrawDimensionRevealOverlay(new Rect(0f, 0f, Screen.width, Screen.height),
                    dimensionRevealTime);
                GUI.matrix = previousMatrix;
                GUI.color = previousColour;
                GUI.contentColor = previousContent;
                return;
            }
            if (IsVisibleSeat(0)) DrawSeat(player, 0, MenuPresentation.Coral);
            if (IsVisibleSeat(1)) DrawSeat(Rival, 1, MenuPresentation.Teal);
            if (SplitScreen) DrawSplitDivider();
            DrawSharedRoundHUD();
            if (IsVisibleSeat(0)) DrawSeatScoreFlights(player, 0);
            if (IsVisibleSeat(1)) DrawSeatScoreFlights(Rival, 1);
            DrawCollisionFeedback();
            if (Finished) DrawResultReveal();
            DrawSessionFade(player.SessionFadeAlpha);
            MenuManager.DrawTutorialCardsAboveHud();
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
            tileCaption.alignment = TextAnchor.MiddleCenter;
            tileCaption.wordWrap = false;
            tileCaption.normal.textColor = Color.white;
            tileValue = new GUIStyle(score) { fontSize = 24, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GameTypography.Apply(tileValue, true);
            tileValue.normal.textColor = Color.white;
            sharedClock = new GUIStyle(tileValue) { fontSize = 32, alignment = TextAnchor.MiddleCenter };
            collisionWord = new GUIStyle(centred) { fontSize = 24, clipping = TextClipping.Overflow };
            GameTypography.Apply(collisionWord, true);
            collisionWord.normal.textColor = Color.white;
            resultTitle = new GUIStyle(centred) { fontSize = 18 };
            GameTypography.Apply(resultTitle, true);
            resultTitle.normal.textColor = Color.white;
            resultName = new GUIStyle(centred) { fontSize = 20, alignment = TextAnchor.MiddleLeft };
            GameTypography.Apply(resultName, true);
            resultName.normal.textColor = Color.white;
            resultPoints = new GUIStyle(centred) { fontSize = 88, alignment = TextAnchor.MiddleLeft };
            GameTypography.Apply(resultPoints, true);
            resultPoints.normal.textColor = Color.white;
            resultWinner = new GUIStyle(centred) { fontSize = 58, wordWrap = false };
            GameTypography.Apply(resultWinner, true);
            resultWinner.normal.textColor = Color.white;
            resultSmall = new GUIStyle(resultName) { fontSize = 12 };
            resultSmall.normal.textColor = MenuPresentation.DisplayColor(new Color32(208, 191, 181, 255));
            resultWorld = new GUIStyle(resultName) { fontSize = 28 };
            resultAction = new GUIStyle(centred) { fontSize = 20 };
            resultAction.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Ink);
            resultKey = new GUIStyle(centred) { fontSize = 12 };
            resultKey.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Ink);
            resultWinBadge = new GUIStyle(resultKey) { fontSize = 11 };
        }

        static void Fill(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawSplitDivider()
        {
            // Keep the split obvious while either cabin is on screen; hide it
            // when both players are looking at their full shaft-travel views.
            if (player.Traveling && Rival.Traveling) return;

            const float width = 12f;
            float x = Screen.width * .5f;
            MenuPresentation.Paint(new Rect(x - width * .5f, 0, width, Screen.height), MenuPresentation.Ink);
            MenuPresentation.Paint(new Rect(x - width * .5f, 0, 2f, Screen.height), MenuPresentation.Coral);
            MenuPresentation.Paint(new Rect(x + width * .5f - 2f, 0, 2f, Screen.height), MenuPresentation.Teal);
        }

        float SharedClockWidth => Mathf.Clamp(Screen.width * .13f, 104f, 160f);

        int AbilityIntroPaneSeat => GameManager.Instance != null ? GameManager.Instance.AbilityIntroPaneSeat : -1;

        Rect SharedHudRect(float width, float top, float height)
        {
            int introSeat = AbilityIntroPaneSeat;
            if (introSeat < 0) return new Rect((Screen.width - width) * .5f, top, width, height);

            // Keep the shared timer and round label entirely on the pane still playing.
            Rect activeView = ViewRect(1 - introSeat);
            width = Mathf.Min(width, activeView.width - 20f);
            float x = introSeat == 0 ? activeView.x + 10f : activeView.xMax - width - 10f;
            return new Rect(x, top, width, height);
        }

        Rect SharedClockRect => SharedHudRect(SharedClockWidth, 8f, 58f);
        Rect SharedRoundStatusRect => SharedHudRect(Mathf.Min(Screen.width - 16f, 520f), 70f, 26f);

        Rect SeatScoreBarRect(int seat)
        {
            Rect view = ViewRect(seat);
            float clockSpace = AbilityIntroPaneSeat == 1 - seat ? SharedClockWidth + 4f : SharedClockWidth * .5f;
            float availableWidth = SplitScreen ? view.width - clockSpace - 16f
                : (view.width - SharedClockWidth) * .5f - 16f;
            float barWidth = Mathf.Clamp(availableWidth, 1f, 290f);
            float barX = SplitScreen && seat == 1 ? view.xMax - 8f - barWidth : view.x + 8f;
            return new Rect(barX, view.y + 8f, barWidth, 58f);
        }

        static Rect SeatScoreTile(Rect bar) =>
            new Rect(bar.x + bar.width * .34f, bar.y, bar.width * .38f, bar.height);

        void DrawSeatScoreFlights(Game actor, int seat)
        {
            if (actor == null) return;
            Rect view = ViewRect(seat);
            Rect bar = SeatScoreBarRect(seat);
            Vector2 anchor = SeatScoreTile(bar).center - view.position;
            anchor.y += 8f;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Translate(new Vector3(view.x, view.y, 0f));
            actor.DrawScoreFlights(view.width, view.height, anchor);
            GUI.matrix = previousMatrix;
        }

        void DrawSeat(Game actor, int seat, Color colour)
        {
            Rect view = ViewRect(seat);
            actor.DrawMatchViewGUI();
            DrawRivalInteractions(actor, seat, view);
            DrawPlayerScreenBorder(view, colour);
            Rect bar = SeatScoreBarRect(seat);
            MenuPresentation.HudPlate(bar, colour);
            Rect badge = new Rect(bar.x + 6f, bar.y + 7f, bar.width * .34f - 12f, bar.height - 14f);
            MenuPresentation.Plaque(badge, colour, MenuPresentation.Metal, 2f);
            DrawResultLabel(badge, badge.width >= 78f ? "PLAYER " + (seat + 1) : "P" + (seat + 1),
                roundStatus, 1f, MenuPresentation.Ink);

            Rect scoreTile = SeatScoreTile(bar);
            DrawSeatTile(scoreTile, "SCORE", actor.Points.ToString(), MenuPresentation.Brass);
            Rect floorTile = Rect.MinMaxRect(scoreTile.xMax, bar.y, bar.xMax, bar.yMax);
            DrawSeatTile(floorTile, "FLOOR", ElevatorRound.FloorNumber(Mathf.Clamp(
                Mathf.RoundToInt(actor.CurrentFloor), actor.ActiveMinFloor, actor.ActiveMaxFloor)).ToString(),
                MenuPresentation.Paper);
        }

        void DrawRivalInteractions(Game actor, int seat, Rect view)
        {
            const float markerHeight = 28f;
            const float hudGap = 8f;
            float hintTop = SharedRoundStatusRect.yMax + hudGap;
            if (IsTutorialCollisionParticipant(actor) && actor == tutorialCollisionAttacker)
            {
                Camera tutorialCamera = actor.sceneView != null ? actor.sceneView.exteriorCamera : null;
                if (tutorialCamera != null && tutorialCamera.enabled)
                {
                    Vector3 target = tutorialCamera.WorldToScreenPoint(tutorialCollisionTarget.ShaftWorldPosition
                        + Vector3.up * (tutorialCollisionTarget.ExteriorCollisionHalfSize.y + .6f));
                    if (target.z > 0f)
                    {
                        float x = Mathf.Clamp(target.x, view.x + 60f, view.xMax - 60f);
                        float y = Mathf.Clamp(Screen.height - target.y - markerHeight,
                            hintTop, Mathf.Max(hintTop, view.yMax - 80f - markerHeight));
                        Rect marker = new Rect(x - 54f, y, 108f, markerHeight);
                        Fill(marker, Ink);
                        var targetLabel = new GUIStyle(centred) { fontSize = 18 };
                        targetLabel.normal.textColor = Gold;
                        GUI.Label(marker, "↓ RIVAL", targetLabel);
                    }
                }
            }
            if (MenuManager.TutorialActive || Finished || actor.IntroPlaying || actor.CountdownPlaying) return;
            Game rival = seat == 0 ? Rival : player;
            var label = new GUIStyle(centred) { fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .032f), 13, 20), wordWrap = false };
            float delta = rival.CurrentFloor - actor.CurrentFloor;
            if (Mathf.Abs(delta) <= 3f)
            {
                string arrow = delta > .45f ? "↑" : delta < -.45f ? "↓" : "↔";
                Rect marker = new Rect(view.xMax - 154f, hintTop, 146f, markerHeight);
                Color accent = seat == 0 ? MenuPresentation.Teal : MenuPresentation.Coral;
                MenuPresentation.HudPlate(marker, accent);
                MenuPresentation.Label(marker, new GUIContent(arrow + " RIVAL F"
                    + ElevatorRound.FloorNumber(Mathf.Clamp(Mathf.RoundToInt(rival.CurrentFloor),
                        rival.ActiveMinFloor, rival.ActiveMaxFloor))), label, MenuPresentation.Paper);
                hintTop = marker.yMax + hudGap;
            }
            if (contestMessageTimes[seat] > 0f || eventMessageTime > 0f)
            {
                float messageY = Mathf.Max(hintTop, view.y + view.height * .18f);
                Rect message = new Rect(view.x + view.width * .15f, messageY, view.width * .7f, 38f);
                Fill(message, new Color(Ink.r, Ink.g, Ink.b, .88f));
                label.normal.textColor = contestMessageTimes[seat] > 0f ? Gold : Coral;
                GUI.Label(message, contestMessageTimes[seat] > 0f ? contestMessages[seat] : eventMessage, label);
            }
            Camera camera = actor.Traveling
                ? (actor.sceneView != null ? actor.sceneView.exteriorCamera : null)
                : actor.CabinCamera;
            DrawVipNavigator(actor, view, camera);
            if (camera == null || !camera.enabled) return;
            foreach (CandyTrail trail in candyTrails)
            {
                Vector3 point = camera.WorldToScreenPoint(trail.Position);
                float x = point.x, y = Screen.height - point.y;
                if (point.z <= 0f || x < view.x + 20f || x > view.xMax - 20f || y < view.y + 60f || y > view.yMax - 25f) continue;
                var candy = new GUIStyle(centred) { fontSize = 38, alignment = TextAnchor.MiddleCenter };
                candy.normal.textColor = trail.Seat == seat ? Gold : Coral;
                GUI.Label(new Rect(x - 24f, y - 19f, 48f, 42f), "●", candy);
                Rect tag = new Rect(x - 43f, y - 35f, 86f, 22f);
                Fill(tag, new Color(Ink.r, Ink.g, Ink.b, .9f));
                label.normal.textColor = candy.normal.textColor;
                label.fontSize = 13;
                GUI.Label(tag, "STICKY", label);
            }
        }

        void DrawVipNavigator(Game actor, Rect view, Camera camera)
        {
            if (matchVip == null || matchVip.Boarded || matchVip.Resolved || camera == null
                || player == null || !player.TryGetWaitingPassengerWorldPosition(matchVip, out Vector3 worldPosition)) return;

            Vector3 projected = camera.WorldToScreenPoint(worldPosition + Vector3.up * 2.65f);
            Vector2 target = new Vector2(projected.x, Screen.height - projected.y);
            Vector2 direction = target - view.center;
            if (projected.z < 0f) direction = -direction;

            float sideInset = Mathf.Clamp(view.width * .06f, 24f, 42f);
            Rect safeView = Rect.MinMaxRect(view.xMin + sideInset, view.yMin + 82f,
                view.xMax - sideInset, view.yMax - 72f);
            if (safeView.width <= 0f || safeView.height <= 0f) return;

            bool targetVisible = projected.z > 0f && safeView.Contains(target);
            Vector2 marker = target;
            if (!targetVisible)
            {
                if (direction.sqrMagnitude < .01f)
                    direction = new Vector2(0f, matchVip.Origin >= actor.CurrentFloor ? -1f : 1f);
                direction.Normalize();
                Vector2 center = safeView.center;
                float xDistance = Mathf.Abs(direction.x) > .001f
                    ? (safeView.width * .5f) / Mathf.Abs(direction.x) : float.MaxValue;
                float yDistance = Mathf.Abs(direction.y) > .001f
                    ? (safeView.height * .5f) / Mathf.Abs(direction.y) : float.MaxValue;
                marker = center + direction * Mathf.Min(xDistance, yDistance);
            }

            float size = Mathf.Clamp(view.width * .055f, 36f, 52f);
            var arrow = new GUIStyle(centred)
            {
                fontSize = Mathf.RoundToInt(size),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            arrow.normal.textColor = Gold;
            if (targetVisible)
            {
                float bob = Mathf.Sin(Time.unscaledTime * 5f) * 3f;
                GUI.Label(new Rect(marker.x - size * .5f, marker.y - size + bob,
                    size, size), "▼", arrow);
                DrawVipTag(new Vector2(marker.x, marker.y - size * 1.25f + bob), view.width);
                return;
            }

            GUI.Label(new Rect(marker.x - size * .5f, marker.y - size * .5f,
                size, size), "●", arrow);
            arrow.fontSize = Mathf.RoundToInt(size * .48f);
            arrow.normal.textColor = Ink;
            GUI.Label(new Rect(marker.x - size * .5f, marker.y - size * .5f,
                size, size), NavigationArrow(direction), arrow);
            DrawVipTag(marker - direction * size * .95f, view.width);
        }

        static string NavigationArrow(Vector2 direction)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            int index = Mathf.RoundToInt((angle + 180f) / 45f) % VipNavigationArrows.Length;
            return VipNavigationArrows[index];
        }

        void DrawVipTag(Vector2 center, float viewWidth)
        {
            float width = Mathf.Clamp(viewWidth * .12f, 58f, 78f);
            const float height = 24f;
            Rect tag = new Rect(center.x - width * .5f, center.y - height * .5f, width, height);
            Fill(tag, Gold);
            Fill(new Rect(tag.x + 2f, tag.y + 2f, tag.width - 4f, tag.height - 4f), Ink);
            var style = new GUIStyle(centred)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            style.normal.textColor = Gold;
            GUI.Label(tag, "VIP", style);
        }

        // Both seats read GameManager's round clock; draw it once across the split.
        void DrawSharedRoundHUD()
        {
            var session = GameManager.Instance;
            float remaining = session != null ? session.MainTimer : player.SecondsLeft;
            int seconds = Mathf.CeilToInt(remaining);
            string clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            Rect timer = SharedClockRect;
            Color clockFace = seconds <= 10 ? MenuPresentation.Coral : MenuPresentation.Paper;
            MenuPresentation.Plaque(timer, clockFace, MenuPresentation.Metal, 3f);
            DrawResultLabel(new Rect(timer.x, timer.y + 4f, timer.width, 15f),
                "TIME", tileCaption, 1f, MenuPresentation.Ink);
            DrawResultLabel(new Rect(timer.x, timer.y + 16f, timer.width, 39f),
                clock, sharedClock, 1f, MenuPresentation.Ink);
            if (session == null) return;
            Rect status = SharedRoundStatusRect;
            float statusWidth = status.width;
            MenuPresentation.HudPlate(status, MenuPresentation.Brass);
            var badge = new Rect(status.x + 4f, status.y + 4f, statusWidth * .22f - 4f, status.height - 6f);
            MenuPresentation.Paint(badge, MenuPresentation.Ink);
            DrawResultLabel(badge, "ROUND " + session.RoundNumber + " / " + GameManager.RoundsPerMatch,
                roundStatus, 1f, MenuPresentation.Paper);
            DrawResultLabel(new Rect(badge.xMax + 4f, status.y + 2f, statusWidth * .36f - 8f, status.height - 2f),
                session.ActiveDimensionName, roundStatus, 1f, MenuPresentation.Paper);
            float winsWidth = statusWidth * .21f;
            for (int seat = 0; seat < 2; seat++)
            {
                float x = status.x + statusWidth * .58f + winsWidth * seat;
                Color accent = seat == 0 ? MenuPresentation.Coral : MenuPresentation.Teal;
                DrawResultLabel(new Rect(x, status.y, 26, status.height), "P" + (seat + 1),
                    roundStatus, 1f, accent);
                DrawRoundWinMarkers(new Rect(x + 26f, status.y + 9f, winsWidth - 28f, 9f),
                    seat == 0 ? session.PlayerOneWins : session.PlayerTwoWins, accent, menuColours: true);
            }
        }

        void DrawPlayerScreenBorder(Rect view, Color playerColour)
        {
            const float thickness = 3f;
            Color border = new Color(playerColour.r, playerColour.g, playerColour.b, .65f);
            MenuPresentation.Paint(new Rect(view.x, view.y, view.width, thickness), border);
            MenuPresentation.Paint(new Rect(view.x, view.yMax - thickness, view.width, thickness), border);
        }

        void DrawSeatTile(Rect area, string caption, string value, Color valueColour)
        {
            MenuPresentation.Paint(new Rect(area.x, area.y + 11f, 1f, area.height - 22f), MenuPresentation.Ink);
            DrawResultLabel(new Rect(area.x, area.y + 5f, area.width, 18f),
                caption, tileCaption, 1f, MenuPresentation.Paper);
            DrawResultLabel(new Rect(area.x, area.y + 21f, area.width, 30f),
                value, tileValue, 1f, valueColour);
        }

        void DrawCollisionFeedback()
        {
            if (collisionFlash <= 0f) return;
            for (int seat = 0; seat < 2; seat++)
            {
                if (!IsVisibleSeat(seat)) continue;
                Rect view = ViewRect(seat);
                float alpha = Mathf.Clamp01(collisionFlash / .62f);
                Color flash = new Color(1f, .35f, .18f, alpha * .9f);
                const float edge = 7f;
                Fill(new Rect(view.x, view.y, view.width, edge), flash);
                Fill(new Rect(view.x, view.yMax - edge, view.width, edge), flash);
                Fill(new Rect(view.x, view.y, edge, view.height), flash);
                Fill(new Rect(view.xMax - edge, view.y, edge, view.height), flash);
                float width = Mathf.Min(330f, view.width - 24f);
                Rect banner = new Rect(view.center.x - width * .5f, view.y + 112f, width, 40f);
                Fill(banner, new Color(.04f, .05f, .08f, alpha * .96f));
                Fill(new Rect(banner.x, banner.yMax - 5f, banner.width, 5f), flash);
                GUI.Label(banner, bouncerCollision ? "BOUNCER IMPACT!" : "BOUNCE!  SWITCH TRACKS", collisionWord);
            }
        }

        void DrawResultReveal()
        {
            float time = ResultRevealTime;
            float curtain = Reveal01(time / .85f);
            float panelWidth = Screen.width * .5f * curtain;
            MenuPresentation.Paint(new Rect(0, 0, panelWidth, Screen.height), Color.black);
            MenuPresentation.Paint(new Rect(Screen.width - panelWidth, 0, panelWidth, Screen.height), Color.black);
            if (time < .85f) return;

            MenuPresentation.Paint(new Rect(0, 0, Screen.width, Screen.height), MenuPresentation.Wall);
            var session = GameManager.Instance;
            bool matchFinished = session != null && session.MatchFinished;
            bool seriesDraw = matchFinished && session.MatchWinner < 0;
            bool draw = seriesDraw || !matchFinished && player.Points == Rival.Points;
            bool playerWon = matchFinished ? session.MatchWinner == 0
                : player.Points > Rival.Points;
            Color winnerColour = draw ? MenuPresentation.Brass : playerWon ? MenuPresentation.Coral : MenuPresentation.Teal;

            // Scale the whole composition, including typography and mouse hitboxes.
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColour = GUI.color;
            float uiScale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1440f * uiScale) * .5f,
                (Screen.height - 900f * uiScale) * .5f, 0f), Quaternion.identity,
                new Vector3(uiScale, uiScale, 1f));
            try
            {
                EnsureResultGlow();
                float light = Reveal01((time - 1.35f) / .8f);
                GUI.color = MenuPresentation.DisplayColor(new Color(winnerColour.r, winnerColour.g, winnerColour.b, .17f * light));
                GUI.DrawTexture(new Rect(160, 0, 1120, 820), resultGlowTexture, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;

                float titleIn = Reveal01((time - 2.05f) / .45f);
                int dimension = session != null ? session.ActiveDimension : 0;
                Color worldColour = ResultWorldColour(dimension);
                MenuPresentation.Paint(new Rect(530, 78, 140, 36), new Color(worldColour.r, worldColour.g, worldColour.b, .16f * titleIn));
                DrawResultLabel(new Rect(530, 78, 140, 36),
                    "ROUND " + (session != null ? session.RoundNumber : 1), resultTitle, titleIn, worldColour);
                DrawResultLabel(new Rect(694, 78, 260, 36),
                    session != null ? session.ActiveDimensionName : "OFFICE", resultName, titleIn, MenuPresentation.Paper);

                string playerName = "PLAYER 1";
                string rivalName = "PLAYER 2";
                string winnerName = playerWon ? playerName : rivalName;
                string headline = seriesDraw ? "MATCH TIED" : matchFinished ? winnerName + " WINS THE MATCH"
                    : draw ? "IT'S A DRAW" : winnerName + " WINS";
                float winnerIn = Reveal01((time - 4.55f) / .6f);
                DrawResultLabel(new Rect(180, 152, 1080, 84), headline, resultWinner, winnerIn, winnerColour);
                DrawResultLabel(new Rect(180, 232, 1080, 30),
                    matchFinished ? seriesDraw ? "SERIES TIED" : "BEST OF THREE CHAMPION"
                        : draw ? "NO ROUND AWARDED" : "ROUND VICTORY",
                    resultTitle, winnerIn, new Color32(210, 191, 178, 255));

                DrawRevealScoreCard(new Rect(220, 300, 488, 256), playerName, player.Points,
                    session != null ? session.PlayerOneWins : 0, MenuPresentation.Coral, !draw && playerWon,
                    Reveal01((time - 2.65f) / .75f));
                DrawRevealScoreCard(new Rect(732, 300, 488, 256), rivalName, Rival.Points,
                    session != null ? session.PlayerTwoWins : 0, MenuPresentation.Teal, !draw && !playerWon,
                    Reveal01((time - 3.55f) / .75f));

                if (time < ResultRevealInputAt) return;
                int nextDimension = matchFinished && session != null ? session.FirstEnabledDimension
                    : (dimension + 1) % ElevatorRound.Dimensions;
                Color nextColour = ResultWorldColour(nextDimension);
                MenuPresentation.Paint(new Rect(220, 596, 1000, 112), new Color32(62, 46, 65, 255));
                MenuPresentation.Paint(new Rect(220, 596, 4, 112), nextColour);
                DrawResultLabel(new Rect(248, 614, 500, 24),
                    matchFinished ? "REMATCH STARTS IN" : "NEXT WORLD", resultSmall, 1f, MenuPresentation.Paper);
                DrawResultLabel(new Rect(248, 642, 500, 44),
                    matchFinished ? GameManager.DimensionDisplayName(nextDimension)
                        : session != null ? session.NextDimensionName : "CANDY",
                    resultWorld, 1f, nextColour);

                if (matchFinished)
                {
                    DrawFinalResultActionButton(new Rect(220, 746, 306, 64),
                        "PLAY AGAIN", MenuPresentation.Teal, 0);
                    DrawFinalResultActionButton(new Rect(557, 746, 306, 64),
                        "RETURN TO LOBBY", MenuPresentation.Coral, 1);
                    DrawFinalResultActionButton(new Rect(894, 746, 306, 64),
                        "RETURN TO MAIN MENU", MenuPresentation.Brass, 2);
                }
                else
                {
                    Rect button = new Rect(832, 620, 364, 64);
                    bool hovered = button.Contains(Event.current.mousePosition);
                    MenuPresentation.Paint(new Rect(button.x, button.y + 4, button.width, button.height), new Color(0, 0, 0, .4f));
                    MenuPresentation.Paint(button, hovered ? Color.Lerp(MenuPresentation.Teal, MenuPresentation.Paper, .2f) : MenuPresentation.Teal);
                    GUI.Label(new Rect(button.x + 24, button.y, button.width - 116, button.height),
                        "NEXT ROUND", resultAction);
                    var key = new Rect(button.xMax - 76, button.y + 15, 56, 34);
                    MenuPresentation.Paint(key, new Color(0, 0, 0, .2f));
                    MenuPresentation.Paint(new Rect(key.x + 1, key.y + 1, key.width - 2, key.height - 2), new Color(1, 1, 1, .2f));
                    GUI.Label(key, ResultConfirmHint, resultKey);
                    if (GUI.Button(button, GUIContent.none, GUIStyle.none)) ContinueMatch();
                }
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColour;
            }
        }

        void UpdateResultActionNavigation(float horizontal)
        {
            bool moving = Mathf.Abs(horizontal) >= .55f;
            if (!moving)
            {
                resultActionMoveHeld = false;
                resultActionMoveRepeatTimer = 0f;
                return;
            }

            if (!resultActionMoveHeld || resultActionMoveRepeatTimer <= 0f)
            {
                int direction = horizontal > 0f ? 1 : -1;
                resultActionSelection = (resultActionSelection + direction + 3) % 3;
                resultActionMoveRepeatTimer = resultActionMoveHeld ? .16f : .32f;
            }
            else
            {
                resultActionMoveRepeatTimer -= Time.unscaledDeltaTime;
            }
            resultActionMoveHeld = true;
        }

        void DrawFinalResultActionButton(Rect button, string label, Color accent, int action)
        {
            bool hovered = button.Contains(Event.current.mousePosition);
            if (hovered) resultActionSelection = action;
            bool selected = resultActionSelection == action || hovered;
            MenuPresentation.Paint(new Rect(button.x, button.y + 4, button.width, button.height), new Color(0, 0, 0, .4f));
            if (selected)
                MenuPresentation.Paint(new Rect(button.x - 3, button.y - 3, button.width + 6, button.height + 6), MenuPresentation.Paper);
            MenuPresentation.Paint(button, selected ? Color.Lerp(accent, MenuPresentation.Paper, .18f) : accent);
            GUI.Label(new Rect(button.x + 8, button.y, button.width - 76, button.height), label, resultAction);
            if (selected)
            {
                Rect key = new Rect(button.xMax - 64, button.y + 15, 54, 34);
                MenuPresentation.Paint(key, new Color(0, 0, 0, .13f));
                MenuPresentation.Paint(new Rect(key.x + 1, key.y + 1, key.width - 2, key.height - 2),
                    new Color(1, 1, 1, .24f));
                GUI.Label(key, ResultConfirmHint, resultKey);
            }
            if (GUI.Button(button, GUIContent.none, GUIStyle.none)) ActivateResultAction(action);
        }

        static string ResultConfirmHint => MenuManager.KeyboardHintsEnabled ? "C / ." : "A";

        void ActivateResultAction(int action)
        {
            if (action == 0) ContinueMatch();
            else if (action == 1) MenuManager.ReturnToLobby();
            else MenuManager.ReturnToMainMenu();
        }

        static Color ResultWorldColour(int dimension) => dimension == 1 ? new Color32(236, 118, 188, 255)
            : dimension == 2 ? new Color32(48, 196, 210, 255)
            : dimension == 3 ? new Color32(145, 204, 144, 255)
            : dimension == 4 ? new Color32(174, 143, 242, 255) : new Color32(130, 165, 214, 255);

        void DrawRevealScoreCard(Rect area, string name, int points, int wins, Color accent, bool winner, float reveal)
        {
            if (reveal <= 0f) return;
            area.y += Mathf.Lerp(26f, 0f, reveal);
            Color border = Color.Lerp(MenuPresentation.Wall, accent, winner ? .75f : .25f);
            MenuPresentation.Paint(new Rect(area.x - 2, area.y - 2, area.width + 4, area.height + 4),
                new Color(border.r, border.g, border.b, reveal));
            MenuPresentation.Paint(area, new Color(62f / 255f, 46f / 255f, 65f / 255f, reveal));
            MenuPresentation.Paint(new Rect(area.x, area.y, area.width, 5), new Color(accent.r, accent.g, accent.b, reveal));
            DrawResultLabel(new Rect(area.x + 26, area.y + 22, area.width - 160, 30),
                name, resultName, reveal, accent);
            if (winner)
            {
                Rect badge = new Rect(area.xMax - 112, area.y + 24, 86, 26);
                MenuPresentation.Paint(badge, new Color(accent.r, accent.g, accent.b, reveal));
                DrawResultLabel(badge, "WINNER", resultWinBadge, reveal, MenuPresentation.Paper);
            }
            int shownPoints = Mathf.RoundToInt(Mathf.Lerp(0f, points, reveal));
            DrawResultLabel(new Rect(area.x + 24, area.y + 62, area.width - 48, 108),
                shownPoints.ToString(), resultPoints, reveal, MenuPresentation.Paper);
            DrawResultLabel(new Rect(area.x + 28, area.y + 170, area.width - 56, 22),
                "DELIVERY SCORE", resultSmall, reveal, MenuPresentation.Paper);
            MenuPresentation.Paint(new Rect(area.x + 26, area.y + 207, area.width - 52, 1), new Color(1, 1, 1, .1f * reveal));
            DrawResultLabel(new Rect(area.x + 28, area.y + 218, 200, 24),
                "ROUND WINS", resultSmall, reveal, MenuPresentation.Paper);
            DrawRoundWinMarkers(new Rect(area.xMax - 122, area.y + 218, 96, 24), wins, accent, reveal, true);
        }

        void DrawRoundWinMarkers(Rect area, int wins, Color accent, float alpha = 1f, bool menuColours = false)
        {
            int target = GameManager.RoundsPerMatch;
            wins = Mathf.Clamp(wins, 0, target);
            float gap = area.height * .4f;
            float size = Mathf.Min(area.height, (area.width - gap * (target - 1)) / target);
            float x = area.center.x - (target * size + (target - 1) * gap) * .5f;
            for (int i = 0; i < target; i++)
            {
                var marker = new Rect(x + i * (size + gap), area.y, size, size);
                Color colour = i < wins ? accent : new Color(.28f, .34f, .43f);
                Color face = new Color(colour.r, colour.g, colour.b, alpha);
                Color inset = new Color(18f / 255f, 24f / 255f, 36f / 255f, alpha);
                if (menuColours)
                {
                    face = MenuPresentation.DisplayColor(face);
                    inset = MenuPresentation.DisplayColor(new Color(62f / 255f, 46f / 255f, 65f / 255f, alpha));
                }
                Fill(marker, face);
                if (i >= wins)
                    Fill(new Rect(marker.x + 2, marker.y + 2, marker.width - 4, marker.height - 4), inset);
            }
        }

        void DrawResultLabel(Rect area, string value, GUIStyle style, float reveal, Color colour)
        {
            DrawRevealLabel(area, value, style, reveal, MenuPresentation.DisplayColor(colour));
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
