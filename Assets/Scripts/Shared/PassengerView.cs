using UnityEngine;

namespace CrazyElevator.Shared
{
    // Replace the model children; preserve the click collider and these labels.
    public sealed class PassengerView : MonoBehaviour
    {
        public string kind;
        public PassengerData data;
        public TextMesh speech, destination;
        public Renderer[] jackets;
        [Min(0f)] public float patienceBarHeight = 1.88f;
        [Min(0f)] public float patienceBarCameraOffset;

        public string ResolvedKind => data != null && !string.IsNullOrEmpty(data.kind) ? data.kind : kind;

        void Awake()
        {
            GameTypography.Apply(speech);
            GameTypography.Apply(destination, true);
        }
    }
}
