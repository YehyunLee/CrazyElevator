using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CrazyElevator.Shared;
using SharedRider = CrazyElevator.Shared.Rider;

namespace CrazyElevator.Managers
{
    // Liam GameManager: pause, timer/score, view mode, reset, dimensions.
    [DefaultExecutionOrder(-500)]
    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public const int MaxNumberOfFloors = ElevatorRound.Floors;
        const string DimensionPoolPreference = "CrazyElevator.DimensionPool.Mask";
        const int DefaultDimensionPoolMask = (1 << ElevatorRound.Dimensions) - 1;

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
        const float AbilityIntroSeconds = 3.5f;
        readonly HashSet<string> introducedAbilities = new HashSet<string>();
        readonly Queue<(ElevatorManager actor, string kind, SharedRider rider)> pendingAbilityIntros =
            new Queue<(ElevatorManager actor, string kind, SharedRider rider)>();
        ElevatorManager abilityIntroActor;
        string abilityIntroKind;
        bool abilityIntroActive;
        float abilityIntroTime, abilityIntroPreviousTimeScale = 1f;
        public bool AbilityIntroActive => abilityIntroActive;
        public const int RoundsToWin = MatchProgress.RoundsToWin;
        public int RoundNumber => matchProgress.RoundNumber;
        public int ActiveDimension => DimensionForRound(matchProgress.RoundNumber - 1);
        public int PlayerOneWins => matchProgress.PlayerOneWins;
        public int PlayerTwoWins => matchProgress.PlayerTwoWins;
        public bool MatchFinished => matchProgress.Finished;
        public bool RoundRecorded => matchProgress.RoundRecorded;
        public int LastRoundWinner => matchProgress.LastRoundWinner;
        public string ActiveDimensionName => DimensionDisplayName(ActiveDimension);
        public int FirstEnabledDimension => DimensionForRound(0);
        public int NextDimensionIndex => DimensionForRound(matchProgress.RoundNumber);
        public string NextDimensionName => DimensionDisplayName(NextDimensionIndex);

        public static string DimensionDisplayName(int dimension) => dimension == 0 ? "OFFICE"
            : dimension == 1 ? "CANDY" : "UNDERWATER";

        public bool IsDimensionEnabled(int dimension)
        {
            if (dimension < 0 || dimension >= ElevatorRound.Dimensions) return false;
            return (CurrentDimensionPoolMask & (1 << dimension)) != 0;
        }

        public int EnabledDimensionCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < ElevatorRound.Dimensions; i++)
                    if (IsDimensionEnabled(i)) count++;
                return count;
            }
        }

        int CurrentDimensionPoolMask
        {
            get
            {
                int mask = PlayerPrefs.GetInt(DimensionPoolPreference, DefaultDimensionPoolMask)
                    & DefaultDimensionPoolMask;
                return mask == 0 ? 1 : mask;
            }
        }

        public bool SetDimensionEnabled(int dimension, bool enabled)
        {
            if (dimension < 0 || dimension >= ElevatorRound.Dimensions) return false;
            int mask = CurrentDimensionPoolMask;
            int next = enabled ? mask | (1 << dimension) : mask & ~(1 << dimension);
            if (next == 0) return false;
            PlayerPrefs.SetInt(DimensionPoolPreference, next);
            PlayerPrefs.Save();
            return true;
        }

        int DimensionForRound(int roundOffset)
        {
            int mask = CurrentDimensionPoolMask;
            int enabledCount = 0;
            for (int i = 0; i < ElevatorRound.Dimensions; i++)
                if ((mask & (1 << i)) != 0) enabledCount++;

            int target = Mathf.Abs(roundOffset) % Mathf.Max(1, enabledCount);
            for (int i = 0; i < ElevatorRound.Dimensions; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                if (target-- == 0) return i;
            }
            return 0;
        }

        public void ResetMatchProgress()
        {
            StopAbilityIntro();
            pendingAbilityIntros.Clear();
            introducedAbilities.Clear();
            matchProgress.Reset();
        }

        public void RequestAbilityIntro(ElevatorManager actor, string kind)
        {
            if (actor == null || MenuManager.TutorialActive || string.IsNullOrEmpty(kind)) return;
            // Only show personal effects for a visible player pane.
            if (kind != "BOUNCER" && actor.Match != null && !actor.Match.IsVisibleSeat(actor.Seat)) return;
            string seenKey = kind != "BOUNCER" && actor.Match != null && actor.Match.SplitScreen
                ? kind + ":" + actor.Seat : kind;
            if (!introducedAbilities.Add(seenKey)) return;
            pendingAbilityIntros.Enqueue((actor, kind, null));
            if (!AbilityIntroActive) StartNextAbilityIntro();
        }

        // The VIP is shared by both players and changes every round, so it is
        // intentionally not part of the per-match ability-introduction set.
        public void RequestVipIntro(ElevatorManager actor, SharedRider rider)
        {
            if (actor == null || rider == null || MenuManager.TutorialActive) return;
            pendingAbilityIntros.Enqueue((actor, "VIP", rider));
            if (!AbilityIntroActive) StartNextAbilityIntro();
        }

        void StartNextAbilityIntro()
        {
            while (pendingAbilityIntros.Count > 0)
            {
                var next = pendingAbilityIntros.Dequeue();
                if (next.actor == null) continue;
                try
                {
                    if (!next.actor.PrepareAbilityIntro(next.kind, next.rider)) continue;
                }
                catch (System.Exception error)
                {
                    Debug.LogWarning("Could not show " + next.kind + " ability introduction: " + error.Message);
                    next.actor.FinishAbilityIntro();
                    continue;
                }
                abilityIntroActor = next.actor;
                abilityIntroKind = next.kind;
                abilityIntroActive = true;
                abilityIntroTime = 0f;
                abilityIntroPreviousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                return;
            }
        }

        void StopAbilityIntro()
        {
            if (!abilityIntroActive) return;
            if (abilityIntroActor != null) abilityIntroActor.FinishAbilityIntro();
            abilityIntroActor = null;
            abilityIntroKind = null;
            abilityIntroActive = false;
            Time.timeScale = abilityIntroPreviousTimeScale;
        }
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
            StopAbilityIntro();
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
            var viewedPlayer = Player;
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
            if (AbilityIntroActive)
            {
                if (abilityIntroActor == null)
                {
                    StopAbilityIntro();
                    StartNextAbilityIntro();
                    return;
                }
                abilityIntroTime += Time.unscaledDeltaTime;
                abilityIntroActor.AnimateAbilityIntro(abilityIntroTime);
                if (abilityIntroTime >= (abilityIntroKind == "VIP" ? ElevatorManager.VipIntroDuration : abilityIntroKind == "HANDYMAN" ? 1.6f : AbilityIntroSeconds))
                {
                    StopAbilityIntro();
                    StartNextAbilityIntro();
                }
                return;
            }
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

        void OnGUI()
        {
            if (AbilityIntroActive && abilityIntroActor != null)
            {
                var match = abilityIntroActor.Match;
                bool shared = abilityIntroKind == "VIP" || abilityIntroKind == "BOUNCER" || match == null || !match.SplitScreen;
                abilityIntroActor.DrawAbilityIntro(abilityIntroTime, AbilityIntroView(),
                    shared && match != null && abilityIntroKind != "VIP", !shared);
            }
        }

        Rect AbilityIntroView()
        {
            var match = abilityIntroActor != null ? abilityIntroActor.Match : null;
            return abilityIntroKind == "VIP" || abilityIntroKind == "BOUNCER" || match == null || !match.SplitScreen
                ? new Rect(0f, 0f, Screen.width, Screen.height)
                : match.ViewRect(abilityIntroActor.Seat);
        }
    }
}
