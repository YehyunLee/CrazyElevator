using System;
using UnityEngine;
using UnityEngine.InputSystem;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    // Central raw → semantic input hub. View map includes menus.
    [DefaultExecutionOrder(-1000)]
    public sealed class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        static readonly int[] joinedGamepadIds = { -1, -1 };
        bool controllerJoining;

        public GameView ActiveView { get; private set; } = GameView.Menu;

        public Vector2 Move { get; private set; }
        public Vector2 Select { get; private set; }
        public bool MovePressed { get; private set; }
        public bool SelectPressed { get; private set; }
        public bool ConfirmPressed { get; private set; }
        public bool KickoutPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool OverlayPressed { get; private set; }
        public bool CloseDoorsPressed { get; private set; }
        public bool HoldDoorHeld { get; private set; }
        public bool RepairHeld { get; private set; }
        public bool BoostHeld { get; private set; }
        public bool MutePressed { get; private set; }
        public bool StartPressed { get; private set; }
        public Vector2 Pointer { get; private set; }
        public bool PointerPressed { get; private set; }
        public bool PointerReleased { get; private set; }
        public bool PointerHeld { get; private set; }

        // Split-screen keyboard input is read per seat so Player 2 can play
        // without stealing Player 1's WASD/actions. Player 1 keeps the
        // existing keyboard and controller mapping; Player 2 uses arrows and
        // the right-side modifier keys.
        public struct SeatInput
        {
            public Vector2 Move;
            public Vector2 Select;
            public bool MovePressed;
            public bool SelectPressed;
            public bool ConfirmPressed;
            public bool KickoutPressed;
            public bool PausePressed;
            public bool OverlayPressed;
            public bool CloseDoorsPressed;
            public bool HoldDoorHeld;
            public bool RepairHeld;
            public bool BoostHeld;
            public bool MutePressed;
            public bool StartPressed;
            public bool StartButtonPressed;
            public Vector2 Pointer;
            public bool PointerPressed;
            public bool PointerReleased;
            public bool PointerHeld;
        }

        public event Action OnPause;
        public event Action OnOverlay;
        public event Action OnConfirm;
        public event Action OnKickout;
        public event Action OnCloseDoors;
        public event Action OnStart;
        public event Action<int> OnControllerJoined;

        void Awake()
        {
            // Scene object in Main — do not spawn a hidden runtime singleton.
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            GetComponent<PlayerInputManager>()?.DisableJoining();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetJoinedControllers()
        {
            joinedGamepadIds[0] = -1;
            joinedGamepadIds[1] = -1;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetView(GameView view) => ActiveView = view;

        public bool HasController(int seat)
            => seat >= 0 && seat < joinedGamepadIds.Length && joinedGamepadIds[seat] >= 0;

        public void BeginControllerJoining()
        {
            controllerJoining = true;
            var playerInputManager = GetComponent<PlayerInputManager>();
            playerInputManager?.EnableJoining();
            if (!HasController(0) && Gamepad.all.Count > 0)
            {
                Gamepad primary = Gamepad.all[0];
                var playerInput = playerInputManager?.JoinPlayer(0, -1, "Gamepad", primary);
                if (playerInput != null)
                    joinedGamepadIds[0] = primary.deviceId;
            }
        }

        public void RegisterPlayerInput(PlayerInput playerInput)
        {
            if (!controllerJoining || playerInput == null) return;
            int seat = playerInput.playerIndex;
            if (seat < 0 || seat >= joinedGamepadIds.Length) return;

            foreach (var device in playerInput.devices)
            {
                if (device is Gamepad gamepad)
                {
                    joinedGamepadIds[seat] = gamepad.deviceId;
                    OnControllerJoined?.Invoke(seat);
                    return;
                }
            }
        }

        Gamepad GamepadForSeat(int seat)
        {
            if (seat >= 0 && seat < joinedGamepadIds.Length && joinedGamepadIds[seat] >= 0)
            {
                foreach (var gamepad in Gamepad.all)
                    if (gamepad.deviceId == joinedGamepadIds[seat]) return gamepad;
            }
            return seat == 0 && Gamepad.all.Count > 0 ? Gamepad.all[0] : null;
        }

        // Read a seat-specific frame without changing the legacy seat-0
        // properties/events used by menus and solo play. In split-screen,
        // Player 1 uses WASD and Player 2 uses the arrow keys.
        public SeatInput ReadForSeat(int seat, bool splitKeyboard)
        {
            var keyboard = Keyboard.current;
            var pad = GamepadForSeat(seat);
            bool controllerAssigned = HasController(seat);
            bool playerTwo = seat == 1 && splitKeyboard && !controllerAssigned;
            bool keyboardAvailable = keyboard != null && Gamepad.all.Count == 0;

            Vector2 keys = Vector2.zero;
            if (keyboardAvailable && !controllerAssigned)
            {
                if (playerTwo)
                {
                    keys.x = (keyboard.rightArrowKey.isPressed ? 1 : 0)
                        - (keyboard.leftArrowKey.isPressed ? 1 : 0);
                    keys.y = (keyboard.upArrowKey.isPressed ? 1 : 0)
                        - (keyboard.downArrowKey.isPressed ? 1 : 0);
                }
                else
                {
                    keys.x = (keyboard.dKey.isPressed || (!splitKeyboard && keyboard.rightArrowKey.isPressed) ? 1 : 0)
                        - (keyboard.aKey.isPressed || (!splitKeyboard && keyboard.leftArrowKey.isPressed) ? 1 : 0);
                    keys.y = (keyboard.wKey.isPressed || (!splitKeyboard && keyboard.upArrowKey.isPressed) ? 1 : 0)
                        - (keyboard.sKey.isPressed || (!splitKeyboard && keyboard.downArrowKey.isPressed) ? 1 : 0);
                }
            }

            Vector2 stick = pad == null ? Vector2.zero : pad.leftStick.ReadValue() + pad.dpad.ReadValue();
            Vector2 navigation = stick + keys;
            bool confirm = playerTwo
                ? keyboardAvailable && keyboard.rightCtrlKey.wasPressedThisFrame
                : !controllerAssigned && keyboardAvailable && keyboard.cKey.wasPressedThisFrame
                    || pad != null && pad.buttonSouth.wasPressedThisFrame;
            bool kickout = playerTwo
                ? keyboardAvailable && keyboard.periodKey.wasPressedThisFrame
                : !controllerAssigned && keyboardAvailable && keyboard.iKey.wasPressedThisFrame
                    || pad != null && pad.buttonEast.wasPressedThisFrame && ActiveView == GameView.Passenger;
            bool pause = !controllerAssigned && keyboardAvailable && keyboard.escapeKey.wasPressedThisFrame
                || pad != null && pad.startButton.wasPressedThisFrame
                    && (ActiveView == GameView.ControlElevator || ActiveView == GameView.Passenger);
            bool overlay = !playerTwo && keyboardAvailable
                && (keyboard.yKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame);
            bool closeDoors = !playerTwo && pad != null && pad.buttonNorth.wasPressedThisFrame
                && (ActiveView == GameView.ControlElevator || ActiveView == GameView.Passenger);
            bool hold = playerTwo
                ? keyboardAvailable && keyboard.rightAltKey.isPressed
                : !controllerAssigned && keyboardAvailable && keyboard.hKey.isPressed
                    || pad != null && pad.buttonEast.isPressed && ActiveView != GameView.Passenger;
            bool repair = playerTwo
                ? keyboardAvailable && keyboard.slashKey.isPressed
                : !controllerAssigned && keyboardAvailable && keyboard.rKey.isPressed
                    || pad != null && pad.rightShoulder.isPressed;
            bool boost = playerTwo
                ? keyboardAvailable && keyboard.rightShiftKey.isPressed
                : !controllerAssigned && keyboardAvailable && (splitKeyboard
                    ? keyboard.leftShiftKey.isPressed
                    : keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
                    || pad != null && (pad.leftShoulder.isPressed
                        || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed);
            bool mute = !playerTwo && keyboardAvailable && keyboard.mKey.wasPressedThisFrame;
            bool start = playerTwo
                ? keyboardAvailable && keyboard.rightCtrlKey.wasPressedThisFrame
                : !controllerAssigned && keyboardAvailable && keyboard.enterKey.wasPressedThisFrame
                    || pad != null && (pad.buttonSouth.wasPressedThisFrame
                        || pad.startButton.wasPressedThisFrame
                            && (ActiveView == GameView.Menu || ActiveView == GameView.Pause));

            var result = new SeatInput
            {
                Move = navigation,
                Select = navigation,
                MovePressed = navigation.magnitude >= .55f,
                SelectPressed = navigation.magnitude >= .55f,
                ConfirmPressed = confirm,
                KickoutPressed = kickout,
                PausePressed = pause,
                OverlayPressed = overlay,
                CloseDoorsPressed = closeDoors,
                HoldDoorHeld = hold,
                RepairHeld = repair,
                BoostHeld = boost,
                MutePressed = mute,
                StartPressed = start,
                StartButtonPressed = pad != null && pad.startButton.wasPressedThisFrame
            };
            if (!controllerAssigned && !playerTwo && Mouse.current != null)
            {
                var mouse = Mouse.current;
                result.Pointer = mouse.position.ReadValue();
                result.PointerPressed = mouse.leftButton.wasPressedThisFrame;
                result.PointerReleased = mouse.leftButton.wasReleasedThisFrame;
                result.PointerHeld = mouse.leftButton.isPressed;
            }
            return result;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var pad = GamepadForSeat(0);
            var mouse = Mouse.current;

            Vector2 stick = pad == null ? Vector2.zero : pad.leftStick.ReadValue() + pad.dpad.ReadValue();
            Vector2 keys = Vector2.zero;
            if (keyboard != null)
            {
                keys.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1 : 0)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1 : 0);
                keys.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1 : 0)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1 : 0);
            }
            Vector2 navigation = stick + keys;
            Move = navigation;
            Select = navigation;
            MovePressed = navigation.magnitude >= .55f;
            SelectPressed = MovePressed;

            bool confirm = (keyboard != null && keyboard.cKey.wasPressedThisFrame)
                || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool kickout = (keyboard != null && keyboard.iKey.wasPressedThisFrame)
                || (pad != null && pad.buttonEast.wasPressedThisFrame && ActiveView == GameView.Passenger);
            bool pause = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (pad != null && pad.startButton.wasPressedThisFrame
                    && (ActiveView == GameView.ControlElevator || ActiveView == GameView.Passenger));
            bool overlay = keyboard != null && (keyboard.yKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame);
            // Keyboard C is contextual: confirm the highlighted passenger, or
            // close the doors when nobody is selected. ElevatorManager owns
            // that choice. Controller Y remains a dedicated close-door input.
            bool closeDoors = pad != null && pad.buttonNorth.wasPressedThisFrame
                && (ActiveView == GameView.ControlElevator || ActiveView == GameView.Passenger);
            bool hold = (keyboard != null && keyboard.hKey.isPressed)
                || (pad != null && pad.buttonEast.isPressed && ActiveView != GameView.Passenger);
            bool repair = (keyboard != null && keyboard.rKey.isPressed)
                || (pad != null && pad.rightShoulder.isPressed);
            bool boost = (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
                || (pad != null && (pad.leftShoulder.isPressed
                    || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed));
            bool mute = keyboard != null && keyboard.mKey.wasPressedThisFrame;
            bool start = (keyboard != null && keyboard.enterKey.wasPressedThisFrame)
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame
                    || (pad.startButton.wasPressedThisFrame
                        && (ActiveView == GameView.Menu || ActiveView == GameView.Pause))));

            ConfirmPressed = confirm;
            KickoutPressed = kickout;
            PausePressed = pause;
            OverlayPressed = overlay;
            CloseDoorsPressed = closeDoors;
            HoldDoorHeld = hold;
            RepairHeld = repair;
            BoostHeld = boost;
            MutePressed = mute;
            StartPressed = start;

            if (mouse != null)
            {
                Pointer = mouse.position.ReadValue();
                PointerPressed = mouse.leftButton.wasPressedThisFrame;
                PointerReleased = mouse.leftButton.wasReleasedThisFrame;
                PointerHeld = mouse.leftButton.isPressed;
                // Left click stays with passenger drag / HUD buttons; right click is kickout.
                if (mouse.rightButton.wasPressedThisFrame)
                    KickoutPressed = true;
            }
            else
            {
                Pointer = Vector2.zero;
                PointerPressed = PointerReleased = PointerHeld = false;
            }

            if (ActiveView == GameView.Menu || ActiveView == GameView.Pause)
            {
                if (pause) OnPause?.Invoke();
                if (start) OnStart?.Invoke();
                return;
            }

            if (pause) OnPause?.Invoke();
            if (overlay) OnOverlay?.Invoke();
            if (confirm) OnConfirm?.Invoke();
            if (kickout) OnKickout?.Invoke();
            if (closeDoors) OnCloseDoors?.Invoke();
            if (start) OnStart?.Invoke();
        }
    }
}
