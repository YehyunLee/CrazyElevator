#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CrazyElevator.Tools.WorldBuilders
{
    // Isolated LOBBY-view builder (doors-open view from the cabin). Never runs in gameplay.
    // Does NOT edit shaft ExteriorWorld. Bakes into ElevatorPersonaRig + LobbyView prefab.
    // Menu or Temp/WorldBandTasks/request.txt with "lobby" / "build".
    [InitializeOnLoad]
    public static class WorldBandSceneBuilder
    {
        const string PersonaPath = "Assets/Static/ElevatorPersonas/Prefabs/ElevatorPersonaRig.prefab";
        const string LobbyPrefabPath = "Assets/Static/WorldBands/Prefabs/LobbyView.prefab";
        const string MatFolder = "Assets/Static/WorldBands/Materials";
        const string CandyMatFolder = "Assets/Static/CottonCandy/Materials";
        const string PersonaMatFolder = "Assets/Static/ElevatorPersonas/Materials";
        const string CandyPrefabFolder = "Assets/Static/CottonCandy/Prefabs";
        const string Work = "Temp/WorldBandTasks";
        const string LobbyRootName = "Lobby Worlds";

        static readonly string[] ClearNames =
        {
            LobbyRootName,
            "Office world wrap", "Candy world wrap", "Underwater world wrap",
            "Cotton candy illustration", "Underwater illustration", "Office illustration",
            "Office wrap front", "Office wrap left", "Office wrap right",
            "Candy wrap front", "Candy wrap left", "Candy wrap right",
            "Water wrap front", "Water wrap left", "Water wrap right"
        };

        static WorldBandSceneBuilder() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            string request = Work + "/request.txt";
            if (!File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim();
            // Shaft builder shares this folder — ignore its actions.
            if (action == "shaft" || action == "exterior") return;
            File.Delete(request);
            try
            {
                if (action == "lobby" || action == "build") BuildLobbyView();
                else throw new InvalidOperationException("Unknown request (use lobby|build): " + action);
                File.WriteAllText(Work + "/result.txt", "PASS lobby " + DateTime.Now.ToString("o"));
            }
            catch (Exception e)
            {
                File.WriteAllText(Work + "/result.txt", e.ToString());
                Debug.LogException(e);
            }
        }

        [MenuItem("Crazy Elevator/World / Rebuild Lobby View (3D)")]
        public static void BuildLobbyView()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding lobby view.");

            Directory.CreateDirectory("Assets/Static/WorldBands/Prefabs");
            Directory.CreateDirectory(Work);
            AssetDatabase.Refresh();

            var persona = PrefabUtility.LoadPrefabContents(PersonaPath);
            try
            {
                ClearOldLobbyArt(persona.transform);
                var lobbies = Group(LobbyRootName, persona.transform);
                // Sit in the open-door hall (doors at z≈0, hall toward -Z).
                lobbies.localPosition = Vector3.zero;

                var office = BuildOfficeLobby(Group("Office Lobby", lobbies));
                var candy = BuildCandyLobby(Group("Candy Lobby", lobbies));
                var water = BuildWaterLobby(Group("Water Lobby", lobbies));
                office.gameObject.SetActive(true);
                candy.gameObject.SetActive(false);
                water.gameObject.SetActive(false);

                WirePersona(persona, office.gameObject, candy.gameObject, water.gameObject);
                PrefabUtility.SaveAsPrefabAsset(persona, PersonaPath);

                // Standalone copy for iteration without opening the persona rig.
                var lobbyClone = UnityEngine.Object.Instantiate(lobbies.gameObject);
                lobbyClone.name = LobbyRootName;
                PrefabUtility.SaveAsPrefabAsset(lobbyClone, LobbyPrefabPath);
                UnityEngine.Object.DestroyImmediate(lobbyClone);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Rebuilt 3D Lobby View into ElevatorPersonaRig + LobbyView.prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(persona);
            }
        }

        static void WirePersona(GameObject personaRoot, GameObject office, GameObject candy, GameObject water)
        {
            var rig = personaRoot.GetComponent<ElevatorPersonaRig>();
            if (rig == null) throw new InvalidOperationException("ElevatorPersonaRig missing on prefab root.");
            Undo.RecordObject(rig, "Wire lobby backdrops");
            rig.officeBackdrop = office;
            rig.candyBackdrop = candy;
            rig.waterBackdrop = water;
            EditorUtility.SetDirty(rig);
        }

        static void ClearOldLobbyArt(Transform root)
        {
            var remove = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (ReferenceEquals(t, root)) continue;
                foreach (string name in ClearNames)
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

        // ---------- Office lobby (cubicle aisle through the doorway) ----------
        static Transform BuildOfficeLobby(Transform band)
        {
            // Compact diorama just beyond the threshold, readable from cabin camera.
            var carpet = Mat("OfficeCarpet");
            var cubicle = Mat("OfficeCubicle");
            var frame = Mat("OfficeFrame");
            var cream = Mat("OfficeCream");
            var wood = Mat("OfficeWood");
            var glass = Mat("OfficeGlass");
            var plant = Mat("OfficePlant");
            var door = Mat("OfficeDoor");

            // Floor / ceiling / end wall stretch into the hall (-Z).
            Box("Carpet", band, new Vector3(0, 0.02f, -2.4f), new Vector3(6.4f, 0.08f, 4.6f), carpet);
            Box("Ceiling", band, new Vector3(0, 3.05f, -2.4f), new Vector3(6.6f, 0.12f, 4.8f), cream);
            Box("End Wall", band, new Vector3(0, 1.55f, -4.55f), new Vector3(6.6f, 3.1f, 0.2f), cream);
            Box("End Door", band, new Vector3(1.1f, 1.2f, -4.42f), new Vector3(1.0f, 2.2f, 0.1f), door);
            Box("Wall Art", band, new Vector3(-1.3f, 2.1f, -4.42f), new Vector3(0.9f, 0.9f, 0.08f), glass);

            // Left / right cubicle rows (also fill the sides of the open doorway).
            for (int i = 0; i < 3; i++)
            {
                float z = -1.1f - i * 1.15f;
                Cubicle(band, new Vector3(-2.55f, 0, z), cubicle, frame, wood, glass, plant, i == 0);
                Cubicle(band, new Vector3(2.55f, 0, z), cubicle, frame, wood, glass, plant, false);
            }

            // Side returns so looking past the jambs still reads office.
            Box("Left Return", band, new Vector3(-3.35f, 1.5f, -2.2f), new Vector3(0.18f, 3f, 4.2f), cream);
            Box("Right Return", band, new Vector3(3.35f, 1.5f, -2.2f), new Vector3(0.18f, 3f, 4.2f), cream);
            Box("Window Band", band, new Vector3(0, 2.7f, -2.4f), new Vector3(6.2f, 0.35f, 4.4f), glass);
            Box("Lamp", band, new Vector3(0, 2.95f, -1.5f), new Vector3(3.5f, 0.06f, 0.25f), glass);
            return band;
        }

        static void Cubicle(Transform parent, Vector3 pos, Material cubicle, Material frame,
            Material wood, Material glass, Material plant, bool withPlant)
        {
            var bay = Group("Cubicle", parent);
            bay.localPosition = pos;
            float face = pos.x < 0 ? 1f : -1f; // panels face the aisle
            Box("Panel", bay, new Vector3(face * 0.05f, 1.0f, 0), new Vector3(1.5f, 1.9f, 0.14f), cubicle);
            Box("Frame", bay, new Vector3(face * 0.05f, 1.0f, -0.02f * face), new Vector3(1.6f, 2.0f, 0.06f), frame);
            Box("Glass Cap", bay, new Vector3(face * 0.05f, 2.15f, 0), new Vector3(1.45f, 0.4f, 0.1f), glass);
            Box("Desk", bay, new Vector3(face * 0.55f, 0.7f, 0), new Vector3(1.1f, 0.1f, 0.85f), wood);
            Box("Drawer", bay, new Vector3(face * 0.55f, 0.35f, 0.15f), new Vector3(0.55f, 0.5f, 0.55f), frame);
            if (withPlant)
            {
                Box("Pot", bay, new Vector3(face * 0.35f, 0.88f, -0.15f), new Vector3(0.22f, 0.18f, 0.22f), Mat("OfficeDoor"));
                Sphere("Leaves", bay, new Vector3(face * 0.35f, 1.1f, -0.15f), new Vector3(0.4f, 0.45f, 0.4f), plant);
            }
        }

        // ---------- Candy lobby ----------
        static Transform BuildCandyLobby(Transform band)
        {
            var hill = AssetDatabase.LoadAssetAtPath<Material>(CandyMatFolder + "/MintRibbonHills.mat");
            var pink = AssetDatabase.LoadAssetAtPath<Material>(CandyMatFolder + "/CandyFlossPink.mat");
            var lemon = AssetDatabase.LoadAssetAtPath<Material>(CandyMatFolder + "/Lemon.mat");
            var blue = AssetDatabase.LoadAssetAtPath<Material>(CandyMatFolder + "/Blueberry.mat");
            var peach = AssetDatabase.LoadAssetAtPath<Material>(CandyMatFolder + "/Peach.mat");
            var vanilla = AssetDatabase.LoadAssetAtPath<Material>(CandyMatFolder + "/Vanilla.mat");
            var raspberry = AssetDatabase.LoadAssetAtPath<Material>(CandyMatFolder + "/Raspberry.mat");
            var cloudMat = Mat("CandyAccentCloud");
            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CandyPrefabFolder + "/CottonCandyTree.prefab");
            var cottagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CandyPrefabFolder + "/CandyCottage.prefab");
            var cloudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CandyPrefabFolder + "/VanillaCloud.prefab");

            Sphere("Hill L", band, new Vector3(-3.2f, -0.6f, -3.2f), new Vector3(5.5f, 3.2f, 4f), hill);
            Sphere("Hill R", band, new Vector3(3.4f, -0.8f, -3.5f), new Vector3(6f, 3.5f, 4.5f), hill);
            Sphere("Rock Lemon", band, new Vector3(-1.8f, 0.35f, -2.2f), new Vector3(1.2f, 0.9f, 1.1f), lemon);
            Sphere("Rock Blue", band, new Vector3(2.0f, 0.3f, -2.4f), new Vector3(1.4f, 1.0f, 1.2f), blue);
            Sphere("Rock Pink", band, new Vector3(0.2f, 0.25f, -3.6f), new Vector3(1.1f, 0.8f, 1.0f), pink);

            if (cottagePrefab != null)
                PlacePrefab(cottagePrefab, band, new Vector3(1.6f, 0.15f, -3.8f), Vector3.one * 0.28f);
            else
            {
                Box("Cottage", band, new Vector3(1.6f, 1.1f, -3.8f), new Vector3(2.2f, 1.8f, 1.8f), peach);
                Box("Roof", band, new Vector3(1.6f, 2.2f, -3.8f), new Vector3(2.6f, 0.7f, 2.1f), pink);
            }

            if (treePrefab != null)
            {
                PlacePrefab(treePrefab, band, new Vector3(-2.4f, 0.1f, -2.8f), Vector3.one * 0.35f);
                PlacePrefab(treePrefab, band, new Vector3(-1.2f, 0.05f, -3.9f), Vector3.one * 0.28f);
            }
            else
            {
                Cylinder("Trunk", band, new Vector3(-2.4f, 0.9f, -2.8f), new Vector3(0.2f, 0.9f, 0.2f), peach);
                Sphere("Canopy", band, new Vector3(-2.4f, 2.1f, -2.8f), new Vector3(1.6f, 1.5f, 1.6f), pink);
            }

            if (cloudPrefab != null)
            {
                PlacePrefab(cloudPrefab, band, new Vector3(-0.5f, 2.6f, -2.0f), Vector3.one * 0.35f);
                PlacePrefab(cloudPrefab, band, new Vector3(2.2f, 2.8f, -3.0f), Vector3.one * 0.28f);
            }
            else
            {
                Sphere("Cloud", band, new Vector3(-0.5f, 2.6f, -2.0f), new Vector3(2.2f, 1.1f, 1.4f), cloudMat);
            }

            for (int i = 0; i < 4; i++)
                Sphere("Flower", band, new Vector3(-2.8f + i * 1.4f, 0.2f, -1.6f - (i % 2) * 0.4f),
                    new Vector3(0.45f, 0.18f, 0.45f), i % 2 == 0 ? raspberry : lemon);

            Sphere("Sky Puff", band, new Vector3(0f, 3.4f, -4.2f), new Vector3(4f, 1.6f, 2f), vanilla);
            return band;
        }

        // ---------- Water lobby ----------
        static Transform BuildWaterLobby(Transform band)
        {
            var temple = Mat("WaterTemple");
            var rock = Mat("WaterRock");
            var sand = Mat("WaterSand");
            var haze = Mat("WaterHaze");
            var coral = Mat("WaterCoralPink");
            var seaweed = Mat("WaterSeaweed");
            var bubble = Mat("BubbleGlow");
            var deep = AssetDatabase.LoadAssetAtPath<Material>(PersonaMatFolder + "/DeepSeaMetal.mat");
            var coralPipe = AssetDatabase.LoadAssetAtPath<Material>(PersonaMatFolder + "/CoralPipe.mat");

            Box("Seabed", band, new Vector3(0, -0.05f, -2.6f), new Vector3(6.8f, 0.2f, 5f), sand);
            Box("Haze", band, new Vector3(0, 1.8f, -4.6f), new Vector3(7f, 3.6f, 0.25f), haze);

            var ruin = Group("Temple", band);
            ruin.localPosition = new Vector3(-0.8f, 0.1f, -3.6f);
            ruin.localRotation = Quaternion.Euler(0, 12f, -8f);
            Box("Base", ruin, new Vector3(0, 0.25f, 0), new Vector3(3.2f, 0.5f, 1.6f), temple);
            for (int c = 0; c < 4; c++)
                Cylinder("Column", ruin, new Vector3(-1.1f + c * 0.75f, 1.35f, 0), new Vector3(0.28f, 1.1f, 0.28f), temple);
            Box("Pediment", ruin, new Vector3(0, 2.55f, 0), new Vector3(3.3f, 0.4f, 0.7f), temple);

            var gate = Group("Gateway", band);
            gate.localPosition = new Vector3(2.0f, 0.05f, -3.2f);
            gate.localRotation = Quaternion.Euler(0, -18f, 6f);
            Cylinder("Pillar L", gate, new Vector3(-0.7f, 1.1f, 0), new Vector3(0.22f, 1.1f, 0.22f), deep);
            Cylinder("Pillar R", gate, new Vector3(0.7f, 1.1f, 0), new Vector3(0.22f, 1.1f, 0.22f), deep);
            Box("Lintel", gate, new Vector3(0, 2.25f, 0), new Vector3(1.8f, 0.28f, 0.4f), deep);

            Sphere("Coral L", band, new Vector3(-2.8f, 0.55f, -1.8f), new Vector3(1.3f, 1.1f, 1.3f), coral);
            Sphere("Coral R", band, new Vector3(2.6f, 0.5f, -2.0f), new Vector3(1.1f, 0.9f, 1.1f), coralPipe != null ? coralPipe : coral);
            Sphere("Rock L", band, new Vector3(-2.2f, 0.25f, -2.8f), new Vector3(1.6f, 0.7f, 1.3f), rock);
            Sphere("Rock R", band, new Vector3(2.3f, 0.22f, -3.0f), new Vector3(1.4f, 0.65f, 1.2f), rock);

            for (int k = 0; k < 5; k++)
            {
                float x = -3.0f + k * 1.4f;
                Cylinder("Kelp", band, new Vector3(x, 1.0f, -1.5f - (k % 2) * 0.4f),
                    new Vector3(0.12f, 1.0f + k * 0.08f, 0.12f), seaweed);
            }

            for (int b = 0; b < 6; b++)
                Sphere("Bubble", band, new Vector3(-2.5f + b * 0.9f, 0.8f + (b % 3) * 0.45f, -1.3f - b * 0.15f),
                    Vector3.one * (0.12f + b * 0.015f), bubble);

            return band;
        }

        // ---------- helpers ----------
        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            return t;
        }

        static void PlacePrefab(GameObject prefab, Transform parent, Vector3 localPos, Vector3 scale)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = scale;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
            => Prim(name, parent, PrimitiveType.Cube, pos, scale, mat);

        static GameObject Sphere(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
            => Prim(name, parent, PrimitiveType.Sphere, pos, scale, mat);

        static GameObject Cylinder(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
            => Prim(name, parent, PrimitiveType.Cylinder, pos, scale, mat);

        static GameObject Prim(string name, Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = 0;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = scale;
            var col = go.GetComponent<Collider>();
            if (col) UnityEngine.Object.DestroyImmediate(col);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer && mat) renderer.sharedMaterial = mat;
            return go;
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
