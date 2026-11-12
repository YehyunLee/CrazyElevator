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
        static Color Ink => PresentingMenu ? MenuPresentation.Ink : new Color32(9, 11, 16, 255);
        static Color Teal => PresentingMenu ? MenuPresentation.Teal : new Color32(75, 226, 202, 255);
        // Shared with the original shaft artwork and the input hints.
        static readonly Color TitleTeal = new Color32(90, 201, 179, 255);
        static Color Coral => PresentingMenu ? MenuPresentation.Coral : new Color32(255, 124, 104, 255);
        static Color Cream => PresentingMenu ? MenuPresentation.Paper : new Color32(255, 250, 234, 255);
        static readonly Color PlayerOneColour = new Color32(255, 80, 80, 255);
        static readonly Color PlayerTwoColour = new Color32(80, 145, 255, 255);
        [SerializeField] Texture2D menuArtwork;
        [SerializeField] Texture2D controllerInstructions;
        const float ArtWidth = 1700f;
        const float ArtHeight = 1082f;
        const float ShaftCabinTop = 185f;
        const float ShaftCabinHeight = 546f;
        const float DirectoryElevatorDescent = (ArtHeight - ShaftCabinHeight) * .5f - ShaftCabinTop;
        const float TransitionSeconds = 1.1f;
        const float LaunchSeconds = .95f;
        const float MenuFadeSeconds = .55f;
        // Lower IMGUI depths draw in front of gameplay HUD and ability overlays.
        const int TutorialOverlayDepth = -3000;
        static readonly Rect[] OptionsButtonRects =
        {
            new Rect(440f, 290f, 820f, 110f),
            new Rect(440f, 445f, 820f, 110f)
        };
        const string InputHintsPreference = "CrazyElevator.InputHints.Keyboard";
        const string TutorialModePreference = "CrazyElevator.TutorialMode.Enabled";
        [Header("First menu buttons")]
        [SerializeField] CanvasGroup titleButtons;
        [Header("Mode screen button rectangles (1700 x 1082)")]
        [SerializeField] Rect playerOneButton = new Rect(260, 300, 540, 480);
        [SerializeField] Rect rivalButton = new Rect(900, 300, 540, 480);
        // The status bars use the same outer frame as the cards and meet their
        // lower edge without a separate seam.
        [SerializeField] Rect playerOneReadyButton = new Rect(300, 665, 460, 80);
        [SerializeField] Rect playerTwoReadyButton = new Rect(940, 665, 460, 80);
        [SerializeField] Rect startButton = new Rect(720, 930, 260, 72);
        [SerializeField] Rect backButton = new Rect(48, 46, 220, 58);
        Texture2D keyIconTexture;
        Texture2D leftChoiceArrowTexture, rightChoiceArrowTexture;
        float transition;
        float launchTimer;
        bool choosingMode;
        bool launching;
        bool playerOneReady;
        bool playerTwoReady;
        bool lastPlayerTwoRegistered;
        bool playerTwoHadController;
        bool playerOneHuman = true;
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
        GUIStyle title, cardTitle, caption, cardCaption, body, menuLabelText, readyLabelText, startCircle;
        GUIStyle poolToggleLabel, poolCycleCaption;
        GUIStyle poolHeaderText, poolInstructionText, poolDimensionNameText, poolNoticeText;
        GUIStyle guideHeaderText, guideCaptionText, guideCardTitleText;
        GUIStyle guideNeighborItemText, guideCurrentItemText, guideArrowText;
        GUIStyle guideNextStopHeaderText, guideNextStopDescriptionText, guidePreviewText;
        bool stylesForMenu;
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
        static bool PresentingMenu => IsOpen || pauseOpen || Current != null
            && (Current.showingControllerInstructions || Current.roundPassengerIntroductionActive
                || Current.tutorialFinalePhase == TutorialFinalePhase.Story
                || Current.tutorialFinalePhase == TutorialFinalePhase.Controls
                || Current.tutorialFinalePhase == TutorialFinalePhase.Countdown);

        static Color MenuColor(Color color)
            => PresentingMenu ? MenuPresentation.DisplayColor(color) : color;
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
                            && menu.tutorialFinalePhase != TutorialFinalePhase.None
                        || menu.roundPassengerIntroductionActive);
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
            ResetLobbyCharacterSelection();
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
            MenuPresentation.ReleaseTextureMaterial();
            ReleasePassengerPortraits();
            OnDestroyDirectoryPreview();
            if (Current == this)
            {
                Current = null;
                roundPassengerIntroductionActive = false;
                RoundPassengerIntroductionActive = false;
            }
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
            if (roundPassengerIntroductionActive)
            {
                UpdateRoundPassengerIntroduction();
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
            bool playerTwoHasController = input.HasController(1);
            bool splitKeyboardMenu = keyboard != null && !playerTwoHasController;
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
                // Keep Player 2's arrow keys independent of Player 1's keyboard controls.
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
                if (playerTwoHasController != playerTwoHadController)
                    lobbyHeldDirection[1] = 0;
                playerTwoHadController = playerTwoHasController;
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
                titleNavigation += KeyboardMenuMove(false);
                UpdateTitleMouseCursor();
            }
            UpdateTitleNavigation(titleNavigation);

            if (choosingMode && transition >= TransitionSeconds)
            {
                UpdateLobbyCharacterNavigation(0, playerOneInput.Move.x);
                UpdateLobbyCharacterNavigation(1, playerTwoAvailable ? playerTwoInput.Move.x : 0f);
                UpdateLobbyPortraits();
            }

            if (playerOneInput.ConfirmPressed)
            {
                if (choosingMode && transition >= TransitionSeconds)
                    ConfirmLobbySeat(0);
            }
            if (choosingMode && transition >= TransitionSeconds && playerTwoAvailable
                && playerTwoInput.ConfirmPressed)
                ConfirmLobbySeat(1);
            if (TryStartReadyLobby()) return;
            if (!choosingMode && (playerOneInput.StartPressed || playerOneInput.ConfirmPressed
                || playerTwoInput.StartPressed || playerTwoInput.ConfirmPressed
                || playerTwoMenuStart || keyboardMenuConfirm))
                ActivateTitleSelection();
            if (choosingMode) transition = Mathf.Min(TransitionSeconds, transition + Time.unscaledDeltaTime);
            SyncTitleButtons();
        }

        void SyncTitleButtons()
        {
            // The title now draws and handles its buttons in the same IMGUI space.
            if (titleButtons != null)
            {
                titleButtons.alpha = 0f;
                titleButtons.interactable = false;
                titleButtons.blocksRaycasts = false;
            }
            SyncTitlePreview();
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
            UpdateOptionsMouseCursor();
            ReadDirectoryNavigation(out Vector2 move, out bool confirm, out bool back);
            confirm |= InputManager.Instance?.ReadForSeat(0, true).StartPressed == true;
            back |= Keyboard.current?.bKey.wasPressedThisFrame == true;
            if (back)
            {
                CloseOptions();
                return;
            }

            bool vertical = Mathf.Abs(move.y) >= .55f;
            bool horizontal = Mathf.Abs(move.x) >= .55f;
            if (vertical && !optionsVerticalHeld)
                optionsSelection = 1 - optionsSelection;
            if (confirm)
            {
                if (optionsSelection == 0) SetKeyboardHints(!KeyboardHintsEnabled);
                else SetTutorialMode(!TutorialModeEnabled);
            }
            else if (horizontal && !optionsHorizontalHeld && optionsSelection == 0)
                SetKeyboardHints(move.x > 0);

            optionsVerticalHeld = vertical;
            optionsHorizontalHeld = horizontal;
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
            lobbyHeldDirection[0] = lobbyHeldDirection[1] = 0;
            lastPlayerTwoRegistered = false;
            playerTwoHadController = false;
            InputManager.Instance?.BeginControllerJoining();
            SyncTitleButtons();
        }

        bool AllPlayersReady => playerOneHuman && playerTwoSelected
            && playerOneReady && playerTwoReady;

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
            if (seat == 0) playerOneReady = !playerOneReady;
            else playerTwoReady = !playerTwoReady;
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
        public static void Close()
        {
            IsOpen = false;
            Current?.SyncTitleButtons();
        }

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
            MusicManager.Instance?.SetTutorialMode(false);
            openLobbyOnLoad = true;
            fadeMenuOnLoad = true;
            showControllerInstructionsOnLoad = false;
            pauseOpen = false;
            InputManager.Instance?.ResetRegistrations();
            SceneManager.LoadScene(DuelScene);
        }

        public static void ReturnToMainMenu()
        {
            MusicManager.Instance?.SetTutorialMode(false);
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
                if (tutorialFinalePhase != TutorialFinalePhase.None)
                {
                    int previousDepth = GUI.depth;
                    GUI.depth = TutorialOverlayDepth;
                    DrawTutorialFinale();
                    GUI.depth = previousDepth;
                }
                DrawTutorialSkip();
                if (!TutorialWaitingPanelActive) DrawMenuFade();
                return;
            }
            if (roundPassengerIntroductionActive)
            {
                int previousDepth = GUI.depth;
                GUI.depth = TutorialOverlayDepth;
                DrawRoundPassengerIntroduction();
                GUI.depth = previousDepth;
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
            GUI.depth = TutorialOverlayDepth - 1;
            bool overGameplay = !FullScreenOverlayActive;
            float top = overGameplay ? (tutorialGame.Match != null ? 102f : 76f) : 18f;
            Rect button = new Rect(Screen.width - 184f, top, 172f, 32f);
            MenuPresentation.Plaque(button, MenuPresentation.Paper, MenuPresentation.Metal, 3f);
            Rect key = new Rect(button.x + 4f, button.y + 4f, 40f, 24f);
            MenuPresentation.Paint(key, MenuPresentation.Ink);
            MenuPresentation.Label(key, new GUIContent(KeyboardHintsEnabled ? "TAB" : "VIEW"),
                caption, MenuPresentation.Paper);
            MenuPresentation.Label(new Rect(key.xMax, button.y, button.width - 44f, button.height),
                new GUIContent("SKIP TUTORIAL"), caption, MenuPresentation.Ink);
            if (GUI.Button(button, GUIContent.none, GUIStyle.none)) SkipTutorial();
            GUI.depth = previousDepth;
        }

        void DrawControllerInstructions()
        {
            int previousDepth = GUI.depth;
            GUI.depth = tutorialActive ? TutorialOverlayDepth : -1000;
            Matrix4x4 previousMatrix = GUI.matrix;
            MenuPresentation.Backdrop(new Rect(0, 0, Screen.width, Screen.height));
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
            Rect confirmHint = new Rect(startButton.xMax + 16f, startButton.y + 11f, 54f, 54f);
            if (KeyboardHintsEnabled && RequiresBothControllerInstructionConfirmations)
            {
                DrawKeyHint(new Rect(startButton.x - 70f, confirmHint.y, 54f, 54f), "C");
                DrawKeyHint(confirmHint, ".");
            }
            else
            {
                string key = KeyboardHintsEnabled ? (tutorialGame?.Seat == 1 ? "." : "C") : "A";
                DrawKeyHint(confirmHint, key);
            }
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

        void DrawGamepadInstructions()
        {
            if (controllerInstructions != null)
            {
                // Fit the complete diagram above the shared ready button at any aspect ratio.
                MenuPresentation.DrawTexture(new Rect(180, 124, 1340, 748), controllerInstructions,
                    ScaleMode.ScaleToFit);
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
            caption.normal.textColor = MenuColor(Cream);
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
            cardTitle.normal.textColor = MenuColor(Cream);
            var content = new GUIContent(key);
            while (cardTitle.fontSize > 12 && cardTitle.CalcSize(content).x > rect.width - 8f)
                cardTitle.fontSize--;
            GUI.Label(rect, content, cardTitle);
            cardTitle.fontSize = previousFontSize;
            cardTitle.alignment = previousAlignment;
            cardTitle.normal.textColor = previousTextColor;
        }

        void DrawOptionsScreen()
        {
            MenuPresentation.Backdrop(new Rect(0, 0, Screen.width, Screen.height));
            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0), Quaternion.identity,
                new Vector3(scale, scale, 1));

            GUI.Label(new Rect(250, 70, 1200, 84), "OPTIONS", poolHeaderText);
            GUI.Label(new Rect(290, 154, 1120, 46),
                "Choose your controls and tutorial settings.", poolInstructionText);

            if (DrawOptionsRow(0, "CONTROL HINTS", KeyboardHintsEnabled ? "KEYBOARD" : "CONTROLLER",
                Teal, Teal, "CHANGE"))
                SetKeyboardHints(!KeyboardHintsEnabled);

            bool tutorialEnabled = TutorialModeEnabled;
            if (DrawOptionsRow(1, "TUTORIAL MODE", tutorialEnabled ? "ON" : "OFF",
                Coral, tutorialEnabled ? Teal : Coral, "TOGGLE"))
                SetTutorialMode(!TutorialModeEnabled);

            Rect back = new Rect(615, 910, 470, 76);
            DrawDirectoryCard(back, Cream, false);
            GUI.Label(back, "BACK", guideCardTitleText);
            DrawKeyHint(new Rect(back.xMax + 18f, back.center.y - 27f, 54f, 54f),
                KeyboardHintsEnabled ? "ESC" : "B", true);
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) CloseOptions();

            GUI.matrix = Matrix4x4.identity;
        }

        bool DrawOptionsRow(int index, string label, string value, Color border, Color valueColor, string action)
        {
            Rect row = OptionsButtonRects[index];
            DrawDirectoryCard(row, border, optionsSelection == index);

            const float valueWidth = 280f;
            Rect pill = new Rect(row.xMax - 24f - valueWidth, row.y + 22f, valueWidth, 66f);
            float labelX = row.x + 34f;
            GUI.Label(new Rect(labelX, row.y + 8f, pill.x - labelX - 16f, row.height - 16f),
                label, poolDimensionNameText);
            Panel(pill, valueColor);
            GUI.Label(new Rect(pill.x + 14f, pill.y, pill.width - 28f, pill.height), value, poolToggleLabel);

            if (optionsSelection == index)
            {
                DrawKeyHint(new Rect(row.xMax + 18f, row.center.y - 27f, 54f, 54f),
                    KeyboardHintsEnabled ? "C" : "A", true);
                GUI.Label(new Rect(row.xMax + 80f, row.center.y - 24f, 108f, 48f), action, poolCycleCaption);
            }
            return GUI.Button(row, GUIContent.none, GUIStyle.none);
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
            if (KeyboardHintsEnabled)
            {
                DrawKeyboardKeycap(hint, key);
                return;
            }
            if (keyIconTexture == null) keyIconTexture = CreateKeyIcon();

            Color previous = GUI.color;
            GUI.color = MenuColor(new Color(TitleTeal.r, TitleTeal.g, TitleTeal.b, .98f));
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
            else keyLabel.normal.textColor = MenuColor(Cream);
            GUI.Label(hint, key, keyLabel);
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

        static void DrawLabel(Rect rect, string label, GUIStyle style, Color colour)
        {
            if (Event.current.type != EventType.Repaint) return;

            Color previous = style.normal.textColor;
            style.normal.textColor = MenuColor(colour);
            // The plaque handles selection and input. Its label always uses the normal color.
            style.Draw(rect, new GUIContent(label), false, false, false, false);
            style.normal.textColor = previous;
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
            GUI.color = MenuColor(colour);
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

        void DrawArt(Rect destination, Rect source, float alpha)
        {
            Color previous = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            MenuPresentation.DrawTextureUV(destination, menuArtwork, source);
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
            if (title != null && stylesForMenu == PresentingMenu) return;
            stylesForMenu = PresentingMenu;
            title = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Clamp(Screen.height / 16, 30, 56), fontStyle = FontStyle.Bold };
            GameTypography.Apply(title, true);
            title.normal.textColor = MenuColor(Cream);
            cardTitle = new GUIStyle(title) { fontSize = Mathf.Clamp(Screen.height / 25, 23, 38) };
            GameTypography.Apply(cardTitle, true);
            cardTitle.normal.textColor = MenuColor(Ink);
            menuLabelText = new GUIStyle(cardTitle) { fontSize = 42 };
            menuLabelText.normal.textColor = MenuColor(Ink);
            readyLabelText = new GUIStyle(menuLabelText) { fontSize = 26 };
            caption = new GUIStyle(title) { fontSize = Mathf.Clamp(Screen.height / 48, 14, 19), fontStyle = FontStyle.Normal };
            GameTypography.Apply(caption);
            caption.normal.textColor = MenuColor(Cream);
            cardCaption = new GUIStyle(caption)
            { fontSize = Mathf.Clamp(Screen.height / 58, 12, 16) };
            GameTypography.Apply(cardCaption);
            cardCaption.normal.textColor = MenuColor(Ink);
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
            poolDimensionNameText.alignment = TextAnchor.MiddleLeft;
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
            startCircle.normal.textColor = MenuColor(Ink);
        }

        static GUIStyle FixedTextColorStyle(GUIStyle source, Color color)
        {
            GUIStyle style = new GUIStyle(source);
            SetFixedTextColor(style, color);
            return style;
        }

        static void SetFixedTextColor(GUIStyle style, Color color)
        {
            color = MenuColor(color);
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
            GUI.color = MenuColor(colour);
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
