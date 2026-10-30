using System;

namespace CrazyElevator.Shared
{
    // GameManager owns this match state; elevators keep only the current round score.
    public sealed class MatchProgress
    {
        public const int RoundsToWin = 3;
        public int RoundNumber { get; private set; } = 1;
        public int ActiveDimension => (RoundNumber - 1) % ElevatorRound.Dimensions;
        public int PlayerOneWins { get; private set; }
        public int PlayerTwoWins { get; private set; }
        public bool RoundRecorded { get; private set; }
        public int LastRoundWinner { get; private set; } = -1;
        public bool Finished => PlayerOneWins >= RoundsToWin || PlayerTwoWins >= RoundsToWin;

        public void Reset()
        {
            RoundNumber = 1;
            PlayerOneWins = PlayerTwoWins = 0;
            RoundRecorded = false;
            LastRoundWinner = -1;
        }

        public void RecordResult(int playerOneScore, int playerTwoScore)
        {
            if (RoundRecorded || Finished) return;
            RoundRecorded = true;
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
        Water = 2
    }
}
