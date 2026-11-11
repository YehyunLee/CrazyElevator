using System;

namespace CrazyElevator.Shared
{
    // GameManager owns this match state; elevators keep only the current round score.
    public sealed class MatchProgress
    {
        public const int MaximumRounds = 3;
        public const int RoundsToWin = MaximumRounds / 2 + 1;
        int[] dimensionOrder = { 0, 1, 2, 3, 4 };
        public int RoundNumber { get; private set; } = 1;
        public int ActiveDimension => DimensionForRound(RoundNumber - 1);
        public int PlayerOneWins { get; private set; }
        public int PlayerTwoWins { get; private set; }
        public int PlayerOneTotalPoints { get; private set; }
        public int PlayerTwoTotalPoints { get; private set; }
        public bool RoundRecorded { get; private set; }
        public int LastRoundWinner { get; private set; } = -1;
        public bool Finished => PlayerOneWins >= RoundsToWin || PlayerTwoWins >= RoundsToWin
            || RoundRecorded && RoundNumber >= MaximumRounds;
        public int MatchWinner => PlayerOneWins != PlayerTwoWins
            ? PlayerOneWins > PlayerTwoWins ? 0 : 1
            : PlayerOneTotalPoints == PlayerTwoTotalPoints ? -1
            : PlayerOneTotalPoints > PlayerTwoTotalPoints ? 0 : 1;

        public int DimensionForRound(int roundOffset)
            => dimensionOrder[Math.Abs(roundOffset) % dimensionOrder.Length];

        public void SetDimensionOrder(int[] order)
        {
            if (order == null || order.Length == 0)
                throw new ArgumentException("At least one match dimension is required.", nameof(order));
            dimensionOrder = (int[])order.Clone();
        }

        public void Reset()
        {
            RoundNumber = 1;
            PlayerOneWins = PlayerTwoWins = 0;
            PlayerOneTotalPoints = PlayerTwoTotalPoints = 0;
            RoundRecorded = false;
            LastRoundWinner = -1;
        }

        public void RecordResult(int playerOneScore, int playerTwoScore)
        {
            if (RoundRecorded || Finished) return;
            RoundRecorded = true;
            PlayerOneTotalPoints += playerOneScore;
            PlayerTwoTotalPoints += playerTwoScore;
            LastRoundWinner = playerOneScore == playerTwoScore ? -1 : playerOneScore > playerTwoScore ? 0 : 1;
            if (LastRoundWinner == 0) PlayerOneWins++;
            if (LastRoundWinner == 1) PlayerTwoWins++;
        }

        public void AdvanceRound()
        {
            if (Finished) return;
            RoundNumber++;
            RoundRecorded = false;
            LastRoundWinner = -1;
        }
    }

    // Which input map InputManager should use.
    public enum GameView
    {
        Menu,
        Pause,
        ControlElevator,
        Passenger
    }

    [Serializable]
    public struct DimensionRange
    {
        public string name;
        public int firstFloor;
        public int lastFloor;

        public DimensionRange(string name, int firstFloor, int lastFloor)
        {
            this.name = name;
            this.firstFloor = firstFloor;
            this.lastFloor = lastFloor;
        }

        public bool Contains(int floor) => floor >= firstFloor && floor <= lastFloor;
    }

    // Office → candy → underwater. Used by cabin décor, hall fronts, and exterior bands.
    public enum WorldBand
    {
        Office = 0,
        Candy = 1,
        Water = 2,
        Forest = 3,
        Space = 4
    }
}
