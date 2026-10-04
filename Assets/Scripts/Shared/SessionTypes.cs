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

    // Office → candy → underwater. Used by cabin décor, hall fronts, and exterior bands.
    public enum WorldBand
    {
        Office = 0,
        Candy = 1,
        Water = 2
    }
}
