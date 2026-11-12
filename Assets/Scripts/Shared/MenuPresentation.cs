using UnityEngine;

namespace CrazyElevator.Shared
{
    public static class MenuPresentation
    {
        public static readonly Color Wall = new Color32(72, 55, 78, 255);
        public static readonly Color Ink = new Color32(50, 36, 45, 255);
        public static readonly Color Paper = new Color32(255, 242, 213, 255);
        public static readonly Color Brass = new Color32(238, 180, 91, 255);
        public static readonly Color Teal = new Color32(104, 182, 168, 255);
        public static readonly Color Coral = new Color32(231, 128, 113, 255);
        static Material textureMaterial;

        public static void ReleaseTextureMaterial()
        {
            if (textureMaterial != null) Object.Destroy(textureMaterial);
            textureMaterial = null;
        }

        public static void DrawTexture(Rect rect, Texture texture, ScaleMode scaleMode = ScaleMode.StretchToFill)
        {
            if (texture == null) return;
            Rect uv = new Rect(0f, 0f, 1f, 1f);
            float aspect = (float)texture.width / texture.height;
            float rectAspect = rect.width / rect.height;
            if (scaleMode == ScaleMode.ScaleToFit)
            {
                Vector2 center = rect.center;
                if (aspect > rectAspect) rect.height = rect.width / aspect;
                else rect.width = rect.height * aspect;
                rect.center = center;
            }
            else if (scaleMode == ScaleMode.ScaleAndCrop)
            {
                if (aspect > rectAspect) uv.width = rectAspect / aspect;
                else uv.height = aspect / rectAspect;
                uv.center = new Vector2(.5f, .5f);
            }
            DrawTextureUV(rect, texture, uv);
        }

        public static void DrawTextureUV(Rect rect, Texture texture, Rect uv)
        {
            if (texture == null || Event.current.type != EventType.Repaint) return;
            if (textureMaterial == null)
                textureMaterial = new Material(Resources.Load<Shader>("MenuTexture"))
                    { hideFlags = HideFlags.HideAndDontSave };
            Graphics.DrawTexture(rect, texture, uv, 0, 0, 0, 0, GUI.color, textureMaterial);
        }

        // Palette values are authored as screen colours. IMGUI tints need linear
        // values when drawing into the project's linear framebuffer.
        public static Color DisplayColor(Color color)
            => QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;

        public static void Paint(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = previous * DisplayColor(color);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static void Plaque(Rect rect, Color face, Color edge, float depth = 6f)
        {
            Paint(new Rect(rect.x, rect.y + depth, rect.width, rect.height), edge);
            Paint(rect, face);
            Paint(new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, 2f),
                new Color(1f, 1f, 1f, .26f));
        }

        public static void Fasteners(Rect rect, Color tint)
        {
            const float inset = 11f;
            const float size = 6f;
            Paint(new Rect(rect.x + inset, rect.y + inset, size, size), tint);
            Paint(new Rect(rect.xMax - inset - size, rect.y + inset, size, size), tint);
            Paint(new Rect(rect.x + inset, rect.yMax - inset - size, size, size), tint);
            Paint(new Rect(rect.xMax - inset - size, rect.yMax - inset - size, size, size), tint);
        }

        public static void Backdrop(Rect view)
        {
            Paint(view, Wall);
            float panel = view.width * .2f;
            float seam = Mathf.Clamp(view.width * .004f, 2f, 8f);
            for (int i = 1; i < 5; i++)
            {
                float x = view.x + i * panel;
                Paint(new Rect(x, view.y, seam, view.height), new Color(0f, 0f, 0f, .18f));
                Paint(new Rect(x + seam, view.y, seam, view.height), new Color(1f, 1f, 1f, .045f));
            }
            Paint(new Rect(view.x, view.yMax - view.height * .09f, view.width, view.height * .09f),
                new Color32(57, 43, 62, 255));
        }

        public static Matrix4x4 Fit(Rect view, float width, float height)
        {
            float scale = Mathf.Min(view.width / width, view.height / height);
            return Matrix4x4.TRS(new Vector3(view.center.x - width * scale * .5f,
                view.center.y - height * scale * .5f, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
        }

        public static void ExcludePreviewLayer(Light[] sceneLights, int layer)
        {
            if (sceneLights == null) return;
            foreach (Light light in sceneLights)
                if (light != null) light.cullingMask &= ~(1 << layer);
        }

        public static void LightPreview(Transform stage, Vector3 center, int layer)
        {
            // Local lights keep distant preview stages from illuminating one another.
            var keyObject = new GameObject("Preview key light") { hideFlags = HideFlags.HideAndDontSave };
            keyObject.transform.SetParent(stage, false);
            keyObject.transform.localPosition = center + new Vector3(-3f, 4f, -6f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Point;
            key.range = 18f;
            key.intensity = 28f;
            key.color = new Color(1f, .97f, .92f);
            key.cullingMask = 1 << layer;

            var fillObject = new GameObject("Preview fill light") { hideFlags = HideFlags.HideAndDontSave };
            fillObject.transform.SetParent(stage, false);
            fillObject.transform.localPosition = center + new Vector3(4f, 1f, -4f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 14f;
            fill.intensity = 4f;
            fill.color = new Color(.86f, .93f, 1f);
            fill.cullingMask = 1 << layer;
        }
    }
}
