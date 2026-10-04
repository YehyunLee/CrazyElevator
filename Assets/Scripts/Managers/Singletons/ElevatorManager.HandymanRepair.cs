using System.Collections.Generic;
using CrazyElevator.Shared;
using UnityEngine;

namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;

    public sealed partial class ElevatorManager
    {
        const float RepairSweepStart = 1.6f;
        const float RepairSweepEnd = 3.2f;
        const float HandymanRepairDuration = 4.2f;

        [Header("Handyman cleaning animation")]
        public Transform handymanShovelPrefab;

        Rider repairingHandyman;
        float handymanRepairTime;
        int repairStarfishLevel;
        bool handymanRepairFinished;
        Vector3 repairHome, repairScale;
        Transform repairToolPivot, repairShovel;
        readonly List<RepairPartPose> repairPartPoses = new List<RepairPartPose>();
        string elevatorSpeech;
        float elevatorSpeechTime;

        sealed class RepairPartPose
        {
            public Transform part, parent;
            public Vector3 position, scale;
            public Quaternion rotation;
            public bool active;
        }

        bool HandymanRepairActive => repairingHandyman != null;
        bool HandymanRepairPending => HandymanRepairActive && !handymanRepairFinished;
        // Boarding changes HasHandyman immediately, so use the last visible level.
        int VisibleStarfishLevel => interiorImpairment && interiorImpairment.gameObject.activeInHierarchy
            ? Mathf.Max(0, interiorImpairment.DisplayedSeverity) : 0;

        // Called only after a passenger has actually boarded, by either input path.
        void BeginHandymanRepair(Rider rider)
        {
            if (!extendedInterior || rider == null || !rider.HasFeature(PassengerFeature.ClearsRust)
                || !rider.Boarded || HandymanRepairActive || !figures.ContainsKey(rider)
                || VisibleStarfishLevel == 0) return;

            repairingHandyman = rider;
            handymanRepairTime = 0;
            handymanRepairFinished = false;
            repairStarfishLevel = VisibleStarfishLevel;
            repairHome = RiderPosition(rider);
            repairScale = boardingTransfers.TryGetValue(rider, out var transfer)
                ? transfer.targetScale : figures[rider].localScale;
            repairProgress = 0;
            ElevatorSays("Those starfish damaged my gears. Please fix me!", 5f);
        }

        bool AnimateHandymanRepair(Rider rider, Transform figure, float dt)
        {
            if (rider != repairingHandyman) return false;
            if (!rider.Boarded || rider.Resolved)
            {
                ClearHandymanRepair();
                return false;
            }
            if (!repairToolPivot) BuildRepairTools(figure);

            handymanRepairTime += Mathf.Max(0, dt);
            float t = handymanRepairTime;
            Vector3 work = new Vector3(-.85f, .12f, .95f);
            float arrive = Mathf.SmoothStep(0, 1, t / .5f);
            float leave = Mathf.SmoothStep(0, 1, (t - RepairSweepEnd) / (HandymanRepairDuration - RepairSweepEnd));
            figure.localPosition = Vector3.Lerp(Vector3.Lerp(repairHome, work, arrive), repairHome, leave);
            figure.localScale = Vector3.Lerp(repairScale, Vector3.one, arrive * (1 - leave));
            float working = t >= .5f && t < RepairSweepStart ? Mathf.Sin((t - .5f) * 18f) : 0;
            figure.localRotation = Quaternion.Euler(working * 4, 0, working * 3);
            repairToolPivot.localRotation = Quaternion.Euler(-35 - working * 25, 0, -15);

            bool sweeping = t >= RepairSweepStart && t < RepairSweepEnd;
            if (repairShovel) repairShovel.gameObject.SetActive(sweeping);
            foreach (var pose in repairPartPoses)
                if (pose.part && pose.part.name.StartsWith("Wrench")) pose.part.gameObject.SetActive(!sweeping);

            if (sweeping)
            {
                float sweep = Mathf.Clamp01((t - RepairSweepStart) / (RepairSweepEnd - RepairSweepStart));
                if (repairShovel)
                {
                    Vector3 grip = figure.TransformPoint(new Vector3(-.3f, .9f, -.08f));
                    // The blade follows the three stars from left to right, then flicks them clear.
                    Vector3 tip = stage.TransformPoint(new Vector3(Mathf.Lerp(-.62f, 1.15f, sweep),
                        2.2f + Mathf.Sin(sweep * Mathf.PI) * .12f, .6f));
                    repairShovel.position = grip;
                    repairShovel.rotation = Quaternion.FromToRotation(Vector3.up, (tip - grip).normalized);
                    repairShovel.localScale = new Vector3(1, (tip - grip).magnitude / 1.1f, 1);
                }
                if (interiorImpairment) interiorImpairment.AnimateRemoval(sweep);
                if (exteriorImpairment) exteriorImpairment.AnimateRemoval(sweep);
            }

            notice = t < RepairSweepStart ? "Casey is tightening the gears..."
                : t < RepairSweepEnd ? "Casey is shovelling the starfish off!"
                : "All fixed! Full power while Casey is aboard.";
            if (t >= RepairSweepEnd && !handymanRepairFinished)
            {
                // Finish the sweep even when this frame steps across its endpoint.
                if (interiorImpairment) interiorImpairment.AnimateRemoval(1);
                if (exteriorImpairment) exteriorImpairment.AnimateRemoval(1);
                handymanRepairFinished = true;
                UpdateImpairmentIndicators();
                ElevatorSays("Ahh, much better. Starfish gone, gears fixed. Thanks, Casey!", 5f);
                Play(ding);
            }
            if (t >= HandymanRepairDuration)
            {
                RestoreHandymanPose();
                repairingHandyman = null;
                SetControlStatus("REPAIRED");
            }
            return true;
        }

        void BuildRepairTools(Transform figure)
        {
            // The visible wrench and arm are authored children of the passenger prefab.
            repairToolPivot = new GameObject("Handyman wrench motion").transform;
            repairToolPivot.SetParent(figure, false);
            repairToolPivot.localPosition = new Vector3(-.3f, 1.04f, 0);
            var parts = new List<Transform>();
            foreach (Transform part in figure)
                if (part.name.StartsWith("Wrench") || part.name == "Arm" && part.localPosition.x < 0)
                    parts.Add(part);
            foreach (var part in parts)
            {
                repairPartPoses.Add(new RepairPartPose
                {
                    part = part, parent = part.parent, position = part.localPosition,
                    rotation = part.localRotation, scale = part.localScale,
                    active = part.gameObject.activeSelf
                });
                part.SetParent(repairToolPivot, true);
            }

            // With scene reload disabled, an already-open editor scene can keep
            // an empty Inspector field after Main.unity gains this reference.
#if UNITY_EDITOR
            if (!handymanShovelPrefab)
            {
                var shovelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Static/ElevatorPersonas/Prefabs/HandymanShovel.prefab");
                if (shovelAsset)
                {
                    handymanShovelPrefab = shovelAsset.transform;
                    Debug.LogWarning("The open scene had no Handyman Shovel reference. Using the authored prefab for this play session; reopen Main to refresh its Inspector reference.", this);
                }
            }
#endif
            if (!handymanShovelPrefab)
            {
                Debug.LogError("Assign the Handyman Shovel prefab on the elevator manager.", this);
                return;
            }
            repairShovel = Instantiate(handymanShovelPrefab, stage, false);
            repairShovel.name = "Handyman starfish shovel";
            repairShovel.gameObject.SetActive(false);
        }

        void RestoreHandymanPose()
        {
            foreach (var pose in repairPartPoses)
            {
                if (!pose.part) continue;
                pose.part.SetParent(pose.parent, false);
                pose.part.localPosition = pose.position;
                pose.part.localRotation = pose.rotation;
                pose.part.localScale = pose.scale;
                pose.part.gameObject.SetActive(pose.active);
            }
            repairPartPoses.Clear();
            if (repairToolPivot) Destroy(repairToolPivot.gameObject);
            if (repairShovel) Destroy(repairShovel.gameObject);
            repairToolPivot = repairShovel = null;
            if (repairingHandyman != null && figures.TryGetValue(repairingHandyman, out var figure) && figure)
            {
                figure.localPosition = repairHome;
                figure.localScale = repairScale;
                figure.localRotation = Quaternion.identity;
            }
        }

        void ClearHandymanRepair()
        {
            RestoreHandymanPose();
            repairingHandyman = null;
            handymanRepairTime = 0;
            handymanRepairFinished = false;
            elevatorSpeechTime = 0;
            if (interiorImpairment) interiorImpairment.ResetRemoval(ImpairmentLevel);
            if (exteriorImpairment) exteriorImpairment.ResetRemoval(ImpairmentLevel);
        }

        void ElevatorSays(string message, float duration)
        {
            elevatorSpeech = message;
            elevatorSpeechTime = duration;
        }

        void DrawElevatorSpeech(float screenHeight, float screenWidth)
        {
            if (BuildingView || elevatorSpeechTime <= 0 || string.IsNullOrEmpty(elevatorSpeech)) return;
            float width = Mathf.Min(520, screenWidth - 24);
            float x = (screenWidth - width) * .5f;
            float y = screenHeight - 210;
            Panel(new Rect(x, y, width, 84), new Color(Ink.r, Ink.g, Ink.b, .96f));
            Panel(new Rect(x, y, 4, 84), HandymanRepairPending ? Gold : Teal);
            Label(new Rect(x + 14, y + 7, width - 28, 20), "ELEVATOR", small);
            Label(new Rect(x + 14, y + 29, width - 28, 50), elevatorSpeech, body);
        }
    }
}
