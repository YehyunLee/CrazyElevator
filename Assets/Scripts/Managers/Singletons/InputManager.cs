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

        public event Action OnPause;
        public event Action OnOverlay;
        public event Action OnConfirm;
        public event Action OnKickout;
        public event Action OnCloseDoors;
        public event Action OnStart;

        void Awake()
        {
            // Scene object in Main — do not spawn a hidden runtime singleton.
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetView(GameView view) => ActiveView = view;

        void Update()
        {
            var keyboard = Keyboard.current;
            var pad = Gamepad.current;
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
