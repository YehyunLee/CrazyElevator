using UnityEngine;

namespace CrazyElevator.Shared
{
    // One typography source for IMGUI and world-space TextMesh labels.
    // The font files live under Resources so player builds retain them.
    public static class GameTypography
    {
        const string RegularPath = "Fonts/LemonMilk-Regular";
        const string BoldPath = "Fonts/LemonMilk-Bold";

        static Font regular;
        static Font bold;

        public static Font Regular => regular != null ? regular : regular = Resources.Load<Font>(RegularPath);
        public static Font Bold => bold != null ? bold : bold = Resources.Load<Font>(BoldPath);

        public static void Apply(GUIStyle style, bool useBold = false)
        {
            if (style == null) return;
            Font selected = useBold ? Bold : Regular;
            if (selected == null) return;
            style.font = selected;
            // Use the authored Regular/Bold files instead of synthetic styling.
            style.fontStyle = FontStyle.Normal;
        }

        public static void Apply(TextMesh label, bool useBold = false)
        {
            if (label == null) return;
            Font selected = useBold ? Bold : Regular;
            if (selected == null) return;
            label.font = selected;
            label.fontStyle = FontStyle.Normal;

            // TextMesh keeps the previous font atlas unless its renderer is
            // updated along with the Font reference.
            MeshRenderer renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = selected.material;
        }

        public static void ApplyToSkin(GUISkin skin)
        {
            if (skin != null && Regular != null) skin.font = Regular;
        }
    }
}
