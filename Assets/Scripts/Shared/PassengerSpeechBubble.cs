using System.Text;
using UnityEngine;

namespace CrazyElevator.Shared
{
    // Lightweight world-space speech bubble created from project-native shapes.
    [DisallowMultipleComponent]
    public sealed class PassengerSpeechBubble : MonoBehaviour
    {
        [SerializeField, Min(.3f)] float width = .82f;
        [SerializeField, Min(.12f)] float height = .27f;
        [SerializeField] Color bubbleColor = new Color32(255, 250, 234, 255);
        [SerializeField] Color outlineColor = new Color32(43, 48, 78, 255);
        [SerializeField] Color textColor = new Color32(43, 48, 78, 255);

        Camera viewCamera;
        TextMesh label;
        Transform tailOutlinePart, tailPart;
        Material sharedMaterial;
        bool built;

        void Awake()
        {
            BuildVisuals();
        }

        public void Bind(Camera cameraToFace)
        {
            viewCamera = cameraToFace;
        }

        public void Show(string message)
        {
            BuildVisuals();
            label.text = Wrap(message, 14);
            gameObject.SetActive(true);
        }

        // side < 0 means the bubble sits to the person's left, so its tail is
        // placed on the right edge (and vice versa).
        public void SetSide(float side)
        {
            float tailX = -Mathf.Sign(side) * width * .27f;
            if (tailOutlinePart != null) tailOutlinePart.localPosition = new Vector3(tailX, -height * .59f, 0);
            if (tailPart != null) tailPart.localPosition = new Vector3(tailX, -height * .58f, -.026f);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            Camera cameraToFace = viewCamera != null ? viewCamera : Camera.main;
            if (cameraToFace == null) return;
            Vector3 awayFromCamera = transform.position - cameraToFace.transform.position;
            if (awayFromCamera.sqrMagnitude > .0001f)
                transform.rotation = Quaternion.LookRotation(awayFromCamera, cameraToFace.transform.up);
            // Near passengers otherwise produce enormous bubbles because of
            // perspective. Keep their screen footprint compact.
            float distanceScale = Mathf.Clamp(awayFromCamera.magnitude / 5f, .46f, .9f);
            transform.localScale = Vector3.one * distanceScale;
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

            Renderer outline = CreatePart("Speech bubble outline", Vector3.zero,
                new Vector3(width + .06f, height + .06f, .032f));
            SetColor(outline, outlineColor);
            Renderer surface = CreatePart("Speech bubble surface", new Vector3(0, 0, -.025f),
                new Vector3(width, height, .022f));
            SetColor(surface, bubbleColor);

            Renderer tailOutline = CreatePart("Speech bubble tail outline", new Vector3(-.11f, -height * .59f, 0),
                new Vector3(.075f, .075f, .031f));
            tailOutlinePart = tailOutline.transform;
            tailOutline.transform.localRotation = Quaternion.Euler(0, 0, 45);
            SetColor(tailOutline, outlineColor);
            Renderer tail = CreatePart("Speech bubble tail", new Vector3(-.11f, -height * .58f, -.026f),
                new Vector3(.05f, .05f, .021f));
            tailPart = tail.transform;
            tail.transform.localRotation = Quaternion.Euler(0, 0, 45);
            SetColor(tail, bubbleColor);

            GameObject textObject = new GameObject("Speech");
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition = new Vector3(0, 0, -.045f);
            label = textObject.AddComponent<TextMesh>();
            label.fontSize = 72;
            label.characterSize = .0145f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = textColor;
            GameTypography.Apply(label, true);
        }

        Renderer CreatePart(string partName, Vector3 position, Vector3 scale)
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
            return renderer;
        }

        static void SetColor(Renderer renderer, Color color)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            renderer.SetPropertyBlock(properties);
        }

        static string Wrap(string message, int lineLength)
        {
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;
            string clean = message.Trim().Trim('"');
            string[] words = clean.Split(' ');
            var result = new StringBuilder();
            int currentLine = 0;
            foreach (string word in words)
            {
                if (currentLine > 0 && currentLine + 1 + word.Length > lineLength)
                {
                    result.Append('\n');
                    currentLine = 0;
                }
                else if (currentLine > 0)
                {
                    result.Append(' ');
                    currentLine++;
                }
                result.Append(word);
                currentLine += word.Length;
            }
            return result.ToString();
        }
    }
}
