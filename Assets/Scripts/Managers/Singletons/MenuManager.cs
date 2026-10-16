using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    // Mode select, pause overlay, and passenger destination overlay.
    [DefaultExecutionOrder(32000)]
    public sealed class MenuManager : MonoBehaviour
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
        const string InputHintsPreference = "CrazyElevator.InputHints.Keyboard";
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
        static int pauseSelection;
        static bool pauseMoveHeld;
        static bool pauseOpen;
        static bool passengerOverlayOpen;
        static ElevatorManager overlayGame;
        public static bool IsOpen { get; private set; }
        public static bool PauseOpen => pauseOpen;
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

        static void LoadInputHintsPreference()
        {
            if (inputHintsLoaded) return;
            keyboardHints = PlayerPrefs.GetInt(InputHintsPreference, 0) == 1;
            inputHintsLoaded = true;
        }

        static void SetKeyboardHints(bool enabled)
        {
            keyboardHints = enabled;
            inputHintsLoaded = true;
            PlayerPrefs.SetInt(InputHintsPreference, enabled ? 1 : 0);
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
            passengerOverlayOpen = false;
            startDuelOnLoad = false;
            showControllerInstructionsOnLoad = false;
            playerTwoHumanOnLoad = false;
            playerTwoSelected = false;
            playerOneCpuOnLoad = false;
            openLobbyOnLoad = false;
            overlayGame = null;
            inputHintsLoaded = false;
        }

        void Awake()
        {
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
                showingControllerInstructions = true;
                showControllerInstructionsOnLoad = false;
            }
            SyncTitleButtons();
            InputManager.Instance?.SetView(IsOpen ? GameView.Menu : GameView.ControlElevator);
        }

        void OnEnable() { title = null; }

        void OnDestroy()
        {
            if (playerOneLabelTexture != null) Destroy(playerOneLabelTexture);
            if (rivalLabelTexture != null) Destroy(rivalLabelTexture);
            if (triangleCursorTexture != null) Destroy(triangleCursorTexture);
            if (keyIconTexture != null) Destroy(keyIconTexture);
            if (leftChoiceArrowTexture != null) Destroy(leftChoiceArrowTexture);
            if (rightChoiceArrowTexture != null) Destroy(rightChoiceArrowTexture);
        }

        void Update()
        {
            if (pauseOpen)
            {
                UpdatePauseNavigation();
                return;
            }
            if (!IsOpen) return;
            if (showingControllerInstructions)
            {
                var instructionInput = InputManager.Instance;
                if (instructionInput == null) return;
                var playerOne = instructionInput.ReadForSeat(0, true);
                var playerTwo = instructionInput.ReadForSeat(1, true);
                if (playerOne.StartPressed || playerOne.ConfirmPressed
                    || playerTwo.StartPressed || playerTwo.ConfirmPressed)
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
            bool keyboardMenuConfirm = keyboard != null && keyboard.cKey.wasPressedThisFrame;
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
            if (!choosingMode && keyboard != null)
            {
                titleNavigation += new Vector2(
                    (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                        - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                        - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0));
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

        void UpdateOptionsNavigation()
        {
            var input = InputManager.Instance;
            if (input == null) return;

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
                playerOne.StartPressed |= keyboard.enterKey.wasPressedThisFrame;
            }
            bool vertical = Mathf.Abs(playerOne.Move.y) >= .55f;
            bool horizontal = Mathf.Abs(playerOne.Move.x) >= .55f;
            if (vertical && !optionsVerticalHeld)
                optionsSelection = optionsSelection == 0 ? 1 : 0;
            if (horizontal && !optionsHorizontalHeld && optionsSelection == 0)
                SetKeyboardHints(playerOne.Move.x > 0);

            optionsVerticalHeld = vertical;
            optionsHorizontalHeld = horizontal;

            bool confirm = playerOne.ConfirmPressed || playerOne.StartPressed;
            bool back = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            if (confirm)
            {
                if (optionsSelection == 1) CloseOptions();
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
            playerTwoSelected = false;
            playerOneHuman = true;
            playerOneCursor = 0;
            playerTwoCursor = 1;
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
            if (seat == 1 && playerTwoSelected && !playerTwoReady)
            {
                playerTwoReady = true;
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
                if (seat == 0 && playerOneHuman) playerOneReady = !playerOneReady;
                return;
            }
            if (cursor == 3)
            {
                if (seat == 1 && playerTwoSelected)
                    playerTwoReady = !playerTwoReady;
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
            var input = InputManager.Instance;
            if (input == null) return;
            for (int seat = 0; seat < 2; seat++)
            {
                if (!input.HasInput(seat)) continue;
                var frame = input.ReadForSeat(seat, true);
                bool moving = Mathf.Abs(frame.Move.y) >= .55f;
                if (moving && !pauseMoveHeld)
                {
                    pauseSelection = (pauseSelection + (frame.Move.y < 0 ? 1 : 2)) % 3;
                    pauseMoveHeld = true;
                }
                if (!moving) pauseMoveHeld = false;
                if (frame.ConfirmPressed)
                {
                    if (pauseSelection == 0)
                    {
                        var game = GameManager.Instance;
                        if (game != null && game.Player != null && game.Player.Match != null)
                            game.Player.Match.TogglePause();
                        else game?.TogglePause();
                    }
                    else if (pauseSelection == 1) ReturnToLobby();
                    else ReturnToMainMenu();
                    return;
                }
            }
        }

        public static void TogglePassengerOverlay(bool visible, ElevatorManager game)
        {
            passengerOverlayOpen = visible;
            overlayGame = game;
        }

        public static void ReturnToLobby()
        {
            openLobbyOnLoad = true;
            showControllerInstructionsOnLoad = false;
            pauseOpen = false;
            InputManager.Instance?.ResetRegistrations();
            SceneManager.LoadScene(DuelScene);
        }

        public static void ReturnToMainMenu()
        {
            openLobbyOnLoad = false;
            showControllerInstructionsOnLoad = false;
            sessionReady = false;
            pauseOpen = false;
            startDuelOnLoad = false;
            playerTwoHumanOnLoad = false;
            playerTwoSelected = false;
            playerOneCpuOnLoad = false;
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
                return;
            }
            if (passengerOverlayOpen) DrawPassengerOverlay();
        }

        void DrawControllerInstructions()
        {
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
            if (GUI.Button(startButton, "START", cardTitle))
                DismissControllerInstructions();
            GUI.Label(new Rect(400, 1038, 900, 38),
                "PRESS " + StartHintKey + " TO START", caption);
            GUI.matrix = Matrix4x4.identity;
        }

        string StartHintKey => KeyboardHintsEnabled ? "C" : "A";

        void DrawKeyboardInstructions()
        {
            DrawKeyboardHint(new Rect(210, 180, 380, 96), "W A S D", "MOVE");
            DrawKeyboardHint(new Rect(660, 180, 380, 96), "C", "SELECT PASSENGER");
            DrawKeyboardHint(new Rect(1110, 180, 380, 96), "E", "CLOSE DOORS");
            DrawKeyboardHint(new Rect(210, 330, 380, 96), "H", "HOLD DOORS");
            DrawKeyboardHint(new Rect(660, 330, 380, 96), "R", "REPAIR");
            DrawKeyboardHint(new Rect(1110, 330, 380, 96), "SHIFT", "SPEED BOOST");
            DrawKeyboardHint(new Rect(435, 510, 830, 104), "Y / TAB", "PASSENGER LIST");
            DrawKeyboardHint(new Rect(435, 650, 830, 104), "ESC", "PAUSE");
        }

        void DrawKeyboardHint(Rect rect, string key, string label)
        {
            Panel(rect, new Color(16f / 255f, 16f / 255f, 17f / 255f, .96f));
            GUI.Label(new Rect(rect.x, rect.y + 8, rect.width, 48), key, cardTitle);
            GUI.Label(new Rect(rect.x, rect.y + 56, rect.width, 30), label, caption);
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
            if (GUI.Button(modeButton, GUIContent.none, GUIStyle.none))
                SetKeyboardHints(!KeyboardHintsEnabled);

            Rect back = new Rect(620, 700, 460, 90);
            DrawSelectionFrame(back, optionsSelection == 1, false);
            Panel(back, Cream);
            GUI.Label(back, "BACK", cardTitle);
            DrawKeyHint(new Rect(back.x + back.width + 16f, back.y + 18f, 54f, 54f),
                KeyboardHintsEnabled ? "C" : "A");
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) CloseOptions();

            GUI.Label(new Rect(420, 850, 860, 42),
                "LEFT / RIGHT TO CHANGE", caption);
            GUI.matrix = Matrix4x4.identity;
        }

        void DismissControllerInstructions()
        {
            showingControllerInstructions = false;
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
                float buttonLeft = titleSelection == 0 ? 402f
                    : titleSelection == 1 ? 383f : 434f;
                float cursorX = buttonLeft - cursorWidth - cursorGap;
                Color cursorColor = GUI.color;
                GUI.color = new Color(TitleTeal.r, TitleTeal.g, TitleTeal.b,
                    choosingMode ? 1f - fade : 1f);
                GUI.DrawTexture(new Rect(cursorX, 742 + titleSelection * 118,
                    cursorWidth, cursorWidth), triangleCursorTexture);
                GUI.color = cursorColor;
                if (!choosingMode)
                {
                    DrawTitleActionHint();
                    DrawMovingTracks(new Rect(1060, 0, 495, ArtHeight), 1);
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
                        ReturnToMainMenu();
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
                        playerOneHuman = !playerOneHuman;
                        playerOneReady = !playerOneHuman;
                    }
                    if (GUI.Button(rivalButton, GUIContent.none, GUIStyle.none)
                        && playerTwoAvailable)
                    {
                        playerTwoSelected = !playerTwoSelected;
                        playerTwoReady = !playerTwoSelected;
                    }
                    if (GUI.Button(playerOneReadyButton, GUIContent.none, GUIStyle.none)
                        && playerOneHuman)
                        playerOneReady = !playerOneReady;
                    if (GUI.Button(playerTwoReadyButton, GUIContent.none, GUIStyle.none)
                        && playerTwoSelected)
                        playerTwoReady = !playerTwoReady;
                    if (GUI.Button(startButton, GUIContent.none, GUIStyle.none) && AllPlayersReady)
                        StartSelectedMode();
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
            float[] buttonWidths = { 246f, 301f, 194f };
            float[] buttonLefts = { 402f, 383f, 434f };
            Rect hint = new Rect(buttonLefts[titleSelection] + buttonWidths[titleSelection] + 16f,
                740f + titleSelection * 118f, 54f, 54f);
            DrawKeyHint(hint, KeyboardHintsEnabled ? "C" : "A");
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
                fontSize = 25,
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
                if (!inCar && (rider.Boarded || rider.Origin != overlayGame.DiscreteFloor)) continue;
                string line = rider.Name + "  →  F" + rider.Destination + (inCar ? "  [IN]" : "  [WAIT]");
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
            showControllerInstructionsOnLoad = true;
            startDuelOnLoad = duel;
            playerTwoHumanOnLoad = duel && playerTwoSelected;
            playerOneCpuOnLoad = !playerOneHuman;
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
