using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    // Mode select, pause overlay, and passenger destination overlay.
    [DefaultExecutionOrder(32000)]
    public sealed partial class MenuManager : MonoBehaviour
    {
        const string SinglePlayerScene = "Assets/Scenes/Main.unity";
        const string DuelScene = SinglePlayerScene;
        static readonly Color Ink = new Color32(9, 11, 16, 255);
        static readonly Color Teal = new Color32(75, 226, 202, 255);
        // Match the turquoise baked into the title artwork's menu buttons.
        static readonly Color TitleTeal = new Color32(90, 201, 179, 255);
        static readonly Color Coral = new Color32(255, 124, 104, 255);
        static readonly Color Cream = new Color32(255, 250, 234, 255);
        static readonly Color PlayerOneColour = new Color32(255, 80, 80, 255);
        static readonly Color PlayerTwoColour = new Color32(80, 145, 255, 255);
        static readonly Color LobbySurface = new Color32(55, 59, 68, 255);
        static readonly Color CpuReadyColour = new Color32(145, 150, 160, 255);
        static readonly Color DisabledColour = new Color32(100, 105, 112, 255);
        [SerializeField] Texture2D menuArtwork;
        [SerializeField] Texture2D controllerInstructions;
        const float ArtWidth = 1700f;
        const float ArtHeight = 1082f;
        const float TransitionSeconds = 1.1f;
        const float LaunchSeconds = .95f;
        const float MenuFadeSeconds = .55f;
        // Keep the IMGUI cursor and action hint locked to the same reference-
        // resolution rectangles as the title Canvas buttons in Main.unity.
        static readonly Rect[] TitleButtonRects =
        {
            new Rect(402f, 707f, 246f, 104f), // START
            new Rect(383f, 826f, 301f, 102f), // OPTIONS
            new Rect(434f, 939f, 185f, 98f)   // EXIT
        };
        static readonly Rect[] OptionsButtonRects =
        {
            new Rect(480f, 360f, 740f, 140f),
            new Rect(480f, 535f, 740f, 120f),
            new Rect(620f, 730f, 460f, 90f)
        };
        const string InputHintsPreference = "CrazyElevator.InputHints.Keyboard";
        const string TutorialModePreference = "CrazyElevator.TutorialMode.Enabled";
        [Header("First menu buttons")]
        [SerializeField] CanvasGroup titleButtons;
        [System.Serializable]
        sealed class MenuLabelShape
        {
            // Coordinates run from 0 to 1 within the label: top-left, top-right,
            // bottom-right, bottom-left. Edit these on Main > MenuManager.
            public Vector2 topLeft = new Vector2(.04f, .04f);
            public Vector2 topRight = new Vector2(.98f, 0);
            public Vector2 bottomRight = new Vector2(1, .92f);
            public Vector2 bottomLeft = new Vector2(0, 1);
        }
        [Header("Menu label corners (0 to 1)")]
        [SerializeField] MenuLabelShape playerOneLabel = new MenuLabelShape
        {
            topLeft = new Vector2(.02f, .02f), topRight = new Vector2(.98f, .18f),
            bottomRight = new Vector2(.98f, .94f), bottomLeft = new Vector2(.02f, 1)
        };
        [SerializeField] MenuLabelShape rivalLabel = new MenuLabelShape
        {
            topLeft = new Vector2(.02f, .18f), topRight = new Vector2(.98f, .02f),
            bottomRight = new Vector2(.98f, 1), bottomLeft = new Vector2(.02f, .94f)
        };
        [Header("Mode screen button rectangles (1700 x 1082)")]
        [SerializeField] Rect playerOneButton = new Rect(535, 795, 260, 72);
        [SerializeField] Rect rivalButton = new Rect(905, 795, 260, 72);
        // The status bars use the same outer frame as the cards and meet their
        // lower edge without a separate seam.
        [SerializeField] Rect playerOneReadyButton = new Rect(535, 873, 260, 46);
        [SerializeField] Rect playerTwoReadyButton = new Rect(905, 873, 260, 46);
        [SerializeField] Rect startButton = new Rect(720, 930, 260, 72);
        [SerializeField] Rect backButton = new Rect(48, 46, 220, 58);
        Texture2D playerOneLabelTexture, rivalLabelTexture;
        Texture2D triangleCursorTexture;
        Texture2D keyIconTexture;
        Texture2D leftChoiceArrowTexture, rightChoiceArrowTexture;
        int playerOneShapeHash, rivalShapeHash;
        float transition;
        float launchTimer;
        bool choosingMode;
        bool launching;
        bool playerTwoUnavailable;
        bool playerOneReady;
        bool playerTwoReady;
        bool lastPlayerTwoRegistered;
        bool playerOneHuman = true;
        int playerOneCursor;
        int playerTwoCursor;
        bool playerOneCursorHeld;
        bool playerTwoCursorHeld;
        int titleSelection;
        int modeSelection;
        bool showingOptions;
        int optionsSelection;
        bool optionsHorizontalHeld;
        bool optionsVerticalHeld;
        static bool keyboardHints;
        static bool inputHintsLoaded;
        static bool tutorialMode = true;
        static bool tutorialModeLoaded;
        bool horizontalHeld;
        bool verticalHeld;
        GUIStyle title, cardTitle, caption, cardCaption, body, menuLabelText, readyLabelText, startCircle;
        static bool startDuelOnLoad;
        static bool showControllerInstructionsOnLoad;
        static bool playerTwoHumanOnLoad;
        bool showingControllerInstructions;
        static bool playerTwoSelected;
        static bool playerOneCpuOnLoad;
        static bool openLobbyOnLoad;
        static bool fadeMenuOnLoad;
        static int pauseSelection;
        static bool pauseMoveHeld;
        static bool pauseOpen;
        static int pauseOpenedFrame = -1;
        static bool passengerOverlayOpen;
        static ElevatorManager overlayGame;
        float menuFadeAlpha;
        public static bool IsOpen { get; private set; }
        public static bool PauseOpen => pauseOpen;
        public static MenuManager Current { get; private set; }
        public static bool FullScreenOverlayActive
        {
            get
            {
                // Scene reloads can briefly leave Current pointing at the
                // destroyed menu object while its replacement is already
                // drawing. Resolve the live scene object as a fallback so
                // the split HUD cannot paint over a full-screen takeover.
                MenuManager menu = FindAnyObjectByType<MenuManager>();
                if (menu == null) menu = Current;
                return menu != null
                    && (menu.showingControllerInstructions
                        || menu.tutorialActive
                            && menu.tutorialFinalePhase != TutorialFinalePhase.None);
            }
        }
        // Survives Main reloads after the player picks a mode (LoadScene).
        static bool sessionReady;

        public static bool KeyboardHintsEnabled
        {
            get
            {
                LoadInputHintsPreference();
                return keyboardHints;
            }
        }

        public static bool TutorialModeEnabled
        {
            get
            {
                LoadTutorialModePreference();
                return tutorialMode;
            }
        }

        static void LoadInputHintsPreference()
        {
            if (inputHintsLoaded) return;
            keyboardHints = PlayerPrefs.GetInt(InputHintsPreference, 0) == 1;
            inputHintsLoaded = true;
        }

        static void LoadTutorialModePreference()
        {
            if (tutorialModeLoaded) return;
            tutorialMode = PlayerPrefs.GetInt(TutorialModePreference, 1) == 1;
            tutorialModeLoaded = true;
        }

        static void SetKeyboardHints(bool enabled)
        {
            keyboardHints = enabled;
            inputHintsLoaded = true;
            PlayerPrefs.SetInt(InputHintsPreference, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        static void SetTutorialMode(bool enabled)
        {
            tutorialMode = enabled;
            tutorialModeLoaded = true;
            PlayerPrefs.SetInt(TutorialModePreference, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        // Enter Play Mode Options can skip Domain Reload; reset statics manually.
        // Show mode select on each Play; Awake keeps the choice through LoadScene via sessionReady.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            sessionReady = false;
            IsOpen = false;
            pauseOpen = false;
            pauseOpenedFrame = -1;
            passengerOverlayOpen = false;
            startDuelOnLoad = false;
            showControllerInstructionsOnLoad = false;
            playerTwoHumanOnLoad = false;
            playerTwoSelected = false;
            playerOneCpuOnLoad = false;
            openLobbyOnLoad = false;
            fadeMenuOnLoad = false;
            tutorialOnLoad = false;
            TutorialActive = false;
            overlayGame = null;
            inputHintsLoaded = false;
            tutorialModeLoaded = false;
        }

        void Awake()
        {
            Current = this;
            menuFadeAlpha = fadeMenuOnLoad ? 1f : 0f;
            fadeMenuOnLoad = false;
            LoadInputHintsPreference();
            LoadTutorialModePreference();
            // Scene object in Main — do not spawn a hidden DDOL menu.
            if (!sessionReady)
            {
                IsOpen = true;
                pauseOpen = false;
                passengerOverlayOpen = false;
                startDuelOnLoad = false;
                playerTwoSelected = false;
                playerOneHuman = true;
                sessionReady = true;
            }
            if (openLobbyOnLoad)
            {
                IsOpen = true;
                openLobbyOnLoad = false;
                BeginModeSelection();
                transition = TransitionSeconds;
            }
            if (showControllerInstructionsOnLoad)
            {
                IsOpen = true;
                ResetControllerInstructionConfirmations();
                showingControllerInstructions = true;
                showControllerInstructionsOnLoad = false;
            }
            InitializeTutorialOnLoad();
            SyncTitleButtons();
            InputManager.Instance?.SetView(IsOpen ? GameView.Menu : GameView.ControlElevator);
        }

        void OnEnable() { title = null; }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (playerOneLabelTexture != null) Destroy(playerOneLabelTexture);
            if (rivalLabelTexture != null) Destroy(rivalLabelTexture);
            if (triangleCursorTexture != null) Destroy(triangleCursorTexture);
            if (keyIconTexture != null) Destroy(keyIconTexture);
            if (leftChoiceArrowTexture != null) Destroy(leftChoiceArrowTexture);
            if (rightChoiceArrowTexture != null) Destroy(rightChoiceArrowTexture);
        }

        void Update()
        {
            menuFadeAlpha = Mathf.MoveTowards(menuFadeAlpha, 0f,
                Time.unscaledDeltaTime / MenuFadeSeconds);
            if (tutorialActive)
            {
                UpdateTutorial();
                return;
            }
            if (pauseOpen)
            {
                UpdatePauseNavigation();
                return;
            }
            if (!IsOpen) return;
            if (showingControllerInstructions)
            {
                if (ControllerInstructionsConfirmed())
                    DismissControllerInstructions();
                return;
            }
            if (showingOptions)
            {
                UpdateOptionsNavigation();
                SyncTitleButtons();
                return;
            }
            if (launching)
            {
                launchTimer += Time.unscaledDeltaTime;
                if (launchTimer >= LaunchSeconds) SelectAndStart(true);
                return;
            }
            var input = InputManager.Instance;
            if (input == null) return;

            var playerOneInput = input.ReadForSeat(0, true);
            var playerTwoInput = input.ReadForSeat(1, true);
            var keyboard = Keyboard.current;
            bool keyboardMenuConfirm = keyboard != null
                && (keyboard.cKey.wasPressedThisFrame
                    || keyboard.enterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame);
            bool splitKeyboardMenu = keyboard != null && !input.HasController(1);
            if (splitKeyboardMenu)
            {
                // Keep Player 2's menu controls independent from PlayerInput
                // joining. Arrow keys navigate; period/slash confirm.
                playerTwoInput.Move = new Vector2(
                    (keyboard.rightArrowKey.isPressed ? 1 : 0)
                        - (keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.upArrowKey.isPressed ? 1 : 0)
                        - (keyboard.downArrowKey.isPressed ? 1 : 0));
                bool playerTwoConfirm = keyboard.periodKey.wasPressedThisFrame
                    || keyboard.slashKey.wasPressedThisFrame;
                playerTwoInput.ConfirmPressed |= playerTwoConfirm;
                playerTwoInput.StartPressed |= playerTwoConfirm;
            }
            if (keyboard != null && choosingMode)
            {
                // The lobby's Player 1 cursor is shared by keyboard, controller,
                // and mouse. Keep Player 2's arrow-key controls independent.
                playerOneInput.Move += KeyboardMenuMove(splitKeyboardMenu);
                playerOneInput.ConfirmPressed |= keyboardMenuConfirm;
                playerOneInput.StartPressed |= keyboardMenuConfirm;
            }
            bool playerTwoMenuStart = !choosingMode && keyboard != null
                && (keyboard.periodKey.wasPressedThisFrame
                    || keyboard.slashKey.wasPressedThisFrame);
            bool playerTwoRegistered = input.HasInput(1);
            bool playerTwoAvailable = input.HasLobbyInput(1) || splitKeyboardMenu;
            if (choosingMode)
            {
                if (playerTwoRegistered && !lastPlayerTwoRegistered)
                {
                    playerTwoSelected = true;
                    playerTwoReady = false;
                }
                // A split keyboard makes Player 2 available, but it does not
                // mean Player 2 has been selected. While the rival is still
                // CPU, keep that side ready so the first keyboard ready toggle
                // can immediately unlock Start.
                if (!playerTwoRegistered && !playerTwoSelected)
                {
                    playerTwoReady = true;
                }
                lastPlayerTwoRegistered = playerTwoRegistered;
            }

            Vector2 titleNavigation = input.Move;
            if (!choosingMode)
            {
                if (keyboard != null)
                {
                    titleNavigation += new Vector2(
                        (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                            - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                        (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                            - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
                }
                UpdateTitleMouseCursor();
            }
            bool horizontal = Mathf.Abs(titleNavigation.x) >= .55f;
            bool vertical = Mathf.Abs(titleNavigation.y) >= .55f;
            if (!choosingMode && vertical && !verticalHeld)
                titleSelection = (titleSelection + (titleNavigation.y < 0 ? 1 : 2)) % 3;
            horizontalHeld = horizontal;
            verticalHeld = vertical;

            if (choosingMode && transition >= TransitionSeconds)
            {
                MoveLobbyCursor(ref playerOneCursor, playerOneInput.Move, ref playerOneCursorHeld, 0);
                if (playerTwoAvailable)
                    MoveLobbyCursor(ref playerTwoCursor, playerTwoInput.Move, ref playerTwoCursorHeld, 1);
                UpdateLobbyMouseCursor(playerTwoAvailable);
            }

            // Start only from the START tile. In particular, Player 2's
            // period/slash input is also exposed as StartPressed for title
            // navigation, but it must still confirm the currently selected
            // lobby control first (for example, CPU -> PLAYER 2).
            if (choosingMode && transition >= TransitionSeconds
                && AllPlayersReady
                && ((playerOneInput.StartPressed && playerOneCursor == 4)
                    || (playerTwoInput.StartPressed && playerTwoCursor == 4)))
            {
                StartSelectedMode();
                return;
            }

            if (playerOneInput.ConfirmPressed)
            {
                if (choosingMode && transition >= TransitionSeconds)
                    HandleLobbyConfirm(0);
            }
            if (choosingMode && transition >= TransitionSeconds && playerTwoAvailable
                && playerTwoInput.ConfirmPressed)
                HandleLobbyConfirm(1);
            if (!choosingMode && (playerOneInput.StartPressed || playerOneInput.ConfirmPressed
                || playerTwoInput.StartPressed || playerTwoInput.ConfirmPressed
                || playerTwoMenuStart || keyboardMenuConfirm))
            {
                if (titleSelection == 0) BeginModeSelection();
                else if (titleSelection == 1) OnTitleOptions();
                else if (titleSelection == 2) ExitGame();
            }
            if (!playerTwoSelected) playerTwoUnavailable = false;
            if (choosingMode) transition = Mathf.Min(TransitionSeconds, transition + Time.unscaledDeltaTime);
            SyncTitleButtons();
        }

        void SyncTitleButtons()
        {
            if (titleButtons == null) return;
            // The IMGUI transition veil fades the entire Canvas together. Keep the
            // artwork and scene Buttons visible underneath it until the mode screen takes over.
            titleButtons.alpha = IsOpen && (!choosingMode || transition < TransitionSeconds) ? 1f : 0f;
            titleButtons.interactable = IsOpen && !choosingMode;
            titleButtons.blocksRaycasts = IsOpen && !choosingMode;
        }

        public void OnTitleStart() { titleSelection = 0; BeginModeSelection(); }
        public void OnTitleOptions()
        {
            titleSelection = 1;
            showingOptions = true;
            optionsSelection = 0;
            optionsHorizontalHeld = false;
            optionsVerticalHeld = false;
            SyncTitleButtons();
        }
        public void OnTitleExit() { titleSelection = 2; ExitGame(); }

        void UpdateTitleMouseCursor()
        {
            if (!TryGetMenuArtPointer(out Vector2 pointer)) return;
            int hovered = MenuRectIndex(pointer, TitleButtonRects);
            if (hovered >= 0) titleSelection = hovered;
        }

        void UpdateOptionsNavigation()
        {
            var input = InputManager.Instance;
            if (input == null) return;

            UpdateOptionsMouseCursor();

            var playerOne = input.ReadForSeat(0, true);
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                playerOne.Move += new Vector2(
                    (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                        - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                        - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
                playerOne.ConfirmPressed |= keyboard.cKey.wasPressedThisFrame;
                playerOne.StartPressed |= keyboard.enterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame;
            }
            bool vertical = Mathf.Abs(playerOne.Move.y) >= .55f;
            bool horizontal = Mathf.Abs(playerOne.Move.x) >= .55f;
            if (vertical && !optionsVerticalHeld)
                optionsSelection = (optionsSelection + (playerOne.Move.y < 0 ? 1 : 2)) % 3;
            if (horizontal && !optionsHorizontalHeld && optionsSelection == 0)
                SetKeyboardHints(playerOne.Move.x > 0);

            optionsVerticalHeld = vertical;
            optionsHorizontalHeld = horizontal;

            bool confirm = playerOne.ConfirmPressed || playerOne.StartPressed;
            bool back = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (confirm)
            {
                if (optionsSelection == 1)
                    SetTutorialMode(!TutorialModeEnabled);
                else if (optionsSelection == 2)
                    CloseOptions();
            }
            else if (back)
            {
                CloseOptions();
            }
        }

        void CloseOptions()
        {
            showingOptions = false;
            titleSelection = 1;
            SyncTitleButtons();
        }

        void BeginModeSelection()
        {
            choosingMode = true;
            playerOneReady = false;
            playerTwoReady = false;
            // Local 1v1 is the default; the rival card can still be switched to CPU.
            playerTwoSelected = true;
            playerOneHuman = true;
            // Confirm starts on READY for each player; choosing CPU is explicit.
            playerOneCursor = 2;
            playerTwoCursor = 3;
            playerOneCursorHeld = false;
            playerTwoCursorHeld = false;
            lastPlayerTwoRegistered = false;
            InputManager.Instance?.BeginControllerJoining();
            SyncTitleButtons();
        }

        bool BothCpu => !playerOneHuman && !playerTwoSelected;
        bool AllPlayersReady => !BothCpu && playerOneReady && playerTwoReady;

        void HandleLobbyConfirm(int seat)
        {
            int cursor = seat == 0 ? playerOneCursor : playerTwoCursor;
            if (seat == 0 && cursor == 5)
            {
                ReturnToMainMenu();
                return;
            }
            // Player 2 should not need a precise cursor position just to ready
            // after selecting the seat. Its second confirm keypress is an
            // unambiguous READY action even if the lobby cursor did not move.
            if (seat == 1 && playerTwoSelected && !playerTwoReady && (cursor == 3 || cursor == 4))
            {
                playerTwoReady = true;
                playerTwoCursor = 4;
                return;
            }
            if (cursor == 0)
            {
                playerOneHuman = !playerOneHuman;
                playerOneReady = !playerOneHuman;
                return;
            }
            if (cursor == 1)
            {
                playerTwoSelected = !playerTwoSelected;
                playerTwoReady = !playerTwoSelected;
                if (playerTwoSelected)
                    playerTwoCursor = 3;
                return;
            }
            if (cursor == 2)
            {
                if (seat == 0 && playerOneHuman)
                {
                    playerOneReady = !playerOneReady;
                    if (playerOneReady) playerOneCursor = 4;
                }
                return;
            }
            if (cursor == 3)
            {
                if (seat == 1 && playerTwoSelected)
                {
                    playerTwoReady = !playerTwoReady;
                    if (playerTwoReady) playerTwoCursor = 4;
                }
                return;
            }
            if (cursor == 4 && AllPlayersReady)
            {
                StartSelectedMode();
            }
        }

        void MoveLobbyCursor(ref int cursor, Vector2 move, ref bool held, int seat)
        {
            // The READY controls belong to their respective seats. Correct a
            // stale cursor position before processing the next movement frame.
            if (seat == 0 && cursor == 3) cursor = 2;
            if (seat == 1 && cursor == 2) cursor = 3;
            bool pressed = move.magnitude >= .55f;
            if (!pressed) { held = false; return; }
            if (held) return;
            held = true;
            bool horizontal = Mathf.Abs(move.x) >= Mathf.Abs(move.y);
            if (horizontal)
            {
                if (cursor == 0 || cursor == 1) cursor = move.x > 0 ? 1 : 0;
                else if (cursor == 2 || cursor == 3)
                    cursor = seat == 0 ? 2 : 3;
                return;
            }
            if (move.y < 0)
            {
                if (cursor == 0) cursor = seat == 0 ? 2 : 4;
                else if (cursor == 1) cursor = seat == 1 ? 3 : 4;
                else if (cursor == 2 || cursor == 3) cursor = 4;
            }
            else
            {
                if (cursor == 0 && seat == 0) cursor = 5;
                else if (cursor == 2) cursor = 0;
                else if (cursor == 3) cursor = 1;
                else if (cursor == 4) cursor = seat == 0 ? 2 : 3;
                else if (cursor == 5) cursor = 0;
            }
        }

        Vector2 KeyboardMenuMove(bool splitKeyboard)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return Vector2.zero;

            bool useArrows = !splitKeyboard;
            return new Vector2(
                (keyboard.dKey.isPressed || useArrows && keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || useArrows && keyboard.leftArrowKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed || useArrows && keyboard.upArrowKey.isPressed ? 1 : 0)
                    - (keyboard.sKey.isPressed || useArrows && keyboard.downArrowKey.isPressed ? 1 : 0));
        }

        void UpdateOptionsMouseCursor()
        {
            if (!TryGetMenuArtPointer(out Vector2 pointer)) return;
            int hovered = MenuRectIndex(pointer, OptionsButtonRects);
            if (hovered >= 0) optionsSelection = hovered;
        }

        void UpdateLobbyMouseCursor(bool playerTwoAvailable)
        {
            if (!TryGetMenuArtPointer(out Vector2 pointer)) return;

            if (backButton.Contains(pointer))
            {
                playerOneCursor = 5;
                return;
            }
            if (playerOneButton.Contains(pointer)) playerOneCursor = 0;
            else if (playerTwoAvailable && rivalButton.Contains(pointer)) playerOneCursor = 1;
            else if (playerOneReadyButton.Contains(pointer)) playerOneCursor = 2;
            else if (playerTwoSelected && playerTwoReadyButton.Contains(pointer)) playerOneCursor = 3;
            else if (startButton.Contains(pointer)) playerOneCursor = 4;
        }

        static int MenuRectIndex(Vector2 point, Rect[] rects)
        {
            for (int i = 0; i < rects.Length; i++)
                if (rects[i].Contains(point)) return i;
            return -1;
        }

        bool TryGetMenuArtPointer(out Vector2 point)
        {
            point = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse == null) return false;
            // A resting pointer must not pull either keyboard cursor back to
            // whichever button the mouse last hovered.
            if (mouse.delta.ReadValue().sqrMagnitude <= .01f
                && !mouse.leftButton.wasPressedThisFrame) return false;

            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            Vector2 offset = new Vector2(
                (Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f);
            Vector2 screenPoint = mouse.position.ReadValue();
            point = new Vector2(
                (screenPoint.x - offset.x) / scale,
                (Screen.height - screenPoint.y - offset.y) / scale);
            return new Rect(0f, 0f, ArtWidth, ArtHeight).Contains(point);
        }

        public static bool ConsumeDuelRequest()
        {
            bool requested = startDuelOnLoad;
            startDuelOnLoad = false;
            return requested;
        }

        public static bool ConsumePlayerTwoRequest()
        {
            bool requested = playerTwoHumanOnLoad;
            playerTwoHumanOnLoad = false;
            return requested;
        }

        public static bool ConsumePlayerOneCpuRequest()
        {
            bool requested = playerOneCpuOnLoad;
            playerOneCpuOnLoad = false;
            return requested;
        }

        public static void Open() => IsOpen = true;
        public static void Close() => IsOpen = false;

        public static void OpenPause()
        {
            pauseOpen = true;
            pauseOpenedFrame = Time.frameCount;
            pauseSelection = 0;
            pauseMoveHeld = false;
            InputManager.Instance?.SetView(GameView.Pause);
        }

        public static void ClosePause()
        {
            pauseOpen = false;
            pauseMoveHeld = false;
        }

        public static int PauseSelection => pauseSelection;

        void UpdatePauseNavigation()
        {
            // Opening pause changes the input view earlier in this frame.
            // Navigation starts on the next frame so that press is consumed once.
            if (Time.frameCount == pauseOpenedFrame) return;
            var input = InputManager.Instance;
            if (input == null) return;
            Vector2 navigation = Vector2.zero;
            bool confirm = false;
            for (int seat = 0; seat < 2; seat++)
            {
                if (!input.HasLobbyInput(seat)) continue;
                var frame = input.ReadForSeat(seat, true);
                if (Mathf.Abs(navigation.y) < .55f) navigation = frame.Move;
                confirm |= frame.ConfirmPressed;
            }
            bool moving = Mathf.Abs(navigation.y) >= .55f;
            if (moving && !pauseMoveHeld)
                pauseSelection = (pauseSelection + (navigation.y < 0 ? 1 : 2)) % 3;
            pauseMoveHeld = moving;
            if (!confirm) return;
            if (pauseSelection == 0)
            {
                var game = GameManager.Instance;
                if (game != null && game.Player != null && game.Player.Match != null)
                    game.Player.Match.TogglePause();
                else game?.TogglePause();
            }
            else if (pauseSelection == 1) ReturnToLobby();
            else ReturnToMainMenu();
        }
        public static void TogglePassengerOverlay(bool visible, ElevatorManager game)
        {
            passengerOverlayOpen = visible;
            overlayGame = game;
        }

        public static void ReturnToLobby()
        {
            openLobbyOnLoad = true;
            fadeMenuOnLoad = true;
            showControllerInstructionsOnLoad = false;
            pauseOpen = false;
            InputManager.Instance?.ResetRegistrations();
            SceneManager.LoadScene(DuelScene);
        }

        public static void ReturnToMainMenu()
        {
            openLobbyOnLoad = false;
            fadeMenuOnLoad = true;
            showControllerInstructionsOnLoad = false;
            tutorialOnLoad = false;
            TutorialActive = false;
            sessionReady = false;
            pauseOpen = false;
            startDuelOnLoad = false;
            playerTwoHumanOnLoad = false;
            playerTwoSelected = false;
            playerOneCpuOnLoad = false;
            InputManager.Instance?.ClearTutorialInputLocks();
            InputManager.Instance?.ResetRegistrations();
            SceneManager.LoadScene(SinglePlayerScene);
        }

        void OnGUI()
        {
            GameTypography.ApplyToSkin(GUI.skin);
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            Styles();
            if (IsOpen)
            {
                if (showingControllerInstructions) DrawControllerInstructions();
                else if (showingOptions) DrawOptionsScreen();
                else DrawModeSelect();
                DrawMenuFade();
                return;
            }
            if (tutorialActive)
            {
                if (showingControllerInstructions)
                {
                    DrawControllerInstructions();
                    DrawMenuFade();
                    return;
                }
                int previousDepth = GUI.depth;
                GUI.depth = -1000;
                if (tutorialFinalePhase != TutorialFinalePhase.None)
                    DrawTutorialFinale();
                else
                    DrawTutorialOverlay();
                GUI.depth = previousDepth;
                if (!TutorialWaitingPanelActive) DrawMenuFade();
                return;
            }
            if (passengerOverlayOpen) DrawPassengerOverlay();
            DrawMenuFade();
        }

        void DrawMenuFade()
        {
            if (menuFadeAlpha <= .001f) return;
            Panel(new Rect(0, 0, Screen.width, Screen.height),
                new Color(0f, 0f, 0f, menuFadeAlpha));
        }

        void DrawControllerInstructions()
        {
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            Matrix4x4 previousMatrix = GUI.matrix;
            Panel(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0), Quaternion.identity,
                new Vector3(scale, scale, 1));
            GUI.Label(new Rect(100, 34, 1500, 82),
                KeyboardHintsEnabled ? "KEYBOARD CONTROLS" : "CONTROLLER CONTROLS", title);
            if (KeyboardHintsEnabled) DrawKeyboardInstructions();
            else
            {
                Rect controllerImage = new Rect(0, 112, ArtWidth, 826);
                if (controllerInstructions != null)
                    GUI.DrawTexture(controllerImage, controllerInstructions, ScaleMode.ScaleToFit);
            }
            Rect startButton = new Rect(610, 952, 480, 76);
            Panel(startButton, Teal);
            if (RequiresBothControllerInstructionConfirmations
                && controllerInstructionConfirmed[0] && controllerInstructionConfirmed[1])
            {
                DrawRectOutline(Expand(startButton, 10f), PlayerOneColour, 5f);
                DrawRectOutline(Expand(startButton, 5f), PlayerTwoColour, 5f);
            }
            else if (controllerInstructionConfirmed[0])
            {
                DrawRectOutline(Expand(startButton, 5f), PlayerOneColour, 5f);
            }
            else if (controllerInstructionConfirmed[1])
            {
                DrawRectOutline(Expand(startButton, 5f), PlayerTwoColour, 5f);
            }
            DrawKeyHint(new Rect(startButton.xMax + 16f, startButton.y + 11f, 54f, 54f), StartHintKey);
            if (GUI.Button(startButton, "START", cardTitle))
            {
                // A shared screen click counts as Player 1's confirmation;
                // Player 2 must still confirm separately in a duel.
                if (ToggleControllerInstructionConfirmation(tutorialGame != null ? tutorialGame.Seat : 0))
                    DismissControllerInstructions();
            }
            GUI.matrix = previousMatrix;
            GUI.depth = previousDepth;
        }

        string StartHintKey => KeyboardHintsEnabled ? "C" : "A";

        void DrawKeyboardInstructions()
        {
            GUI.Label(new Rect(210, 126, 1280, 42), "PLAYER 1", caption);
            DrawKeyboardHint(new Rect(210, 180, 380, 96), "W A S D", "MOVE / CHOOSE DOOR");
            DrawKeyboardHint(new Rect(660, 180, 380, 96), "C", "OPEN DOOR / STOP");
            DrawKeyboardHint(new Rect(1110, 180, 380, 96), "E", "CLOSE DOORS");
            DrawKeyboardHint(new Rect(210, 300, 380, 96), "I", "KICK");
            DrawKeyboardHint(new Rect(660, 300, 380, 96), "R", "REPAIR");
            DrawKeyboardHint(new Rect(1110, 300, 380, 96), "LSHIFT", "SPEED BOOST");
            if (tutorialActors[1] != null
                || GameManager.Instance != null && GameManager.Instance.Player != null
                    && GameManager.Instance.Player.Match != null
                    && !GameManager.Instance.Player.Match.Rival.IsNpc)
            {
                GUI.Label(new Rect(210, 426, 1280, 42), "PLAYER 2", caption);
                DrawKeyboardHint(new Rect(210, 480, 380, 96), "ARROWS", "MOVE / CHOOSE DOOR");
                DrawKeyboardHint(new Rect(660, 480, 380, 96), ".", "OPEN DOOR / STOP");
                DrawKeyboardHint(new Rect(1110, 480, 380, 96), "/", "CLOSE DOORS");
                DrawKeyboardHint(new Rect(210, 600, 380, 96), ",", "KICK");
                DrawKeyboardHint(new Rect(660, 600, 380, 96), ";", "REPAIR");
                DrawKeyboardHint(new Rect(1110, 600, 380, 96), "RSHIFT", "SPEED BOOST");
            }
            DrawKeyboardHint(new Rect(435, 780, 830, 104), "ESC", "PAUSE");
        }
        void DrawKeyboardHint(Rect rect, string key, string label)
        {
            Panel(rect, new Color(16f / 255f, 16f / 255f, 17f / 255f, .96f));

            if (key == "W A S D")
            {
                float keySize = 28f;
                float gap = 3f;
                float topY = rect.y + 4f;
                float bottomY = topY + keySize + 1f;
                float centerX = rect.center.x;
                DrawKeyboardKeycap(new Rect(centerX - keySize * .5f, topY, keySize, keySize), "W");
                DrawKeyboardKeycap(new Rect(centerX - keySize * 1.5f - gap, bottomY, keySize, keySize), "A");
                DrawKeyboardKeycap(new Rect(centerX - keySize * .5f, bottomY, keySize, keySize), "S");
                DrawKeyboardKeycap(new Rect(centerX + keySize * .5f + gap, bottomY, keySize, keySize), "D");
            }
            else if (key == "Y / TAB")
            {
                DrawKeyboardKeycap(new Rect(rect.center.x - 57f, rect.y + 8f, 44f, 32f), "Y");
                DrawKeyboardKeycap(new Rect(rect.center.x + 8f, rect.y + 8f, 78f, 32f), "TAB");
            }
            else
            {
                float keyWidth = Mathf.Clamp(28f + key.Length * 15f, 44f, 112f);
                DrawKeyboardKeycap(new Rect(rect.center.x - keyWidth * .5f, rect.y + 8f,
                    keyWidth, 32f), key);
            }

            Color previousCaption = caption.normal.textColor;
            caption.normal.textColor = Cream;
            GUI.Label(new Rect(rect.x, rect.y + 62f, rect.width, 30f), label, caption);
            caption.normal.textColor = previousCaption;
        }

        void DrawKeyboardKeycap(Rect rect, string key)
        {
            Panel(rect, Teal);
            Panel(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), Ink);

            int previousFontSize = cardTitle.fontSize;
            TextAnchor previousAlignment = cardTitle.alignment;
            Color previousTextColor = cardTitle.normal.textColor;
            cardTitle.fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * .55f), 15, 24);
            cardTitle.alignment = TextAnchor.MiddleCenter;
            cardTitle.normal.textColor = Cream;
            GUI.Label(rect, key, cardTitle);
            cardTitle.fontSize = previousFontSize;
            cardTitle.alignment = previousAlignment;
            cardTitle.normal.textColor = previousTextColor;
        }

        void DrawOptionsScreen()
        {
            Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);
            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0), Quaternion.identity,
                new Vector3(scale, scale, 1));

            GUI.Label(new Rect(360, 120, 980, 90), "OPTIONS", title);
            GUI.Label(new Rect(420, 250, 860, 46), "CHOOSE YOUR KEY HINTS", caption);

            Rect modeButton = new Rect(480, 360, 740, 140);
            DrawSelectionFrame(modeButton, optionsSelection == 0, false);
            Panel(modeButton, Teal);
            GUI.Label(new Rect(510, 382, 680, 54),
                KeyboardHintsEnabled ? "KEYBOARD" : "CONTROLLER", cardTitle);
            GUI.Label(new Rect(510, 438, 680, 34),
                KeyboardHintsEnabled ? "C / WASD" : "A / D-PAD", caption);
            // Keep the navigation instruction attached to the key-hint choice.
            GUI.Label(new Rect(modeButton.xMax + 16f, modeButton.y + 45f, 360f, 50f),
                "LEFT / RIGHT TO CHANGE", caption);
            if (GUI.Button(modeButton, GUIContent.none, GUIStyle.none))
                SetKeyboardHints(!KeyboardHintsEnabled);

            Rect tutorialButton = new Rect(480, 535, 740, 120);
            DrawSelectionFrame(tutorialButton, optionsSelection == 1, false);
            Panel(tutorialButton, TutorialModeEnabled ? Teal : Coral);
            GUI.Label(new Rect(510, 550, 680, 48), "TUTORIAL MODE", cardTitle);
            GUI.Label(new Rect(510, 600, 680, 30), TutorialModeEnabled ? "ON" : "OFF", caption);
            if (optionsSelection == 1)
            {
                DrawKeyHint(new Rect(tutorialButton.xMax + 16f, tutorialButton.y + 33f, 54f, 54f),
                    KeyboardHintsEnabled ? "C" : "A");
                GUI.Label(new Rect(tutorialButton.xMax + 84f, tutorialButton.y + 35f, 300f, 50f),
                    "PRESS TO TOGGLE", caption);
            }
            if (GUI.Button(tutorialButton, GUIContent.none, GUIStyle.none))
                SetTutorialMode(!TutorialModeEnabled);

            Rect back = new Rect(620, 730, 460, 90);
            DrawSelectionFrame(back, optionsSelection == 2, false);
            Panel(back, Cream);
            GUI.Label(back, "BACK", cardTitle);
            DrawKeyHint(new Rect(back.x + back.width + 16f, back.y + 18f, 54f, 54f),
                KeyboardHintsEnabled ? "C" : "A");
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) CloseOptions();

            GUI.matrix = Matrix4x4.identity;
        }

        void DismissControllerInstructions()
        {
            if (tutorialActive && tutorialFinalePhase == TutorialFinalePhase.Controls)
            {
                showingControllerInstructions = false;
                tutorialFinalePhase = TutorialFinalePhase.Countdown;
                tutorialFinaleTimer = 0f;
                Close();
                SyncTitleButtons();
                InputManager.Instance?.SetView(GameView.ControlElevator);
                return;
            }
            showingControllerInstructions = false;
            if (tutorialActive) ResumeTutorialGame();
            Close();
            SyncTitleButtons();
            InputManager.Instance?.SetView(GameView.ControlElevator);
        }

        void DrawModeSelect()
        {
            if (menuArtwork != null)
            {
                DrawArtworkMenu();
                return;
            }
            Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);

            float scale = Mathf.Min(Screen.width / 1200f, Screen.height / 720f);
            float cardWidth = Mathf.Min(430f, Screen.width * .39f);
            float cardHeight = Mathf.Min(250f, Screen.height * .38f);
            float gap = Mathf.Min(34f, Screen.width * .04f);
            float totalWidth = cardWidth * 2 + gap;
            float left = (Screen.width - totalWidth) * .5f;
            float top = (Screen.height - cardHeight) * .5f + 30f * scale;

            GUI.Label(new Rect(20, top - 128f * scale, Screen.width - 40, 55f * scale),
                "CRAZY ELEVATORS", title);
            GUI.Label(new Rect(20, top - 70f * scale, Screen.width - 40, 32f * scale),
                "CHOOSE YOUR MODE", caption);

            DrawMode(new Rect(left, top, cardWidth, cardHeight), Teal,
                "SINGLE PLAYER", "Office → candy → water  ·  3 minutes", false, !playerTwoSelected);
            DrawMode(new Rect(left + cardWidth + gap, top, cardWidth, cardHeight), Coral,
                "1V1", "Local duel  ·  CPU or Player 2", true, playerTwoSelected);
            GUI.Label(new Rect(12, Screen.height - 38f * scale, Screen.width - 24, 24f * scale),
                "PICK A MODE", caption);
        }

        void DrawArtworkMenu()
        {
            bool hasCanvasTitle = titleButtons != null;
            float fade = Mathf.Clamp01(transition / TransitionSeconds);
            // Scene UI renders behind OnGUI. Only draw a veil during the transition;
            // an opaque panel here would hide the editable title Buttons in Game view.
            if (!hasCanvasTitle)
                Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);
            else if (choosingMode)
                Panel(new Rect(0, 0, Screen.width, Screen.height),
                    new Color(Ink.r, Ink.g, Ink.b, fade));
            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            var oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0), Quaternion.identity,
                new Vector3(scale, scale, 1));

            if (!hasCanvasTitle && fade < 1f)
            {
                DrawArt(new Rect(0, 0, ArtWidth, ArtHeight), new Rect(0, 0, 1, 1), 1f - fade);
                // Fallback if this script is opened in a scene without the title Canvas.
                Color cleanBackground = Color.Lerp(new Color32(16, 16, 17, 255), Ink, fade);
                Panel(new Rect(380, 690, 315, 370), cleanBackground);
                Panel(new Rect(278, 733, 112, 66), cleanBackground);
            }
            if (!choosingMode || fade < 1f)
            {
                if (triangleCursorTexture == null) triangleCursorTexture = CreateTriangleCursor();
                const float cursorWidth = 48f;
                const float cursorGap = 2f;
                Rect selectedButton = TitleButtonRects[titleSelection];
                float cursorX = selectedButton.x - cursorWidth - cursorGap;
                float cursorY = selectedButton.y + (selectedButton.height - cursorWidth) * .5f;
                Color cursorColor = GUI.color;
                GUI.color = new Color(TitleTeal.r, TitleTeal.g, TitleTeal.b,
                    choosingMode ? 1f - fade : 1f);
                GUI.DrawTexture(new Rect(cursorX, cursorY,
                    cursorWidth, cursorWidth), triangleCursorTexture);
                GUI.color = cursorColor;
                if (!choosingMode)
                {
                    DrawTitleActionHint();
                    DrawMovingTracks(new Rect(1060, 0, 495, ArtHeight), 1);
                    if (!hasCanvasTitle)
                    {
                        for (int i = 0; i < TitleButtonRects.Length; i++)
                        {
                            if (!GUI.Button(TitleButtonRects[i], GUIContent.none, GUIStyle.none))
                                continue;
                            titleSelection = i;
                            ActivateTitleSelection();
                            break;
                        }
                    }
                }
            }

            if (choosingMode)
            {
                // The first car slides left as its companion appears beside it.
                float eased = fade * fade * (3f - 2f * fade);
                float descent = launching ? Mathf.Pow(Mathf.Clamp01(launchTimer / LaunchSeconds), 2) * (ArtHeight + 200) : 0;
                DrawShaft(new Rect(Mathf.Lerp(1060, 500, eased), 0, Mathf.Lerp(495, 330, eased), ArtHeight), 1, descent);
                DrawShaft(new Rect(870, 0, 330, ArtHeight), eased, descent);
                if (fade >= 1f && !launching)
                {
                    Panel(backButton, Cream);
                    GUI.Label(backButton, "BACK", cardTitle);
                    DrawKeyHint(new Rect(backButton.x + backButton.width + 16f,
                        backButton.y + 2f, 54f, 54f), KeyboardHintsEnabled ? "C" : "A");
                    if (GUI.Button(backButton, GUIContent.none, GUIStyle.none))
                    {
                        playerOneCursor = 5;
                        ReturnToMainMenu();
                    }
                    bool playerTwoAvailable = InputManager.Instance != null
                        && InputManager.Instance.HasLobbyInput(1);
                    bool startEnabled = !BothCpu;
                    bool p1OnFirstCard = playerOneCursor == 0;
                    bool p2OnFirstCard = playerTwoAvailable && playerTwoCursor == 0;
                    bool p1OnSecondCard = playerOneCursor == 1;
                    bool p2OnSecondCard = playerTwoAvailable && playerTwoCursor == 1;

                    DrawPlayerCard(playerOneButton, playerOneHuman ? "PLAYER 1" : "CPU",
                        playerOneLabel, ref playerOneLabelTexture, ref playerOneShapeHash,
                        p1OnFirstCard, p2OnFirstCard);
                    DrawPlayerCard(rivalButton, playerTwoSelected ? "PLAYER 2" : "CPU",
                        rivalLabel, ref rivalLabelTexture, ref rivalShapeHash,
                        p1OnSecondCard, p2OnSecondCard);

                    string playerOneStatus = playerOneHuman
                        ? (playerOneReady ? "READY" : "UNREADY") : "CPU READY";
                    string playerTwoStatus = playerTwoSelected
                        ? (playerTwoReady ? "READY" : "UNREADY") : "CPU READY";
                    Color playerOneStatusFill = !playerOneHuman ? CpuReadyColour
                        : playerOneReady ? PlayerOneColour : LobbySurface;
                    Color playerTwoStatusFill = !playerTwoSelected ? CpuReadyColour
                        : playerTwoReady ? PlayerTwoColour : LobbySurface;
                    DrawStatusBar(playerOneReadyButton, playerOneStatus, playerOneStatusFill,
                        playerOneHuman && !playerOneReady ? Cream : Ink,
                        playerOneCursor == 2, playerTwoAvailable && playerTwoCursor == 2);
                    DrawStatusBar(playerTwoReadyButton, playerTwoStatus, playerTwoStatusFill,
                        playerTwoSelected && !playerTwoReady ? Cream : Ink,
                        playerOneCursor == 3, playerTwoAvailable && playerTwoCursor == 3);

                    Color startFill = AllPlayersReady ? Teal
                        : startEnabled ? LobbySurface : DisabledColour;
                    DrawActionButton(startButton, "START", startFill,
                        AllPlayersReady ? Ink : Cream,
                        playerOneCursor == 4, playerTwoAvailable && playerTwoCursor == 4);
                    DrawLobbyKeyHint(playerOneButton, 0, playerOneCursor == 0);
                    DrawLobbyKeyHint(rivalButton, 1, playerTwoAvailable && playerTwoCursor == 1);
                    DrawLobbyKeyHint(playerOneReadyButton, 0, playerOneCursor == 2);
                    DrawLobbyKeyHint(playerTwoReadyButton, 1, playerTwoAvailable && playerTwoCursor == 3);
                    DrawLobbyKeyHint(startButton, 0, playerOneCursor == 4);
                    DrawLobbyKeyHint(startButton, 1, playerTwoAvailable && playerTwoCursor == 4);
                    // The lobby is drawn with IMGUI labels, so provide
                    // transparent hitboxes for mouse users as well as the
                    // keyboard/controller cursor.
                    if (GUI.Button(playerOneButton, GUIContent.none, GUIStyle.none))
                    {
                        playerOneCursor = 0;
                        HandleLobbyConfirm(0);
                    }
                    if (GUI.Button(rivalButton, GUIContent.none, GUIStyle.none)
                        && playerTwoAvailable)
                    {
                        playerOneCursor = 1;
                        HandleLobbyConfirm(0);
                    }
                    if (GUI.Button(playerOneReadyButton, GUIContent.none, GUIStyle.none)
                        && playerOneHuman)
                    {
                        playerOneCursor = 2;
                        HandleLobbyConfirm(0);
                    }
                    if (GUI.Button(playerTwoReadyButton, GUIContent.none, GUIStyle.none)
                        && playerTwoSelected)
                    {
                        playerTwoCursor = 3;
                        HandleLobbyConfirm(1);
                    }
                    if (GUI.Button(startButton, GUIContent.none, GUIStyle.none) && AllPlayersReady)
                    {
                        playerOneCursor = 4;
                        HandleLobbyConfirm(0);
                    }
                    DrawLobbyCursor(backButton, playerOneCursor == 5, false, playerTwoAvailable);
                    if (AllPlayersReady)
                    {
                        Panel(new Rect(560, 1018, 580, 52), new Color(Ink.r, Ink.g, Ink.b, .94f));
                        GUI.Label(new Rect(580, 1022, 540, 44), "BOTH READY  ·  PRESS START", caption);
                    }
                }
                if (launching) Panel(new Rect(0, 0, ArtWidth, ArtHeight), new Color(0, 0, 0,
                    Mathf.Clamp01((launchTimer / LaunchSeconds - .55f) * 2.2f)));
            }
            GUI.matrix = oldMatrix;
        }

        void DrawTitleActionHint()
        {
            Rect selectedButton = TitleButtonRects[titleSelection];
            const float hintSize = 54f;
            Rect hint = new Rect(selectedButton.xMax + 16f,
                selectedButton.y + (selectedButton.height - hintSize) * .5f,
                hintSize, hintSize);
            DrawKeyHint(hint, KeyboardHintsEnabled ? "C" : "A");
        }

        void ActivateTitleSelection()
        {
            if (titleSelection == 0) BeginModeSelection();
            else if (titleSelection == 1) OnTitleOptions();
            else if (titleSelection == 2) ExitGame();
        }

        void DrawKeyHint(Rect hint, string key)
        {
            if (keyIconTexture == null) keyIconTexture = CreateKeyIcon();

            Color previous = GUI.color;
            GUI.color = new Color(TitleTeal.r, TitleTeal.g, TitleTeal.b, .98f);
            GUI.DrawTexture(hint, keyIconTexture);
            GUI.color = previous;

            GUIStyle keyLabel = new GUIStyle(cardTitle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(27 - key.Length * 3, 14, 25),
                wordWrap = false,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0)
            };
            keyLabel.normal.textColor = Cream;
            GUI.Label(hint, key, keyLabel);
        }

        void DrawLobbyKeyHint(Rect control, int seat, bool selected)
        {
            if (!selected) return;
            string key = !KeyboardHintsEnabled ? "A" : seat == 0 ? "C" : ".";
            Rect hint = seat == 0
                ? new Rect(control.x - 120f, control.y + (control.height - 54f) * .5f, 54f, 54f)
                : new Rect(control.xMax + 66f, control.y + (control.height - 54f) * .5f, 54f, 54f);
            DrawKeyHint(hint, key);
        }

        void DrawShaft(Rect rect, float alpha, float descent)
        {
            Rect shaftSource = new Rect(1060f / ArtWidth, 0, 495f / ArtWidth, 1);
            DrawArt(rect, shaftSource, alpha);
            DrawMovingTracks(rect, alpha);
            if (descent <= 0) return;

            float cabinX = rect.x + rect.width * 32f / 495f;
            float cabinWidth = rect.width * 427f / 495f;
            Rect originalCabin = new Rect(cabinX, 185, cabinWidth, 546);
            Panel(originalCabin, new Color(16f / 255f, 16f / 255f, 17f / 255f, alpha));
            Rect cabinSource = new Rect(1092f / ArtWidth, 1f - 731f / ArtHeight,
                427f / ArtWidth, 546f / ArtHeight);
            DrawArt(new Rect(cabinX, 185 + descent, cabinWidth, 546), cabinSource, alpha);
        }

        void DrawMovingTracks(Rect shaft, float alpha)
        {
            float stripWidth = shaft.width * 34f / 495f;
            DrawMovingTrack(new Rect(shaft.x, shaft.y, stripWidth, shaft.height), alpha);
            DrawMovingTrack(new Rect(shaft.xMax - stripWidth, shaft.y, stripWidth, shaft.height), alpha);
        }

        void DrawMovingTrack(Rect rect, float alpha)
        {
            GUI.BeginGroup(rect);
            Panel(new Rect(0, 0, rect.width, rect.height), new Color(29f / 255f, 40f / 255f, 38f / 255f, alpha));
            float offset = Mathf.Repeat(Time.unscaledTime * 130f, 160f);
            for (float y = offset - 160f; y < rect.height; y += 160f)
                Panel(new Rect(0, y, rect.width, 18), new Color(12f / 255f, 55f / 255f, 48f / 255f, alpha));
            GUI.EndGroup();
        }

        void DrawPlayerCard(Rect rect, string label, MenuLabelShape shape,
            ref Texture2D texture, ref int shapeHash,
            bool playerOneHovering, bool playerTwoHovering)
        {
            EnsureLabelTexture(shape, ref texture, ref shapeHash);

            if (playerOneHovering && playerTwoHovering)
            {
                DrawTintedTexture(Expand(rect, 10f), texture, PlayerOneColour);
                DrawTintedTexture(Expand(rect, 5f), texture, PlayerTwoColour);
            }
            else if (playerOneHovering || playerTwoHovering)
            {
                DrawTintedTexture(Expand(rect, 5f), texture,
                    playerOneHovering ? PlayerOneColour : PlayerTwoColour);
            }
            DrawTintedTexture(rect, texture, Teal);
            DrawLabel(rect, label, menuLabelText, Ink);
            DrawSelectionArrows(rect, playerOneHovering, playerTwoHovering);
        }

        void EnsureLabelTexture(MenuLabelShape shape, ref Texture2D texture, ref int shapeHash)
        {
            int currentHash = shape.topLeft.GetHashCode();
            currentHash = currentHash * 31 + shape.topRight.GetHashCode();
            currentHash = currentHash * 31 + shape.bottomRight.GetHashCode();
            currentHash = currentHash * 31 + shape.bottomLeft.GetHashCode();
            if (texture == null || shapeHash != currentHash)
            {
                if (texture != null) Destroy(texture);
                texture = CreateLabelTexture(shape);
                shapeHash = currentHash;
            }
        }

        void DrawStatusBar(Rect rect, string label, Color fill, Color textColour,
            bool playerOneHovering, bool playerTwoHovering)
        {
            DrawSelectionFrame(rect, playerOneHovering, playerTwoHovering);
            Panel(rect, fill);
            DrawLabel(rect, label, readyLabelText, textColour);
            DrawSelectionArrows(rect, playerOneHovering, playerTwoHovering);
        }

        void DrawActionButton(Rect rect, string label, Color fill, Color textColour,
            bool playerOneHovering, bool playerTwoHovering)
        {
            DrawSelectionFrame(rect, playerOneHovering, playerTwoHovering);
            Panel(rect, fill);
            DrawLabel(rect, label, menuLabelText, textColour);
            DrawSelectionArrows(rect, playerOneHovering, playerTwoHovering);
        }

        void DrawSelectionArrows(Rect rect, bool playerOneHovering, bool playerTwoHovering)
        {
            const float arrowWidth = 52f;
            const float arrowGap = 8f;
            if (playerOneHovering)
            {
                DrawChoiceArrow(new Rect(rect.x - arrowWidth - arrowGap, rect.y,
                    arrowWidth, rect.height), true, PlayerOneColour,
                    ref leftChoiceArrowTexture);
            }
            if (playerTwoHovering)
            {
                DrawChoiceArrow(new Rect(rect.xMax + arrowGap, rect.y,
                    arrowWidth, rect.height), false, PlayerTwoColour,
                    ref rightChoiceArrowTexture);
            }
        }

        static void DrawSelectionFrame(Rect rect, bool playerOneHovering, bool playerTwoHovering)
        {
            if (playerOneHovering && playerTwoHovering)
            {
                Panel(Expand(rect, 10f), PlayerOneColour);
                Panel(Expand(rect, 5f), PlayerTwoColour);
            }
            else if (playerOneHovering || playerTwoHovering)
            {
                Panel(Expand(rect, 5f), playerOneHovering ? PlayerOneColour : PlayerTwoColour);
            }
        }

        static void DrawLabel(Rect rect, string label, GUIStyle style, Color colour)
        {
            Color previous = style.normal.textColor;
            style.normal.textColor = colour;
            GUI.Label(rect, label, style);
            style.normal.textColor = previous;
        }

        static void DrawTintedTexture(Rect rect, Texture2D texture, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, texture);
            GUI.color = previous;
        }

        static Rect Expand(Rect rect, float amount) => new Rect(
            rect.x - amount, rect.y - amount,
            rect.width + amount * 2f, rect.height + amount * 2f);

        void DrawLobbyCursor(Rect rect, bool playerOneHovering, bool playerTwoHovering,
            bool playerTwoRegistered)
        {
            if (!playerOneHovering && (!playerTwoRegistered || !playerTwoHovering)) return;
            bool bothHovering = playerOneHovering && playerTwoRegistered && playerTwoHovering;
            if (bothHovering)
            {
                DrawRectOutline(Expand(rect, 10f), PlayerOneColour, 5f);
                DrawRectOutline(Expand(rect, 5f), PlayerTwoColour, 5f);
                return;
            }

            DrawRectOutline(Expand(rect, 5f),
                playerOneHovering ? PlayerOneColour : PlayerTwoColour, 5f);
        }

        static void DrawRectOutline(Rect rect, Color colour, float thickness)
        {
            Panel(new Rect(rect.x, rect.y, rect.width, thickness), colour);
            Panel(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), colour);
            float sideHeight = Mathf.Max(0f, rect.height - thickness * 2f);
            Panel(new Rect(rect.x, rect.y + thickness, thickness, sideHeight), colour);
            Panel(new Rect(rect.xMax - thickness, rect.y + thickness, thickness, sideHeight), colour);
        }

        static Texture2D CreateLabelTexture(MenuLabelShape shape)
        {
            const int width = 256, height = 80;
            var pixels = new Color32[width * height];
            Vector2[] corners = { shape.topLeft, shape.topRight, shape.bottomRight, shape.bottomLeft };
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    var point = new Vector2((x + (sx + .5f) * .5f) / width,
                        1f - (y + (sy + .5f) * .5f) / height);
                    bool inside = true;
                    for (int edge = 0; edge < 4; edge++)
                    {
                        Vector2 a = corners[edge], b = corners[(edge + 1) % 4];
                        if ((b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x) < 0)
                        { inside = false; break; }
                    }
                    if (inside) covered++;
                }
                pixels[y * width + x] = new Color32(255, 255, 255,
                    (byte)(covered * 255 / 4));
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D CreateTriangleCursor()
        {
            const int width = 48, height = 48;
            var pixels = new Color32[width * height];
            // Equal side lengths create a regular triangle rotated right.
            Vector2 leftTop = new Vector2(2, 2), leftBottom = new Vector2(2, 46),
                tip = new Vector2(40, 24);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2 point = new Vector2(x + .5f, y + .5f);
                float distance = Mathf.Min(DistanceToSegment(point, leftTop, leftBottom),
                    Mathf.Min(DistanceToSegment(point, leftBottom, tip),
                        DistanceToSegment(point, tip, leftTop)));
                byte alpha = (byte)(Mathf.Clamp01(3f - distance) * 255);
                pixels[y * width + x] = new Color32(255, 255, 255, alpha);
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static Texture2D CreateKeyIcon()
        {
            const int size = 64;
            const float radius = 29f;
            const float ring = 3f;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * .5f, size * .5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                float edge = Mathf.Clamp01(radius + 1f - distance);
                bool inside = distance <= radius;
                bool outline = inside && distance >= radius - ring;
                byte alpha = (byte)(edge * 255f);
                pixels[y * size + x] = outline
                    ? new Color32(255, 255, 255, alpha)
                    : inside ? new Color32(0, 0, 0, (byte)(alpha * .78f))
                    : new Color32(255, 255, 255, 0);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void DrawChoiceArrow(Rect hitArea, bool pointsRight, Color colour, ref Texture2D texture)
        {
            if (texture == null) texture = CreateChoiceArrow(pointsRight);
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(new Rect(hitArea.center.x - 13, hitArea.center.y - 16, 26, 32), texture);
            GUI.color = previous;
        }

        static Texture2D CreateChoiceArrow(bool pointsRight)
        {
            const int width = 64, height = 80;
            var pixels = new Color32[width * height];
            Vector2 tip = new Vector2(pointsRight ? 59 : 5, 40);
            Vector2 top = new Vector2(pointsRight ? 5 : 59, 74);
            Vector2 bottom = new Vector2(pointsRight ? 5 : 59, 6);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int covered = 0;
                for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                {
                    Vector2 point = new Vector2(x + (sx + .5f) * .5f, y + (sy + .5f) * .5f);
                    float a = Cross(tip, top, point), b = Cross(top, bottom, point), c = Cross(bottom, tip, point);
                    if (!((a < 0 || b < 0 || c < 0) && (a > 0 || b > 0 || c > 0))) covered++;
                }
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)(covered * 255 / 4));
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 point) =>
            (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);

        static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 segment = b - a;
            float position = Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude);
            return Vector2.Distance(point, a + segment * position);
        }

        void DrawArt(Rect destination, Rect source, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.DrawTextureWithTexCoords(destination, menuArtwork, source);
            GUI.color = previous;
        }

        static void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void DrawPassengerOverlay()
        {
            if (overlayGame == null) return;
            float width = Mathf.Min(360f, Screen.width * .34f);
            float height = Mathf.Min(420f, Screen.height * .7f);
            var card = new Rect(Screen.width - width - 16f, 80f, width, height);
            Panel(card, new Color(Ink.r, Ink.g, Ink.b, .92f));
            GUI.Label(new Rect(card.x + 12, card.y + 10, card.width - 24, 28), "PASSENGERS", caption);
            float y = card.y + 44;
            var onboard = new HashSet<CrazyElevator.Shared.Rider>(overlayGame.CurrentPassengers);
            foreach (var rider in overlayGame.AllRiders)
            {
                if (rider == null || rider.Resolved) continue;
                bool inCar = onboard.Contains(rider);
                if (!inCar && (rider.Boarded || rider.Origin != overlayGame.DiscreteFloor
                    || rider.OriginStop != overlayGame.DiscreteStop)) continue;
                string line = rider.Name + "  →  F" + ElevatorManager.DestinationTag(rider) + (inCar ? "  [IN]" : "  [WAIT]");
                GUI.Label(new Rect(card.x + 14, y, card.width - 28, 22), line, body);
                y += 22;
                if (y > card.yMax - 28) break;
            }
        }

        void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Clamp(Screen.height / 16, 30, 56), fontStyle = FontStyle.Bold };
            GameTypography.Apply(title, true);
            title.normal.textColor = Cream;
            cardTitle = new GUIStyle(title) { fontSize = Mathf.Clamp(Screen.height / 25, 23, 38) };
            GameTypography.Apply(cardTitle, true);
            cardTitle.normal.textColor = Ink;
            menuLabelText = new GUIStyle(cardTitle) { fontSize = 42 };
            menuLabelText.normal.textColor = Ink;
            readyLabelText = new GUIStyle(menuLabelText) { fontSize = 26 };
            caption = new GUIStyle(title) { fontSize = Mathf.Clamp(Screen.height / 48, 14, 19), fontStyle = FontStyle.Normal };
            GameTypography.Apply(caption);
            caption.normal.textColor = Cream;
            cardCaption = new GUIStyle(caption)
            { fontSize = Mathf.Clamp(Screen.height / 58, 12, 16) };
            GameTypography.Apply(cardCaption);
            cardCaption.normal.textColor = Ink;
            body = new GUIStyle(caption) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.Clamp(Screen.height / 55, 12, 16) };
            GameTypography.Apply(body);
            startCircle = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = 48, fontStyle = FontStyle.Bold };
            startCircle.normal.textColor = Ink;
        }

        void DrawMode(Rect rect, Color accent, string heading, string detail, bool duel, bool selected)
        {
            Panel(new Rect(rect.x + 6, rect.y + 7, rect.width, rect.height), Color.black);
            Panel(rect, Cream);
            Panel(new Rect(rect.x, rect.y, rect.width, 12), accent);
            if (selected)
            {
                Panel(new Rect(rect.x - 5, rect.y - 5, rect.width + 10, 5), accent);
                Panel(new Rect(rect.x - 5, rect.yMax, rect.width + 10, 5), accent);
                Panel(new Rect(rect.x - 5, rect.y, 5, rect.height), accent);
                Panel(new Rect(rect.xMax, rect.y, 5, rect.height), accent);
            }
            AccentBar(new Rect(rect.x, rect.y + 12, 8, rect.height - 12), accent);
            GUI.Label(new Rect(rect.x + 20, rect.y + 47, rect.width - 36, 60), heading, cardTitle);
            GUI.Label(new Rect(rect.x + 24, rect.y + 116, rect.width - 48, 46), detail, cardCaption);
            var button = new Rect(rect.x + 24, rect.yMax - 66, rect.width - 48, 46);
            Panel(button, accent);
            if (GUI.Button(button, "PLAY", cardTitle))
                SelectAndStart(duel);
        }

        void StartSelectedMode()
        {
            launching = true;
            launchTimer = 0;
        }

        void SelectAndStart(bool duel)
        {
            Close();
            tutorialOnLoad = TutorialModeEnabled;
            // Tutorial mode presents the controls after both players finish
            // the guided shift. No-tutorial mode goes straight into the intro.
            showControllerInstructionsOnLoad = false;
            startDuelOnLoad = duel;
            playerTwoHumanOnLoad = duel && playerTwoSelected;
            playerOneCpuOnLoad = !playerOneHuman;
            if (duel && playerTwoSelected)
                InputManager.Instance?.EnableSplitKeyboard();
            SceneManager.LoadScene(duel ? DuelScene : SinglePlayerScene);
        }

        static void Panel(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        static void AccentBar(Rect rect, Color colour)
        {
            Panel(new Rect(Mathf.Round(rect.x), Mathf.Round(rect.y),
                Mathf.Round(rect.width), Mathf.Round(rect.height)), colour);
        }
    }
}
