using System;

namespace CrazyElevator.Shared
{
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
}
