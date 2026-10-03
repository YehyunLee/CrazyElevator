using UnityEngine;

// Three authored starfish communicate the same rust level in both camera views.
public sealed class StarfishImpairmentView : MonoBehaviour
{
    public Transform[] starfish;
    public Material inactiveMaterial;
    public Material[] severityMaterials;
    int displayedSeverity = -1;
    int removedStarfishCount;
    Vector3[] restingPositions;
    Quaternion[] restingRotations;
    public int DisplayedSeverity => displayedSeverity;

    void RememberRestingPoses()
    {
        if (restingPositions != null && restingPositions.Length == starfish.Length) return;
        restingPositions = new Vector3[starfish.Length];
        restingRotations = new Quaternion[starfish.Length];
        for (int i = 0; i < starfish.Length; i++)
        {
            restingPositions[i] = starfish[i].localPosition;
            restingRotations[i] = starfish[i].localRotation;
        }
    }

    public void SetSeverity(int level)
    {
        level = Mathf.Clamp(level, 0, 3);
        if (level == displayedSeverity) return;
        RememberRestingPoses();
        if (level > 0) removedStarfishCount = 0;
        displayedSeverity = level;
        for (int i = 0; i < starfish.Length; i++)
        {
            starfish[i].localPosition = restingPositions[i];
            starfish[i].localRotation = restingRotations[i];
            foreach (var surface in starfish[i].GetComponentsInChildren<MeshRenderer>(true))
                if (!surface.name.StartsWith("Eye")) surface.sharedMaterial = i < level ? severityMaterials[level - 1] : inactiveMaterial;
            float size = level == 0 && i < removedStarfishCount ? 0 : i < level ? 1 : .72f;
            starfish[i].localScale = Vector3.one * size;
        }
    }

    public void AnimateRemoval(float progress)
    {
        RememberRestingPoses();
        progress = Mathf.Clamp01(progress);
        removedStarfishCount = Mathf.Max(removedStarfishCount, displayedSeverity);
        for (int i = 0; i < starfish.Length; i++)
        {
            if (i >= displayedSeverity) continue;
            float sweep = Mathf.Clamp01(progress * 3f - i);
            starfish[i].localPosition = restingPositions[i] + new Vector3(sweep * .7f, -sweep * .4f, 0);
            starfish[i].localRotation = restingRotations[i] * Quaternion.Euler(0, 0, -sweep * 90f);
            starfish[i].localScale = Vector3.one * (1f - sweep);
        }
    }

    public void ResetRemoval(int level)
    {
        RememberRestingPoses();
        displayedSeverity = -1;
        removedStarfishCount = 0;
        SetSeverity(level);
    }
}
