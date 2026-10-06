#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CrazyElevator.Tools.WorldBuilders
{
    // Isolated SHAFT / ExteriorWorld rebuild. Never runs in gameplay.
    // Swaps weak desk/blockout art for the authored Office/Candy/Water band prefabs.
    // Menu or Temp/WorldBandTasks/request.txt with "shaft".
    [InitializeOnLoad]
    public static class WorldBandShaftBuilder
    {
        const string ExteriorPath = "Assets/Prefabs/Resources/ExteriorWorld.prefab";
        const string BandFolder = "Assets/Static/WorldBands/Prefabs";
        const string MatFolder = "Assets/Static/WorldBands/Materials";
        const string Work = "Temp/WorldBandTasks";
        const string ShaftRootName = "Shaft World Bands";
        const float FloorHeight = 3.3f;

        static readonly string[] RemoveNames =
        {
            ShaftRootName,
            "Desk", "Desk support", "Office divider", "Office back wall",
            "Underwater blockout",
            "Freya candy landing 4", "Freya candy landing 5",
            "Freya candy landing 6", "Freya candy landing 7",
            "CandyCottage(Clone)",
            "Office window", "Office lamp", "Office planter", "Office plant", "Office monitor",
            "Candy drift cloud", "Water haze", "Kelp stalk", "Kelp frond",
            "Coral bloom", "Sea rock", "Bubble", "Sea pipe", "Sea floor apron",
            "WorldBand Remaster"
        };

        static WorldBandShaftBuilder() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            string request = Work + "/request.txt";
            if (!File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim();
            // Lobby builder also watches this folder — only handle shaft here.
            if (action != "shaft" && action != "exterior") return;
            File.Delete(request);
            try
            {
                RebuildShaft();
                File.WriteAllText(Work + "/result.txt", "PASS shaft " + DateTime.Now.ToString("o"));
            }
            catch (Exception e)
            {
                File.WriteAllText(Work + "/result.txt", e.ToString());
                Debug.LogException(e);
            }
        }

        [MenuItem("Crazy Elevator/World / Rebuild Shaft Map (3D bands)")]
        public static void RebuildShaft()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding the shaft map.");

            Directory.CreateDirectory(Work);
            var officeBand = LoadBand("OfficeBand");
            var candyBand = LoadBand("CandyBand");
            var waterBand = LoadBand("WaterBand");

            var root = PrefabUtility.LoadPrefabContents(ExteriorPath);
            try
            {
                ClearWeakDecor(root.transform);

                var bands = new GameObject(ShaftRootName).transform;
                bands.SetParent(root.transform, false);
                bands.localPosition = Vector3.zero;

                PlaceBand(officeBand, bands, Vector3.zero, "Office Band");
                // CandyBand's landscape is already authored at y = floor 4.
                PlaceBand(candyBand, bands, Vector3.zero, "Candy Band");
                // WaterBand floors are local 0–3 → shift to world floors 8–11.
                PlaceBand(waterBand, bands, new Vector3(0f, FloorHeight * 8f, 0f), "Water Band");
                // Mirror the authored world scenery across the central shaft.
                PlaceBand(officeBand, bands, new Vector3(18f, 0f, 0f), "Office Band Right", true);
                PlaceBand(candyBand, bands, new Vector3(18f, 0f, 0f), "Candy Band Right", true);
                PlaceBand(waterBand, bands, new Vector3(18f, FloorHeight * 8f, 0f), "Water Band Right", true);

                PaintLandings(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, ExteriorPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Rebuilt ExteriorWorld shaft with Office/Candy/Water band prefabs.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static GameObject LoadBand(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BandFolder}/{name}.prefab");
            if (prefab == null) throw new InvalidOperationException("Missing band prefab: " + name);
            return prefab;
        }

        static void PlaceBand(GameObject prefab, Transform parent, Vector3 localPos, string name, bool mirrored = false)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = mirrored ? new Vector3(-1f, 1f, 1f) : Vector3.one;
        }

        static void ClearWeakDecor(Transform root)
        {
            var remove = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (ReferenceEquals(t, root)) continue;
                foreach (string name in RemoveNames)
                {
                    if (t.name == name)
                    {
                        remove.Add(t.gameObject);
                        break;
                    }
                }
            }
            for (int i = 0; i < remove.Count; i++)
            {
                var go = remove[i];
                if (!ReferenceEquals(go, null)) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        static void PaintLandings(Transform root)
        {
            var officeTrim = Mat("OfficeTrim");
            var officeWall = Mat("OfficeWall");
            var vanilla = AssetDatabase.LoadAssetAtPath<Material>("Assets/Static/CottonCandy/Materials/Vanilla.mat");
            var pink = AssetDatabase.LoadAssetAtPath<Material>("Assets/Static/CottonCandy/Materials/CandyFlossPink.mat");
            var deep = AssetDatabase.LoadAssetAtPath<Material>("Assets/Static/ElevatorPersonas/Materials/DeepSeaMetal.mat");
            var sand = Mat("WaterSand");

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.gameObject.name;
                Material mat = null;
                if (name == "Ground") mat = officeWall;
                else if (name.StartsWith("World landing "))
                {
                    // Right wing landings keep the source floor number in their name.
                    string floorName = name.Substring("World landing ".Length).Split(' ')[0];
                    int floor = int.Parse(floorName);
                    mat = floor <= 3 ? officeTrim : floor <= 7 ? vanilla : sand;
                }
                else if (name.StartsWith("Shaft landing "))
                {
                    int floor = int.Parse(name.Substring("Shaft landing ".Length));
                    mat = floor <= 3 ? officeTrim : floor <= 7 ? pink : deep;
                }
                if (mat != null) renderer.sharedMaterial = mat;
            }
        }

        static Material Mat(string name)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{name}.mat");
            if (mat == null) throw new InvalidOperationException("Missing material: " + name);
            return mat;
        }
    }
}
#endif
