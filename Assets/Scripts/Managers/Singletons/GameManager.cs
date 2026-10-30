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

        public const int MaxNumberOfFloors = ElevatorRound.Floors;

        public DimensionRange[] dimensions =
        {
            new DimensionRange("office", 0, ElevatorRound.FloorsPerDimension - 1),
            new DimensionRange("candyland", ElevatorRound.FloorsPerDimension,
                ElevatorRound.FloorsPerDimension * 2 - 1),
            new DimensionRange("underwater", ElevatorRound.FloorsPerDimension * 2,
                ElevatorRound.Floors - 1)
        };

        public ElevatorManager Player { get; private set; }
        public ElevatorManager Elevator => Player;
        public PassengerManager Passengers { get; private set; }
        public GameView CurrentView { get; private set; } = GameView.Menu;
        public bool PassengerOverlayVisible { get; private set; }

        public float MainTimer => Player != null ? Player.SecondsLeft : ElevatorRound.Duration;
        public int Score => Player != null ? Player.Points : 0;
        public bool IsPaused => Player != null && Player.IsPaused;

        readonly MatchProgress matchProgress = new MatchProgress();
        public const int RoundsToWin = MatchProgress.RoundsToWin;
        public int RoundNumber => matchProgress.RoundNumber;
        public int ActiveDimension => matchProgress.ActiveDimension;
        public int PlayerOneWins => matchProgress.PlayerOneWins;
        public int PlayerTwoWins => matchProgress.PlayerTwoWins;
        public bool MatchFinished => matchProgress.Finished;
        public bool RoundRecorded => matchProgress.RoundRecorded;
        public int LastRoundWinner => matchProgress.LastRoundWinner;
        public string ActiveDimensionName => DimensionName(ActiveDimension);
        public string NextDimensionName => DimensionName((ActiveDimension + 1) % ElevatorRound.Dimensions);

        static string DimensionName(int dimension) => dimension == 0 ? "OFFICE"
            : dimension == 1 ? "CANDY" : "UNDERWATER";

        public void ResetMatchProgress() => matchProgress.Reset();
        public void RecordRoundResult(int playerOneScore, int playerTwoScore)
            => matchProgress.RecordResult(playerOneScore, playerTwoScore);
        public void AdvanceRound() => matchProgress.AdvanceRound();

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
            foreach (var elevator in FindObjectsByType<ElevatorManager>())
            {
                if (elevator != null && elevator.Seat == 0) { player = elevator; break; }
            }
            BindPlayer(player);
            if (MenuManager.IsOpen) GoToView(GameView.Menu);
            else SyncViewFromPlayer();
        }

        public void BindPlayer(ElevatorManager player)
        {
            Player = player;
            if (player == null) return;
            Passengers = player.GetComponent<PassengerManager>();
            if (Passengers == null)
            {
                Debug.LogWarning("PassengerManager missing on player elevator — add it in Main.");
                Passengers = player.gameObject.AddComponent<PassengerManager>();
            }
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
            var viewedPlayer = Player.IsNpc && Player.Match != null
                && Player.Match.Rival != null && !Player.Match.Rival.IsNpc
                ? Player.Match.Rival : Player;
            if (viewedPlayer.TutorialPassengerViewActive)
            {
                GoToView(GameView.Passenger);
                return;
            }
            GoToView(viewedPlayer.Traveling ? GameView.ControlElevator : GameView.Passenger);
        }

        public void ResetRound()
        {
            PassengerOverlayVisible = false;
            ResetMatchProgress();
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
            if (Player == null) return;
            if (MenuManager.IsOpen)
            {
                GoToView(GameView.Menu);
                return;
            }
            if (!IsPaused) SyncViewFromPlayer();

            var currentDimension = DimensionForFloor(Player.DiscreteFloor);
            // MusicManager.Instance?.SetDimension(currentDimension.name);

            var input = InputManager.Instance;
            if (input == null) return;
            // The tutorial owns Esc (exit) and must not open a hidden pause or overlay.
            if (MenuManager.TutorialActive) return;

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
