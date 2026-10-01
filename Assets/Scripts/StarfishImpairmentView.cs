using UnityEngine;

// Three authored starfish communicate the same rust level in both camera views.
public sealed class StarfishImpairmentView : MonoBehaviour
{
    public Transform[] starfish;
    public Material inactiveMaterial;
    public Material[] severityMaterials;
    int displayedSeverity = -1;
    public int DisplayedSeverity => displayedSeverity;

    public void SetSeverity(int level)
    {
        level = Mathf.Clamp(level, 0, 3);
        if (level == displayedSeverity) return;
        displayedSeverity = level;
        for (int i = 0; i < starfish.Length; i++)
        {
            foreach (var surface in starfish[i].GetComponentsInChildren<MeshRenderer>(true))
                if (!surface.name.StartsWith("Eye")) surface.sharedMaterial = i < level ? severityMaterials[level - 1] : inactiveMaterial;
            starfish[i].localScale = Vector3.one * (i < level ? 1 : .72f);
        }
    }
}
