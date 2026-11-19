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
        static readonly int[] TutorialLateralStops = { 3, 1 };
        const int OppositeMovementStep = 10;
        const float TutorialCardTravelSeconds = .18f;
        const float TutorialPracticeSeconds = 1.25f;
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
            "DELIVER PASSENGERS FOR POINTS.",
            "OUTSCORE YOUR RIVAL TO WIN THE ROUND."
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

        internal static bool TutorialHoldsAtShaft(ElevatorManager actor)
        {
            MenuManager menu = Current;
            if (menu == null || !menu.tutorialActive || actor == null) return false;
            int index = System.Array.IndexOf(menu.tutorialActors, actor);
            return index >= 0 && menu.tutorialSteps[index] == LateralTravelStep
                && menu.TutorialLateralStopsReached(index) && !menu.tutorialRejoinedShaft[index];
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
        readonly int[] tutorialLateralGoalsReached = new int[2];
        readonly float[] tutorialLastLateralX = new float[2];
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
            tutorialGame = null;
            tutorialActors[0] = tutorialActors[1] = null;
            InitializeTutorialActors();
            InputManager.Instance?.SetView(GameView.ControlElevator);
        }

        bool InitializeTutorialActors()
        {
            if (tutorialGame != null && (tutorialActors[0] != null || tutorialActors[1] != null)) return true;
            tutorialGame = GameManager.Instance != null ? GameManager.Instance.Player : null;
            if (tutorialGame == null) return false;
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
                tutorialLateralGoalsReached[i] = 0;
                tutorialLastLateralX[i] = actor != null ? actor.RouteLateralX : 0f;
                tutorialRejoinedShaft[i] = false;
                tutorialBoostCompleted[i] = false;
                tutorialStopCompleted[i] = false;
            }
            if (showingControllerInstructions) PauseTutorialGame();
            return true;
        }

        void UpdateTutorial()
        {
            if (!tutorialActive) return;
            TutorialActive = true;
            if (!InitializeTutorialActors()) return;
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
                    return actor.HasChosenBoardingDoor;
                case 1:
                    return actor.OpenBoardingDoorCount == 3
                        && actor.CurrentPassengers.Count >= ElevatorRound.PracticePassengersPerSeat;
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
            if (!tutorialActive) return;
            ElevatorManager actor = tutorialActors[index];
            if (actor == null) return;
            int goal = tutorialLateralGoalsReached[index];
            float currentX = actor.RouteLateralX;
            float previousX = tutorialLastLateralX[index];
            tutorialLastLateralX[index] = currentX;
            // Reach each real side-stop column in order; a small reversal or
            // a central lane change cannot stand in for travelling left/right.
            if (actor.OnFloorRail && goal < TutorialLateralStops.Length)
            {
                int direction = TutorialLateralDirection(index);
                float target = ElevatorManager.StopWorldX(TutorialLateralStops[goal]);
                if ((currentX - previousX) * direction > .001f && (currentX - target) * direction >= -1.5f)
                    tutorialLateralGoalsReached[index]++;
            }
            if (!TutorialLateralStopsReached(index) || tutorialRejoinedShaft[index] || !actor.OnMainVerticalTrack) return;
            tutorialRejoinedShaft[index] = true;
            actor.ShowActionFeedback("NICE!", true, true);
        }

        bool TutorialLateralStopsReached(int index)
            => tutorialLateralGoalsReached[index] >= TutorialLateralStops.Length;

        int TutorialLateralDirection(int index)
            => TutorialLateralStopsReached(index) ? tutorialActors[index].TutorialShaftDirection
                : TutorialLateralStops[tutorialLateralGoalsReached[index]] > ElevatorRound.CenterStop ? 1 : -1;

        string TutorialLateralInstruction(int index)
        {
            if (TutorialLateralStopsReached(index))
                return tutorialActors[index].TutorialReadyToRejoin ? "REJOIN THE SHAFT" : "GO TO THE SHAFT";
            return TutorialLateralDirection(index) > 0 ? "MOVE RIGHT" : "MOVE LEFT";
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
                tutorialLastLateralX[index] = tutorialActors[index] != null
                    ? tutorialActors[index].RouteLateralX : 0f;
                tutorialRejoinedShaft[index] = false;
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
            roundPassengerPreviewActor?.AnimateRoundPassengerDemo(roundPassengerTimer);
            if (PassengerGalleryNextPressed()) AdvanceRoundPassenger();
        }

        void AdvanceRoundPassenger()
        {
            roundPassengerTimer = 0f;
            while (++roundPassengerIndex < roundPassengerKinds.Count)
                if (roundPassengerPreviewActor.PrepareRoundPassengerDemo(roundPassengerKinds[roundPassengerIndex]))
                    return;
            roundPassengerPreviewActor.FinishRoundPassengerDemo();
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
                name, roundPassengerPreviewActor.RoundPassengerDemoCaption(kind));
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

        bool TutorialUsesKeyboard(int seat)
        {
            InputManager input = InputManager.Instance;
            return input == null ? KeyboardHintsEnabled
                : !input.HasController(seat) && (input.CanUseSplitKeyboard || KeyboardHintsEnabled);
        }

        string TutorialActionLabel(int step, int index)
        {
            ElevatorManager actor = tutorialActors[index];
            switch (step)
            {
                case 0: return "Tap to open";
                case 1: return "Passengers are boarding…";
                case 2: return "Press to leave";
                case 3: case OppositeMovementStep: return "Hold to move";
                case 4: return "Hold both to boost";
                case 5:
                    if (actor.TutorialStopInProgress) return "Doors opening…";
                    return TutorialStopDirection(index) == 0 ? "Press to stop here" : "Hold to steer";
                case 6: return "Riders get off on their own";
                case 7: return actor.HasSelectedBoardedPassenger ? "Press to kick" : "Select the highlighted rider";
                case LateralTravelStep:
                    if (TutorialLateralStopsReached(index) && actor.TutorialReadyToRejoin)
                        return actor.TutorialRejoinVerticalDirection > 0 ? "Hold to go up" : "Hold to go down";
                    return "Hold to steer";
                case 8: return tutorialCollisionHitConfirmed ? "You knocked the rival back" : "Hold both to bump";
                default: return string.Empty;
            }
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

        void DrawTutorialOverlayForView(Rect pixelView, int index)
        {
            ElevatorManager actor = tutorialActors[index];
            if (actor == null || tutorialCardMotions[index] == TutorialCardMotion.Hidden) return;

            bool collision = tutorialFinalePhase == TutorialFinalePhase.Collision && index == tutorialCollisionSeat;
            int step = collision ? 8 : tutorialSteps[index];
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            Color previousContent = GUI.contentColor;
            GUI.matrix = Matrix4x4.identity;
            GUI.BeginGroup(pixelView);
            GUI.color = GUI.contentColor = Color.white;
            try
            {
                if (step < 0)
                {
                    DrawPracticeMessage(pixelView, index);
                    return;
                }

                if (step != TutorialWaitingStep || collision)
                {
                    DrawTutorialElevatorSpotlight(pixelView, index, step);
                    if (step == LateralTravelStep && TutorialLateralStopsReached(index) && !tutorialRejoinedShaft[index])
                        DrawTutorialShaftMarker(pixelView, actor);
                }

                float scale = MenuPresentation.GameplayHudScale(pixelView.width, pixelView.height);
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
                var view = new Rect(0f, 0f, pixelView.width / scale, pixelView.height / scale);
                if (step == TutorialWaitingStep && !collision)
                {
                    bool twoPlayers = tutorialActors[0] != null && tutorialActors[1] != null;
                    DrawTutorialWaitingPanel(view, twoPlayers, actor.Seat);
                    return;
                }

                float width = Mathf.Min(570f, view.width - 32f);
                float top = step == LateralTravelStep ? view.height - 193f : 24f;
                float t = Mathf.Clamp01(tutorialCardMotionTimes[index] / TutorialCardTravelSeconds);
                float entrance = Mathf.SmoothStep(0f, 1f, t);
                TutorialCardMotion motion = tutorialCardMotions[index];
                float offset = motion == TutorialCardMotion.Settled ? 0f
                    : motion == TutorialCardMotion.Entering ? -12f * (1f - entrance) : -8f * entrance;
                Rect card = new Rect((view.width - width) * .5f, top + offset, width, 136f);
                Color accent = actor.Seat == 0 && tutorialGame.Match != null && tutorialGame.Match.SplitScreen
                    ? MenuPresentation.Coral : MenuPresentation.Teal;
                if (step == 5 && !actor.TutorialStopInProgress)
                {
                    if (tutorialTargetFloor[index] >= 0 && !DrawTutorialFloorMarker(pixelView, card, index, scale)
                        && TutorialStopDirection(index) != 0)
                        DrawTutorialStopGuide(view, card, index);
                }
                if (step == LateralTravelStep && motion == TutorialCardMotion.Settled)
                    DrawTutorialLateralGuide(view, index, top);

                DrawTutorialActionCard(card, step, index, collision, accent);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GUI.contentColor = previousContent;
                GUI.EndGroup();
            }
        }

        void DrawTutorialActionCard(Rect card, int step, int index, bool collision, Color accent)
        {
            ElevatorManager actor = tutorialActors[index];
            MenuPresentation.HudPlate(card, accent);
            bool split = tutorialGame.Match != null && tutorialGame.Match.SplitScreen;
            string player = split ? "PLAYER " + (actor.Seat + 1) : "PRACTICE";
            DrawTutorialText(new Rect(card.x + 14f, card.y + 8f, 116f, 21f),
                player, caption, 15, accent, TextAnchor.MiddleLeft);
            DrawTutorialProgress(card, step, collision, actor.Match != null, accent);

            string heading = step == 5 && tutorialTargetFloor[index] >= 0 ? TutorialStopInstruction(index)
                : step == 3 || step == OppositeMovementStep ? (TutorialMovementDirection(index, step) > 0 ? "MOVE UP" : "MOVE DOWN")
                : step == LateralTravelStep ? TutorialLateralInstruction(index)
                : TutorialTitle(step);
            DrawTutorialText(new Rect(card.x + 14f, card.y + 33f, card.width - 28f, 42f),
                heading, cardTitle, 34, MenuPresentation.Paper);

            int doorSlot = actor.TutorialSuggestedDoorSlot(0);
            bool passive = step == 6 || step == 1 && actor.OpenBoardingDoorCount == 3
                || step == 5 && actor.TutorialStopInProgress || collision && tutorialCollisionHitConfirmed;
            string label = TutorialActionLabel(step, index);
            Rect row = new Rect(card.x + 14f, card.y + 79f, card.width - 28f, 43f);
            if (passive)
            {
                DrawTutorialText(row, label, body, 24, MenuPresentation.Paper);
                return;
            }

            bool keyboard = TutorialUsesKeyboard(actor.Seat);
            if (step == 1)
            {
                // Show the real open doors so the player can tell which door remains.
                for (int slot = 0; slot < 3; slot++)
                {
                    bool open = actor.TutorialDoorOpen(slot);
                    string doorKey = TutorialExampleKey(1, keyboard ? actor.Seat : 1, index, slot);
                    Rect key = new Rect(row.center.x - 157f + slot * 106f, row.y, 98f, row.height);
                    if (!keyboard) doorKey = "STICK " + doorKey;
                    DrawTutorialInputCap(key, open ? "OPEN" : doorKey, accent, !open && slot == doorSlot, open);
                }
                return;
            }

            string keyText = TutorialActionKey(step, index, doorSlot, keyboard);
            float keyWidth = keyText.Length > 9 ? 166f : keyText.Length > 2 ? 128f : 53f;
            float labelWidth = Mathf.Min(270f, row.width - keyWidth - 14f);
            float x = row.center.x - (keyWidth + 14f + labelWidth) * .5f;
            Rect cap = new Rect(x, row.y, keyWidth, row.height);
            DrawTutorialInputCap(cap, keyText, accent, true, false);
            DrawTutorialText(new Rect(cap.xMax + 14f, row.y, labelWidth, row.height),
                label, body, 23, MenuPresentation.Paper, TextAnchor.MiddleLeft);
        }

        string TutorialActionKey(int step, int index, int doorSlot, bool keyboard)
        {
            ElevatorManager actor = tutorialActors[index];
            if (step == 7 && !actor.HasSelectedBoardedPassenger)
                return keyboard ? "CLICK RIDER" : "STICK";
            if (step == 4 || step == 8)
                return !keyboard ? "STICK ↑ + LB" : actor.Seat == 0 ? "W + SHIFT" : "↑ + R SHIFT";
            if (keyboard) return TutorialExampleKey(step, actor.Seat, index, doorSlot);
            if (step == 2 || step == 5 && TutorialStopDirection(index) == 0) return "A";
            if (step == 7) return "B";
            return "STICK " + TutorialExampleKey(step, 1, index, doorSlot);
        }

        void DrawTutorialProgress(Rect card, int step, bool collision, bool hasRival, Color accent)
        {
            int total = hasRival ? 11 : 9;
            int current = collision ? total : step == OppositeMovementStep ? 5
                : step == LateralTravelStep ? 6 : step < 4 ? step + 1 : step + (hasRival ? 3 : 2);
            float x = card.xMax - 14f - total * 12f + 4f;
            for (int i = 1; i <= total; i++)
            {
                Color fill = i == current ? MenuPresentation.Paper : i < current ? accent : MenuPresentation.Ink;
                MenuPresentation.Paint(new Rect(x + (i - 1) * 12f, card.y + 14f, 8f, 8f), fill);
            }
        }

        void DrawTutorialInputCap(Rect rect, string key, Color accent, bool active, bool complete)
        {
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 5f);
            if (active)
            {
                Color glow = accent;
                glow.a = .25f + pulse * .35f;
                MenuPresentation.Paint(Expand(rect, 2f + pulse * 3f), glow);
            }
            Color face = active || complete ? accent : MenuPresentation.Ink;
            MenuPresentation.Plaque(rect, face, MenuPresentation.Ink, 2f);
            DrawTutorialText(rect, key, cardTitle, 23, active || complete ? MenuPresentation.Ink : MenuPresentation.Paper);
        }

        static void DrawTutorialText(Rect rect, string text, GUIStyle template, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            if (Event.current.type != EventType.Repaint) return;
            var style = new GUIStyle(template)
            {
                fontSize = size,
                alignment = alignment,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset()
            };
            var content = new GUIContent(text);
            while (style.fontSize > 1 && style.CalcSize(content).x > rect.width - 6f) style.fontSize--;
            MenuPresentation.Label(rect, content, style, color);
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
                if (TutorialLateralStopsReached(index) && actor.TutorialReadyToRejoin)
                    return actor.TutorialRejoinVerticalDirection > 0
                        ? (seat == 1 ? "↑" : "W") : (seat == 1 ? "↓" : "S");
                bool left = TutorialLateralDirection(index) < 0;
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
            string arrow = direction == 1 ? "↑" : direction == -1 ? "↓" : direction == 2 ? "→" : "←";
            Rect guide = new Rect((view.width - 46f) * .5f, card.yMax + 9f, 46f, 40f);
            MenuPresentation.HudPlate(guide, MenuPresentation.Brass);
            DrawTutorialText(guide, arrow, cardTitle, 27, MenuPresentation.Brass);
        }

        bool DrawTutorialFloorMarker(Rect view, Rect card, int index, float scale)
        {
            if (Mathf.Abs(TutorialStopDirection(index)) == 2) return false;
            ElevatorManager actor = tutorialActors[index];
            if (!actor.TryGetTutorialFloorScreenPosition(tutorialTargetFloor[index], out Vector3 point)) return false;
            float x = (point.x - view.x) / scale;
            float y = (Screen.height - point.y - view.y) / scale;
            // The guide below the card handles floors outside the visible area.
            if (x < 60f || x > view.width / scale - 60f || y < card.yMax + 70f || y > view.height / scale - 45f) return false;
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
            return true;
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
            const float spotlightSeconds = 1.5f;
            if (age >= spotlightSeconds) return;
            ElevatorManager actor = tutorialActors[index];
            Camera camera = actor != null ? actor.ShaftCamera : null;
            if (camera == null || !camera.enabled) return;
            Vector3 point = camera.WorldToScreenPoint(actor.ShaftWorldPosition);
            if (point.z <= 0f) return;
            float x = point.x - view.x;
            float y = Screen.height - point.y - view.y;
            if (x < 0f || x > view.width || y < 0f || y > view.height) return;
            float radius = Mathf.Min(view.width * .14f, view.height * .12f);
            float alpha = .28f * Mathf.Clamp01((spotlightSeconds - age) / .5f);
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

        void DrawTutorialLateralGuide(Rect view, int index, float cardTop)
        {
            if (TutorialLateralStopsReached(index)) return;
            int direction = TutorialLateralDirection(index);
            float width = Mathf.Min(400f, view.width - 36f);
            float scale = width / 600f;
            float top = Mathf.Max(8f, Mathf.Min(view.height * .3f, cardTop - 92f * scale - 18f));
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.TRS(
                new Vector3((view.width - width) * .5f, top, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));

            const float arrowWidth = 132f;
            float march = Mathf.Sin(Time.unscaledTime * 5.5f) * 8f * direction;
            Rect rect = new Rect((600f - arrowWidth) * .5f + march, 0f, arrowWidth, 78f);
            DrawTutorialDirectionArrow(rect, direction, MenuPresentation.Paper);
            GUI.matrix = previous;
        }

        void DrawTutorialDirectionArrow(Rect rect, int direction, Color fill)
        {
            if (tutorialDirectionArrowTexture == null)
                tutorialDirectionArrowTexture = CreateTutorialDirectionArrow(fill);
            // One continuous image avoids gaps between rows at fractional UI scales.
            Rect uv = direction > 0 ? new Rect(0f, 0f, 1f, 1f) : new Rect(1f, 0f, -1f, 1f);
            MenuPresentation.DrawTextureUV(rect, tutorialDirectionArrowTexture, uv);
        }

        static Texture2D CreateTutorialDirectionArrow(Color fill)
        {
            const int width = 132, height = 78;
            const float stroke = 7f;
            Vector2[] corners =
            {
                new Vector2(4f, 25f), new Vector2(72f, 25f), new Vector2(72f, 4f),
                new Vector2(128f, 39f), new Vector2(72f, 74f),
                new Vector2(72f, 53f), new Vector2(4f, 53f)
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int covered = 0;
                Color sum = Color.clear;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    Vector2 point = new Vector2(x + (sx + .5f) * .5f, y + (sy + .5f) * .5f);
                    bool inside = false;
                    float edgeDistanceSquared = float.MaxValue;
                    for (int edge = 0; edge < corners.Length; edge++)
                    {
                        Vector2 start = corners[edge], end = corners[(edge + 1) % corners.Length];
                        if ((start.y > point.y) != (end.y > point.y)
                            && point.x < start.x + (end.x - start.x) * (point.y - start.y) / (end.y - start.y))
                            inside = !inside;
                        Vector2 segment = end - start;
                        float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / segment.sqrMagnitude);
                        edgeDistanceSquared = Mathf.Min(edgeDistanceSquared,
                            (point - start - segment * t).sqrMagnitude);
                    }
                    if (!inside) continue;
                    covered++;
                    sum += edgeDistanceSquared < stroke * stroke ? MenuPresentation.Ink : fill;
                }
                if (covered == 0) continue;
                Color pixel = sum / covered;
                pixel.a = covered / 4f;
                pixels[y * width + x] = pixel;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Tutorial direction arrow", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
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
            float scale = MenuPresentation.GameplayHudScale(view.width, view.height);
            float maxTitleWidth = view.width * .82f;
            cardTitle.fontSize = Mathf.Max(1, Mathf.RoundToInt(70f * scale));
            float measuredWidth = cardTitle.CalcSize(new GUIContent("TUTORIAL")).x;
            if (measuredWidth > maxTitleWidth)
                cardTitle.fontSize = Mathf.Max(1,
                    Mathf.FloorToInt(cardTitle.fontSize * maxTitleWidth / measuredWidth));
            cardTitle.alignment = TextAnchor.MiddleCenter;
            cardTitle.normal.textColor = MenuColor(new Color(Cream.r, Cream.g, Cream.b, alpha));
            GUI.Label(new Rect(30f * scale, (view.height - 120f * scale) * .5f,
                    view.width - 60f * scale, 120f * scale),
                "TUTORIAL", cardTitle);
            cardTitle.fontSize = previousFontSize;
            cardTitle.alignment = previousAlignment;
            cardTitle.normal.textColor = previousTextColor;
        }

        void DrawTutorialWaitingPanel(Rect view, bool waitingForOtherPlayer, int seat)
        {
            float width = Mathf.Min(view.width - 28f, 470f);
            const float height = 96f;
            Rect card = new Rect((view.width - width) * .5f, 24f, width, height);
            Color accent = waitingForOtherPlayer ? MenuPresentation.Brass : MenuPresentation.Teal;
            MenuPresentation.HudPlate(card, accent);
            var label = new GUIStyle(caption);
            label.normal.textColor = MenuPresentation.DisplayColor(accent);
            GUI.Label(new Rect(card.x + 14f, card.y + 9f, card.width - 28f, 22f),
                waitingForOtherPlayer ? "READY" : "PRACTICE COMPLETE", label);

            var heading = new GUIStyle(cardTitle)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(width * .066f), 16, 30),
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            heading.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            var message = new GUIContent(waitingForOtherPlayer ? "WAITING FOR PLAYER " + (2 - seat) : "GET READY");
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

        void DrawPassengerGalleryCard(ElevatorManager actor, string kicker, string headingText, string summary)
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
            actor.DrawRoundPassengerDemo(new Rect(282f, 135f, 636f, 357.75f), roundPassengerTimer);

            Rect counter = new Rect(449f, 31f, 302f, 39f);
            MenuPresentation.Plaque(counter, MenuPresentation.Teal, ink, 5f);
            DrawLabel(counter, kicker, label, ink);

            Rect nameplate = new Rect(330f, 482f, 540f, 62f);
            MenuPresentation.Plaque(nameplate, MenuPresentation.Paper, brassEdge, 7f);
            MenuPresentation.Fasteners(nameplate, brassEdge);
            heading.fontSize = 29;
            DrawLabel(new Rect(nameplate.x + 22f, nameplate.y + 3f, nameplate.width - 44f, 32f),
                headingText, heading, ink);
            DrawLabel(new Rect(nameplate.x + 22f, nameplate.y + 33f, nameplate.width - 44f, 22f),
                summary, label, ink);

            if (roundPassengerTimer >= RoundPassengerSkipDelay)
            {
                Rect confirm = new Rect(481f, 573f, 238f, 48f);
                Color face = confirm.Contains(Event.current.mousePosition) ? MenuPresentation.Paper : MenuPresentation.Teal;
                MenuPresentation.Plaque(confirm, face, ink, 5f);
                DrawLabel(confirm, "NEXT", label, ink);
                string key = TutorialUsesKeyboard(actor.Seat) ? (actor.Seat == 1 ? "." : "C") : "A";
                DrawKeyHint(new Rect(confirm.xMax + 12f, confirm.y, 48f, 48f), key);
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
    }
}
