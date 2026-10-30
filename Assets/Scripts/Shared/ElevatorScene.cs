using UnityEngine;

namespace CrazyElevator.Shared
{
    // Scene wiring only. Edit the referenced GameObjects, not geometry code.
    public sealed class ElevatorScene : MonoBehaviour
    {
        [Header("Cabin")]
        public Transform cabin;
        public Transform leftDoor, rightDoor, passingLights;
        [Header("Side boarding doors")]
        public Transform leftSideDoor, rightSideDoor;
        public Vector3 leftSideDoorClosed = new Vector3(-2.9f, 1.5f, 2.58f);
        public Vector3 rightSideDoorClosed = new Vector3(2.9f, 1.5f, 2.58f);
        public float sideDoorTravel = 3.2f;
        [Header("Door selection")]
        public Renderer[] frontDoorFrame;
        public Renderer[] leftDoorFrame;
        public Renderer[] rightDoorFrame;
        public TextMesh floorDisplay;
        // Nine editable labels, ordered front-to-back and left-to-right.
        public TextMesh[] gridDestinations;
        public Shader gridLabelShader;
        public TextMesh[] controlDisplays;
        // Doors fill the doorway (jambs at ±2.675, threshold top .13, header 2.95). The panels overlap
        // at the centre, offset .02 in depth so the overlap doesn't z-fight.
        public Vector3 leftDoorClosed = new Vector3(-1.335f, 1.54f, .08f);
        public Vector3 rightDoorClosed = new Vector3(1.335f, 1.54f, .06f);
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

        [Header("Shaft hazards (moved by ElevatorManager.Hazards)")]
        public GameObject tornado;
        public GameObject monster;
    }
}
