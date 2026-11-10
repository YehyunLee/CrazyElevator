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
        [SerializeField] Texture2D passengerPortraitAtlas;
        [SerializeField] Texture2D controllerInstructions;
        const float ArtWidth = 1700f;
        const float ArtHeight = 1082f;
        const float ShaftCabinTop = 185f;
        const float ShaftCabinHeight = 546f;
        const float DirectoryElevatorDescent = (ArtHeight - ShaftCabinHeight) * .5f - ShaftCabinTop;
        const float TransitionSeconds = 1.1f;
        const float LaunchSeconds = .95f;
        const float MenuFadeSeconds = .55f;
        // Keep the IMGUI cursor and action hint locked to the same reference-
        // resolution rectangles as the title Canvas buttons in Main.unity.
        static readonly Rect[] TitleButtonRects =
        {
            new Rect(300f, 680f, 310f, 70f), // START, left column
            new Rect(650f, 740f, 310f, 70f), // DIMENSION POOL, right column
            new Rect(300f, 825f, 310f, 70f), // GUIDE, left column
            new Rect(650f, 885f, 310f, 70f), // OPTIONS, right column
            new Rect(300f, 970f, 310f, 70f)  // EXIT, left column
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
        [SerializeField] Rect playerOneButton = new Rect(260, 300, 540, 480);
        [SerializeField] Rect rivalButton = new Rect(900, 300, 540, 480);
        // The status bars use the same outer frame as the cards and meet their
        // lower edge without a separate seam.
        [SerializeField] Rect playerOneReadyButton = new Rect(300, 665, 460, 80);
        [SerializeField] Rect playerTwoReadyButton = new Rect(940, 665, 460, 80);
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
        GUIStyle poolToggleLabel, poolCycleCaption;
        GUIStyle poolHeaderText, poolInstructionText, poolDimensionNameText;
        GUIStyle poolDimensionDetailText, poolNoticeText;
        GUIStyle guideHeaderText, guideCaptionText, guideCardTitleText;
        GUIStyle guideNeighborItemText, guideCurrentItemText, guideArrowText;
        GUIStyle guideNextStopHeaderText, guideNextStopDescriptionText, guidePreviewText;
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
        static float pauseRepeatTimer;
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
                    && (pauseOpen || menu.showingControllerInstructions
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
            tutorialMode = true;
        }

        void Awake()
        {
            Current = this;
            menuFadeAlpha = fadeMenuOnLoad ? 1f : 0f;
            fadeMenuOnLoad = false;
            LoadInputHintsPreference();
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
            OnDestroyDirectoryPreview();
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
            if (showingDimensionPool)
            {
                UpdateDimensionPoolNavigation();
                SyncTitleButtons();
                return;
            }
            if (showingPassengerGuide)
            {
                UpdatePassengerGuideNavigation();
                if (directoryPreviewModel == null) UpdateDirectoryPreview();
                UpdateDirectoryPreviewAnimation();
                SyncTitleButtons();
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
            if (choosingMode && transition >= TransitionSeconds)
            {
                bool backPressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
                foreach (Gamepad pad in Gamepad.all)
                    if (pad != null) backPressed |= pad.buttonEast.wasPressedThisFrame;
                if (backPressed)
                {
                    ReturnToMainMenu();
                    return;
                }
            }
            if (choosingMode)
            {
                if (playerTwoRegistered && !lastPlayerTwoRegistered)
                {
                    playerTwoSelected = true;
                    playerTwoReady = false;
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
                titleSelection = (titleSelection + (titleNavigation.y < 0 ? 1 : 4)) % TitleButtonRects.Length;
            else if (!choosingMode && horizontal && !horizontalHeld)
            {
                if (titleNavigation.x > 0f)
                {
                    if (titleSelection == 0) titleSelection = 1;
                    else if (titleSelection == 2 || titleSelection == 4) titleSelection = 3;
                }
                else
                {
                    if (titleSelection == 1) titleSelection = 0;
                    else if (titleSelection == 3) titleSelection = 2;
                }
            }
            horizontalHeld = horizontal;
            verticalHeld = vertical;

            if (choosingMode && transition >= TransitionSeconds)
            {
                MoveLobbyCursor(ref playerOneCursor, playerOneInput.Move, ref playerOneCursorHeld, 0);
                if (playerTwoAvailable)
                    MoveLobbyCursor(ref playerTwoCursor, playerTwoInput.Move, ref playerTwoCursorHeld, 1);
                UpdateLobbyMouseCursor(playerTwoAvailable);
            }

            if (playerOneInput.ConfirmPressed)
            {
                if (choosingMode && transition >= TransitionSeconds)
                    HandleLobbyConfirm(0);
            }
            if (choosingMode && transition >= TransitionSeconds && playerTwoAvailable
                && playerTwoInput.ConfirmPressed)
                HandleLobbyConfirm(1);
            if (TryStartReadyLobby()) return;
            if (!choosingMode && (playerOneInput.StartPressed || playerOneInput.ConfirmPressed
                || playerTwoInput.StartPressed || playerTwoInput.ConfirmPressed
                || playerTwoMenuStart || keyboardMenuConfirm))
                ActivateTitleSelection();
            if (!playerTwoSelected) playerTwoUnavailable = false;
            if (choosingMode) transition = Mathf.Min(TransitionSeconds, transition + Time.unscaledDeltaTime);
            SyncTitleButtons();
        }

        void SyncTitleButtons()
        {
            if (titleButtons == null) return;
            // The IMGUI transition veil fades the entire Canvas together. Keep the
            // artwork and scene Buttons visible underneath it until the mode screen takes over.
            bool titleVisible = !showingDimensionPool && !showingPassengerGuide && !showingOptions;
            titleButtons.alpha = IsOpen && titleVisible && (!choosingMode || transition < TransitionSeconds) ? 1f : 0f;
            titleButtons.interactable = IsOpen && titleVisible && !choosingMode;
            titleButtons.blocksRaycasts = IsOpen && titleVisible && !choosingMode;
        }

        public void OnTitleStart() { titleSelection = 0; BeginModeSelection(); }
        public void OnTitleDimensionPool() => OpenDimensionPool();
        public void OnTitlePassengerGuide() => OpenPassengerGuide();
        public void OnTitleOptions()
        {
            titleSelection = 3;
            showingOptions = true;
            optionsSelection = 0;
            optionsHorizontalHeld = false;
            optionsVerticalHeld = false;
            SyncTitleButtons();
        }
        public void OnTitleExit() { titleSelection = 4; ExitGame(); }

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
            titleSelection = 3;
            SyncTitleButtons();
        }

        void BeginModeSelection()
        {
            choosingMode = true;
            playerOneReady = false;
            playerTwoReady = false;
            // Each seat belongs to a human; a shared keyboard is always supported.
            playerTwoSelected = true;
            playerOneHuman = true;
            // Each confirm controls only that player's ready state.
            playerOneCursor = 2;
            playerTwoCursor = 3;
            playerOneCursorHeld = false;
            playerTwoCursorHeld = false;
            lastPlayerTwoRegistered = false;
            InputManager.Instance?.BeginControllerJoining();
            SyncTitleButtons();
        }

        bool AllPlayersReady => playerOneReady && playerTwoReady;

        bool TryStartReadyLobby()
        {
            if (!choosingMode || launching || transition < TransitionSeconds || !AllPlayersReady)
                return false;
            StartSelectedMode();
            return true;
        }

        void HandleLobbyConfirm(int seat)
        {
            if (launching) return;
            if (seat == 0)
            {
                playerOneReady = !playerOneReady;
                playerOneCursor = playerOneReady ? 4 : 2;
            }
            else
            {
                playerTwoReady = !playerTwoReady;
                playerTwoCursor = playerTwoReady ? 4 : 3;
            }
        }

        void MoveLobbyCursor(ref int cursor, Vector2 move, ref bool held, int seat)
        {
            // There is one action per seat, so confirming never changes a mode.
            cursor = seat == 0 ? 2 : 3;
            held = move.magnitude >= .55f;
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

            if (playerOneReadyButton.Contains(pointer)) playerOneCursor = 2;
            if (playerTwoReadyButton.Contains(pointer)) playerTwoCursor = 3;
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
            pauseRepeatTimer = 0f;
            InputManager.Instance?.SetView(GameView.Pause);
        }

        public static void ClosePause()
        {
            pauseOpen = false;
            pauseMoveHeld = false;
            pauseRepeatTimer = 0f;
        }

        public static int PauseSelection => pauseSelection;

        void UpdatePauseNavigation()
        {
            // Opening pause changes the input view earlier in this frame.
            // Navigation starts on the next frame so that press is consumed once.
            if (Time.frameCount == pauseOpenedFrame) return;
            Vector2 navigation = Vector2.zero;
            bool confirm = false, back = false;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                navigation.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
                confirm = keyboard.cKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame;
                back = keyboard.escapeKey.wasPressedThisFrame;
            }
            foreach (Gamepad pad in Gamepad.all)
            {
                if (pad == null) continue;
                Vector2 padMove = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
                if (Mathf.Abs(padMove.y) > Mathf.Abs(navigation.y)) navigation.y = padMove.y;
                confirm |= pad.buttonSouth.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
            }

            bool moving = Mathf.Abs(navigation.y) >= .55f;
            pauseRepeatTimer -= Time.unscaledDeltaTime;
            if (moving && (!pauseMoveHeld || pauseRepeatTimer <= 0f))
            {
                pauseSelection = (pauseSelection + (navigation.y < 0 ? 1 : 2)) % 3;
                pauseRepeatTimer = pauseMoveHeld ? .12f : .28f;
            }
            pauseMoveHeld = moving;
            if (back)
            {
                ResumeFromPause();
                return;
            }
            if (!confirm) return;
            if (pauseSelection == 0) ResumeFromPause();
            else if (pauseSelection == 1) ReturnToLobby();
            else ReturnToMainMenu();
        }

        static void ResumeFromPause()
        {
            var game = GameManager.Instance;
            if (game != null && game.Player != null && game.Player.Match != null)
                game.Player.Match.TogglePause();
            else game?.TogglePause();
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
                else if (showingDimensionPool) DrawDimensionPoolScreen();
                else if (showingPassengerGuide) DrawPassengerGuideScreen();
                else if (showingOptions) DrawOptionsScreen();
                else DrawModeSelect();
                DrawMenuFade();
                return;
            }
            if (pauseOpen)
            {
                DrawPauseScreen();
                return;
            }
            if (tutorialActive)
            {
                if (showingControllerInstructions)
                {
                    DrawControllerInstructions();
                    DrawTutorialSkip();
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
                DrawTutorialSkip();
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

        void DrawTutorialSkip()
        {
            if (!tutorialActive || tutorialGame == null) return;
            int previousDepth = GUI.depth;
            GUI.depth = -1001;
            Rect button = new Rect(Screen.width - 220f, 18f, 200f, 48f);
            Panel(button, Cream);
            if (GUI.Button(button, "SKIP TUTORIAL", cardCaption)) SkipTutorial();
            Color previousCaption = caption.normal.textColor;
            caption.normal.textColor = Cream;
            GUI.Label(new Rect(button.x, button.yMax + 3f, button.width, 22f),
                "TAB / VIEW", caption);
            caption.normal.textColor = previousCaption;
            GUI.depth = previousDepth;
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
            else DrawGamepadInstructions();
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

        void DrawGamepadInstructions()
        {
            if (controllerInstructions != null)
            {
                // Fit the complete diagram above the shared ready button at any aspect ratio.
                GUI.DrawTexture(new Rect(180, 124, 1340, 748), controllerInstructions,
                    ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(100, 884, 1500, 44),
                    "AT A STOP: STICK / D-PAD OPENS DOORS  ·  A STOPS / CLOSES  ·  LB BOOSTS  ·  RB REPAIRS",
                    caption);
                return;
            }

            DrawKeyboardHint(new Rect(210, 220, 380, 110), "STICK / D-PAD", "MOVE / OPEN DOOR");
            DrawKeyboardHint(new Rect(660, 220, 380, 110), "A", "STOP / CLOSE DOORS");
            DrawKeyboardHint(new Rect(1110, 220, 380, 110), "B", "KICK");
            DrawKeyboardHint(new Rect(210, 365, 380, 110), "DOWN", "SELECT PASSENGER");
            DrawKeyboardHint(new Rect(660, 365, 380, 110), "RB", "REPAIR");
            DrawKeyboardHint(new Rect(1110, 365, 380, 110), "LB", "SPEED BOOST");
            DrawKeyboardHint(new Rect(570, 510, 560, 110), "START", "PAUSE");
        }

        void DrawKeyboardInstructions()
        {
            GUI.Label(new Rect(210, 126, 1280, 42), "PLAYER 1", caption);
            DrawKeyboardHint(new Rect(210, 180, 380, 96), "W A S D", "MOVE / OPEN DOOR");
            DrawKeyboardHint(new Rect(660, 180, 380, 96), "C", "STOP / CLOSE DOORS");
            DrawKeyboardHint(new Rect(1110, 180, 380, 96), "I", "KICK");
            DrawKeyboardHint(new Rect(435, 300, 380, 96), "R", "REPAIR");
            DrawKeyboardHint(new Rect(885, 300, 380, 96), "LSHIFT", "SPEED BOOST");
            if (tutorialActors[1] != null
                || GameManager.Instance != null && GameManager.Instance.Player != null
                    && GameManager.Instance.Player.Match != null
                    && !GameManager.Instance.Player.Match.Rival.IsNpc)
            {
                GUI.Label(new Rect(210, 426, 1280, 42), "PLAYER 2", caption);
                DrawKeyboardHint(new Rect(210, 480, 380, 96), "ARROWS", "MOVE / OPEN DOOR");
                DrawKeyboardHint(new Rect(660, 480, 380, 96), ".", "STOP / CLOSE DOORS");
                DrawKeyboardHint(new Rect(1110, 480, 380, 96), ",", "KICK");
                DrawKeyboardHint(new Rect(435, 600, 380, 96), ";", "REPAIR");
                DrawKeyboardHint(new Rect(885, 600, 380, 96), "RSHIFT", "SPEED BOOST");
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
                float keyWidth = Mathf.Clamp(28f + key.Length * 15f, 44f, rect.width - 20f);
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
                "B");
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) CloseOptions();

            GUI.matrix = Matrix4x4.identity;
        }

        void DismissControllerInstructions()
        {
            if (tutorialActive && tutorialFinalePhase == TutorialFinalePhase.Controls)
            {
                showingControllerInstructions = false;
                Close();
                FinishTutorial();
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

        void DrawModeSelect() => DrawArtworkMenu();

        void DrawArtworkMenu()
        {
            float fade = Mathf.Clamp01(transition / TransitionSeconds);
            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            var oldMatrix = GUI.matrix;
            if (choosingMode || titleButtons == null)
                Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0), Quaternion.identity,
                new Vector3(scale, scale, 1));

            bool hasCanvasTitle = titleButtons != null;
            if (!hasCanvasTitle && fade < 1f)
            {
                DrawArt(new Rect(0, 0, ArtWidth, ArtHeight), new Rect(0, 0, 1, 1), 1f - fade);
                // Fallback if this script is opened in a scene without the title Canvas.
                Color cleanBackground = Color.Lerp(new Color32(16, 16, 17, 255), Ink, fade);
                Panel(new Rect(380, 690, 315, 370), cleanBackground);
                Panel(new Rect(278, 733, 112, 66), cleanBackground);
            }
            if (!choosingMode)
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

            else
            {
                Panel(new Rect(0, 0, ArtWidth, ArtHeight), new Color(.035f, .045f, .10f, .90f));
                DrawLocalLobby();
                if (fade < 1f)
                    Panel(new Rect(0, 0, ArtWidth, ArtHeight), new Color(Ink.r, Ink.g, Ink.b, 1f - fade));
                if (launching)
                {
                    Panel(new Rect(0, 0, ArtWidth, ArtHeight), new Color(.035f, .045f, .1f, .94f));
                    MenuText(new Rect(250, 420, 1200, 110), "ALL ABOARD!", 68, Teal);
                }
            }
            GUI.matrix = oldMatrix;
        }

        void DrawLocalLobby()
        {
            MenuText(new Rect(400, 65, 900, 48), "LOCAL MULTIPLAYER  /  2 PLAYERS", 24, Teal);
            MenuText(new Rect(320, 135, 1060, 92), "PLAYER SELECT", 46, Cream);
            Panel(backButton, LobbySurface);
            MenuText(backButton, "< BACK", 24, Cream);
            if (GUI.Button(backButton, GUIContent.none, GUIStyle.none))
            {
                choosingMode = false;
                transition = 0;
                playerOneReady = playerTwoReady = false;
                SyncTitleButtons();
            }
            DrawLocalPlayerCard(playerOneButton, playerOneReadyButton, 0, playerOneReady, Coral);
            DrawLocalPlayerCard(rivalButton, playerTwoReadyButton, 1, playerTwoReady, Teal);
            MenuText(new Rect(810, 480, 80, 65), "VS", 32, Cream);
            string status = AllPlayersReady ? "LET'S GO!"
                : playerOneReady ? "WAITING FOR PLAYER 2..."
                : playerTwoReady ? "WAITING FOR PLAYER 1..." : "BOTH PLAYERS: PRESS YOUR READY KEY";
            MenuText(new Rect(300, 805, 1100, 50), status, 27, Cream);
            Rect tutorialToggle = new Rect(480, 890, 740, 62);
            Panel(tutorialToggle, LobbySurface);
            MenuText(tutorialToggle, TutorialModeEnabled ? "[ON]  GUIDED PRACTICE" : "[OFF]  GUIDED PRACTICE", 24, Teal);
            if (GUI.Button(tutorialToggle, GUIContent.none, GUIStyle.none)) SetTutorialMode(!TutorialModeEnabled);
        }

        void DrawLocalPlayerCard(Rect card, Rect readyButton, int seat, bool ready, Color accent)
        {
            Panel(new Rect(card.x + 8, card.y + 8, card.width, card.height), Color.black);
            Panel(card, accent);
            Panel(new Rect(card.x + 4, card.y + 4, card.width - 8, card.height - 8),
                new Color32(31, 38, 59, 255));
            MenuText(new Rect(card.x, card.y + 20, card.width, 55), "PLAYER " + (seat + 1), 34, accent);
            if (passengerPortraitAtlas != null)
            {
                int cell = seat == 0 ? 0 : 8;
                GUI.DrawTextureWithTexCoords(new Rect(card.center.x - 80, card.y + 92, 160, 160),
                    passengerPortraitAtlas, new Rect((cell % 4) * .25f, 1f - (cell / 4 + 1) * .25f, .25f, .25f));
            }
            bool controller = InputManager.Instance != null && InputManager.Instance.HasController(seat);
            string move = controller ? "STICK / D-PAD" : seat == 0 ? "W A S D" : "ARROW KEYS";
            string key = controller ? "A" : seat == 0 ? "C" : ".";
            MenuText(new Rect(card.x, card.y + 265, card.width, 40), move + "  TO MOVE", 24, Cream);
            MenuText(new Rect(card.x, card.y + 310, card.width, 30), controller ? "CONTROLLER CONNECTED" : "SHARED KEYBOARD", 18, accent);
            Panel(readyButton, ready ? accent : LobbySurface);
            MenuText(readyButton, ready ? "READY" : "[" + key + "]  I'M READY", 27, ready ? Ink : Cream);
            if (GUI.Button(readyButton, GUIContent.none, GUIStyle.none)) HandleLobbyConfirm(seat);
        }

        void MenuText(Rect rect, string text, int size, Color colour)
        {
            GUIStyle style = new GUIStyle(caption) { fontSize = size, wordWrap = false };
            SetFixedTextColor(style, colour);
            GUI.Label(rect, text, style);
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
            else if (titleSelection == 1) OpenDimensionPool();
            else if (titleSelection == 2) OpenPassengerGuide();
            else if (titleSelection == 3) OnTitleOptions();
            else if (titleSelection == 4) ExitGame();
        }

        void DrawKeyHint(Rect hint, string key, bool fixedTextColor = false)
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
            if (fixedTextColor) SetFixedTextColor(keyLabel, Cream);
            else keyLabel.normal.textColor = Cream;
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

            float verticalScale = rect.height / ArtHeight;
            float cabinY = rect.y + ShaftCabinTop * verticalScale;
            float cabinHeight = ShaftCabinHeight * verticalScale;
            float cabinX = rect.x + rect.width * 32f / 495f;
            float cabinWidth = rect.width * 427f / 495f;
            Rect originalCabin = new Rect(cabinX, cabinY, cabinWidth, cabinHeight);
            Panel(originalCabin, new Color(16f / 255f, 16f / 255f, 17f / 255f, alpha));
            Rect cabinSource = new Rect(1092f / ArtWidth,
                1f - (ShaftCabinTop + ShaftCabinHeight) / ArtHeight,
                427f / ArtWidth, ShaftCabinHeight / ArtHeight);
            DrawArt(new Rect(cabinX, cabinY + descent * verticalScale,
                cabinWidth, cabinHeight), cabinSource, alpha);
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
            poolToggleLabel = new GUIStyle(cardTitle)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Screen.height / 36, 24, 32),
                wordWrap = false
            };
            poolCycleCaption = new GUIStyle(cardCaption)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            SetFixedTextColor(poolCycleCaption, Color.white);
            poolHeaderText = FixedTextColorStyle(title, Cream);
            poolInstructionText = FixedTextColorStyle(caption, Cream);
            poolDimensionNameText = FixedTextColorStyle(cardTitle, Ink);
            poolDimensionDetailText = FixedTextColorStyle(cardCaption, Ink);
            poolNoticeText = FixedTextColorStyle(caption, Cream);
            body = new GUIStyle(caption) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.Clamp(Screen.height / 55, 12, 16) };
            GameTypography.Apply(body);
            guideHeaderText = FixedTextColorStyle(title, Cream);
            guideCaptionText = FixedTextColorStyle(caption, Cream);
            guideCardTitleText = FixedTextColorStyle(cardTitle, Ink);
            guideNeighborItemText = FixedTextColorStyle(cardTitle, new Color32(155, 155, 165, 255));
            GameTypography.Apply(guideNeighborItemText);
            guideNeighborItemText.fontSize = 16;
            guideNeighborItemText.alignment = TextAnchor.MiddleCenter;
            guideNeighborItemText.wordWrap = false;
            guideCurrentItemText = FixedTextColorStyle(cardTitle, Ink);
            GameTypography.Apply(guideCurrentItemText, true);
            SetFixedTextColor(guideCurrentItemText, Ink);
            guideCurrentItemText.fontSize = 23;
            guideCurrentItemText.alignment = TextAnchor.MiddleCenter;
            guideCurrentItemText.wordWrap = false;
            guideArrowText = FixedTextColorStyle(cardTitle, Ink);
            guideArrowText.fontSize = 48;
            guideArrowText.alignment = TextAnchor.MiddleCenter;
            guideNextStopHeaderText = FixedTextColorStyle(cardTitle, Color.white);
            GameTypography.Apply(guideNextStopHeaderText, true);
            SetFixedTextColor(guideNextStopHeaderText, Color.white);
            guideNextStopHeaderText.alignment = TextAnchor.MiddleCenter;
            guideNextStopHeaderText.fontSize = 23;
            guideNextStopDescriptionText = FixedTextColorStyle(body, Color.white);
            guideNextStopDescriptionText.fontSize = 24;
            guideNextStopDescriptionText.alignment = TextAnchor.MiddleCenter;
            guideNextStopDescriptionText.wordWrap = true;
            guidePreviewText = FixedTextColorStyle(caption, Cream);
            GameTypography.Apply(guidePreviewText, true);
            SetFixedTextColor(guidePreviewText, Cream);
            guidePreviewText.fontSize = Mathf.Clamp(Screen.height / 42, 20, 26);
            guidePreviewText.alignment = TextAnchor.MiddleCenter;
            guidePreviewText.wordWrap = false;
            startCircle = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = 48, fontStyle = FontStyle.Bold };
            startCircle.normal.textColor = Ink;
        }

        static GUIStyle FixedTextColorStyle(GUIStyle source, Color color)
        {
            GUIStyle style = new GUIStyle(source);
            SetFixedTextColor(style, color);
            return style;
        }

        static void SetFixedTextColor(GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            style.hover.textColor = color;
            style.active.textColor = color;
            style.focused.textColor = color;
            style.onNormal.textColor = color;
            style.onHover.textColor = color;
            style.onActive.textColor = color;
            style.onFocused.textColor = color;
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
            startDuelOnLoad = true;
            playerTwoHumanOnLoad = true;
            playerOneCpuOnLoad = false;
            InputManager.Instance?.EnableSplitKeyboard();
            SceneManager.LoadScene(DuelScene);
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
