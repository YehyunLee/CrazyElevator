using System.Collections.Generic;
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

        internal void ApplyMatteFinish(Dictionary<Material, Material> finishes)
        {
            Material template = Resources.Load<Material>("TransitFinishes/PassengerFinish");
            if (template == null) return;

            foreach (Renderer part in GetComponentsInChildren<Renderer>(true))
            {
                if (!(part is MeshRenderer || part is SkinnedMeshRenderer)
                    || part.TryGetComponent<TextMesh>(out _)) continue;

                Material[] slots = part.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (!CanRefinish(source)) continue;
                    if (!finishes.TryGetValue(source, out Material finish))
                    {
                        finish = new Material(template) { name = source.name + " (matte finish)" };
                        // Hold on to the model's authored palette, with a small
                        // warm shift so fabric, hair and skin share the cabin light.
                        Color tint = source.color;
                        tint.r *= .99f;
                        tint.g *= .97f;
                        tint.b *= .94f;
                        finish.color = tint;
                        finishes.Add(source, finish);
                    }
                    slots[i] = finish;
                    changed = true;
                }
                if (changed) part.sharedMaterials = slots;
            }
        }

        static bool CanRefinish(Material material)
        {
            if (material == null || material.shader == null || material.renderQueue >= 2450
                || material.IsKeywordEnabled("_ALPHATEST_ON") || material.IsKeywordEnabled("_EMISSION"))
                return false;

            string shaderName = material.shader.name;
            if (shaderName != "Universal Render Pipeline/Lit"
                && shaderName != "Universal Render Pipeline/Simple Lit"
                && shaderName != "Standard") return false;

            // Keep artwork with hand painted maps or normal maps in its authored shader.
            return !HasTexture(material, "_BaseMap") && !HasTexture(material, "_MainTex")
                && !HasTexture(material, "_BumpMap") && !HasTexture(material, "_DetailAlbedoMap");
        }

        static bool HasTexture(Material material, string property)
        {
            if (!material.HasProperty(property)) return false;
            Texture map = material.GetTexture(property);
            return map != null && map != Texture2D.whiteTexture;
        }

        void Awake()
        {
            GameTypography.Apply(speech);
            GameTypography.Apply(destination, true);
        }
    }
}
