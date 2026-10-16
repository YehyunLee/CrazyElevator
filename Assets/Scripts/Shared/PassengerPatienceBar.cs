using UnityEngine;

namespace CrazyElevator.Shared
{
    // A small world-space patience meter. Green is the remaining patience on
    // the left; the red danger segment grows from the right as it runs out.
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
        SpriteRenderer madIcon;
        Sprite madSprite;
        Texture2D madTexture;
        Material sharedMaterial;
        Vector3 anchorLocalPosition;
        float cameraOffset;
        bool built;
        bool mad;

        public float DisplayedPatience { get; private set; } = 1f;
        public bool IsMad => mad;

        void Awake()
        {
            BuildVisuals();
            SetState(1f, false);
        }

        public void Bind(Camera cameraToFace, float offsetTowardCamera)
        {
            viewCamera = cameraToFace;
            anchorLocalPosition = transform.localPosition;
            cameraOffset = Mathf.Max(0f, offsetTowardCamera);
        }

        public void SetState(float remainingFraction, bool isMad)
        {
            BuildVisuals();
            DisplayedPatience = Mathf.Clamp01(remainingFraction);
            mad = isMad;

            float redFraction = 1f - DisplayedPatience;
            SetSegment(greenFill, DisplayedPatience, -width * .5f, true);
            SetSegment(redFill, redFraction, width * .5f, false);
            madIcon.gameObject.SetActive(isMad);
        }

        void LateUpdate()
        {
            Camera cameraToFace = viewCamera != null ? viewCamera : Camera.main;
            if (cameraToFace != null)
            {
                if (cameraOffset > 0f && transform.parent != null)
                {
                    Vector3 anchor = transform.parent.TransformPoint(anchorLocalPosition);
                    Vector3 towardCamera = cameraToFace.transform.position - anchor;
                    towardCamera.y = 0f;
                    transform.position = anchor + towardCamera.normalized * cameraOffset;
                }
                Vector3 awayFromCamera = transform.position - cameraToFace.transform.position;
                if (awayFromCamera.sqrMagnitude > .0001f)
                    transform.rotation = Quaternion.LookRotation(awayFromCamera, cameraToFace.transform.up);
                float distanceScale = Mathf.Clamp(awayFromCamera.magnitude / 5f, .38f, .72f);
                transform.localScale = Vector3.one * distanceScale;
            }

            if (madIcon != null && mad)
            {
                float pulse = 1f + Mathf.Sin(Time.unscaledTime * 9f) * .08f;
                madIcon.transform.localScale = Vector3.one * (.42f * pulse);
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

            GameObject iconObject = new GameObject("MAD icon");
            iconObject.transform.SetParent(transform, false);
            iconObject.transform.localPosition = new Vector3(0, .10f, -.04f);
            iconObject.transform.localScale = Vector3.one * .42f;
            madIcon = iconObject.AddComponent<SpriteRenderer>();
            madTexture = PassengerIconLibrary.CreateAngryFace(impatientColor, frameColor);
            madSprite = Sprite.Create(madTexture, new Rect(0, 0, madTexture.width, madTexture.height),
                new Vector2(.5f, .5f), madTexture.width);
            madSprite.name = "MAD icon sprite";
            madIcon.sprite = madSprite;
            madIcon.sortingOrder = 1;
            madIcon.gameObject.SetActive(false);
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

        void OnDestroy()
        {
            if (madSprite != null) Destroy(madSprite);
            if (madTexture != null) Destroy(madTexture);
        }
    }
}
