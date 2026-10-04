#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CrazyElevator.Tools.WorldBuilders
{
    // Menu helpers for the ExteriorWorld remaster. Geometry lives in the prefab;
    // this only re-applies band materials if an artist resets colors by accident.
    public static class WorldBandRemaster
    {
        const string ExteriorPath = "Assets/Prefabs/Resources/ExteriorWorld.prefab";
        const string MatFolder = "Assets/Static/WorldBands/Materials";

        [MenuItem("Crazy Elevator/World / Reapply Exterior Band Materials")]
        public static void ReapplyMaterials()
        {
            var root = PrefabUtility.LoadPrefabContents(ExteriorPath);
            try
            {
                var officeWall = Load("OfficeWall");
                var officeWood = Load("OfficeWood");
                var officeTrim = Load("OfficeTrim");
                var officeGlass = Load("OfficeGlass");
                var waterRock = Load("WaterRock");
                var waterHaze = Load("WaterHaze");
                var waterSand = Load("WaterSand");
                var kelp = Load("KelpGreen");
                var candyCloud = Load("CandyAccentCloud");
                var deepSea = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Static/ElevatorPersonas/Materials/DeepSeaMetal.mat");
                var coral = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Static/ElevatorPersonas/Materials/CoralPipe.mat");
                var vanilla = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Static/CottonCandy/Materials/Vanilla.mat");
                var pink = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Static/CottonCandy/Materials/CandyFlossPink.mat");

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    string name = renderer.gameObject.name;
                    Material mat = null;
                    if (name == "Underwater blockout" || name == "Sea pipe") mat = deepSea;
                    else if (name == "Office back wall" || name == "Ground") mat = officeWall;
                    else if (name == "Office divider" || name == "Desk support") mat = officeTrim;
                    else if (name == "Desk" || name == "Office planter") mat = officeWood;
                    else if (name == "Office window" || name == "Office lamp" || name == "Office monitor") mat = officeGlass;
                    else if (name == "Office plant") mat = Load("OfficePlant");
                    else if (name == "Water haze") mat = waterHaze;
                    else if (name == "Sea floor apron") mat = waterSand;
                    else if (name.StartsWith("Kelp")) mat = kelp;
                    else if (name == "Coral bloom") mat = coral;
                    else if (name == "Sea rock") mat = waterRock;
                    else if (name == "Bubble") mat = Load("BubbleGlow");
                    else if (name == "Candy drift cloud") mat = candyCloud;
                    else if (name.StartsWith("World landing "))
                    {
                        int floor = int.Parse(name.Substring("World landing ".Length));
                        mat = floor <= 3 ? officeTrim : floor <= 7 ? vanilla : deepSea;
                    }
                    else if (name.StartsWith("Shaft landing "))
                    {
                        int floor = int.Parse(name.Substring("Shaft landing ".Length));
                        mat = floor <= 3 ? officeTrim : floor <= 7 ? pink : deepSea;
                    }
                    if (mat != null) renderer.sharedMaterial = mat;
                }

                PrefabUtility.SaveAsPrefabAsset(root, ExteriorPath);
                Debug.Log("Reapplied ExteriorWorld band materials.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Material Load(string name)
            => AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{name}.mat");
    }
}
#endif
