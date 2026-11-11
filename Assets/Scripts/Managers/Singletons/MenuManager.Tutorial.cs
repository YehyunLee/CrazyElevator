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
        readonly bool[] passengerGalleryReady = new bool[2];
        ElevatorManager roundPassengerPreviewActor;
        int roundPassengerIndex;
        float roundPassengerTimer;
        static readonly string[] TutorialCharacterKinds =
            { "COURIER", "INTERVIEW", "HUNTER", "PREGNANT", "BOSS", "GROUP", "BOUNCER", "ELDERLY", "HANDYMAN", "DIVER" };
        static readonly string[] TutorialCharacterNames =
            { "COURIER", "INTERVIEW CANDIDATE", "HUNTER", "MOM + BABY", "BOSS", "THE TRIO", "BOUNCER", "GRANDMA", "HANDYMAN", "DIVER" };
        static readonly string[] TutorialCharacterDescriptions =
        {
            "2 SPACES • SHORT TRIPS",
            "LOW PATIENCE • DELIVER FAST",
            "STUNS MONSTERS",
            "2 SPACES • BABY DRAINS PATIENCE",
            "ON TIME = BIG BONUS",
            "3 SPACES • SPEED UP + STICKY TRAIL",
            "STRONGER COLLISIONS",
            "SLOWS YOU • CALMS THE BABY",
            "CLEARS RUST • FULL POWER",
            "BLOCKS TORNADOES"
        };
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
        readonly bool[] tutorialSteeredLeft = new bool[2];
        readonly bool[] tutorialSteeredRight = new bool[2];
        readonly float[] tutorialBoostSeconds = new float[2];
        readonly bool[] tutorialMoveCompleted = new bool[2];
        readonly bool[] tutorialBoostCompleted = new bool[2];
        readonly bool[] tutorialStopCompleted = new bool[2];
        readonly bool[] controllerInstructionConfirmed = new bool[2];
        readonly float[] tutorialLastFloor = new float[2];
        readonly int[] tutorialTargetFloor = new int[2];
        ElevatorManager tutorialGame;

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
            ResetPassengerGalleryReady();
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
                tutorialSteeredLeft[i] = false;
                tutorialSteeredRight[i] = false;
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
                    return actor.CurrentPassengers.Count >= ElevatorRound.PracticePassengersPerSeat
                        && tutorialStepSettledTimes[index] >= BoardingCardSeconds;
                case 2:
                    return actor.TravelStartCount > tutorialTravelStartBaseline[index];
                // Move and boost progress adds up across presses instead of resetting,
                // so letting go, reversing, or bumping the shaft end never loses it.
                case 3:
                    if (tutorialMoveCompleted[index]) return true;
                    if (actor.MovingInShaft && actor.TravelInputActive)
                    {
                        tutorialMoveSeconds[index] += Time.unscaledDeltaTime;
                        tutorialMoveDistance[index] += Mathf.Abs(
                            actor.CurrentFloor - tutorialLastFloor[index]);
                    }
                    tutorialLastFloor[index] = actor.CurrentFloor;
                    if (tutorialMoveSeconds[index] >= .8f
                        && tutorialMoveDistance[index] >= .25f)
                        tutorialMoveCompleted[index] = true;
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
                    tutorialSteeredLeft[index] |= actor.LateralTravelVelocity < 0f;
                    tutorialSteeredRight[index] |= actor.LateralTravelVelocity > 0f;
                    return tutorialSteeredLeft[index] && tutorialSteeredRight[index]
                        && actor.OnMainVerticalTrack;
                default:
                    return false;
            }
        }

        void CompleteTutorialStep(int index)
        {
            ElevatorManager actor = tutorialActors[index];
            int step = tutorialSteps[index];
            int nextStep = step + 1;
            if (step == 3 && actor.Match != null)
                nextStep = LateralTravelStep;
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

        void ResetPassengerGalleryReady()
        {
            passengerGalleryReady[0] = passengerGalleryReady[1] = false;
        }

        bool PassengerGalleryNextPressed(ElevatorManager first, ElevatorManager second,
            float elapsed, float minimumSeconds)
        {
            InputManager inputManager = InputManager.Instance;
            if (inputManager == null || elapsed < minimumSeconds) return false;
            bool hasPlayer = false;
            bool allReady = true;
            for (int seat = 0; seat < passengerGalleryReady.Length; seat++)
            {
                ElevatorManager actor = first != null && first.Seat == seat ? first
                    : second != null && second.Seat == seat ? second : null;
                if (actor == null) continue;
                hasPlayer = true;
                InputManager.SeatInput input = inputManager.ReadForSeat(seat, true);
                if (input.ConfirmPressed) passengerGalleryReady[seat] = true;
                if (!passengerGalleryReady[seat]) allReady = false;
            }
            if (!hasPlayer || !allReady) return false;
            ResetPassengerGalleryReady();
            return true;
        }

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
            ResetPassengerGalleryReady();
            roundPassengerIndex = -1;
            roundPassengerTimer = 0f;
            if (roundPassengerPreviewActor == null) return false;

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
            if (PassengerGalleryNextPressed(roundPassengerActors[0], roundPassengerActors[1],
                roundPassengerTimer, .7f)) AdvanceRoundPassenger();
        }

        void AdvanceRoundPassenger()
        {
            roundPassengerTimer = 0f;
            ResetPassengerGalleryReady();
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
            string description = knownIndex >= 0 ? TutorialCharacterDescriptions[knownIndex]
                : RoundPassengerDescription(kind);
            DrawPassengerGalleryCard(roundPassengerPreviewActor,
                "ROUND PASSENGERS  " + (roundPassengerIndex + 1) + " / " + roundPassengerKinds.Count,
                name, description, passengerGalleryReady[0], passengerGalleryReady[1]);
        }

        string RoundPassengerName(string kind)
        {
            foreach (var rider in roundPassengerPreviewActor.AllRiders)
                if (rider != null && rider.Kind == kind && rider.Data != null
                    && !string.IsNullOrEmpty(rider.Data.displayName)) return rider.Data.displayName;
            return kind.Replace('_', ' ');
        }

        string RoundPassengerDescription(string kind)
        {
            foreach (var rider in roundPassengerPreviewActor.AllRiders)
                if (rider != null && rider.Kind == kind && rider.Data != null
                    && !string.IsNullOrEmpty(rider.Data.request)) return rider.Data.request;
            return "MEET THIS ROUND'S PASSENGER.";
        }

        void SkipTutorial()
        {
            if (!tutorialActive || tutorialGame == null) return;
            FinishTutorial();
        }

        string TutorialKey(int step, int seat)
        {
            if (step == 6) return "AUTO";
            if (!KeyboardHintsEnabled)
            {
                switch (step)
                {
                    case 0: case 1: return "STICK";
                    case 2: return "A";
                    case 3: case LateralTravelStep: return "STICK";
                    case 4: return "LB";
                    case 5: return "A";
                    case 7: return "B";
                    case 8: return "LB";
                    default: return "A";
                }
            }
            if (seat == 1)
            {
                switch (step)
                {
                    case 0: case 1: return "ARROWS";
                    case 2: return ".";
                    case 3: return "↑/↓";
                    case LateralTravelStep: return "←/→";
                    case 4: return "RSHIFT";
                    case 5: return ".";
                    case 7: return ",";
                    case 8: return "↑";
                    default: return ".";
                }
            }
            switch (step)
            {
                case 0: case 1: return "W/A/D";
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
                case 1: return "BOARD TWO";
                case 2: return "CLOSE DOORS";
                case 3: return "MOVE";
                case LateralTravelStep: return "MOVE LEFT / RIGHT";
                case 4: return "BOOST";
                case 5: return "STOP";
                case 6: return "DROP-OFF";
                case 7: return "KICK";
                case 8: return tutorialCollisionHitConfirmed ? "NICE HIT!" : "BUMP YOUR RIVAL";
                default: return "CORE CONTROLS LEARNED";
            }
        }

        string TutorialBody(int step, int seat)
        {
            switch (step)
            {
                case -1: return "TUTORIAL";
                case 0:
                    if (!KeyboardHintsEnabled) return "MOVE THE LEFT STICK TO OPEN A DOOR.";
                    return seat == 1 ? "PRESS ← (LEFT), ↑ (FRONT), OR → (RIGHT)."
                        : "PRESS A (LEFT), W (FRONT), OR D (RIGHT).";
                case 1: return "OPEN ANOTHER DOOR.";
                case 2: return "PRESS TO LEAVE.";
                case 3: return KeyboardHintsEnabled ? seat == 1 ? "HOLD ↑ / ↓." : "HOLD W / S."
                    : "HOLD UP / DOWN.";
                case 4: return KeyboardHintsEnabled
                    ? seat == 1 ? "HOLD ↑ + RSHIFT." : "HOLD W + SHIFT."
                    : "HOLD UP + LB.";
                case 5:
                    int targetFloor = tutorialTargetFloor[Mathf.Clamp(seat, 0, 1)];
                    return targetFloor >= 0
                        ? "STOP AT FLOOR " + ElevatorRound.FloorNumber(targetFloor) + "."
                        : "STOP AT PASSENGER FLOOR.";
                case 6: return "AUTOMATIC!";
                case 7: return "KICK HIGHLIGHTED RIDER.";
                case LateralTravelStep:
                    if (tutorialSteeredLeft[seat] && tutorialSteeredRight[seat])
                    {
                        string vertical = KeyboardHintsEnabled ? (seat == 1 ? "↑/↓" : "W/S") : "UP/DOWN";
                        return "STEER TOWARD CENTER AND HOLD " + vertical + " TO REJOIN THE SHAFT.";
                    }
                    if (tutorialSteeredLeft[seat]) return "STEER RIGHT ALONG THE RAIL.";
                    if (tutorialSteeredRight[seat]) return "STEER LEFT ALONG THE RAIL.";
                    return "TAP LEFT OR RIGHT UNTIL YOU TURN ONTO A FLOOR RAIL.";
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

            float width = Mathf.Min(view.width - 36f, view.width < 1200f ? 700f : 860f);
            float height = Mathf.Clamp(view.height * .25f, 280f, 300f);
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
            body.wordWrap = true;
            GUI.Label(new Rect(card.x + 30f, card.y + 17f, card.width - 60f, 24f),
                "TUTORIAL", caption);
            GUI.Label(new Rect(card.x + 30f, card.y + 44f, card.width - 60f, 58f),
                TutorialTitle(step), cardTitle);

            float rowY = card.y + 106f;
            if (step >= 0)
            {
                float hintWidth = KeyboardHintsEnabled || step == 6 ? 68f : Mathf.Min(220f, card.width * .35f);
                if (!KeyboardHintsEnabled && step != 6)
                    DrawTutorialController(new Rect(card.x + 24f, rowY - 15f, hintWidth, 145f), step, accent);
                else
                    DrawTutorialKeyHint(new Rect(card.x + 30f, rowY, hintWidth, 68f),
                        TutorialKey(step, inputSeat));
                float textX = card.x + 48f + hintWidth;
                GUI.Label(new Rect(textX, rowY - 4f, card.xMax - textX - 24f, 78f),
                    TutorialBody(step, index), body);
            }
            else
            {
                GUI.Label(new Rect(card.x + 30f, rowY + 10f, card.width - 60f, 48f),
                    TutorialBody(step, index), body);
            }

            string footer = collisionPractice ? "BUMP THEM AWAY. BOARD FIRST." : "";
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
                waitingForOtherPlayer ? "OTHER DRIVER'S TURN." : "GET READY.", cardTitle);
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
            title.normal.textColor = Cream;
            GUI.Label(new Rect(0f, Screen.height * .34f, Screen.width, Screen.height * .26f),
                countdown, title);
            title.fontSize = previousCountdownFontSize;
            title.alignment = previousCountdownAlignment;
            title.normal.textColor = previousCountdownTextColor;
        }

        void DrawPassengerGalleryCard(ElevatorManager actor, string kicker, string headingText,
            string descriptionText, bool playerOneReady, bool playerTwoReady)
        {
            Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280f * scale) * .5f,
                (Screen.height - 720f * scale) * .5f, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            GUIStyle heading = new GUIStyle(cardTitle)
                { fontSize = 40, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUIStyle description = new GUIStyle(body)
                { fontSize = 25, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            GUIStyle small = new GUIStyle(caption)
                { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            heading.normal.textColor = Cream;
            description.normal.textColor = Cream;
            small.normal.textColor = Teal;
            GUI.Label(new Rect(80, 24, 1120, 32), kicker, small);
            GUI.Label(new Rect(80, 64, 1120, 62), headingText, heading);
            Texture preview = actor != null ? actor.DimensionRevealTexture : null;
            if (preview != null)
                GUI.DrawTexture(new Rect(240, 136, 800, 390), preview, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(160, 536, 960, 104), descriptionText, description);
            int requiredPlayers = roundPassengerActors[1] != null ? 2 : 1;
            string skipHint = "PRESS A TO SKIP";
            if (KeyboardHintsEnabled)
                skipHint += requiredPlayers > 1 ? "  (P1 C / P2 PERIOD)" : "  (C)";
            GUI.Label(new Rect(80, 650, 1120, 30),
                skipHint, small);
            float readiness = (playerOneReady ? 1f : 0f) + (playerTwoReady ? 1f : 0f);
            Panel(new Rect(400, 694, 480, 4), new Color(.2f, .25f, .3f));
            Panel(new Rect(400, 694, 480 * Mathf.Clamp01(readiness / requiredPlayers), 4), Teal);
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

        // Draw in a fixed local space so the same demonstration fits both player panes.
        // Unscaled time keeps the demonstration moving while tutorial gameplay is paused.
        void DrawTutorialController(Rect rect, int step, Color accent)
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

            bool useStick = boardingStep || step == 3 || step == 4 || step == 8
                || step == LateralTravelStep;
            Vector2 offset = Vector2.zero;
            if (useStick && pressed)
            {
                if (boardingStep)
                {
                    int direction = Mathf.FloorToInt(Time.unscaledTime / cycleSeconds) % 3;
                    offset = direction == 0 ? Vector2.left : direction == 1 ? Vector2.up : Vector2.right;
                }
                else if (step == 8) offset = Vector2.up;
                else if (step == LateralTravelStep)
                    offset = Mathf.FloorToInt(Time.unscaledTime / 2.4f) % 2 == 0 ? Vector2.left : Vector2.right;
                else offset = Mathf.FloorToInt(Time.unscaledTime / 2.4f) % 2 == 0 ? Vector2.up : Vector2.down;
            }
            TutorialControllerOval(new Rect(33, 36, 38, 38), useStick ? accent : idle);
            TutorialControllerOval(new Rect(37 + offset.x * 9f, 40 - offset.y * 9f, 30, 30), idle);
            TutorialControllerOval(new Rect(42 + offset.x * 9f, 45 - offset.y * 9f, 20, 20),
                useStick && pressed ? active : shell);
            Panel(new Rect(72, 70, 9, 25), idle);
            Panel(new Rect(64, 78, 25, 9), idle);
            TutorialControllerOval(new Rect(110, 70, 25, 25), idle);
            TutorialControllerButton(new Rect(145, 61, 21, 21), "A", (step == 2 || step == 5) && pressed, active);
            TutorialControllerButton(new Rect(166, 42, 21, 21), "B", step == 7 && pressed, active);
            TutorialControllerButton(new Rect(124, 42, 21, 21), "X", false, active);
            TutorialControllerButton(new Rect(145, 23, 21, 21), "Y", false, active);
            GUI.matrix = previous;
        }

        void TutorialControllerButton(Rect rect, string label, bool pressed, Color accent)
        {
            TutorialControllerOval(rect, pressed ? accent : Ink);
            GUIStyle style = new GUIStyle(caption) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = pressed ? Ink : Cream;
            GUI.Label(rect, label, style);
        }

        void DrawTutorialShoulder(Rect rect, string label, bool pressed, Color accent)
        {
            Panel(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), Cream);
            Panel(new Rect(rect.x, rect.y + (pressed ? 2f : 0f), rect.width, rect.height),
                pressed ? accent : Ink);
            GUIStyle style = new GUIStyle(caption) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = pressed ? Ink : Cream;
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

        void DrawTutorialKeyHint(Rect rect, string key)
        {
            DrawKeyHint(rect, key);
        }
    }
}
