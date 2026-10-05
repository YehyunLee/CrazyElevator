using UnityEngine;

namespace CrazyElevator.Shared
{
    // A small world-space patience meter. The red segment grows from left to
    // right while the green segment shrinks, matching the passenger's clock.
    [DisallowMultipleComponent]
    public sealed class PassengerPatienceBar : MonoBehaviour
    {
        [SerializeField, Min(.2f)] float width = .38f;
        [SerializeField, Min(.02f)] float height = .022f;
        [SerializeField] Color frameColor = new Color32(35, 39, 57, 255);
        [SerializeField] Color patientColor = new Color32(87, 222, 103, 255);
        [SerializeField] Color impatientColor = new Color32(244, 58, 49, 255);

        Camera viewCamera;
        Transform greenFill, redFill;
        TextMesh madLabel;
        Material sharedMaterial;
        bool built;
        bool mad;

        public float DisplayedPatience { get; private set; } = 1f;
        public bool IsMad => mad;

        void Awake()
        {
            BuildVisuals();
            SetState(1f, false);
        }

        public void Bind(Camera cameraToFace)
        {
            viewCamera = cameraToFace;
        }

        public void SetState(float remainingFraction, bool isMad)
        {
            BuildVisuals();
            DisplayedPatience = Mathf.Clamp01(remainingFraction);
            mad = isMad;

            float redFraction = 1f - DisplayedPatience;
            SetSegment(redFill, redFraction, -width * .5f, true);
            SetSegment(greenFill, DisplayedPatience, width * .5f, false);
            madLabel.gameObject.SetActive(isMad);
        }

        void LateUpdate()
        {
            Camera cameraToFace = viewCamera != null ? viewCamera : Camera.main;
            if (cameraToFace != null)
            {
                Vector3 awayFromCamera = transform.position - cameraToFace.transform.position;
                if (awayFromCamera.sqrMagnitude > .0001f)
                    transform.rotation = Quaternion.LookRotation(awayFromCamera, cameraToFace.transform.up);
                float distanceScale = Mathf.Clamp(awayFromCamera.magnitude / 5f, .38f, .72f);
                transform.localScale = Vector3.one * distanceScale;
            }

            if (madLabel != null && mad)
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 9f) * .08f;
                madLabel.transform.localScale = Vector3.one * pulse;
            }
        }

        void SetSegment(Transform segment, float fraction, float edge, bool anchoredLeft)
        {
            bool visible = fraction > .001f;
            segment.gameObject.SetActive(visible);
            if (!visible) return;

            float segmentWidth = width * fraction;
            segment.localScale = new Vector3(segmentWidth, height, .018f);
            segment.localPosition = new Vector3(
                anchoredLeft ? edge + segmentWidth * .5f : edge - segmentWidth * .5f,
                0,
                -.021f);
        }

        void BuildVisuals()
        {
            if (built) return;
            built = true;

            sharedMaterial = Resources.Load<Material>("GameJam2Lit");
            if (sharedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                sharedMaterial = new Material(shader);
            }

            Renderer frame = CreatePart("Frame", new Vector3(0, 0, 0),
                new Vector3(width + .06f, height + .045f, .032f), out _);
            SetColor(frame, frameColor);

            Renderer greenRenderer = CreatePart("Patient (green)", Vector3.zero, Vector3.one, out greenFill);
            Renderer redRenderer = CreatePart("Impatient (red)", Vector3.zero, Vector3.one, out redFill);
            SetColor(greenRenderer, patientColor);
            SetColor(redRenderer, impatientColor);

            GameObject labelObject = new GameObject("MAD label");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0, .075f, -.04f);
            madLabel = labelObject.AddComponent<TextMesh>();
            madLabel.text = "MAD!";
            madLabel.fontSize = 64;
            madLabel.characterSize = .009f;
            madLabel.anchor = TextAnchor.LowerCenter;
            madLabel.alignment = TextAlignment.Center;
            madLabel.color = impatientColor;
            GameTypography.Apply(madLabel, true);
        }

        Renderer CreatePart(string partName, Vector3 position, Vector3 scale, out Transform part)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = partName;
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = scale;
            Collider hitbox = visual.GetComponent<Collider>();
            if (hitbox != null)
            {
                hitbox.enabled = false;
                Destroy(hitbox);
            }
            Renderer renderer = visual.GetComponent<Renderer>();
            renderer.sharedMaterial = sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            part = visual.transform;
            return renderer;
        }

        static void SetColor(Renderer renderer, Color color)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }
    }
}
