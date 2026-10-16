using UnityEngine;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    public sealed partial class MenuManager
    {
        const int SoloTutorialStepCount = 8;
        const int DuelTutorialStepCount = 8;
        const float TutorialCardTravelSeconds = .42f;
        const float TutorialPracticeSeconds = 1.7f;
        const float TutorialStoryLineSeconds = 3.8f;
        const float TutorialStoryFadeSeconds = .6f;
        const float TutorialHandoffFadeSeconds = .8f;
        const float TutorialCountdownSeconds = 4f;
        static readonly string[] TutorialStoryLines =
        {
            "YOU ARE THE ELEVATOR DRIVER.",
            "YOU ARE FIGHTING FOR A PROMOTION.",
            "BEAT YOUR COLLEAGUE.",
            "UNDER 3 MINUTES, DELIVER THE MOST PASSENGERS.",
            "COLLIDE WITH THEIR ELEVATOR TO ATTACK.",
            "USE UNIQUE PASSENGERS AND THE MULTIVERSE TO GET AHEAD."
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
            Story,
            FadeToControls,
            Controls,
            Countdown
        }

        static bool tutorialOnLoad;
        public static bool TutorialActive { get; private set; }

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
        readonly bool[] tutorialResumeActorOnCardSettled = new bool[2];
        readonly int[] tutorialExitBaseline = new int[2];
        readonly int[] tutorialTravelStartBaseline = new int[2];
        readonly int[] tutorialFloorStopBaseline = new int[2];
        readonly float[] tutorialMoveSeconds = new float[2];
        readonly float[] tutorialMoveDistance = new float[2];
        readonly float[] tutorialBoostSeconds = new float[2];
        readonly bool[] tutorialMoveCompleted = new bool[2];
        readonly bool[] tutorialBoostCompleted = new bool[2];
        readonly bool[] tutorialStopCompleted = new bool[2];
        readonly bool[] controllerInstructionConfirmed = new bool[2];
        readonly float[] tutorialLastFloor = new float[2];
        readonly int[] tutorialTargetFloor = new int[2];
        ElevatorManager tutorialGame;

        int ActiveTutorialStepCount => tutorialActors[1] != null
            ? DuelTutorialStepCount : SoloTutorialStepCount;

        bool TutorialWaitingPanelActive
        {
            get
            {
                if (!tutorialActive) return false;
                for (int i = 0; i < tutorialActors.Length; i++)
                    if (tutorialActors[i] != null && tutorialSteps[i] >= ActiveTutorialStepCount)
                        return true;
                return false;
            }
        }

        bool RequiresBothControllerInstructionConfirmations
            => tutorialActive ? tutorialActors[1] != null : playerTwoHumanOnLoad;

        void ResetControllerInstructionConfirmations()
        {
            controllerInstructionConfirmed[0] = false;
            controllerInstructionConfirmed[1] = false;
        }

        bool ToggleControllerInstructionConfirmation(int seat)
        {
            if (seat < 0 || seat >= controllerInstructionConfirmed.Length) return false;
            controllerInstructionConfirmed[seat] = !controllerInstructionConfirmed[seat];
            return ControllerInstructionsReady();
        }

        bool ControllerInstructionsReady()
        {
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
            tutorialActors[0] = tutorialGame;
            tutorialActors[1] = tutorialGame != null && tutorialGame.Match != null
                && tutorialGame.Match.Rival != null && !tutorialGame.Match.Rival.IsNpc
                ? tutorialGame.Match.Rival : null;
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                ElevatorManager actor = tutorialActors[i];
                tutorialSteps[i] = actor != null ? -1 : ActiveTutorialStepCount;
                tutorialNextSteps[i] = -1;
                tutorialCardMotions[i] = TutorialCardMotion.Hidden;
                tutorialCardMotionTimes[i] = 0f;
                tutorialPracticeTimes[i] = 0f;
                tutorialCompletionTimes[i] = 0f;
                tutorialResumeActorOnCardSettled[i] = false;
                actor?.SuppressEncounterCards();
                tutorialExitBaseline[i] = actor != null ? actor.PassengerExitCount : 0;
                tutorialTravelStartBaseline[i] = actor != null ? actor.TravelStartCount : 0;
                tutorialFloorStopBaseline[i] = actor != null ? actor.FloorStopCount : 0;
                tutorialLastFloor[i] = actor != null ? actor.CurrentFloor : 0f;
                tutorialTargetFloor[i] = -1;
                tutorialMoveCompleted[i] = false;
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

            InputManager inputManager = InputManager.Instance;
            InputManager.SeatInput input = inputManager != null
                ? inputManager.ReadForSeat(0, true) : default;
            InputManager.SeatInput secondInput = inputManager != null && tutorialActors[1] != null
                ? inputManager.ReadForSeat(1, true) : default;
            if (input.PausePressed || secondInput.PausePressed)
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
                if (step >= ActiveTutorialStepCount)
                {
                    tutorialCompletionTimes[i] += Time.unscaledDeltaTime;
                    continue;
                }

                if (TutorialStepCompletedForActor(i, step))
                    CompleteTutorialStep(i);
            }

            bool allPlayersFinished = true;
            bool allPlayersReady = true;
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                if (tutorialActors[i] == null) continue;
                allPlayersFinished &= tutorialSteps[i] >= ActiveTutorialStepCount
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
                    return actor.HasSelectedPassenger;
                case 1:
                    return actor.CurrentPassengers.Count >= 2;
                case 2:
                    return actor.TravelStartCount > tutorialTravelStartBaseline[index];
                case 3:
                    if (tutorialMoveCompleted[index]) return true;
                    if (actor.MovingInShaft && actor.TravelInputActive
                        && Mathf.Abs(actor.TravelVelocity) > .05f)
                    {
                        tutorialMoveSeconds[index] += Time.unscaledDeltaTime;
                        tutorialMoveDistance[index] += Mathf.Abs(
                            actor.CurrentFloor - tutorialLastFloor[index]);
                    }
                    else
                    {
                        tutorialMoveSeconds[index] = 0f;
                        tutorialMoveDistance[index] = 0f;
                    }
                    tutorialLastFloor[index] = actor.CurrentFloor;
                    if (tutorialMoveSeconds[index] >= 1.15f
                        && tutorialMoveDistance[index] >= .35f)
                        tutorialMoveCompleted[index] = true;
                    return tutorialMoveCompleted[index];
                case 4:
                    if (tutorialBoostCompleted[index]) return true;
                    if (actor.MovingInShaft && actor.BoostActive)
                        tutorialBoostSeconds[index] += Time.unscaledDeltaTime;
                    else tutorialBoostSeconds[index] = 0f;
                    if (tutorialBoostSeconds[index] >= .65f)
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
                case 6:
                case 7:
                    return actor.PassengerExitCount > tutorialExitBaseline[index];
                default:
                    return false;
            }
        }

        void CompleteTutorialStep(int index)
        {
            ElevatorManager actor = tutorialActors[index];
            int nextStep = tutorialSteps[index] + 1;
            tutorialMoveSeconds[index] = 0f;
            tutorialMoveDistance[index] = 0f;
            tutorialBoostSeconds[index] = 0f;
            tutorialMoveCompleted[index] = false;
            tutorialBoostCompleted[index] = false;
            tutorialStopCompleted[index] = false;
            if (nextStep == 1)
                tutorialTargetFloor[index] = actor.SelectedPassengerDestination;
            if (nextStep == 2 && tutorialTargetFloor[index] < 0)
                tutorialTargetFloor[index] = FindTutorialPassengerFloor(actor);
            if (nextStep == 7)
                tutorialExitBaseline[index] = actor.PassengerExitCount;
            if (nextStep == 3)
                tutorialLastFloor[index] = actor.CurrentFloor;
            BeginTutorialCardTransition(index, nextStep, nextStep < ActiveTutorialStepCount);
        }

        bool TutorialHasPassengerAtCurrentFloor(ElevatorManager actor)
        {
            if (actor == null) return false;
            foreach (var rider in actor.CurrentPassengers)
                if (rider.Destination == actor.DiscreteFloor) return true;
            return false;
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
            tutorialResumeActorOnCardSettled[index] = resumeWhenSettled;
            InputManager.TutorialInputMode inputMode = TutorialInputModeForStep(nextStep);
            InputManager.Instance?.SetTutorialInputMode(tutorialActors[index] != null
                ? tutorialActors[index].Seat : index, inputMode);
            if (nextStep >= ActiveTutorialStepCount)
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
            tutorialCardMotions[index] = TutorialCardMotion.Exiting;
            tutorialCardMotionTimes[index] = 0f;
            if (nextStep == 0) tutorialPracticeTimes[index] = 0f;
            PauseTutorialActor(index);
        }

        InputManager.TutorialInputMode TutorialInputModeForStep(int step)
        {
            switch (step)
            {
                case 0: return InputManager.TutorialInputMode.SelectPassenger;
                case 1: return InputManager.TutorialInputMode.Free;
                case 2: return InputManager.TutorialInputMode.CloseDoors;
                case 3: return InputManager.TutorialInputMode.Move;
                case 4: return InputManager.TutorialInputMode.Boost;
                case 5: return InputManager.TutorialInputMode.Stop;
                case 6: return InputManager.TutorialInputMode.HelpPassenger;
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
                && tutorialSteps[index] < ActiveTutorialStepCount)
                ResumeTutorialActor(index);
            tutorialResumeActorOnCardSettled[index] = false;
        }

        void PauseTutorialActor(int index)
        {
            tutorialActors[index]?.SetPausedPublic(true);
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
            tutorialFinalePhase = TutorialFinalePhase.Story;
            tutorialFinaleTimer = 0f;
            tutorialStoryIndex = 0;
            PauseTutorialGame();
        }

        void UpdateTutorialFinale()
        {
            tutorialFinaleTimer += Time.unscaledDeltaTime;
            switch (tutorialFinalePhase)
            {
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
            tutorialActive = false;
            TutorialActive = false;
            tutorialFinalePhase = TutorialFinalePhase.None;
            showingControllerInstructions = false;
            InputManager.Instance?.ClearTutorialInputLocks();
            for (int i = 0; i < tutorialActors.Length; i++)
            {
                ResumeTutorialActor(i);
            }
            ResumeTutorialGame();
            menuFadeAlpha = 1f;
            InputManager.Instance?.SetView(GameView.ControlElevator);
        }

        string TutorialKey(int step, int seat)
        {
            if (!KeyboardHintsEnabled)
            {
                switch (step)
                {
                    case 0: return "STICK";
                    case 2: return "Y";
                    case 3: return "STICK";
                    case 4: return "BOOST";
                    case 5: return "A";
                    case 7: return "A";
                    case 8: return "A/D";
                    default: return "A";
                }
            }
            if (seat == 1)
            {
                switch (step)
                {
                    case 0: return "ARROWS";
                    case 2: return "/";
                    case 3: return "↑/↓";
                    case 4: return "RSHIFT";
                    case 5: return ".";
                    case 7: return ".";
                    case 8: return "A/D";
                    default: return ".";
                }
            }
            switch (step)
            {
                case 0: return "WASD";
                case 2: return "E";
                case 3: return "W/S";
                case 4: return "SHIFT";
                case 5: return "C";
                case 7: return "C";
                case 8: return "A/D";
                default: return "C";
            }
        }

        string TutorialTitle(int step)
        {
            switch (step)
            {
                case -1: return "PRACTICE SHIFT";
                case 0: return "SELECT PASSENGER";
                case 1: return "ONBOARD";
                case 2: return "CLOSE DOORS";
                case 3: return "MOVE";
                case 4: return "SPEED BOOST";
                case 5: return "STOP";
                case 6: return "OFFBOARD";
                case 7: return "KICK OUT";
                default: return "CORE CONTROLS LEARNED";
            }
        }

        string TutorialBody(int step, int seat)
        {
            switch (step)
            {
                case -1: return "TUTORIAL";
                case 0: return "MOVE TO A PASSENGER.";
                case 1: return "BOARD 2 PASSENGERS.";
                case 2: return "CLOSE THE DOORS.";
                case 3: return "HOLD UP OR DOWN.";
                case 4: return "HOLD BUMPERS (LB + RB) + MOVE.";
                case 5:
                    int targetFloor = tutorialTargetFloor[Mathf.Clamp(seat, 0, 1)];
                    return targetFloor >= 0
                        ? "STOP AT FLOOR " + targetFloor + "."
                        : "STOP AT PASSENGER FLOOR.";
                case 6: return "HELP A PASSENGER OUT.";
                case 7:
                    return "KICK A PASSENGER OUT.";
                default: return "START YOUR SHIFT.";
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

        void DrawTutorialOverlayForView(Rect view, int index)
        {
            if (index < 0 || index >= tutorialActors.Length
                || tutorialActors[index] == null
                || tutorialCardMotions[index] == TutorialCardMotion.Hidden)
                return;
            int step = tutorialSteps[index];
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

            if (step >= ActiveTutorialStepCount)
            {
                DrawTutorialWaitingPanel(view, tutorialActors[1] != null);
                GUI.matrix = previousMatrix;
                GUI.color = previousGuiColor;
                GUI.contentColor = previousContentColor;
                return;
            }

            float width = Mathf.Min(view.width - 36f, view.width < 1200f ? 700f : 860f);
            float height = Mathf.Clamp(view.height * .25f, 230f, 260f);
            float margin = Mathf.Clamp(view.width * .035f, 18f, 32f);
            float settledX = margin;
            float settledY = Mathf.Max(margin, view.height - height - margin);
            float motionT = Mathf.Clamp01(tutorialCardMotionTimes[index] / TutorialCardTravelSeconds);
            float eased = 1f - Mathf.Pow(1f - motionT, 3f);
            float cardX = settledX;
            float cardY = settledY;
            if (tutorialCardMotions[index] == TutorialCardMotion.Entering)
                cardY = Mathf.Lerp(-height - 28f, settledY, eased);
            else if (tutorialCardMotions[index] == TutorialCardMotion.Exiting)
                cardY = Mathf.Lerp(settledY, view.height + 28f, eased);
            Rect card = new Rect(cardX, cardY, width, height);
            Color accent = step == 6 ? Coral
                : step == 7 ? new Color32(255, 205, 82, 255) : Teal;
            Panel(new Rect(card.x + 6f, card.y + 7f, card.width, card.height), new Color(0f, 0f, 0f, .38f));
            Panel(card, accent);
            Panel(new Rect(card.x + 4f, card.y + 4f, card.width - 8f, card.height - 8f), Ink);

            Color previousTitle = cardTitle.normal.textColor;
            Color previousBody = body.normal.textColor;
            Color previousCaption = caption.normal.textColor;
            int previousTitleSize = cardTitle.fontSize;
            int previousBodySize = body.fontSize;
            TextAnchor previousBodyAlignment = body.alignment;
            bool previousBodyWrap = body.wordWrap;
            cardTitle.normal.textColor = Cream;
            body.normal.textColor = new Color(Cream.r, Cream.g, Cream.b, .9f);
            caption.normal.textColor = accent;
            cardTitle.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .06f), 34, 48);
            body.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .034f), 19, 28);
            body.alignment = TextAnchor.MiddleLeft;
            body.wordWrap = false;
            GUI.Label(new Rect(card.x + 30f, card.y + 17f, card.width - 60f, 24f),
                "TUTORIAL", caption);
            GUI.Label(new Rect(card.x + 30f, card.y + 44f, card.width - 60f, 58f),
                TutorialTitle(step), cardTitle);

            float rowY = card.y + 120f;
            if (step >= 0)
            {
                const float keySize = 68f;
                DrawTutorialKeyHint(new Rect(card.x + 30f, rowY, keySize, keySize),
                    TutorialKey(step, inputSeat));
                GUI.Label(new Rect(card.x + 116f, rowY + 10f, card.width - 146f, 48f),
                    TutorialBody(step, index), body);
            }
            else
            {
                GUI.Label(new Rect(card.x + 30f, rowY + 10f, card.width - 60f, 48f),
                    TutorialBody(step, index), body);
            }

            string footer = "ESC TO EXIT";
            if (!string.IsNullOrEmpty(footer))
            {
                int previousFooterSize = caption.fontSize;
                caption.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .022f), 13, 18);
                caption.normal.textColor = Cream;
                GUI.Label(new Rect(card.x + 30f, card.yMax - 38f, card.width - 60f, 24f), footer, caption);
                caption.fontSize = previousFooterSize;
            }

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
            cardTitle.normal.textColor = new Color(Cream.r, Cream.g, Cream.b, alpha);
            GUI.Label(new Rect(30f, (view.height - 120f) * .5f, view.width - 60f, 120f),
                "TUTORIAL", cardTitle);
            cardTitle.fontSize = previousFontSize;
            cardTitle.alignment = previousAlignment;
            cardTitle.normal.textColor = previousTextColor;
        }

        void DrawTutorialWaitingPanel(Rect view, bool waitingForOtherPlayer)
        {
            float width = Mathf.Min(view.width - 36f, view.width < 1200f ? 700f : 860f);
            float height = Mathf.Clamp(view.height * .23f, 205f, 235f);
            float x = Mathf.Max(18f, (view.width - width) * .5f);
            float y = Mathf.Max(18f, view.height - height - 28f);
            Color accent = waitingForOtherPlayer
                ? new Color32(255, 205, 82, 255) : Teal;
            Panel(new Rect(x + 6f, y + 7f, width, height), new Color(0f, 0f, 0f, .38f));
            Panel(new Rect(x, y, width, height), accent);
            Panel(new Rect(x + 4f, y + 4f, width - 8f, height - 8f), Ink);

            Color previousTitle = cardTitle.normal.textColor;
            Color previousBody = body.normal.textColor;
            Color previousCaption = caption.normal.textColor;
            int previousTitleSize = cardTitle.fontSize;
            int previousBodySize = body.fontSize;
            TextAnchor previousTitleAlignment = cardTitle.alignment;
            TextAnchor previousBodyAlignment = body.alignment;
            bool previousTitleWrap = cardTitle.wordWrap;
            cardTitle.normal.textColor = Cream;
            body.normal.textColor = new Color(Cream.r, Cream.g, Cream.b, .92f);
            caption.normal.textColor = accent;
            cardTitle.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .045f), 24, 36);
            body.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.width * .027f), 18, 25);
            cardTitle.alignment = TextAnchor.MiddleCenter;
            body.alignment = TextAnchor.MiddleCenter;
            cardTitle.wordWrap = false;
            GUI.Label(new Rect(x + 24f, y + 18f, width - 48f, 26f),
                waitingForOtherPlayer ? "WAITING" : "TUTORIAL COMPLETE", caption);
            GUI.Label(new Rect(x + 24f, y + 50f, width - 48f, 48f),
                waitingForOtherPlayer ? "WAITING FOR OTHER DRIVER." : "GET READY.", cardTitle);
            GUI.Label(new Rect(x + 24f, y + 126f, width - 48f, 36f),
                waitingForOtherPlayer ? "YOUR TUTORIAL IS COMPLETE." : "SHIFT STARTING SOON.", body);
            cardTitle.fontSize = previousTitleSize;
            body.fontSize = previousBodySize;
            cardTitle.alignment = previousTitleAlignment;
            body.alignment = previousBodyAlignment;
            cardTitle.wordWrap = previousTitleWrap;
            cardTitle.normal.textColor = previousTitle;
            body.normal.textColor = previousBody;
            caption.normal.textColor = previousCaption;
        }

        void DrawTutorialFinale()
        {
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
            string countdown = tutorialFinaleTimer < 3f
                ? (3 - Mathf.FloorToInt(tutorialFinaleTimer)).ToString()
                : "START";
            int previousCountdownFontSize = title.fontSize;
            TextAnchor previousCountdownAlignment = title.alignment;
            Color previousCountdownTextColor = title.normal.textColor;
            title.fontSize = countdown == "START"
                ? Mathf.Clamp(Mathf.RoundToInt(Screen.width * .07f), 64, 118)
                : Mathf.Clamp(Mathf.RoundToInt(Screen.width * .12f), 100, 190);
            title.alignment = TextAnchor.MiddleCenter;
            title.normal.textColor = Cream;
            GUI.Label(new Rect(0f, Screen.height * .34f, Screen.width, Screen.height * .26f),
                countdown, title);
            title.fontSize = previousCountdownFontSize;
            title.alignment = previousCountdownAlignment;
            title.normal.textColor = previousCountdownTextColor;
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
            cardTitle.normal.textColor = new Color(Cream.r, Cream.g, Cream.b, alpha);
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

        void DrawTutorialKeyHint(Rect rect, string key)
        {
            DrawKeyHint(rect, key);
        }
    }
}
