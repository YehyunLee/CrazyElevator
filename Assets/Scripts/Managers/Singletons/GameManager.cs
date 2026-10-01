using System.Collections;
using UnityEngine;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    // Liam GameManager: pause, timer/score, view mode, reset, dimensions.
    [DefaultExecutionOrder(-500)]
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public const int MaxNumberOfFloors = 12;

        public DimensionRange[] dimensions =
        {
            new DimensionRange("office", 0, 5),
            new DimensionRange("candyland", 6, 7),
            new DimensionRange("underwater", 8, 11)
        };

        public ElevatorManager Player { get; private set; }
        public ElevatorManager Elevator => Player;
        public PassengerManager Passengers { get; private set; }
        public GameView CurrentView { get; private set; } = GameView.Menu;
        public bool PassengerOverlayVisible { get; private set; }

        public float MainTimer => Player != null ? Player.SecondsLeft : ElevatorRound.Duration;
        public int Score => Player != null ? Player.Points : 0;
        public bool IsPaused => Player != null && Player.IsPaused;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            StartCoroutine(Startup());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        IEnumerator Startup()
        {
            yield return null;
            ElevatorManager player = null;
            foreach (var elevator in FindObjectsByType<ElevatorManager>(FindObjectsSortMode.None))
            {
                if (elevator != null && !elevator.IsNpc) { player = elevator; break; }
            }
            BindPlayer(player);
            if (MenuManager.IsOpen) GoToView(GameView.Menu);
            else SyncViewFromPlayer();
        }

        public void BindPlayer(ElevatorManager player)
        {
            Player = player;
            if (player == null) return;
            Passengers = player.GetComponent<PassengerManager>() ?? player.gameObject.AddComponent<PassengerManager>();
            Passengers.Bind(player);
            player.BindSession(this);
        }

        public void TogglePause()
        {
            if (MenuManager.IsOpen) return;
            if (Player == null) return;
            bool next = !Player.IsPaused;
            Player.SetPausedPublic(next);
            if (next)
            {
                GoToView(GameView.Pause);
                MenuManager.OpenPause();
            }
            else
            {
                MenuManager.ClosePause();
                SyncViewFromPlayer();
            }
        }

        public void GoToView(GameView view)
        {
            CurrentView = view;
            InputManager.Instance?.SetView(view);
        }

        public void SyncViewFromPlayer()
        {
            if (Player == null) { GoToView(GameView.Menu); return; }
            if (Player.IsPaused) { GoToView(GameView.Pause); return; }
            GoToView(Player.Traveling ? GameView.ControlElevator : GameView.Passenger);
        }

        public void ResetRound()
        {
            PassengerOverlayVisible = false;
            Player?.ResetRoundPublic();
            SyncViewFromPlayer();
        }

        public void TogglePassengerOverlay()
        {
            PassengerOverlayVisible = !PassengerOverlayVisible;
            MenuManager.TogglePassengerOverlay(PassengerOverlayVisible, Player);
        }

        public DimensionRange DimensionForFloor(int floor)
        {
            if (dimensions == null) return default;
            foreach (var range in dimensions)
                if (range.Contains(floor)) return range;
            return default;
        }

        public void NotifyVictory() => GoToView(GameView.Passenger);

        void Update()
        {
            if (Player == null || Player.IsNpc) return;
            if (MenuManager.IsOpen)
            {
                GoToView(GameView.Menu);
                return;
            }
            if (!IsPaused) SyncViewFromPlayer();

            var input = InputManager.Instance;
            if (input == null) return;

            if (input.PausePressed)
            {
                if (Player.Match != null) Player.Match.TogglePause();
                else TogglePause();
            }
            if (input.OverlayPressed) TogglePassengerOverlay();
            if (input.MutePressed) MusicManager.Instance?.ToggleMute();
        }
    }
}
