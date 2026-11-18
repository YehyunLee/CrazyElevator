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
        readonly float[] leadGlow = new float[2];
        readonly int[] targetTrack = { 0, 1 };
        readonly float[] trackPosition = { 0f, 1f };
        float collisionCooldown;
        float collisionFlash;
        const float CollisionFlashSeconds = .52f;
        Vector3 collisionPoint;
        Color collisionAccent;
        bool collisionContactActive;
        bool bouncerCollision;
        const int BumpRewardPoints = 50;
        const int ContestedDeliveryBonus = 30;
        const float DisplacedPickupSeconds = 6f;
        readonly (int floor, int stop, float until)[] displacedPickups = new (int, int, float)[2];
        SharedRider matchVip;
        Texture2D vipBadge;
        int vipClaimSeat = -1;
        const float VipClaimSeconds = 1.1f;
        float vipClaimTime;
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
        const float ResultRevealInputAt = 4.9f;
        const float WinnerSpotlightAt = 3.5f;
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
        readonly List<int> dimensionRoulette = new List<int>();
        readonly Texture2D[] rouletteArt = new Texture2D[5];
        static readonly string[] RouletteArtNames = { "office", "candy", "underwater", "forest", "space" };
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
                int lead = player.HudScore.CompareTo(Rival.HudScore);
                leadGlow[0] = Mathf.MoveTowards(leadGlow[0], lead > 0 ? 1f : 0f, dt * 5f);
                leadGlow[1] = Mathf.MoveTowards(leadGlow[1], lead < 0 ? 1f : 0f, dt * 5f);
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
            leadGlow[0] = leadGlow[1] = 0f;
            collisionContactActive = false;
            bouncerCollision = false;
            collisionCount = 0;
            System.Array.Clear(displacedPickups, 0, displacedPickups.Length);
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
            vipClaimSeat = -1;
            vipClaimTime = 0f;
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
            if (!Running || MenuManager.TutorialActive || actor == null || rider == null
                || rider != matchVip || rider.BoardedBySeat != actor.Seat) return;
            vipClaimSeat = actor.Seat;
            vipClaimTime = VipClaimSeconds;
        }

        void UpdateRivalInteractions(float dt)
        {
            vipClaimTime = Mathf.Max(0f, vipClaimTime - dt);
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
                SharedRider arrivingVip = player.ChooseMatchVip(Rival);
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

        bool VipWaiting => matchVip != null && !matchVip.Boarded && !matchVip.Resolved;

        internal bool TryFrameRival(Game actor, out Vector2 rivalPosition, out float blend)
        {
            rivalPosition = default;
            blend = 0f;
            Game other = Other(actor);
            if (!Running || Paused || Finished || other == null || actor.ShiftFinished || other.ShiftFinished)
                return false;

            rivalPosition = other.ShaftRoutePosition;
            Vector2 separation = rivalPosition - actor.ShaftRoutePosition;
            float proximity = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(5f, 22f, separation.magnitude));
            float closingSpeed = Vector2.Dot(actor.ShaftRouteVelocity - other.ShaftRouteVelocity,
                separation.normalized);
            float closing = Mathf.InverseLerp(1f, 6f, closingSpeed);
            bool racing = VipWaiting && actor.NearingVipPickup(matchVip) && other.NearingVipPickup(matchVip);
            blend = proximity * Mathf.Max(racing ? 1f : closing, collisionFlash / CollisionFlashSeconds);
            return blend > 0f;
        }

        void BeginRoundIntroduction()
        {
            dimensionRevealIndex = GameManager.Instance != null ? GameManager.Instance.ActiveDimension : 0;
            dimensionRoulette.Clear();
            var session = GameManager.Instance;
            if (session != null)
                for (int dimension = 0; dimension < ElevatorRound.Dimensions; dimension++)
                    if (session.IsDimensionEnabled(dimension)) dimensionRoulette.Add(dimension);
            if (dimensionRoulette.Count == 0) dimensionRoulette.Add(dimensionRevealIndex);
            ShuffleDimensionRoulette();
            dimensionRoulette.Remove(dimensionRevealIndex);
            dimensionRoulette.Add(dimensionRevealIndex);
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
            if (dimensionRevealTime < DimensionRouletteSeconds + Game.DimensionRevealConfirmDelay
                || !(player.DimensionRevealConfirmed || Rival != null && Rival.DimensionRevealConfirmed)) return;
            player.FinishDimensionReveal();
            Rival?.FinishDimensionReveal();
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            dimensionRoulette.Clear();
            if (!MenuManager.BeginRoundPassengerIntroduction(player, Rival))
            {
                player.BeginRoundCountdown();
                Rival?.BeginRoundCountdown();
            }
        }

        const float DimensionRouletteSeconds = 3.6f;

        void ShuffleDimensionRoulette()
        {
            for (int i = dimensionRoulette.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                (dimensionRoulette[i], dimensionRoulette[swap]) =
                    (dimensionRoulette[swap], dimensionRoulette[i]);
            }
        }

        void DrawDimensionRoulette()
        {
            if (dimensionRoulette.Count == 0 || Event.current.type != EventType.Repaint) return;
            float elapsed = Mathf.Min(dimensionRevealTime, DimensionRouletteSeconds);
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .22f))
                * (1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.Clamp01((elapsed - DimensionRouletteSeconds + .22f) / .22f)));
            Color backdrop = new Color(MenuPresentation.Wall.r, MenuPresentation.Wall.g,
                MenuPresentation.Wall.b, .94f * fade);
            GUI.color = MenuPresentation.DisplayColor(backdrop);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            const float spinDuration = 2.9f;
            int shownDimension = dimensionRevealIndex;
            bool spinning = elapsed < spinDuration;
            if (spinning)
            {
                float progress = Mathf.Clamp01(elapsed / (spinDuration - .2f));
                float totalSteps = dimensionRoulette.Count * 3f - 1f;
                float easedStops = (1f - Mathf.Pow(1f - progress, 3f)) * totalSteps;
                int index = Mathf.FloorToInt(easedStops) % dimensionRoulette.Count;
                shownDimension = dimensionRoulette[index];
            }

            float scale = MatchHudScale;
            float width = Mathf.Min(Screen.width * .62f, 520f * scale);
            float height = Mathf.Min(Screen.height * .7f, 320f * scale);
            var plate = new Rect((Screen.width - width) * .5f,
                (Screen.height - height) * .5f, width, height);
            Color previousGuiColour = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            MenuPresentation.HudPlate(plate, spinning ? MenuPresentation.Brass : MenuPresentation.Teal);
            DrawHudLabel(new Rect(plate.x + 8f * scale, plate.y + 3f * scale,
                    plate.width - 16f * scale, 22f * scale),
                "ROUND " + (GameManager.Instance != null ? GameManager.Instance.RoundNumber : 1)
                    + "  ·  " + (spinning ? "SELECTING WORLD" : "WORLD SELECTED"),
                tileCaption, MenuPresentation.Paper);
            if (shownDimension >= 0 && shownDimension < rouletteArt.Length)
            {
                if (rouletteArt[shownDimension] == null)
                    rouletteArt[shownDimension] = Resources.Load<Texture2D>("WorldRoulette/" + RouletteArtNames[shownDimension]);
                Texture2D art = rouletteArt[shownDimension];
                if (art != null)
                    GUI.DrawTexture(new Rect(plate.x + 12f * scale, plate.y + 29f * scale,
                        plate.width - 24f * scale, height - 102f * scale), art, ScaleMode.ScaleToFit, true);
            }
            DrawHudLabel(new Rect(plate.x + 8f * scale, plate.yMax - 68f * scale,
                    plate.width - 16f * scale, 60f * scale),
                GameManager.DimensionDisplayName(shownDimension), sharedClock, MenuPresentation.Paper);
            GUI.color = previousGuiColour;
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
            // Both learners can practice at the same stop without blocking each other.
            if (MenuManager.TryGetTutorialStopFloor(actor, out _)) return candidate;
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
            var displaced = displacedPickups[actor.Seat];
            bool claimedAfterBump = displaced.until > 0f && actor.SecondsLeft >= displaced.until
                && rider.Origin == displaced.floor && rider.OriginStop == displaced.stop;
            if (other == null || (!claimedAfterBump && !other.ApproachingPickup(rider)
                && !(rider == matchVip && other.NearingVipPickup(rider)))) return;

            if (claimedAfterBump) displacedPickups[actor.Seat] = default;
            rider.ContestedPickupBonus = ContestedDeliveryBonus;
            if (rider == matchVip) return;
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
            Game bumped = bumpWinner == null ? null : bumpWinner == first ? second : first;
            int pickupFloor = -1, pickupStop = -1;
            bool wasPickingUp = bumped != null && bumped.TryGetPickupStop(out pickupFloor, out pickupStop);
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
            collisionFlash = CollisionFlashSeconds;
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
            }
            if (first != null && second != null)
            {
                collisionPoint = (first.ShaftWorldPosition + second.ShaftWorldPosition) * .5f;
                collisionAccent = bumpWinner != null ? SeatAccent(bumpWinner.Seat) : Gold;
                EmitCollisionSparks(collisionPoint);
                if (!MenuManager.TutorialActive)
                {
                    Vector2 recoil = verticalDirection != 0f ? Vector2.down * verticalDirection
                        : first.ShaftRoutePosition - second.ShaftRoutePosition;
                    if (recoil.sqrMagnitude < .01f) recoil = Vector2.left;
                    first.KickShaftCamera(recoil);
                    second.KickShaftCamera(-recoil);
                }
            }
            SfxManager.Instance?.PlaySfx(collisionClip, 1f);
            if (bouncerCollision)
            {
                first?.PulseMatchImpact(true);
                second?.PulseMatchImpact(true);
            }

            if (bumpWinner != null && bumpWinner.AwardBumpPoints(BumpRewardPoints, collisionPoint)
                && wasPickingUp && !bumped.TryGetPickupStop(out _, out _))
                displacedPickups[bumpWinner.Seat] = (pickupFloor, pickupStop,
                    Mathf.Max(.001f, bumpWinner.SecondsLeft - DisplacedPickupSeconds));
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
            if (vipBadge != null) Destroy(vipBadge);
            if (leadEdgeTexture != null) Destroy(leadEdgeTexture);
            if (resultTheaterMaterial != null) Destroy(resultTheaterMaterial);
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
        [System.NonSerialized] GUIStyle heading, text, score, centred, tileCaption, tileValue,
            resultTitle, resultName, resultPoints, resultWinner, roundStatus, sharedClock, sharedScore,
            resultSmall, resultWorld, resultAction, resultKey, resultWinBadge;
        [System.NonSerialized] Material resultTheaterMaterial;
        Texture2D leadEdgeTexture;

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
                if (dimensionRevealTime < DimensionRouletteSeconds)
                    DrawDimensionRoulette();
                else
                    player.DrawDimensionRevealOverlay(new Rect(0f, 0f, Screen.width, Screen.height),
                        dimensionRevealTime - DimensionRouletteSeconds);
                GUI.matrix = previousMatrix;
                GUI.color = previousColour;
                GUI.contentColor = previousContent;
                return;
            }
            DrawLeadEdges();
            if (IsVisibleSeat(0)) DrawSeat(player, 0, MenuPresentation.Coral);
            if (IsVisibleSeat(1)) DrawSeat(Rival, 1, MenuPresentation.Teal);
            if (SplitScreen) DrawSplitDivider();
            if (!MenuManager.TutorialActive)
            {
                DrawSharedRoundHUD();
                if (IsVisibleSeat(0)) DrawSeatScoreFlights(player, 0);
                if (IsVisibleSeat(1)) DrawSeatScoreFlights(Rival, 1);
            }
            if (Finished) DrawResultReveal();
            DrawSessionFade(player.SessionFadeAlpha);
            GUI.matrix = previousMatrix;
            GUI.color = previousColour;
            GUI.contentColor = previousContent;
        }

        void DrawLeadEdges()
        {
            if (Event.current.type != EventType.Repaint || !Running || Paused || Finished
                || MenuManager.TutorialActive || player.ShiftFinished) return;
            var session = GameManager.Instance;
            int introSeat = AbilityIntroPaneSeat;
            if (session != null && session.AbilityIntroActive && introSeat < 0) return;

            float remaining = session != null ? session.MainTimer : player.SecondsLeft;
            float urgency = remaining > 0f && remaining <= GameManager.FinalCountdownSeconds
                ? .14f + .18f * GameManager.FinalCountdownBeat(remaining)
                : 0f;
            float musicPulse = MusicManager.Instance != null ? MusicManager.Instance.BassPulse : 0f;
            for (int seat = 0; seat < leadGlow.Length; seat++)
            {
                if (leadGlow[seat] <= .001f || seat == introSeat) continue;
                if (leadEdgeTexture == null)
                    leadEdgeTexture = CreateLeadEdgeTexture();

                Rect view = ViewRect(seat);
                float width = Mathf.Min(view.width * .18f,
                    view.height * (.065f + urgency * .05f + musicPulse * .068f));
                var edge = new Rect(seat == 0 ? view.xMin : view.xMax - width, view.y, width, view.height);
                var uv = seat == 0 ? new Rect(0f, 0f, 1f, 1f) : new Rect(1f, 0f, -1f, 1f);
                Color tint = SeatAccent(seat);
                tint.a = leadGlow[seat] * (.34f + urgency + musicPulse * .19f);
                GUI.color = MenuPresentation.DisplayColor(tint);
                GUI.DrawTextureWithTexCoords(edge, leadEdgeTexture, uv);
            }
            GUI.color = Color.white;
        }

        static Texture2D CreateLeadEdgeTexture()
        {
            const int width = 64;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
            {
                name = "Score leader glow",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[width];
            for (int x = 0; x < width; x++)
            {
                float opacity = 1f - (float)x / (width - 1);
                pixels[x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * opacity * opacity));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
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
            sharedScore = new GUIStyle(tileValue) { fontSize = 40, clipping = TextClipping.Clip };
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

        float MatchHudScale => MenuPresentation.GameplayHudScale(ViewRect(0).width, Screen.height);
        const float ScoreboardWidth = 520f;
        const float SeatInfoWidth = 176f;

        int AbilityIntroPaneSeat => GameManager.Instance != null ? GameManager.Instance.AbilityIntroPaneSeat : -1;

        Rect SharedHudRect(float width, float top, float height)
        {
            float scale = MatchHudScale;
            width *= scale;
            top *= scale;
            height *= scale;
            int introSeat = AbilityIntroPaneSeat;
            if (introSeat < 0) return new Rect((Screen.width - width) * .5f, top, width, height);

            // The ability intro leaves the other player in control of their pane.
            Rect activeView = ViewRect(1 - introSeat);
            width = Mathf.Min(width, activeView.width - (SeatInfoWidth + 28f) * scale);
            float x = introSeat == 0 ? activeView.x + 10f * scale : activeView.xMax - width - 10f * scale;
            return new Rect(x, top, width, height);
        }

        Rect SharedScoreboardRect => SharedHudRect(ScoreboardWidth, 8f, 70f);
        Rect SharedRoundStatusRect => SharedHudRect(ScoreboardWidth, 84f, 26f);

        Rect SeatInfoRect(int seat)
        {
            Rect view = ViewRect(seat);
            float scale = MatchHudScale;
            float barWidth = SeatInfoWidth * scale;
            float barX = SplitScreen && seat == 1 ? view.xMax - 8f * scale - barWidth : view.x + 8f * scale;
            bool narrow = !SplitScreen && view.width < (ScoreboardWidth + 2f * (SeatInfoWidth + 16f)) * scale;
            float top = narrow ? SharedRoundStatusRect.yMax + 8f * scale : view.y + 8f * scale;
            return new Rect(barX, top, barWidth, 58f * scale);
        }

        static Rect SharedScoreTile(Rect board, int seat)
        {
            float width = board.width * .34f;
            return new Rect(seat == 0 ? board.x : board.xMax - width, board.y, width, board.height);
        }

        void DrawSeatScoreFlights(Game actor, int seat)
        {
            if (actor == null) return;
            Rect view = ViewRect(seat);
            Vector2 anchor = SharedScoreTile(SharedScoreboardRect, seat).center - view.position;
            anchor.y += 11f * MatchHudScale;
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
            if (MenuManager.TutorialActive) return;
            Rect bar = SeatInfoRect(seat);
            MenuPresentation.HudPlate(bar, colour);
            float scale = MatchHudScale;
            Rect badge = new Rect(bar.x + 6f * scale, bar.y + 7f * scale,
                bar.width * .59f - 12f * scale, bar.height - 14f * scale);
            MenuPresentation.Plaque(badge, colour, MenuPresentation.Metal, 2f);
            DrawHudLabel(badge, "PLAYER " + (seat + 1), roundStatus, MenuPresentation.Ink);

            Rect floorTile = Rect.MinMaxRect(badge.xMax + 6f * scale, bar.y, bar.xMax, bar.yMax);
            DrawSeatTile(floorTile, "FLOOR", ElevatorRound.FloorNumber(Mathf.Clamp(
                Mathf.RoundToInt(actor.CurrentFloor), actor.ActiveMinFloor, actor.ActiveMaxFloor)).ToString(),
                MenuPresentation.Paper);
            if (seat == vipClaimSeat && vipClaimTime > 0f && !Finished)
                DrawVipClaim(new Vector2(badge.center.x, bar.yMax + 22f * scale), colour, scale);
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
                        float markerTop = view.y + 174f * MatchHudScale;
                        float y = Mathf.Clamp(Screen.height - target.y - markerHeight,
                            markerTop, Mathf.Max(markerTop, view.yMax - 80f - markerHeight));
                        Rect marker = new Rect(x - 54f, y, 108f, markerHeight);
                        Fill(marker, Ink);
                        var targetLabel = new GUIStyle(centred) { fontSize = 18 };
                        targetLabel.normal.textColor = Gold;
                        GUI.Label(marker, "↓ RIVAL", targetLabel);
                    }
                }
            }
            if (MenuManager.TutorialActive || Finished || actor.IntroPlaying || actor.CountdownPlaying) return;
            var label = new GUIStyle(centred)
                { fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .032f), 13, 20), wordWrap = false };
            if (contestMessageTimes[seat] > 0f || eventMessageTime > 0f)
            {
                float messageY = Mathf.Max(hintTop, view.y + view.height * .18f);
                Rect message = new Rect(view.x + view.width * .15f, messageY, view.width * .7f, 38f);
                Fill(message, new Color(Ink.r, Ink.g, Ink.b, .88f));
                label.normal.textColor = contestMessageTimes[seat] > 0f ? Gold : Coral;
                GUI.Label(message, contestMessageTimes[seat] > 0f ? contestMessages[seat] : eventMessage, label);
            }
            Camera camera = actor.Traveling ? actor.ShaftCamera : actor.CabinCamera;
            if (camera == null || !camera.enabled) return;
            if (actor.Traveling)
            {
                DrawVipNavigator(actor, view, camera);
                DrawCollisionFeedback(view, camera);
            }
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
            if (!VipWaiting || Event.current.type != EventType.Repaint
                || player == null || !player.TryGetWaitingPassengerWorldPosition(matchVip, out Vector3 worldPosition)) return;

            Vector3 projected = camera.WorldToScreenPoint(worldPosition + Vector3.up * 3.1f);
            Vector2 target = GuiPoint(projected);
            Vector2 direction = target - view.center;
            if (projected.z < 0f) direction = -direction;

            float scale = MatchHudScale;
            float sideInset = 42f * scale;
            Rect safeView = Rect.MinMaxRect(view.xMin + sideInset, SharedRoundStatusRect.yMax + 36f * scale,
                view.xMax - sideInset, view.yMax - 88f * scale);
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

            float size = 44f * scale;
            if (targetVisible)
            {
                float clock = Time.unscaledTime;
                float bob = Mathf.Sin(clock * 6f) * 4f * scale;
                DrawVipIcon(marker + Vector2.up * (bob - 16f * scale), size);
                Vector3 landing = camera.WorldToScreenPoint(worldPosition);
                float wave = Mathf.Repeat(clock * .75f, 1f);
                if (landing.z > 0f && safeView.Contains(GuiPoint(landing)))
                    DrawRivalPulse(GuiPoint(landing), new Vector2(1f, .36f)
                        * (28f + 50f * wave) * scale, Gold, (1f - wave) * .9f, 3f * scale);
                if (player.NearingVipPickup(matchVip) && Rival.NearingVipPickup(matchVip))
                {
                    DrawVipApproach(camera, view, marker, player, -1f, clock, scale);
                    DrawVipApproach(camera, view, marker, Rival, 1f, clock, scale);
                }
                return;
            }

            DrawVipIcon(marker - direction * 42f * scale, size);
            Rect arrowBounds = new Rect(marker.x - size * .5f, marker.y - size * .5f, size, size);
            Fill(arrowBounds, Ink);
            var arrow = new GUIStyle(centred)
            {
                fontSize = Mathf.Max(1, Mathf.RoundToInt(size * .72f)),
                wordWrap = false
            };
            arrow.normal.textColor = Gold;
            GUI.Label(arrowBounds, NavigationArrow(direction), arrow);
        }

        static string NavigationArrow(Vector2 direction)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            int index = Mathf.RoundToInt((angle + 180f) / 45f) % VipNavigationArrows.Length;
            return VipNavigationArrows[index];
        }

        static Color SeatAccent(int seat) => seat == 0 ? MenuPresentation.Coral : MenuPresentation.Teal;
        static Vector2 GuiPoint(Vector3 projected) => new Vector2(projected.x, Screen.height - projected.y);

        void DrawVipIcon(Vector2 center, float size)
        {
            if (vipBadge == null) vipBadge = PassengerIconLibrary.CreatePassengerBadge("VIP", Gold, Ink, Gold);
            GUI.DrawTexture(new Rect(center.x - size * .5f, center.y - size * .5f, size, size), vipBadge);
        }

        void DrawVipApproach(Camera camera, Rect view, Vector2 target, Game actor, float side, float clock, float scale)
        {
            Vector2 start = GuiPoint(camera.WorldToScreenPoint(actor.ShaftWorldPosition));
            Vector2 fromTarget = (start - target).normalized;
            if ((start - target).sqrMagnitude < 16f * scale * scale) fromTarget = Vector2.right * side;
            // Keep the two colors readable when both cars are on one shaft.
            Vector2 across = new Vector2(-fromTarget.y, fromTarget.x);
            float beat = Mathf.Repeat(clock * 1.35f, 1f);
            Vector2 tip = target + fromTarget * ((65f - 31f * beat) * scale) + across * (side * 8f * scale);
            Rect arrowBounds = new Rect(tip.x - 18f * scale, tip.y - 18f * scale, 36f * scale, 36f * scale);
            if (!view.Contains(arrowBounds.min) || !view.Contains(arrowBounds.max)) return;
            var arrow = new GUIStyle(centred) { fontSize = Mathf.Max(1, Mathf.RoundToInt(34f * scale)) };
            arrow.normal.textColor = SeatAccent(actor.Seat);
            GUI.Label(arrowBounds, NavigationArrow(-fromTarget), arrow);
        }

        void DrawVipClaim(Vector2 centre, Color accent, float scale)
        {
            if (Event.current.type != EventType.Repaint) return;
            float progress = 1f - vipClaimTime / VipClaimSeconds;
            float pop = Mathf.Sin(Mathf.Clamp01(progress * 2.5f) * Mathf.PI);
            DrawRivalPulse(centre, Vector2.one * (36f + 30f * progress) * scale,
                accent, 1f - progress, 3f * scale);
            Color saved = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((1f - progress) * 4f));
            DrawVipIcon(centre, (33f + 12f * pop) * scale);
            GUI.color = saved;
        }

        static void DrawRivalPulse(Vector2 centre, Vector2 size, Color colour, float alpha, float thickness)
        {
            Rect bounds = new Rect(centre - size * .5f, size);
            colour.a = alpha;
            GUI.DrawTexture(bounds, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                colour, Mathf.Max(1f, thickness), size.y * .5f);
        }

        // Both seats read GameManager's round clock; draw it once across the split.
        void DrawSharedRoundHUD()
        {
            var session = GameManager.Instance;
            float remaining = session != null ? session.MainTimer : player.SecondsLeft;
            int seconds = Mathf.CeilToInt(remaining);
            string clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            float scale = MatchHudScale;
            Rect board = SharedScoreboardRect;
            Rect leftScore = SharedScoreTile(board, 0);
            Rect rightScore = SharedScoreTile(board, 1);
            DrawSharedScore(leftScore, player, 0);
            DrawSharedScore(rightScore, Rival, 1);
            float gap = 6f * scale;
            Rect timer = Rect.MinMaxRect(leftScore.xMax + gap, board.y, rightScore.xMin - gap, board.yMax);
            Color clockFace = seconds <= GameManager.FinalCountdownSeconds ? MenuPresentation.Coral : MenuPresentation.Paper;
            MenuPresentation.Plaque(timer, clockFace, MenuPresentation.Metal, 3f * scale);
            DrawHudLabel(new Rect(timer.x, timer.y + 6f * scale, timer.width, 16f * scale),
                "TIME", tileCaption, MenuPresentation.Ink);
            DrawHudLabel(new Rect(timer.x, timer.y + 22f * scale, timer.width, 40f * scale),
                clock, sharedClock, MenuPresentation.Ink);
            if (session == null) return;
            Rect status = SharedRoundStatusRect;
            float statusWidth = status.width;
            MenuPresentation.HudPlate(status, MenuPresentation.Brass);
            var badge = new Rect(status.x + 4f * scale, status.y + 4f * scale,
                statusWidth * .22f - 4f * scale, status.height - 6f * scale);
            MenuPresentation.Paint(badge, MenuPresentation.Ink);
            DrawHudLabel(badge, "ROUND " + session.RoundNumber + " / " + GameManager.RoundsPerMatch,
                roundStatus, MenuPresentation.Paper);
            DrawHudLabel(new Rect(badge.xMax + 4f * scale, status.y + 2f * scale,
                statusWidth * .36f - 8f * scale, status.height - 2f * scale),
                session.ActiveDimensionName, roundStatus, MenuPresentation.Paper);
            float winsWidth = statusWidth * .21f;
            for (int seat = 0; seat < 2; seat++)
            {
                float x = status.x + statusWidth * .58f + winsWidth * seat;
                Color accent = seat == 0 ? MenuPresentation.Coral : MenuPresentation.Teal;
                DrawHudLabel(new Rect(x, status.y, 26f * scale, status.height), "P" + (seat + 1),
                    roundStatus, accent);
                DrawRoundWinMarkers(new Rect(x + 26f * scale, status.y + 9f * scale,
                    winsWidth - 28f * scale, 9f * scale),
                    seat == 0 ? session.PlayerOneWins : session.PlayerTwoWins, accent, menuColours: true);
            }
        }

        void DrawSharedScore(Rect plate, Game actor, int seat)
        {
            float scale = MatchHudScale;
            float pulse = actor.HudScorePulse;
            Color accent = SeatAccent(seat);
            MenuPresentation.HudPlate(plate, Color.Lerp(accent, MenuPresentation.Brass, pulse));
            DrawHudLabel(new Rect(plate.x, plate.y + 5f * scale, plate.width, 16f * scale),
                actor.IsNpc ? "CPU SCORE" : "P" + (seat + 1) + " SCORE", tileCaption, accent);

            Rect number = new Rect(plate.x + 6f * scale, plate.y + 19f * scale,
                plate.width - 12f * scale, 47f * scale);
            var value = new GUIContent(actor.HudScore.ToString());
            sharedScore.fontSize = Mathf.Max(1, Mathf.RoundToInt((40f + 5f * pulse) * scale));
            float available = Mathf.Clamp01(number.width / Mathf.Max(1f, sharedScore.CalcSize(value).x));
            sharedScore.fontSize = Mathf.Max(1, Mathf.FloorToInt(sharedScore.fontSize * available));
            MenuPresentation.Label(number, value, sharedScore,
                Color.Lerp(MenuPresentation.Paper, MenuPresentation.Brass, pulse));
            if (Running && !Finished && !MenuManager.TutorialActive && leadGlow[seat] > .01f)
            {
                float musicPulse = MusicManager.Instance != null ? MusicManager.Instance.BassPulse : 0f;
                float size = (22f + 4f * musicPulse) * scale;
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, leadGlow[seat]);
                DrawVipIcon(new Vector2(plate.xMax - 18f * scale, plate.y + 16f * scale), size);
                GUI.color = previous;
            }
        }

        void DrawHudLabel(Rect area, string value, GUIStyle style, Color colour)
        {
            int previousSize = style.fontSize;
            style.fontSize = Mathf.Max(1, Mathf.RoundToInt(previousSize * MatchHudScale));
            MenuPresentation.Label(area, new GUIContent(value), style, colour);
            style.fontSize = previousSize;
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
            float scale = MatchHudScale;
            MenuPresentation.Paint(new Rect(area.x, area.y + 11f * scale, scale, area.height - 22f * scale),
                MenuPresentation.Ink);
            DrawHudLabel(new Rect(area.x, area.y + 5f * scale, area.width, 18f * scale),
                caption, tileCaption, MenuPresentation.Paper);
            DrawHudLabel(new Rect(area.x, area.y + 21f * scale, area.width, 30f * scale),
                value, tileValue, valueColour);
        }

        void DrawCollisionFeedback(Rect view, Camera camera)
        {
            if (collisionFlash <= 0f || Event.current.type != EventType.Repaint) return;
            Vector3 projected = camera.WorldToScreenPoint(collisionPoint);
            Vector2 point = GuiPoint(projected);
            if (projected.z <= 0f || !view.Contains(point)) return;
            float alpha = collisionFlash / CollisionFlashSeconds;
            float scale = MatchHudScale;
            float size = Mathf.Lerp(20f, bouncerCollision ? 160f : 108f, 1f - alpha) * scale;
            GUI.BeginGroup(view);
            point -= view.position;
            DrawRivalPulse(point, Vector2.one * size, collisionAccent, alpha, 4f * scale);
            DrawRivalPulse(point, Vector2.one * (size * .68f), Gold, alpha * .85f, 2f * scale);
            GUI.EndGroup();
        }

        static readonly Color TheaterBack = new Color32(28, 19, 32, 255);
        static readonly Vector2 PlayerOneStageLight = new Vector2(464f, 411f);
        static readonly Vector2 PlayerTwoStageLight = new Vector2(976f, 411f);

        void DrawResultReveal()
        {
            float time = ResultRevealTime;
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            const float blackoutEnd = .38f;
            if (time < blackoutEnd)
            {
                MenuPresentation.Paint(screen, new Color(0f, 0f, 0f, Reveal01(time / blackoutEnd)));
                return;
            }

            GameManager session = GameManager.Instance;
            bool matchFinished = session != null && session.MatchFinished;
            int winner = matchFinished ? session.MatchWinner
                : player.Points == Rival.Points ? -1 : player.Points > Rival.Points ? 0 : 1;
            Color winnerColour = winner < 0 ? MenuPresentation.Brass : SeatAccent(winner);
            float scoresIn = Reveal01((time - 4.02f) / .68f);
            float winnerIn = Reveal01((time - WinnerSpotlightAt) / .34f);
            float open = Reveal01((time - .58f) / 1.04f);
            float lamp = Reveal01((time - 1.06f) / .34f) * Mathf.Lerp(1f, .46f, scoresIn);

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColour = GUI.color;
            Matrix4x4 stageMatrix = previousMatrix * MenuPresentation.Fit(screen, 1440f, 900f);
            try
            {
                MenuPresentation.Paint(screen, TheaterBack);
                GUI.matrix = stageMatrix;
                DrawStageCandidates(time, winner, winnerIn, 1f - scoresIn);

                Vector2 target = SpotlightPosition(time, winner);
                Vector2 glowingCenter = stageMatrix.MultiplyPoint3x4(target);
                Vector2 radii = stageMatrix.MultiplyVector(new Vector3(
                    Mathf.Lerp(winner < 0 ? 370f : 227f, 310f, scoresIn), 203f, 0f));
                GUI.matrix = previousMatrix;
                DrawTheaterLighting(screen, glowingCenter, radii, open, lamp,
                    Mathf.Lerp(.94f, .19f, scoresIn), winnerColour);

                GUI.matrix = stageMatrix;
                float suspense = Reveal01((time - 1.70f) / .38f)
                    * (1f - Reveal01((time - 3.08f) / .32f));
                DrawResultLabel(new Rect(220, 139, 1000, 55), "AND THE WINNER IS…", resultTitle,
                    suspense, MenuPresentation.Brass);

                string headline = winner < 0 ? matchFinished ? "MATCH TIED" : "IT’S A DRAW"
                    : "PLAYER " + (winner + 1) + (matchFinished ? " WINS THE MATCH" : " WINS");
                DrawResultLabel(new Rect(150, 156, 1140, 91), headline, resultWinner,
                    winnerIn, winnerColour);
                if (scoresIn > 0f)
                    DrawResultSummary(session, matchFinished, winner, time, scoresIn);

                GUI.matrix = previousMatrix;
                float stageFade = 1f - Reveal01((time - blackoutEnd) / .24f);
                if (stageFade > 0f)
                    MenuPresentation.Paint(screen, new Color(0f, 0f, 0f, stageFade));
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColour;
            }
        }

        static Vector2 SpotlightPosition(float time, int winner)
        {
            Vector2 start = new Vector2(720f, 407f);
            Vector2 left = PlayerOneStageLight;
            Vector2 right = PlayerTwoStageLight;
            Vector2 mid = new Vector2(690f, 454f);
            Vector2 target = winner < 0 ? start : winner == 0 ? left : right;
            if (time < 1.60f) return Vector2.Lerp(start, left, Reveal01((time - 1.05f) / .55f));
            if (time < 2.42f) return Vector2.Lerp(left, right, Reveal01((time - 1.60f) / .82f));
            if (time < 2.92f) return Vector2.Lerp(right, mid, Reveal01((time - 2.42f) / .50f));
            return Vector2.Lerp(mid, target, Reveal01((time - 2.92f) / (WinnerSpotlightAt - 2.92f)));
        }

        void DrawStageCandidates(float time, int winner, float winnerIn, float alpha)
        {
            if (alpha <= 0f) return;
            StageFill(new Rect(134, 543, 1172, 48), new Color32(67, 31, 47, 255), alpha);
            StageFill(new Rect(134, 543, 1172, 2), new Color32(151, 89, 58, 255), alpha);
            StageFill(new Rect(160, 592, 1120, 14), new Color32(41, 21, 36, 255), alpha);
            DrawStageElevator(PlayerOneStageLight.x, 0, winner == 0 ? winnerIn : 0f, alpha, time);
            DrawStageElevator(PlayerTwoStageLight.x, 1, winner == 1 ? winnerIn : 0f, alpha, time);
        }

        void DrawStageElevator(float x, int seat, float opening, float alpha, float time)
        {
            Color trim = SeatAccent(seat);
            Rect car = new Rect(x - 83, 323, 166, 163);
            StageFill(new Rect(car.x - 11, car.y - 9, car.width + 22, car.height + 17),
                new Color32(32, 25, 42, 255), alpha);
            StageFill(car, trim, alpha);
            Rect cabin = new Rect(car.x + 8, car.y + 14, car.width - 16, car.height - 21);
            StageFill(cabin, Color.Lerp(new Color32(73, 61, 84, 255), MenuPresentation.Brass, opening), alpha);
            float doorWidth = Mathf.Lerp(cabin.width * .5f - 1f, cabin.width * .17f, opening);
            Color door = Color.Lerp(MenuPresentation.Wall, trim, .28f);
            StageFill(new Rect(cabin.x, cabin.y, doorWidth, cabin.height), door, alpha);
            StageFill(new Rect(cabin.xMax - doorWidth, cabin.y, doorWidth, cabin.height), door, alpha);
            StageFill(new Rect(x - 29, car.y - 20, 58, 23), TheaterBack, alpha);
            StageFill(new Rect(x - 6, car.y - 11, 12, 5), trim, alpha);
            DrawResultLabel(new Rect(x - 155f, 494f, 310f, 38f), "PLAYER " + (seat + 1),
                resultTitle, alpha, trim);
            if (opening <= 0f) return;
            DrawStageSparkle(new Vector2(x, cabin.center.y), time, opening * alpha);
        }

        static void DrawStageSparkle(Vector2 center, float time, float alpha)
        {
            float length = 18f + 7f * Mathf.Sin(Mathf.PI * Reveal01((time - WinnerSpotlightAt) / .55f));
            StageFill(new Rect(center.x - length, center.y - 2f, length * 2f, 4f),
                MenuPresentation.Paper, alpha);
            StageFill(new Rect(center.x - 2f, center.y - length, 4f, length * 2f),
                MenuPresentation.Paper, alpha);
        }

        static void StageFill(Rect area, Color colour, float alpha)
        {
            colour.a *= alpha;
            MenuPresentation.Paint(area, colour);
        }

        void DrawTheaterLighting(Rect screen, Vector2 center, Vector2 radii,
            float open, float lamp, float darkness, Color accent)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (resultTheaterMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("ResultTheater");
                if (shader != null && shader.isSupported)
                    resultTheaterMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (resultTheaterMaterial == null)
            {
                // Keep the stage usable on a graphics backend without the theater shader.
                float curtain = screen.width * Mathf.Lerp(.5f, .084f, open);
                Color red = new Color32(109, 25, 49, 255);
                MenuPresentation.Paint(new Rect(0f, 0f, curtain, screen.height), red);
                MenuPresentation.Paint(new Rect(screen.xMax - curtain, 0f, curtain, screen.height), red);
                return;
            }

            resultTheaterMaterial.SetVector("_Theater", new Vector4(open, lamp, darkness, 1f));
            resultTheaterMaterial.SetVector("_Focus", new Vector4(center.x / screen.width, center.y / screen.height,
                radii.x / screen.width, radii.y / screen.height));
            resultTheaterMaterial.SetColor("_Accent", accent);
            Graphics.DrawTexture(screen, Texture2D.whiteTexture, resultTheaterMaterial);
        }

        void DrawResultSummary(GameManager session, bool matchFinished, int winner, float time, float reveal)
        {
            int dimension = session != null ? session.ActiveDimension : 0;
            Color worldColour = ResultWorldColour(dimension);
            StageFill(new Rect(528, 104, 144, 34), worldColour, .25f * reveal);
            DrawResultLabel(new Rect(528, 104, 144, 34),
                "ROUND " + (session != null ? session.RoundNumber : 1), resultTitle, reveal, worldColour);
            DrawResultLabel(new Rect(688, 104, 255, 34),
                session != null ? session.ActiveDimensionName : "OFFICE", resultName, reveal, MenuPresentation.Paper);
            DrawRevealScoreCard(new Rect(220, 285, 488, 256), "PLAYER 1", player.Points,
                session != null ? session.PlayerOneWins : 0, MenuPresentation.Coral, winner == 0, reveal);
            DrawRevealScoreCard(new Rect(732, 285, 488, 256), "PLAYER 2", Rival.Points,
                session != null ? session.PlayerTwoWins : 0, MenuPresentation.Teal, winner == 1, reveal);

            if (time < ResultRevealInputAt) return;
            int nextDimension = session == null ? (dimension + 1) % ElevatorRound.Dimensions
                : matchFinished ? session.FirstEnabledDimension : session.NextDimensionIndex;
            Color nextColour = ResultWorldColour(nextDimension);
            MenuPresentation.Paint(new Rect(220, 581, 1000, 112), new Color32(62, 46, 65, 255));
            MenuPresentation.Paint(new Rect(220, 581, 4, 112), nextColour);
            DrawResultLabel(new Rect(248, 599, 500, 24),
                matchFinished ? "REMATCH STARTS IN" : "NEXT WORLD", resultSmall, 1f, MenuPresentation.Paper);
            DrawResultLabel(new Rect(248, 627, 500, 44),
                GameManager.DimensionDisplayName(nextDimension), resultWorld, 1f, nextColour);

            if (matchFinished)
            {
                DrawFinalResultActionButton(new Rect(220, 731, 306, 64), "PLAY AGAIN", MenuPresentation.Teal, 0);
                DrawFinalResultActionButton(new Rect(557, 731, 306, 64), "RETURN TO LOBBY", MenuPresentation.Coral, 1);
                DrawFinalResultActionButton(new Rect(894, 731, 306, 64), "RETURN TO MAIN MENU", MenuPresentation.Brass, 2);
                return;
            }

            Rect button = new Rect(832, 605, 364, 64);
            bool hovered = button.Contains(Event.current.mousePosition);
            MenuPresentation.Paint(new Rect(button.x, button.y + 4, button.width, button.height), new Color(0, 0, 0, .4f));
            MenuPresentation.Paint(button, hovered ? Color.Lerp(MenuPresentation.Teal, MenuPresentation.Paper, .2f) : MenuPresentation.Teal);
            GUI.Label(new Rect(button.x + 24, button.y, button.width - 116, button.height), "NEXT ROUND", resultAction);
            var key = new Rect(button.xMax - 76, button.y + 15, 56, 34);
            MenuPresentation.Paint(key, new Color(0, 0, 0, .2f));
            MenuPresentation.Paint(new Rect(key.x + 1, key.y + 1, key.width - 2, key.height - 2), new Color(1, 1, 1, .2f));
            GUI.Label(key, ResultConfirmHint, resultKey);
            if (GUI.Button(button, GUIContent.none, GUIStyle.none)) ContinueMatch();
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
