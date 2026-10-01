using UnityEngine;

namespace CrazyElevator.Shared
{
    // Scene wiring only. Edit the referenced GameObjects, not geometry code.
    public sealed class ElevatorScene : MonoBehaviour
    {
        [Header("Cabin")]
        public Transform cabin;
        public Transform leftDoor, rightDoor, passingLights;
        public TextMesh floorDisplay;
        public TextMesh[] controlDisplays;
        public Vector3 leftDoorClosed = new Vector3(-1.32f, 1.35f, .08f);
        public Vector3 rightDoorClosed = new Vector3(1.32f, 1.35f, .08f);
        public float doorTravel = 2.7f;

        [Header("Cameras and lights")]
        public Camera cabinCamera;
        public Camera exteriorCamera;
        public Camera clearCamera;
        public Light[] lights;

        [Header("Exterior")]
        public Transform exteriorCar;

        [Header("Passenger art (one prefab per kind)")]
        public PassengerView[] passengers;
    }
}
