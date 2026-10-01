using UnityEngine;

namespace CrazyElevator.Shared
{
    // Put this on a clickable button. Its label/shape can be changed freely.
    [RequireComponent(typeof(Collider))]
    public sealed class ElevatorButton : MonoBehaviour
    {
        public enum Action { Floor, Open, Close }
        public Action action;
        [Range(0, 11)] public int floor;
        public Renderer highlight;
    }
}
