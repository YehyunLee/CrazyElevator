using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    public sealed partial class MenuManager
    {
        const int TutorialWaitingStep = 8;
        const int LateralTravelStep = 9;
        const int OppositeMovementStep = 10;
        const float TutorialCardTravelSeconds = .42f;
        const float TutorialPracticeSeconds = 1.7f;
        const float BoardingCardSeconds = 3f;
        const float TutorialStoryLineSeconds = 2.8f;
        const float TutorialStoryFadeSeconds = .6f;
        const float TutorialHandoffFadeSeconds = .8f;
        const float TutorialCountdownSeconds = 3f;
        int tutorialCollisionSeat = -1;
        bool tutorialCollisionHitConfirmed;
        public static bool RoundPassengerIntroductionActive { get; private set; }
        bool roundPassengerIntroductionActive;
        readonly ElevatorManager[] roundPassengerActors = new ElevatorManager[2];
        readonly List<string> roundPassengerKinds = new List<string>();
        const float RoundPassengerSkipDelay = .7f;
        ElevatorManager roundPassengerPreviewActor;
        int roundPassengerIndex;
        float roundPassengerTimer;
        static readonly string[] TutorialCharacterKinds =
            { "COURIER", "INTERVIEW", "HUNTER", "PREGNANT", "BOSS", "GROUP", "BOUNCER", "ELDERLY", "HANDYMAN", "DIVER" };
        static readonly string[] TutorialCharacterNames =
            { "COURIER", "INTERVIEW CANDIDATE", "HUNTER", "MOM + BABY", "BOSS", "THE TRIO", "BOUNCER", "GRANDMA", "HANDYMAN", "DIVER" };
        static readonly string[] TutorialStoryLines =
        {
            "YOU'RE A BELLHOP FIGHTING FOR A PROMOTION.",
            "DROP OFF PASSENGERS FOR POINTS. AVOID HAZARDS.",
            "BEST OF 3 ROUNDS. 1:30 PER ROUND.",
            "WINNER GETS THE PROMOTION."
        };
        enum TutorialCardMotion
        {
            Hidden,
            Entering,
            Settled,
            Exiting
        }
        enum TutorialFinalePhase
        {
            None,
            Collision,
            Story,
            FadeToControls,
            Controls,
            Countdown
        }

        static bool tutorialOnLoad;
        public static bool TutorialActive { get; private set; }
        public static bool TutorialWillStart => tutorialOnLoad || TutorialActive;

        public static bool TutorialSidewaysPractice(ElevatorManager actor)
        {
            MenuManager menu = Current;
            if (menu == null || !menu.tutorialActive || actor == null) return false;
            int index = System.Array.IndexOf(menu.tutorialActors, actor);
            if (index < 0) return false;
            return menu.tutorialSteps[index] == LateralTravelStep
                || menu.tutorialCardMotions[index] == TutorialCardMotion.Exiting
                    && menu.tutorialNextSteps[index] == LateralTravelStep;
        }

        public static bool TryGetTutorialStopFloor(ElevatorManager actor, out int floor)
        {
            floor = -1;
            MenuManager menu = Current;
            if (menu == null || !menu.tutorialActive || actor == null) return false;
            int index = System.Array.IndexOf(menu.tutorialActors, actor);
            if (index < 0) return false;
            int step = menu.tutorialCardMotions[index] == TutorialCardMotion.Exiting
                ? menu.tutorialNextSteps[index] : menu.tutorialSteps[index];
            if (step != 5 || menu.tutorialTargetFloor[index] < 0) return false;
            floor = menu.tutorialTargetFloor[index];
            return true;
        }

        bool tutorialActive;
        bool tutorialIntroReady;
        TutorialFinalePhase tutorialFinalePhase;
        float tutorialFinaleTimer;
        int tutorialStoryIndex;
        readonly ElevatorManager[] tutorialActors = new ElevatorManager[2];
        readonly int[] tutorialSteps = { -1, -1 };
        readonly int[] tutorialNextSteps = { -1, -1 };
        readonly TutorialCardMotion[] tutorialCardMotions =
            { TutorialCardMotion.Hidden, TutorialCardMotion.Hidden };
        readonly float[] tutorialCardMotionTimes = new float[2];
        readonly float[] tutorialPracticeTimes = new float[2];
        readonly float[] tutorialCompletionTimes = new float[2];
        // How long the current step's card has been fully on screen.
        readonly float[] tutorialStepSettledTimes = new float[2];
        const float TutorialInfoCardSeconds = 1.8f;
        readonly bool[] tutorialResumeActorOnCardSettled = new bool[2];
        readonly int[] tutorialKickBaseline = new int[2];
        readonly int[] tutorialTravelStartBaseline = new int[2];
        readonly int[] tutorialFloorStopBaseline = new int[2];
        readonly float[] tutorialMoveSeconds = new float[2];
        readonly float[] tutorialMoveDistance = new float[2];
        readonly int[] tutorialFirstMoveDirection = { 1, 1 };
        readonly bool[] tutorialReachedSide = new bool[2];
        readonly bool[] tutorialRejoinedShaft = new bool[2];
        readonly float[] tutorialBoostSeconds = new float[2];
        readonly bool[] tutorialMoveCompleted = new bool[2];
        readonly bool[] tutorialBoostCompleted = new bool[2];
        readonly bool[] tutorialStopCompleted = new bool[2];
        readonly bool[] controllerInstructionConfirmed = new bool[2];
        readonly float[] tutorialLastFloor = new float[2];
        readonly int[] tutorialTargetFloor = new int[2];
        readonly Texture2D[] tutorialSpotlightMasks = new Texture2D[2];
        readonly Color32[][] tutorialSpotlightPixels = new Color32[2][];
        readonly Vector2[] tutorialSpotlightCenters = { new Vector2(-1f, -1f), new Vector2(-1f, -1f) };
        readonly Vector2[] tutorialSpotlightViewSizes = new Vector2[2];
        readonly float[] tutorialSpotlightRadii = new float[2];
        ElevatorManager tutorialGame;
        Texture2D tutorialDirectionArrowTexture;

        bool TutorialWaitingPanelActive
        {
            get
            {
                if (!tutorialActive) return false;
                for (int i = 0; i < tutorialActors.Length; i++)
                    if (tutorialActors[i] != null && tutorialSteps[i] == TutorialWaitingStep)
                        return true;
                return false;
            }
        }

        bool RequiresBothControllerInstructionConfirmations
            => tutorialActive ? tutorialActors[0] != null && tutorialActors[1] != null : playerTwoHumanOnLoad;

        void ResetControllerInstructionConfirmations()
        {
            controllerInstructionConfirmed[0] = false;
            controllerInstructionConfirmed[1] = false;
        }

        bool ToggleControllerInstructionConfirmation(int seat)
        {
            if (seat < 0 || seat >= controllerInstructionConfirmed.Length) return false;
            controllerInstructionConfirmed[seat] = true;
            return ControllerInstructionsReady();
        }

        bool ControllerInstructionsReady()
        {
            if (tutorialActive)
            {
                for (int seat = 0; seat < tutorialActors.Length; seat++)
                    if (tutorialActors[seat] != null && !controllerInstructionConfirmed[seat]) return false;
                return true;
            }
            return controllerInstructionConfirmed[0]
                && (!RequiresBothControllerInstructionConfirmations
                    || controllerInstructionConfirmed[1]);
        }

        bool ControllerInstructionsConfirmed()
        {
            InputManager inputManager = InputManager.Instance;
            if (inputManager == null) return false;
            InputManager.SeatInput playerOne = inputManager.ReadForTutorialControls(0, true);
            InputManager.SeatInput playerTwo = inputManager.ReadForTutorialControls(1, true);
            if (playerOne.StartPressed || playerOne.ConfirmPressed)
                ToggleControllerInstructionConfirmation(0);
            if (playerTwo.StartPressed || playerTwo.ConfirmPressed)
                ToggleControllerInstructionConfirmation(1);
            return ControllerInstructionsReady();
        }

        void InitializeTutorialOnLoad()
        {
            if (!tutorialOnLoad) return;
            tutorialOnLoad = false;
            InputManager.Instance?.ClearTutorialInputLocks();
            tutorialActive = true;
            TutorialActive = true;
            MusicManager.Instance?.SetTutorialMode(true);
            IsOpen = false;
            pauseOpen = false;
            tutorialIntroReady = false;
            tutorialFinalePhase = TutorialFinalePhase.None;
            tutorialFinaleTimer = 0f;
            tutorialStoryIndex = 0;
            tutorialGame = GameManager.Instance != null
                ? GameManager.Instance.Player : FindAnyObjectByType<ElevatorManager>();
            if (tutorialGame != null && tutorialGame.IsNpc && tutorialGame.Match != null
                && tutorialGame.Match.Rival != null && !tutorialGame.Match.Rival.IsNpc)
                tutorialGame = tutorialGame.Match.Rival;
            // Keep tutorial slots aligned with input seats. A CPU in seat 0
            // must not duplicate Player 2 into both slots or require its keys.
            tutorialActors[0] = tutorialGame != null && tutorialGame.Match != null
                ? (!tutorialGame.Match.player.IsNpc ? tutorialGame.Match.player : null)
                : tutorialGame;
            tutorialActors[1] = tutorialGame != null && tutorialGame.Match != null
                && tutorialGame.Match.Rival != null && !tutorialGame.Match.Rival.IsNpc
                ? tutorialGame.Match.Rival : null;
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                ElevatorManager actor = tutorialActors[i];
                tutorialSteps[i] = actor != null ? -1 : TutorialWaitingStep;
                tutorialNextSteps[i] = -1;
                tutorialCardMotions[i] = TutorialCardMotion.Hidden;
                tutorialCardMotionTimes[i] = 0f;
                tutorialPracticeTimes[i] = 0f;
                tutorialCompletionTimes[i] = 0f;
                tutorialResumeActorOnCardSettled[i] = false;
                actor?.SuppressEncounterCards();
                actor?.SetTutorialStops(true);
                tutorialKickBaseline[i] = actor != null ? actor.KickoutCount : 0;
                tutorialTravelStartBaseline[i] = actor != null ? actor.TravelStartCount : 0;
                tutorialFloorStopBaseline[i] = actor != null ? actor.FloorStopCount : 0;
                tutorialLastFloor[i] = actor != null ? actor.CurrentFloor : 0f;
                tutorialTargetFloor[i] = -1;
                tutorialMoveCompleted[i] = false;
                tutorialFirstMoveDirection[i] = 1;
                tutorialReachedSide[i] = false;
                tutorialRejoinedShaft[i] = false;
                tutorialBoostCompleted[i] = false;
                tutorialStopCompleted[i] = false;
            }
            if (showingControllerInstructions) PauseTutorialGame();
            InputManager.Instance?.SetView(GameView.ControlElevator);
        }

        void UpdateTutorial()
        {
            if (!tutorialActive) return;
            TutorialActive = true;
            InputManager inputManager = InputManager.Instance;
            InputManager.SeatInput input = inputManager != null
                ? inputManager.ReadForSeat(0, true) : default;
            InputManager.SeatInput secondInput = inputManager != null && tutorialActors[1] != null
                ? inputManager.ReadForSeat(1, true) : default;
            bool skipKey = Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
            bool skipController = false;
            foreach (Gamepad pad in Gamepad.all)
                if (pad.selectButton.wasPressedThisFrame) skipController = true;
            if (skipKey || skipController)
            {
                SkipTutorial();
                return;
            }
            if (showingControllerInstructions)
            {
                if (ControllerInstructionsConfirmed())
                    DismissControllerInstructions();
                return;
            }
            if (tutorialGame == null)
            {
                tutorialGame = GameManager.Instance != null
                    ? GameManager.Instance.Player : FindAnyObjectByType<ElevatorManager>();
                if (tutorialGame == null) return;
                for (int i = 0; i < tutorialActors.Length; i++)
                    tutorialActors[i]?.SuppressEncounterCards();
            }

            // Esc always exits, even when seat 0 plays on a controller and its
            // seat input ignores the keyboard.
            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (input.PausePressed || secondInput.PausePressed || escape)
            {
                ReturnToMainMenu();
                return;
            }

            if (tutorialFinalePhase != TutorialFinalePhase.None)
            {
                UpdateTutorialFinale();
                return;
            }

            // Let the authored reveal play once. As soon as the playable cabin
            // is ready, pause the real shift behind the first tutorial card.
            if (!tutorialIntroReady)
            {
                if (tutorialGame.IntroPlaying) return;
                if (tutorialGame.Match != null && !tutorialGame.Match.Running)
                    tutorialGame.Match.StartMatch();
                PauseTutorialGame();
                tutorialIntroReady = true;
                for (int i = 0; i < tutorialActors.Length; i++)
                {
                    if (tutorialActors[i] == null) continue;
                    tutorialCardMotions[i] = TutorialCardMotion.Entering;
                    tutorialCardMotionTimes[i] = 0f;
                }
                return;
            }

            for (int i = 0; i < tutorialActors.Length; i++)
            {
                if (tutorialActors[i] != null
                    && tutorialCardMotions[i] != TutorialCardMotion.Settled)
                    UpdateTutorialCardMotion(i);
            }

            // Input is already enabled during the card transition. Count real
            // movement then as well, so an early press is not lost.
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                if (tutorialActors[i] == null) continue;
                int movementStep = tutorialCardMotions[i] == TutorialCardMotion.Exiting
                    ? tutorialNextSteps[i] : tutorialSteps[i];
                if (movementStep == 3 || movementStep == OppositeMovementStep)
                    TrackTutorialMovement(i, movementStep);
                bool enteringSideways = tutorialCardMotions[i] == TutorialCardMotion.Exiting
                    && tutorialNextSteps[i] == LateralTravelStep;
                if (tutorialSteps[i] == LateralTravelStep || enteringSideways) TrackTutorialShaftReturn(i);
            }

            // Practice Shift is an orientation card, not another confirmation
            // step. Each player can reveal their own first action.
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                if (tutorialActors[i] == null
                    || tutorialCardMotions[i] != TutorialCardMotion.Settled)
                    continue;
                if (tutorialSteps[i] < 0)
                {
                    tutorialPracticeTimes[i] += Time.unscaledDeltaTime;
                    if (tutorialPracticeTimes[i] >= TutorialPracticeSeconds)
                        BeginTutorialCardTransition(i, 0, true);
                    continue;
                }

                int step = tutorialSteps[i];
                if (step == TutorialWaitingStep)
                {
                    tutorialCompletionTimes[i] += Time.unscaledDeltaTime;
                    continue;
                }

                tutorialStepSettledTimes[i] += Time.unscaledDeltaTime;
                if (TutorialStepCompletedForActor(i, step))
                    CompleteTutorialStep(i);
            }

            bool allPlayersFinished = true;
            bool allPlayersReady = true;
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                if (tutorialActors[i] == null) continue;
                allPlayersFinished &= tutorialSteps[i] == TutorialWaitingStep
                    && tutorialCompletionTimes[i] >= .3f;
                allPlayersReady &= tutorialSteps[i] >= 0
                    && tutorialCardMotions[i] == TutorialCardMotion.Settled;
            }
            if (allPlayersReady && tutorialGame != null
                && (tutorialGame.Match == null || tutorialGame.Match.Paused))
                ResumeTutorialGame();
            if (allPlayersFinished) BeginTutorialHandoff();
        }

        bool TutorialStepCompletedForActor(int index, int step)
        {
            ElevatorManager actor = tutorialActors[index];
            switch (step)
            {
                case 0:
                    return actor.HasChosenBoardingDoor
                        && tutorialStepSettledTimes[index] >= BoardingCardSeconds;
                case 1:
                    return actor.OpenBoardingDoorCount == 3
                        && actor.CurrentPassengers.Count >= ElevatorRound.PracticePassengersPerSeat
                        && tutorialStepSettledTimes[index] >= BoardingCardSeconds;
                case 2:
                    return actor.TravelStartCount > tutorialTravelStartBaseline[index];
                // Movement progress is gathered throughout the lesson transition.
                case 3:
                case OppositeMovementStep:
                    return tutorialMoveCompleted[index];
                case 4:
                    if (tutorialBoostCompleted[index]) return true;
                    if (actor.MovingInShaft && actor.BoostActive)
                        tutorialBoostSeconds[index] += Time.unscaledDeltaTime;
                    if (tutorialBoostSeconds[index] >= .5f)
                        tutorialBoostCompleted[index] = true;
                    return tutorialBoostCompleted[index];
                case 5:
                    if (tutorialStopCompleted[index]) return true;
                    if (actor.FloorStopCount > tutorialFloorStopBaseline[index]
                        && actor.AtStop
                        && (tutorialTargetFloor[index] >= 0
                            ? actor.DiscreteFloor == tutorialTargetFloor[index]
                            : TutorialHasPassengerAtCurrentFloor(actor)))
                        tutorialStopCompleted[index] = true;
                    return tutorialStopCompleted[index];
                // Drop-offs are automatic: this card just explains it once a rider has left.
                case 6:
                    return tutorialStepSettledTimes[index] >= TutorialInfoCardSeconds
                        && !TutorialHasPassengerAtCurrentFloor(actor);
                case 7:
                    if (actor.KickoutCount > tutorialKickBaseline[index]) return true;
                    actor.PrepareTutorialKickPassenger();
                    return false;
                case LateralTravelStep:
                    return tutorialRejoinedShaft[index];
                default:
                    return false;
            }
        }

        int TutorialMovementDirection(int index, int step)
            => tutorialFirstMoveDirection[index] * (step == OppositeMovementStep ? -1 : 1);

        void TrackTutorialMovement(int index, int step)
        {
            ElevatorManager actor = tutorialActors[index];
            float floor = actor.CurrentFloor;
            int direction = TutorialMovementDirection(index, step);
            if (actor.MovingInShaft && actor.TravelInputAxis * direction > .25f)
            {
                tutorialMoveSeconds[index] += Time.deltaTime;
                tutorialMoveDistance[index] += Mathf.Max(0f, (floor - tutorialLastFloor[index]) * direction);
                if (tutorialMoveSeconds[index] >= .2f && tutorialMoveDistance[index] >= .08f)
                    tutorialMoveCompleted[index] = true;
            }
            tutorialLastFloor[index] = floor;
        }

        void TrackTutorialShaftReturn(int index)
        {
            ElevatorManager actor = tutorialActors[index];
            if (actor.TutorialOffMainShaft) tutorialReachedSide[index] = true;
            if (!tutorialReachedSide[index] || tutorialRejoinedShaft[index] || !actor.OnMainVerticalTrack) return;
            tutorialRejoinedShaft[index] = true;
            actor.ShowActionFeedback("NICE!", true, true);
        }

        void CompleteTutorialStep(int index)
        {
            ElevatorManager actor = tutorialActors[index];
            int step = tutorialSteps[index];
            int nextStep = step + 1;
            if (step == 3)
                nextStep = OppositeMovementStep;
            else if (step == OppositeMovementStep)
                nextStep = actor.Match != null ? LateralTravelStep : 4;
            else if (step == LateralTravelStep)
                nextStep = 4;
            tutorialMoveSeconds[index] = 0f;
            tutorialMoveDistance[index] = 0f;
            tutorialBoostSeconds[index] = 0f;
            tutorialMoveCompleted[index] = false;
            tutorialBoostCompleted[index] = false;
            tutorialStopCompleted[index] = false;
            if (nextStep == 1)
                tutorialTargetFloor[index] = actor.SelectedPassengerDestination;
            // The stop target must belong to someone actually aboard, so they get off there.
            if (nextStep == 2 && (tutorialTargetFloor[index] < 0
                    || !TutorialHasPassengerFor(actor, tutorialTargetFloor[index])))
                tutorialTargetFloor[index] = FindTutorialPassengerFloor(actor);
            if (nextStep == 7)
            {
                tutorialKickBaseline[index] = actor.KickoutCount;
                actor.PrepareTutorialKickPassenger();
            }
            if (nextStep == 3)
            {
                // If the car is already near the top, teach down first so it
                // cannot reach the boundary while the new card enters.
                tutorialFirstMoveDirection[index] = actor.CurrentFloor >= actor.ActiveMaxFloor - 1f ? -1 : 1;
                tutorialLastFloor[index] = actor.CurrentFloor;
            }
            else if (nextStep == OppositeMovementStep)
                tutorialLastFloor[index] = actor.CurrentFloor;
            if (nextStep == LateralTravelStep)
                tutorialReachedSide[index] = tutorialRejoinedShaft[index] = false;
            BeginTutorialCardTransition(index, nextStep, nextStep != TutorialWaitingStep);
        }

        bool TutorialHasPassengerAtCurrentFloor(ElevatorManager actor)
            => actor != null && TutorialHasPassengerFor(actor, actor.DiscreteFloor);

        static bool TutorialHasPassengerFor(ElevatorManager actor, int floor)
        {
            foreach (var rider in actor.CurrentPassengers)
                if (rider.Destination == floor) return true;
            return false;
        }

        // Tutorial passengers use the center stop, where they can actually get off.
        public static bool TutorialAllowsStopAt(ElevatorManager actor, int floor, int stop)
        {
            MenuManager menu = Current;
            InputManager input = InputManager.Instance;
            if (menu == null || !menu.tutorialActive || actor == null || input == null) return true;
            int index = System.Array.IndexOf(menu.tutorialActors, actor);
            if (index < 0 || input.GetTutorialInputMode(actor.Seat) != InputManager.TutorialInputMode.Stop)
                return true;
            int target = menu.tutorialTargetFloor[index];
            return stop == ElevatorRound.CenterStop
                && (target >= 0 ? floor == target : TutorialHasPassengerFor(actor, floor));
        }

        int FindTutorialPassengerFloor(ElevatorManager actor)
        {
            if (actor == null) return -1;
            foreach (var rider in actor.CurrentPassengers)
                if (rider.Destination != actor.DiscreteFloor) return rider.Destination;
            return -1;
        }

        void BeginTutorialCardTransition(int index, int nextStep, bool resumeWhenSettled)
        {
            tutorialNextSteps[index] = nextStep;
            tutorialStepSettledTimes[index] = 0f;
            tutorialResumeActorOnCardSettled[index] = resumeWhenSettled;
            InputManager.TutorialInputMode inputMode = TutorialInputModeForStep(nextStep);
            InputManager.Instance?.SetTutorialInputMode(tutorialActors[index] != null
                ? tutorialActors[index].Seat : index, inputMode);
            if (nextStep == LateralTravelStep)
            {
                tutorialLateralGoalsReached[index] = 0;
                tutorialLastLateralX[index] = tutorialActors[index].RouteLateralX;
            }
            if (nextStep == TutorialWaitingStep)
            {
                // The waiting state is a handoff, not another tutorial card.
                // Settle it immediately so the completed player's message
                // cannot get trapped between the exit and enter animations.
                tutorialSteps[index] = nextStep;
                tutorialCardMotions[index] = TutorialCardMotion.Settled;
                tutorialCardMotionTimes[index] = TutorialCardTravelSeconds;
                tutorialCompletionTimes[index] = 0f;
                tutorialResumeActorOnCardSettled[index] = false;
                InputManager.Instance?.LockTutorialInput(tutorialActors[index] != null
                    ? tutorialActors[index].Seat : index);
                return;
            }
            // The car keeps running while the card swaps; the input mode above
            // already limits the player to the next step's action.
            tutorialCardMotions[index] = TutorialCardMotion.Exiting;
            tutorialCardMotionTimes[index] = 0f;
            if (nextStep == 0) tutorialPracticeTimes[index] = 0f;
        }

        InputManager.TutorialInputMode TutorialInputModeForStep(int step)
        {
            switch (step)
            {
                case 0: return InputManager.TutorialInputMode.OpenDoor;
                case 1: return InputManager.TutorialInputMode.BoardPassengers;
                case 2: return InputManager.TutorialInputMode.CloseDoors;
                case 3:
                case OppositeMovementStep:
                case LateralTravelStep: return InputManager.TutorialInputMode.Move;
                case 4: return InputManager.TutorialInputMode.Boost;
                case 5: return InputManager.TutorialInputMode.Stop;
                case 6: return InputManager.TutorialInputMode.SelectPassenger;
                case 7: return InputManager.TutorialInputMode.Kickout;
                default: return InputManager.TutorialInputMode.Free;
            }
        }

        void UpdateTutorialCardMotion(int index)
        {
            tutorialCardMotionTimes[index] += Time.unscaledDeltaTime;
            if (tutorialCardMotionTimes[index] < TutorialCardTravelSeconds) return;

            if (tutorialCardMotions[index] == TutorialCardMotion.Exiting)
            {
                tutorialSteps[index] = tutorialNextSteps[index];
                tutorialCardMotions[index] = TutorialCardMotion.Entering;
                tutorialCardMotionTimes[index] = 0f;
                return;
            }

            tutorialCardMotions[index] = TutorialCardMotion.Settled;
            tutorialCardMotionTimes[index] = TutorialCardTravelSeconds;
            if (tutorialResumeActorOnCardSettled[index]
                && tutorialSteps[index] != TutorialWaitingStep)
                ResumeTutorialActor(index);
            tutorialResumeActorOnCardSettled[index] = false;
        }

        void ResumeTutorialActor(int index)
        {
            tutorialActors[index]?.SetPausedPublic(false);
        }

        void PauseTutorialGame()
        {
            if (tutorialGame == null) return;
            if (tutorialGame.Match != null)
            {
                if (!tutorialGame.Match.Paused) tutorialGame.Match.TogglePause();
            }
            else tutorialGame.SetPausedPublic(true);
        }

        void ResumeTutorialGame()
        {
            if (tutorialGame == null) return;
            if (tutorialGame.Match != null)
            {
                if (tutorialGame.Match.Paused) tutorialGame.Match.TogglePause();
            }
            else tutorialGame.SetPausedPublic(false);
        }

        void BeginTutorialHandoff()
        {
            tutorialFinalePhase = TutorialFinalePhase.Collision;
            tutorialFinaleTimer = 0f;
            tutorialStoryIndex = 0;
            tutorialCollisionSeat = -1;
            PauseTutorialGame();
            AdvanceTutorialCollision();
        }

        void AdvanceTutorialCollision()
        {
            PauseTutorialGame();
            tutorialFinaleTimer = 0f;
            tutorialCollisionHitConfirmed = false;
            tutorialGame.Match?.EndTutorialCollisionPractice();
            while (++tutorialCollisionSeat < tutorialActors.Length)
            {
                ElevatorManager actor = tutorialActors[tutorialCollisionSeat];
                if (actor == null || tutorialGame.Match == null) continue;
                for (int seat = 0; seat < tutorialActors.Length; seat++)
                    InputManager.Instance?.LockTutorialInput(seat);
                if (!tutorialGame.Match.BeginTutorialCollisionPractice(actor.Seat)) continue;
                InputManager.Instance?.UnlockTutorialInput(actor.Seat);
                InputManager.Instance?.SetTutorialInputMode(actor.Seat, InputManager.TutorialInputMode.Collision);
                ResumeTutorialGame();
                return;
            }
            tutorialFinalePhase = TutorialFinalePhase.Story;
        }

        bool PassengerGalleryNextPressed()
        {
            InputManager inputManager = InputManager.Instance;
            if (inputManager == null || roundPassengerTimer < RoundPassengerSkipDelay) return false;
            for (int seat = 0; seat < roundPassengerActors.Length; seat++)
                if (HasRoundPassengerViewer(seat) && inputManager.ReadForSeat(seat, true).ConfirmPressed)
                    return true;
            return false;
        }

        bool HasRoundPassengerViewer(int seat) =>
            roundPassengerActors[seat] != null && !roundPassengerActors[seat].IsNpc;

        void UpdateTutorialFinale()
        {
            tutorialFinaleTimer += Time.unscaledDeltaTime;
            switch (tutorialFinalePhase)
            {
                case TutorialFinalePhase.Collision:
                    if (!tutorialCollisionHitConfirmed
                        && tutorialGame.Match != null
                        && tutorialGame.Match.TutorialCollisionPracticeCompleted(tutorialCollisionSeat))
                    {
                        tutorialCollisionHitConfirmed = true;
                        tutorialFinaleTimer = 0f;
                        InputManager.Instance?.LockTutorialInput(tutorialCollisionSeat);
                    }
                    if (tutorialCollisionHitConfirmed && tutorialFinaleTimer >= 1.2f) AdvanceTutorialCollision();
                    break;
                case TutorialFinalePhase.Story:
                    if (tutorialFinaleTimer >= TutorialStoryLineSeconds)
                    {
                        tutorialStoryIndex++;
                        tutorialFinaleTimer = 0f;
                        if (tutorialStoryIndex >= TutorialStoryLines.Length)
                            tutorialFinalePhase = TutorialFinalePhase.FadeToControls;
                    }
                    break;
                case TutorialFinalePhase.FadeToControls:
                    if (tutorialFinaleTimer >= TutorialHandoffFadeSeconds)
                    {
                        tutorialFinalePhase = TutorialFinalePhase.Controls;
                        tutorialFinaleTimer = 0f;
                        ResetControllerInstructionConfirmations();
                        showingControllerInstructions = true;
                    }
                    break;
                case TutorialFinalePhase.Countdown:
                    if (tutorialFinaleTimer >= TutorialCountdownSeconds)
                        FinishTutorial();
                    break;
            }
        }

        void FinishTutorial()
        {
            tutorialGame?.Match?.EndTutorialCollisionPractice();
            tutorialGame?.FinishTutorialCharacter();
            tutorialActive = false;
            TutorialActive = false;
            MusicManager.Instance?.SetTutorialMode(false);
            tutorialFinalePhase = TutorialFinalePhase.None;
            showingControllerInstructions = false;
            InputManager.Instance?.ClearTutorialInputLocks();
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                ResumeTutorialActor(i);
                tutorialActors[i]?.SetTutorialStops(false);
            }
            ResumeTutorialGame();
            if (tutorialGame != null)
            {
                if (tutorialGame.Match != null) tutorialGame.Match.StartMatch();
                else tutorialGame.StartSoloRound();
            }
            menuFadeAlpha = 1f;
            InputManager.Instance?.SetView(GameView.ControlElevator);
        }

        public static bool BeginRoundPassengerIntroduction(ElevatorManager first, ElevatorManager second)
        {
            MenuManager menu = Current != null ? Current : FindAnyObjectByType<MenuManager>();
            return menu != null && menu.StartRoundPassengerIntroduction(first, second);
        }

        bool StartRoundPassengerIntroduction(ElevatorManager first, ElevatorManager second)
        {
            roundPassengerIntroductionActive = false;
            RoundPassengerIntroductionActive = false;
            roundPassengerActors[0] = first != null && first.Seat == 0 ? first
                : second != null && second.Seat == 0 ? second : null;
            roundPassengerActors[1] = first != null && first.Seat == 1 ? first
                : second != null && second.Seat == 1 ? second : null;
            roundPassengerPreviewActor = first != null ? first : second;
            roundPassengerKinds.Clear();
            roundPassengerIndex = -1;
            roundPassengerTimer = 0f;
            if (!HasRoundPassengerViewer(0) && !HasRoundPassengerViewer(1)) return false;

            // Keep the familiar passenger order, but only include kinds that
            // can actually appear in this round's configured dimension.
            foreach (string kind in TutorialCharacterKinds)
                if (RoundHasPassengerKind(kind)) roundPassengerKinds.Add(kind);
            foreach (var rider in roundPassengerPreviewActor.AllRiders)
                if (rider != null && !string.IsNullOrEmpty(rider.Kind)
                    && !roundPassengerKinds.Contains(rider.Kind)) roundPassengerKinds.Add(rider.Kind);
            if (roundPassengerKinds.Count == 0) return false;

            roundPassengerIntroductionActive = true;
            RoundPassengerIntroductionActive = true;
            AdvanceRoundPassenger();
            return roundPassengerIntroductionActive;
        }

        bool RoundHasPassengerKind(string kind)
        {
            foreach (var rider in roundPassengerPreviewActor.AllRiders)
                if (rider != null && rider.Kind == kind) return true;
            return false;
        }

        void UpdateRoundPassengerIntroduction()
        {
            roundPassengerTimer += Time.unscaledDeltaTime;
            roundPassengerPreviewActor?.AnimateTutorialCharacter(roundPassengerTimer);
            if (PassengerGalleryNextPressed()) AdvanceRoundPassenger();
        }

        void AdvanceRoundPassenger()
        {
            roundPassengerTimer = 0f;
            while (++roundPassengerIndex < roundPassengerKinds.Count)
                if (roundPassengerPreviewActor.PrepareTutorialCharacter(roundPassengerKinds[roundPassengerIndex]))
                    return;
            roundPassengerPreviewActor.FinishTutorialCharacter();
            roundPassengerActors[0]?.BeginRoundCountdown();
            roundPassengerActors[1]?.BeginRoundCountdown();
            roundPassengerIntroductionActive = false;
            RoundPassengerIntroductionActive = false;
        }

        void DrawRoundPassengerIntroduction()
        {
            if (!roundPassengerIntroductionActive || roundPassengerPreviewActor == null
                || roundPassengerIndex < 0 || roundPassengerIndex >= roundPassengerKinds.Count) return;
            string kind = roundPassengerKinds[roundPassengerIndex];
            int knownIndex = System.Array.IndexOf(TutorialCharacterKinds, kind);
            string name = knownIndex >= 0 ? TutorialCharacterNames[knownIndex] : RoundPassengerName(kind);
            DrawPassengerGalleryCard(roundPassengerPreviewActor,
                "ROUND PASSENGERS  " + (roundPassengerIndex + 1) + " / " + roundPassengerKinds.Count,
                name);
        }

        string RoundPassengerName(string kind)
        {
            foreach (var rider in roundPassengerPreviewActor.AllRiders)
                if (rider != null && rider.Kind == kind && rider.Data != null
                    && !string.IsNullOrEmpty(rider.Data.displayName)) return rider.Data.displayName;
            return kind.Replace('_', ' ');
        }

        void SkipTutorial()
        {
            if (!tutorialActive || tutorialGame == null) return;
            FinishTutorial();
        }

        static string TutorialKeyboardKey(int step, int seat)
        {
            if (seat == 1)
            {
                switch (step)
                {
                    case 0: case 1: return "←/↑/→";
                    case 2: return ".";
                    case 3: return "↑/↓";
                    case LateralTravelStep: return "←/→";
                    case 4: return "R SHIFT";
                    case 5: return ".";
                    case 7: return ",";
                    case 8: return "↑";
                    default: return ".";
                }
            }
            switch (step)
            {
                case 0: case 1: return "A/W/D";
                case 2: return "C";
                case 3: return "W/S";
                case LateralTravelStep: return "A/D";
                case 4: return "SHIFT";
                case 5: return "C";
                case 7: return "I";
                case 8: return "W";
                default: return "C";
            }
        }

        string TutorialTitle(int step)
        {
            switch (step)
            {
                case -1: return "PRACTICE SHIFT";
                case 0: return "OPEN A DOOR";
                case 1: return "OPEN ALL 3 DOORS";
                case 2: return "CLOSE & GO";
                case 3: case OppositeMovementStep: return "MOVE THE ELEVATOR";
                case LateralTravelStep: return "MOVE SIDEWAYS";
                case 4: return "BOOST";
                case 5: return "STOP";
                case 6: return "DROP-OFF";
                case 7: return "KICK";
                case 8: return tutorialCollisionHitConfirmed ? "NICE HIT!" : "BUMP YOUR RIVAL";
                default: return "CORE CONTROLS LEARNED";
            }
        }

        string TutorialBody(int step, int index)
        {
            int seat = tutorialActors[index].Seat;
            switch (step)
            {
                case -1: return "TUTORIAL";
                case 0:
                    return "TRY ANY DOOR";
                case 1:
                    if (tutorialActors[index].OpenBoardingDoorCount >= 3)
                        return "PASSENGERS BOARDING";
                    int slot = tutorialActors[index].TutorialSuggestedDoorSlot(0);
                    string door = slot == 0 ? "LEFT" : slot == 1 ? "FRONT" : "RIGHT";
                    return "TRY " + door + " · " + tutorialActors[index].OpenBoardingDoorCount + "/3 OPEN";
                case 2: return "PRESS TO LEAVE";
                case 3: case OppositeMovementStep:
                    return TutorialMovementDirection(index, step) > 0 ? "HOLD UP" : "HOLD DOWN";
                case 4: return KeyboardHintsEnabled
                    ? seat == 1 ? "HOLD ↑ + RSHIFT." : "HOLD W + SHIFT."
                    : "HOLD UP + LB.";
                case 5:
                    if (tutorialActors[index].TutorialStopInProgress) return "STOPPING...";
                    int stopDirection = TutorialStopDirection(index);
                    if (Mathf.Abs(stopDirection) == 2) return "MOVE TOWARD THE SHAFT";
                    if (stopDirection != 0) return "HOLD TO REACH THE FLOOR";
                    return "PRESS TO STOP & OPEN DOORS";
                case 6: return "PASSENGERS EXIT AUTOMATICALLY";
                case 7: return "SELECT A RIDER, THEN KICK";
                case LateralTravelStep:
                    if (!tutorialReachedSide[index]) return "HOLD LEFT OR RIGHT";
                    if (tutorialActors[index].TutorialReadyToRejoin)
                        return tutorialActors[index].TutorialRejoinVerticalDirection > 0
                            ? "HOLD UP TO REJOIN" : "HOLD DOWN TO REJOIN";
                    return tutorialActors[index].TutorialShaftDirection < 0
                        ? "GO LEFT TO THE SHAFT" : "GO RIGHT TO THE SHAFT";
                case 8:
                    if (tutorialCollisionHitConfirmed) return "YOU MADE AN OPENING!";
                    return KeyboardHintsEnabled ? seat == 1 ? "HOLD ↑ + RSHIFT." : "HOLD W + SHIFT."
                        : "HOLD UP + LB.";
                default: return "START YOUR SHIFT.";
            }
        }

        // Draw in the gameplay HUD's own pass so its signs and arrows stay behind the cards.
        internal static void DrawTutorialCardsAboveHud()
        {
            MenuManager menu = Current;
            if (menu == null || !menu.tutorialActive || menu.tutorialFinalePhase != TutorialFinalePhase.None
                || Event.current.type != EventType.Repaint)
                return;

            menu.Styles();
            menu.DrawTutorialOverlay();
        }

        void DrawTutorialOverlay()
        {
            // The Crazy Elevator reveal owns the screen until it has finished.
            // Do not let the tutorial card appear over the intro animation.
            if (!tutorialIntroReady)
                return;

            if (tutorialGame != null && tutorialGame.Match != null && tutorialActors[1] != null)
            {
                for (int i = 0; i < tutorialActors.Length; i++)
                {
                    ElevatorManager actor = tutorialActors[i];
                    if (actor != null)
                        DrawTutorialOverlayForView(tutorialGame.Match.ViewRect(actor.Seat), i);
                }
                return;
            }

            Rect view = tutorialGame != null && tutorialGame.Match != null
                ? tutorialGame.Match.ViewRect(tutorialGame.Seat)
                : new Rect(0, 0, Screen.width, Screen.height);
            DrawTutorialOverlayForView(view, 0);
        }

        void DrawTutorialOverlayForView(Rect view, int index)
        {
            if (index < 0 || index >= tutorialActors.Length
                || tutorialActors[index] == null
                || tutorialCardMotions[index] == TutorialCardMotion.Hidden)
                return;
            bool collisionPractice = tutorialFinalePhase == TutorialFinalePhase.Collision
                && index == tutorialCollisionSeat;
            int step = collisionPractice ? 8 : tutorialSteps[index];
            int inputSeat = tutorialActors[index].Seat;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousGuiColor = GUI.color;
            Color previousContentColor = GUI.contentColor;
            GUI.matrix = Matrix4x4.Translate(new Vector3(view.x, view.y, 0f));
            GUI.color = Color.white;
            GUI.contentColor = Color.white;

            if (step < 0)
            {
                DrawPracticeMessage(view, index);
                GUI.matrix = previousMatrix;
                GUI.color = previousGuiColor;
                GUI.contentColor = previousContentColor;
                return;
            }

            if (step == TutorialWaitingStep && !collisionPractice)
            {
                DrawTutorialWaitingPanel(view, tutorialActors[0] != null && tutorialActors[1] != null);
                GUI.matrix = previousMatrix;
                GUI.color = previousGuiColor;
                GUI.contentColor = previousContentColor;
                return;
            }

            DrawTutorialElevatorSpotlight(view, index, step);
            if (step == LateralTravelStep && tutorialReachedSide[index] && !tutorialRejoinedShaft[index])
                DrawTutorialShaftMarker(view, tutorialActors[index]);

            // Layout in a consistent coordinate space so a 4K screen does not
            // make the fixed-size tutorial card appear half as large.
            Rect pixelView = view;
            float tutorialScale = Mathf.Clamp(Mathf.Min(view.height / 900f, view.width / 620f), .7f, 3f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(view.x, view.y, 0f), Quaternion.identity,
                new Vector3(tutorialScale, tutorialScale, 1f));
            view = new Rect(0f, 0f, view.width / tutorialScale, view.height / tutorialScale);

            const float margin = 14f;
            bool split = tutorialGame.Match != null && tutorialGame.Match.SplitScreen;
            float width = Mathf.Min(700f, view.width - margin * 2f);
            float height = Mathf.Min(collisionPractice ? 200f : 184f, view.height * .31f);
            float settledX = (view.width - width) * .5f;
            float settledY = Mathf.Min(view.height - height - margin, Mathf.Max(64f, view.height * .12f));
            float motionT = Mathf.Clamp01(tutorialCardMotionTimes[index] / TutorialCardTravelSeconds);
            float eased = 1f - Mathf.Pow(1f - motionT, 3f);
            float cardX = settledX;
            float cardY = settledY;
            if (tutorialCardMotions[index] == TutorialCardMotion.Entering)
                cardY = Mathf.Lerp(-height - 28f, settledY, eased);
            else if (tutorialCardMotions[index] == TutorialCardMotion.Exiting)
                cardY = Mathf.Lerp(settledY, view.height + 28f, eased);
            Rect card = new Rect(cardX, cardY, width, height);
            Color accent = step == 7 || collisionPractice ? MenuPresentation.Brass
                : tutorialActors[index].Seat == 0 && split ? MenuPresentation.Coral : MenuPresentation.Teal;
            if (step == LateralTravelStep && tutorialCardMotions[index] == TutorialCardMotion.Settled)
                DrawTutorialLateralGuide(view, index, settledY);
            MenuPresentation.HudPlate(card, accent);
            Rect badge = new Rect(card.x + 15f, card.y + 10f, 102f, 22f);
            MenuPresentation.Plaque(badge, accent, MenuPresentation.Metal, 2f);

            Color previousTitle = cardTitle.normal.textColor;
            Color previousBody = body.normal.textColor;
            Color previousCaption = caption.normal.textColor;
            int previousTitleSize = cardTitle.fontSize;
            int previousBodySize = body.fontSize;
            TextAnchor previousBodyAlignment = body.alignment;
            bool previousBodyWrap = body.wordWrap;
            TextAnchor previousTitleAlignment = cardTitle.alignment;
            cardTitle.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            body.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            cardTitle.fontSize = Mathf.Clamp(Mathf.RoundToInt(card.width * .065f), 24, 40);
            cardTitle.alignment = TextAnchor.MiddleCenter;
            body.fontSize = Mathf.Clamp(Mathf.RoundToInt(card.width * .043f), 19, 28);
            body.alignment = TextAnchor.MiddleLeft;
            body.wordWrap = true;
            MenuPresentation.Label(badge, new GUIContent("TUTORIAL"), caption, MenuPresentation.Ink);
            Rect titleRect = new Rect(card.x + 12f, card.y + 34f, card.width - 24f, 43f);
            string instruction = step == 5 && tutorialTargetFloor[index] >= 0
                ? TutorialStopInstruction(index)
                : step == 3 || step == OppositeMovementStep
                    ? (TutorialMovementDirection(index, step) > 0 ? "MOVE UP" : "MOVE DOWN")
                : step == LateralTravelStep && tutorialReachedSide[index]
                    ? (tutorialActors[index].TutorialReadyToRejoin ? "REJOIN THE SHAFT" : "GO TO THE SHAFT")
                    : TutorialTitle(step);
            GUIContent titleContent = new GUIContent(instruction);
            while (cardTitle.fontSize > 19 && cardTitle.CalcSize(titleContent).x > titleRect.width)
                cardTitle.fontSize--;
            GUI.Label(titleRect, titleContent, cardTitle);

            float rowY = card.y + 78f;
            float hintWidth;
            int doorSlot = tutorialActors[index].TutorialSuggestedDoorSlot(0);
            bool waitingForBoarding = step == 1 && tutorialActors[index].OpenBoardingDoorCount >= 3;
            bool stopping = step == 5 && tutorialActors[index].TutorialStopInProgress;
            if (!KeyboardHintsEnabled && step != 6 && !waitingForBoarding && !stopping)
            {
                hintWidth = Mathf.Min(164f, card.width * .32f);
                DrawTutorialController(new Rect(card.x + 15f, rowY, hintWidth, Mathf.Min(88f, card.yMax - rowY - 8f)), step, accent, doorSlot, index);
            }
            else
            {
                string key = step == 6 || waitingForBoarding || stopping ? "AUTO" : TutorialExampleKey(step, inputSeat, index, doorSlot);
                bool combined = step == 4 || step == 8;
                hintWidth = combined ? 130f : key.Length > 4 ? 94f : 64f;
                if (combined)
                {
                    DrawPulsingTutorialKey(new Rect(card.x + 15f, rowY + 5f, 48f, 56f),
                        inputSeat == 1 ? "↑" : "W", accent);
                    var plusStyle = new GUIStyle(body)
                    {
                        fontSize = 21,
                        alignment = TextAnchor.MiddleCenter,
                        padding = new RectOffset(0, 0, 0, 0),
                        wordWrap = false
                    };
                    GUI.Label(new Rect(card.x + 63f, rowY + 5f, 21f, 56f), "+", plusStyle);
                    DrawPulsingTutorialKey(new Rect(card.x + 84f, rowY + 5f, 62f, 56f),
                        inputSeat == 1 ? "R SHIFT" : "SHIFT", accent);
                }
                else if (step == 6 || waitingForBoarding || stopping) DrawKeyHint(new Rect(card.x + 15f, rowY + 5f, hintWidth, 62f), key);
                else DrawPulsingTutorialKey(new Rect(card.x + 15f, rowY + 5f, hintWidth, 62f), key, accent);
            }
            float textX = card.x + 26f + hintWidth;
            GUI.Label(new Rect(textX, rowY, card.xMax - textX - 15f, card.yMax - rowY - 8f),
                TutorialBody(step, index), body);

            if (step == 5 && tutorialTargetFloor[index] >= 0 && !stopping)
            {
                DrawTutorialFloorMarker(pixelView, card, index, tutorialScale);
                DrawTutorialStopGuide(view, card, index);
            }

            if (collisionPractice)
            {
                int previousFooterSize = caption.fontSize;
                caption.fontSize = Mathf.Clamp(Mathf.RoundToInt(card.width * .032f), 12, 17);
                caption.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Brass);
                GUI.Label(new Rect(card.x + 15f, card.yMax - 30f, card.width - 30f, 22f),
                    "BUMP THEM AWAY. BOARD FIRST.", caption);
                caption.fontSize = previousFooterSize;
            }

            cardTitle.alignment = previousTitleAlignment;
            cardTitle.fontSize = previousTitleSize;
            body.fontSize = previousBodySize;
            body.alignment = previousBodyAlignment;
            body.wordWrap = previousBodyWrap;
            cardTitle.normal.textColor = previousTitle;
            body.normal.textColor = previousBody;
            caption.normal.textColor = previousCaption;

            GUI.matrix = previousMatrix;
            GUI.color = previousGuiColor;
            GUI.contentColor = previousContentColor;
        }

        string TutorialExampleKey(int step, int seat, int index, int doorSlot)
        {
            if (step == 0 || step == 1)
                return seat == 1 ? new[] { "←", "↑", "→" }[doorSlot]
                    : new[] { "A", "W", "D" }[doorSlot];
            if (step == 3 || step == OppositeMovementStep)
                return TutorialMovementDirection(index, step) > 0
                    ? (seat == 1 ? "↑" : "W") : (seat == 1 ? "↓" : "S");
            if (step == 5)
            {
                int direction = TutorialStopDirection(index);
                if (direction == 1) return seat == 1 ? "↑" : "W";
                if (direction == -1) return seat == 1 ? "↓" : "S";
                if (direction == 2) return seat == 1 ? "→" : "D";
                if (direction == -2) return seat == 1 ? "←" : "A";
            }
            if (step == LateralTravelStep)
            {
                ElevatorManager actor = tutorialActors[index];
                if (tutorialReachedSide[index] && actor.TutorialReadyToRejoin)
                    return actor.TutorialRejoinVerticalDirection > 0
                        ? (seat == 1 ? "↑" : "W") : (seat == 1 ? "↓" : "S");
                bool left = tutorialReachedSide[index] ? actor.TutorialShaftDirection < 0
                    : Mathf.FloorToInt(Time.unscaledTime / 2.4f) % 2 == 0;
                return left ? (seat == 1 ? "←" : "A") : (seat == 1 ? "→" : "D");
            }
            return TutorialKeyboardKey(step, seat);
        }

        int TutorialStopDirection(int index)
            => tutorialTargetFloor[index] >= 0
                ? tutorialActors[index].TutorialStopNavigation(tutorialTargetFloor[index]) : 0;

        string TutorialStopInstruction(int index)
        {
            ElevatorManager actor = tutorialActors[index];
            if (actor.TutorialStopInProgress) return "STOPPING";
            int direction = TutorialStopDirection(index);
            if (Mathf.Abs(direction) == 2) return "GO TO THE SHAFT";
            string action = direction > 0 ? "GO UP TO FLOOR "
                : direction < 0 ? "GO DOWN TO FLOOR " : "STOP AT FLOOR ";
            return action + ElevatorRound.FloorNumber(tutorialTargetFloor[index]);
        }

        void DrawTutorialStopGuide(Rect view, Rect card, int index)
        {
            int direction = TutorialStopDirection(index);
            string arrow = direction == 1 ? "↑" : direction == -1 ? "↓"
                : direction == 2 ? "→" : direction == -2 ? "←" : "●";
            string target = Mathf.Abs(direction) == 2 ? "SHAFT"
                : direction == 0 ? "STOP HERE" : "FLOOR " + ElevatorRound.FloorNumber(tutorialTargetFloor[index]);
            float width = Mathf.Min(220f, view.width - 28f);
            Rect guide = new Rect((view.width - width) * .5f,
                Mathf.Min(card.yMax + 9f, view.height - 53f), width, 44f);
            MenuPresentation.HudPlate(guide, MenuPresentation.Brass);
            var style = new GUIStyle(cardTitle) { fontSize = 23, alignment = TextAnchor.MiddleCenter,
                wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            style.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            GUI.Label(guide, arrow + "  " + target, style);
        }

        void DrawTutorialFloorMarker(Rect view, Rect card, int index, float scale)
        {
            if (Mathf.Abs(TutorialStopDirection(index)) == 2) return;
            ElevatorManager actor = tutorialActors[index];
            if (!actor.TryGetTutorialFloorScreenPosition(tutorialTargetFloor[index], out Vector3 point)) return;
            float x = (point.x - view.x) / scale;
            float y = (Screen.height - point.y - view.y) / scale;
            // The guide below the card handles floors outside the visible area.
            if (x < 60f || x > view.width / scale - 60f || y < card.yMax + 70f || y > view.height / scale - 45f) return;
            float bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f)) * 5f;
            Rect marker = new Rect(x - 55f, y - 62f - bounce, 110f, 42f);
            MenuPresentation.HudPlate(marker, MenuPresentation.Brass);
            var style = new GUIStyle(cardTitle) { fontSize = 23, alignment = TextAnchor.MiddleCenter,
                wordWrap = false, padding = new RectOffset(0, 0, 0, 0) };
            style.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            GUI.Label(marker, "FLOOR " + ElevatorRound.FloorNumber(tutorialTargetFloor[index]), style);
            style.fontSize = 30;
            style.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Brass);
            GUI.Label(new Rect(x - 22f, marker.yMax - 1f, 44f, 34f), "↓", style);
        }

        void DrawPulsingTutorialKey(Rect rect, string key, Color accent)
        {
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 5f);
            float spread = 3f + pulse * 3f;
            Panel(new Rect(rect.x - spread, rect.y - spread, rect.width + spread * 2f,
                rect.height + spread * 2f), new Color(accent.r, accent.g, accent.b, .25f + pulse * .45f));
            DrawKeyHint(new Rect(rect.x, rect.y - pulse * 3f, rect.width, rect.height), key);
        }

        void DrawTutorialShaftMarker(Rect view, ElevatorManager actor)
        {
            if (!actor.TryGetTutorialShaftScreenPosition(out Vector3 target)) return;
            float x = Mathf.Clamp(target.x - view.x, 60f, view.width - 60f);
            float y = Mathf.Clamp(Screen.height - target.y - view.y - 76f, view.height * .42f, view.height - 90f);
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 5f);
            var background = new Rect(x - 52f, y, 104f, 55f);
            MenuPresentation.HudPlate(background, MenuPresentation.Brass);
            var label = new GUIStyle(cardTitle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                wordWrap = false
            };
            label.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            GUI.Label(new Rect(background.x, background.y + 2f, background.width, 23f), "SHAFT", label);
            label.fontSize = 30;
            label.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Brass);
            string arrow = target.x < view.x + 60f ? "←" : target.x > view.xMax - 60f ? "→" : "↓";
            GUI.Label(new Rect(background.x, background.y + 21f + pulse * 3f, background.width, 30f), arrow, label);
        }

        // Briefly frame the car at the start of a movement lesson, then let
        // the player see the entire shaft while they perform the action.
        void DrawTutorialElevatorSpotlight(Rect view, int index, int step)
        {
            if (step != 3 && step != OppositeMovementStep && step != 4 && step != 5 && step != LateralTravelStep && step != 8) return;
            if (tutorialCardMotions[index] != TutorialCardMotion.Settled) return;
            float age = tutorialStepSettledTimes[index];
            if (age >= 2.5f) return;
            ElevatorManager actor = tutorialActors[index];
            Camera camera = actor != null ? actor.ShaftCamera : null;
            if (camera == null || !camera.enabled) return;
            Vector3 point = camera.WorldToScreenPoint(actor.ShaftWorldPosition);
            if (point.z <= 0f) return;
            float x = point.x - view.x;
            float y = Screen.height - point.y - view.y;
            if (x < 0f || x > view.width || y < 0f || y > view.height) return;
            float radius = Mathf.Clamp(Mathf.Min(view.width * .14f, view.height * .12f), 55f, 110f);
            float alpha = .42f * Mathf.Clamp01((2.5f - age) / .5f);
            Texture2D mask = TutorialSpotlightMask(index, view, x, y, radius);
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, view.width, view.height), mask, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        // One mask per player keeps the shade seamless and contained within the
        // player's viewport. Reuse its pixels instead of allocating every frame.
        Texture2D TutorialSpotlightMask(int index, Rect view, float x, float y, float radius)
        {
            int width = Mathf.Clamp(Mathf.CeilToInt(view.width / 3f), 64, 384);
            int height = Mathf.Clamp(Mathf.CeilToInt(view.height / 3f), 64, 384);
            Texture2D texture = tutorialSpotlightMasks[index];
            if (texture == null || texture.width != width || texture.height != height)
            {
                if (texture != null) Destroy(texture);
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    name = "Tutorial spotlight " + index,
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                tutorialSpotlightMasks[index] = texture;
                tutorialSpotlightPixels[index] = new Color32[width * height];
                tutorialSpotlightCenters[index] = new Vector2(-1f, -1f);
            }

            // Texture rows start at the bottom; GUI coordinates start at the top.
            Vector2 center = new Vector2(x / view.width * width, (1f - y / view.height) * height);
            if ((tutorialSpotlightCenters[index] - center).sqrMagnitude < .25f
                && Mathf.Approximately(tutorialSpotlightRadii[index], radius)
                && tutorialSpotlightViewSizes[index] == view.size) return texture;

            Color32[] pixels = tutorialSpotlightPixels[index];
            float scaleX = view.width / width, scaleY = view.height / height;
            float feather = Mathf.Max(scaleX, scaleY) * 2f;
            float inner = Mathf.Max(0f, radius - feather);
            float outer = radius + feather;
            float innerSquared = inner * inner, outerSquared = outer * outer;
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    float dx = (column + .5f - center.x) * scaleX;
                    float dy = (row + .5f - center.y) * scaleY;
                    float distanceSquared = dx * dx + dy * dy;
                    byte opacity = distanceSquared <= innerSquared ? (byte)0
                        : distanceSquared >= outerSquared ? (byte)255
                        : (byte)Mathf.RoundToInt(255f * Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(inner, outer, Mathf.Sqrt(distanceSquared))));
                    pixels[row * width + column] = new Color32(0, 0, 0, opacity);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            tutorialSpotlightCenters[index] = center;
            tutorialSpotlightRadii[index] = radius;
            tutorialSpotlightViewSizes[index] = view.size;
            return texture;
        }

        void DrawPracticeMessage(Rect view, int index)
        {
            float alpha = 1f;
            float motionT = Mathf.Clamp01(tutorialCardMotionTimes[index] / TutorialCardTravelSeconds);
            if (tutorialCardMotions[index] == TutorialCardMotion.Entering)
                alpha = Mathf.SmoothStep(0f, 1f, motionT);
            else if (tutorialCardMotions[index] == TutorialCardMotion.Exiting)
                alpha = 1f - Mathf.SmoothStep(0f, 1f, motionT);

            int previousFontSize = cardTitle.fontSize;
            TextAnchor previousAlignment = cardTitle.alignment;
            Color previousTextColor = cardTitle.normal.textColor;
            cardTitle.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .075f), 52, 90);
            cardTitle.alignment = TextAnchor.MiddleCenter;
            cardTitle.normal.textColor = MenuColor(new Color(Cream.r, Cream.g, Cream.b, alpha));
            GUI.Label(new Rect(30f, (view.height - 120f) * .5f, view.width - 60f, 120f),
                "TUTORIAL", cardTitle);
            cardTitle.fontSize = previousFontSize;
            cardTitle.alignment = previousAlignment;
            cardTitle.normal.textColor = previousTextColor;
        }

        void DrawTutorialWaitingPanel(Rect view, bool waitingForOtherPlayer)
        {
            float width = Mathf.Min(view.width - 28f, 470f);
            const float height = 96f;
            Rect card = new Rect((view.width - width) * .5f, view.height - height - 16f, width, height);
            Color accent = waitingForOtherPlayer ? MenuPresentation.Brass : MenuPresentation.Teal;
            MenuPresentation.HudPlate(card, accent);
            var label = new GUIStyle(caption);
            label.normal.textColor = MenuPresentation.DisplayColor(accent);
            GUI.Label(new Rect(card.x + 14f, card.y + 9f, card.width - 28f, 22f),
                waitingForOtherPlayer ? "TUTORIAL COMPLETE" : "PRACTICE COMPLETE", label);

            var heading = new GUIStyle(cardTitle)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(width * .066f), 16, 30),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            heading.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            var message = new GUIContent(waitingForOtherPlayer ? "OTHER DRIVER’S TURN" : "GET READY");
            Rect textArea = new Rect(card.x + 12f, card.y + 34f, card.width - 24f, 45f);
            while (heading.fontSize > 12 && heading.CalcSize(message).x > textArea.width)
                heading.fontSize--;
            GUI.Label(textArea, message, heading);
        }

        void DrawTutorialFinale()
        {
            if (tutorialFinalePhase == TutorialFinalePhase.Collision)
            {
                DrawTutorialOverlay();
                return;
            }
            if (tutorialFinalePhase == TutorialFinalePhase.Story)
            {
                DrawTutorialStoryForView(new Rect(0f, 0f, Screen.width, Screen.height));
                return;
            }

            if (tutorialFinalePhase == TutorialFinalePhase.FadeToControls)
            {
                float alpha = Mathf.Clamp01(tutorialFinaleTimer / TutorialHandoffFadeSeconds);
                Panel(new Rect(0f, 0f, Screen.width, Screen.height),
                    new Color(0f, 0f, 0f, alpha));
                return;
            }

            if (tutorialFinalePhase != TutorialFinalePhase.Countdown) return;
            Panel(new Rect(0f, 0f, Screen.width, Screen.height), Color.black);
            string countdown = (3 - Mathf.FloorToInt(tutorialFinaleTimer)).ToString();
            int previousCountdownFontSize = title.fontSize;
            TextAnchor previousCountdownAlignment = title.alignment;
            Color previousCountdownTextColor = title.normal.textColor;
            title.fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width * .12f), 100, 190);
            title.alignment = TextAnchor.MiddleCenter;
            title.normal.textColor = MenuColor(Cream);
            GUI.Label(new Rect(0f, Screen.height * .34f, Screen.width, Screen.height * .26f),
                countdown, title);
            title.fontSize = previousCountdownFontSize;
            title.alignment = previousCountdownAlignment;
            title.normal.textColor = previousCountdownTextColor;
        }

        void DrawPassengerGalleryCard(ElevatorManager actor, string kicker, string headingText)
        {
            Rect view = new Rect(0f, 0f, Screen.width, Screen.height);
            MenuPresentation.Backdrop(view);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = previousMatrix * MenuPresentation.Fit(view, 1200f, 640f);
            Color brass = MenuPresentation.Brass;
            Color ink = MenuPresentation.Ink;
            Color brassEdge = new Color32(130, 82, 48, 255);
            var heading = new GUIStyle(cardTitle)
                { fontSize = 34, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            var label = new GUIStyle(caption)
                { fontSize = 17, alignment = TextAnchor.MiddleCenter };

            Rect display = new Rect(254f, 108f, 692f, 416f);
            MenuPresentation.Plaque(display, brass, brassEdge, 10f);
            MenuPresentation.Fasteners(display, brassEdge);
            Rect opening = new Rect(274f, 128f, 652f, 376f);
            MenuPresentation.Plaque(opening, MenuPresentation.Wall, brassEdge, 4f);
            Texture preview = actor.DimensionRevealTexture;
            if (preview != null)
                MenuPresentation.DrawTexture(new Rect(282f, 135f, 636f, 362f), preview, ScaleMode.ScaleToFit);

            Rect counter = new Rect(449f, 31f, 302f, 39f);
            MenuPresentation.Plaque(counter, MenuPresentation.Teal, ink, 5f);
            DrawLabel(counter, kicker, label, ink);

            Rect nameplate = new Rect(330f, 482f, 540f, 62f);
            MenuPresentation.Plaque(nameplate, MenuPresentation.Paper, brassEdge, 7f);
            MenuPresentation.Fasteners(nameplate, brassEdge);
            DrawLabel(new Rect(nameplate.x + 22f, nameplate.y, nameplate.width - 44f, nameplate.height),
                headingText, heading, ink);

            if (roundPassengerTimer >= RoundPassengerSkipDelay)
            {
                Rect confirm = new Rect(481f, 573f, 238f, 48f);
                Color face = confirm.Contains(Event.current.mousePosition) ? MenuPresentation.Paper : MenuPresentation.Teal;
                MenuPresentation.Plaque(confirm, face, ink, 5f);
                DrawLabel(confirm, "CONFIRM TO SKIP", label, ink);
                if (GUI.Button(confirm, GUIContent.none, GUIStyle.none)) AdvanceRoundPassenger();
            }
            GUI.matrix = previousMatrix;
        }

        void DrawTutorialStoryForView(Rect view)
        {
            Panel(view, new Color(0f, 0f, 0f, .8f));
            float fadeIn = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(tutorialFinaleTimer / TutorialStoryFadeSeconds));
            float fadeOut = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((TutorialStoryLineSeconds - tutorialFinaleTimer)
                    / TutorialStoryFadeSeconds));
            float alpha = Mathf.Min(fadeIn, fadeOut);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Translate(new Vector3(view.x, view.y, 0f));
            int previousFontSize = cardTitle.fontSize;
            TextAnchor previousAlignment = cardTitle.alignment;
            bool previousWordWrap = cardTitle.wordWrap;
            Color previousTextColor = cardTitle.normal.textColor;
            cardTitle.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .072f), 26, 58);
            cardTitle.alignment = TextAnchor.MiddleCenter;
            cardTitle.wordWrap = true;
            cardTitle.normal.textColor = MenuColor(new Color(Cream.r, Cream.g, Cream.b, alpha));
            float slideIn = Mathf.Lerp(30f, 0f, fadeIn);
            float slideOut = Mathf.Lerp(-30f, 0f, fadeOut);
            GUI.Label(new Rect(24f, view.height * .34f + slideIn + slideOut,
                view.width - 48f, view.height * .34f), TutorialStoryLines[tutorialStoryIndex], cardTitle);
            cardTitle.fontSize = previousFontSize;
            cardTitle.alignment = previousAlignment;
            cardTitle.wordWrap = previousWordWrap;
            cardTitle.normal.textColor = previousTextColor;
            GUI.matrix = previousMatrix;
        }

        // Draw in a fixed local space so the same demonstration fits both player panes.
        // Unscaled time keeps the demonstration moving while tutorial gameplay is paused.
        void DrawTutorialController(Rect rect, int step, Color accent, int doorSlot, int index)
        {
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.TRS(new Vector3(rect.x, rect.y + rect.height * 24f / 135f, 0f),
                Quaternion.identity, new Vector3(rect.width / 200f, rect.height / 135f, 1f));
            bool boardingStep = step == 0 || step == 1;
            float cycleSeconds = boardingStep ? 3.2f : 2.4f;
            float cycle = Mathf.Repeat(Time.unscaledTime, cycleSeconds);
            bool pressed = cycle < (boardingStep ? 2.2f : 1.65f);
            Color shell = new Color32(104, 119, 136, 255);
            Color idle = new Color32(38, 48, 63, 255);
            Color active = Color.Lerp(accent, Cream, .15f + .2f * Mathf.Sin(Time.unscaledTime * 6f));

            // Rounded shoulders, central body and angled grips form a gamepad silhouette.
            TutorialControllerOval(new Rect(12, 23, 68, 81), shell);
            TutorialControllerOval(new Rect(120, 23, 68, 81), shell);
            TutorialControllerOval(new Rect(22, 19, 156, 65), shell);
            Panel(new Rect(45, 28, 110, 49), shell);
            bool boost = (step == 4 || step == 8) && pressed;
            DrawTutorialShoulder(new Rect(18, 0, 56, 23), "LB", boost, active);
            DrawTutorialShoulder(new Rect(126, 0, 56, 23), "RB", false, active);
            GUI.Label(new Rect(77, 28, 46, 20), boost ? "HOLD" : "", new GUIStyle(caption)
                { fontSize = 11, alignment = TextAnchor.MiddleCenter });

            int stopDirection = step == 5 ? TutorialStopDirection(index) : 0;
            bool useStick = boardingStep || step == 3 || step == OppositeMovementStep || step == 4 || step == 7 || step == 8
                || step == 5 && stopDirection != 0
                || step == LateralTravelStep;
            Vector2 offset = Vector2.zero;
            if (useStick && pressed)
            {
                if (boardingStep)
                {
                    offset = doorSlot == 0 ? Vector2.left : doorSlot == 1 ? Vector2.up : Vector2.right;
                }
                else if (step == 3 || step == OppositeMovementStep)
                    offset = TutorialMovementDirection(index, step) > 0 ? Vector2.up : Vector2.down;
                else if (step == 5)
                    offset = stopDirection == 1 ? Vector2.up : stopDirection == -1 ? Vector2.down
                        : stopDirection == 2 ? Vector2.right : Vector2.left;
                else if (step == 8) offset = Vector2.up;
                else if (step == LateralTravelStep)
                {
                    ElevatorManager actor = tutorialActors[index];
                    if (tutorialReachedSide[index] && actor.TutorialReadyToRejoin)
                        offset = actor.TutorialRejoinVerticalDirection > 0 ? Vector2.up : Vector2.down;
                    else if (tutorialReachedSide[index])
                        offset = actor.TutorialShaftDirection < 0 ? Vector2.left : Vector2.right;
                    else offset = Mathf.FloorToInt(Time.unscaledTime / 2.4f) % 2 == 0 ? Vector2.left : Vector2.right;
                }
                else offset = Mathf.FloorToInt(Time.unscaledTime / 2.4f) % 2 == 0 ? Vector2.up : Vector2.down;
            }
            if (useStick)
            {
                float glow = 4f + 3f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 5f));
                TutorialControllerOval(new Rect(33 - glow, 36 - glow, 38 + glow * 2f, 38 + glow * 2f),
                    new Color(accent.r, accent.g, accent.b, .45f));
            }
            TutorialControllerOval(new Rect(33, 36, 38, 38), useStick ? accent : idle);
            TutorialControllerOval(new Rect(37 + offset.x * 9f, 40 - offset.y * 9f, 30, 30), idle);
            TutorialControllerOval(new Rect(42 + offset.x * 9f, 45 - offset.y * 9f, 20, 20),
                useStick && pressed ? active : shell);
            Panel(new Rect(72, 70, 9, 25), idle);
            Panel(new Rect(64, 78, 25, 9), idle);
            TutorialControllerOval(new Rect(110, 70, 25, 25), idle);
            TutorialControllerButton(new Rect(145, 61, 21, 21), "A", (step == 2 || step == 5 && stopDirection == 0) && pressed, active);
            TutorialControllerButton(new Rect(166, 42, 21, 21), "B", step == 7 && pressed, active);
            TutorialControllerButton(new Rect(124, 42, 21, 21), "X", false, active);
            TutorialControllerButton(new Rect(145, 23, 21, 21), "Y", false, active);
            GUI.matrix = previous;
        }

        void TutorialControllerButton(Rect rect, string label, bool pressed, Color accent)
        {
            if (pressed)
            {
                float glow = 2f + 2f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 6f));
                TutorialControllerOval(new Rect(rect.x - glow, rect.y - glow,
                    rect.width + glow * 2f, rect.height + glow * 2f), accent);
            }
            TutorialControllerOval(rect, pressed ? accent : Ink);
            GUIStyle style = new GUIStyle(caption) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = MenuColor(pressed ? Ink : Cream);
            GUI.Label(rect, label, style);
        }

        void DrawTutorialShoulder(Rect rect, string label, bool pressed, Color accent)
        {
            Panel(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), Cream);
            Panel(new Rect(rect.x, rect.y + (pressed ? 2f : 0f), rect.width, rect.height),
                pressed ? accent : Ink);
            GUIStyle style = new GUIStyle(caption) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = MenuColor(pressed ? Ink : Cream);
            GUI.Label(rect, label, style);
        }

        void TutorialControllerOval(Rect rect, Color color)
        {
            // Horizontal strips avoid allocating textures or meshes during GUI repaint.
            for (float y = 0; y < rect.height; y += 2f)
            {
                float normalized = (y + 1f) / rect.height * 2f - 1f;
                float halfWidth = rect.width * .5f * Mathf.Sqrt(Mathf.Max(0f, 1f - normalized * normalized));
                Panel(new Rect(rect.center.x - halfWidth, rect.y + y, halfWidth * 2f,
                    Mathf.Min(2f, rect.height - y)), color);
            }
        }

    }
}
