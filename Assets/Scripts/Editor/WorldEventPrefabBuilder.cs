using CrazyElevator.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CrazyElevator.EditorTools
{
    // Authors the lightweight event props as editable prefabs; gameplay only
    // moves and animates these saved objects.
    public static class WorldEventPrefabBuilder
    {
        const string PrefabFolder = "Assets/Static/ShaftHazards/Prefabs";
        const string MaterialFolder = "Assets/Static/ShaftHazards/Materials";
        const string ScenePrefabPath = "Assets/Prefabs/Resources/ElevatorScene.prefab";
        const string MainScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/Crazy Elevator/Build Forest and Space Event Art")]
        public static void Build()
        {
            GameObject vines = BuildForestVines();
            GameObject gravity = BuildSpaceGravitySweep();
            WireEvents(vines, gravity);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Built and wired the Forest vine and Space gravity event prefabs.");
        }

        static GameObject BuildForestVines()
        {
            GameObject root = new GameObject("Forest Vine Sweep");
            root.layer = 31;
            Transform strands = Child(root.transform, "Vines");
            Material deep = MakeMaterial("ForestVineDeep", new Color32(47, 102, 78, 255));
            Material leaf = MakeMaterial("ForestVineLeaf", new Color32(91, 194, 128, 255));
            Material blossom = MakeMaterial("ForestVineBlossom", new Color32(235, 121, 207, 255));
            Material guide = MakeMaterial("ForestFireflyGuide", new Color32(94, 239, 213, 255));

            BuildVineStrand(strands, "Upper vine", new[]
            {
                new Vector3(-4.5f, -.45f, 0f), new Vector3(-2.4f, .55f, 0f),
                new Vector3(-.2f, -.15f, 0f), new Vector3(2.1f, .75f, 0f), new Vector3(4.4f, .15f, 0f)
            }, .19f, deep, leaf);
            BuildVineStrand(strands, "Lower vine", new[]
            {
                new Vector3(-4.5f, .78f, 0f), new Vector3(-2.3f, -.25f, 0f),
                new Vector3(.1f, .62f, 0f), new Vector3(2.3f, -.6f, 0f), new Vector3(4.4f, .42f, 0f)
            }, .15f, leaf, deep);
            BuildVineStrand(strands, "Crossing vine", new[]
            {
                new Vector3(-3.7f, -.9f, 0f), new Vector3(-1.3f, .1f, 0f),
                new Vector3(1.2f, -.75f, 0f), new Vector3(3.7f, .85f, 0f)
            }, .11f, deep, leaf);

            for (int i = 0; i < 5; i++)
            {
                float x = -3.4f + i * 1.7f;
                GameObject flower = Part(strands, PrimitiveType.Sphere, "Glow flower " + i,
                    new Vector3(x, .15f + .25f * Mathf.Sin(i * 2f), -.16f),
                    new Vector3(.25f, .25f, .12f), blossom);
                flower.transform.localRotation = Quaternion.Euler(0f, 0f, i * 37f);
            }
            Transform route = Child(root.transform, "Fairy safe route");
            route.gameObject.SetActive(false);
            for (int i = 0; i < 5; i++)
            {
                float x = -.55f - i * .73f;
                Part(route, PrimitiveType.Sphere, "Firefly " + i,
                    new Vector3(x, -.95f + .06f * Mathf.Sin(i * 1.4f), -.3f),
                    Vector3.one * (i == 4 ? .24f : .15f), guide);
            }
            Part(route, PrimitiveType.Sphere, "Safe turn upper", new Vector3(-3.75f, -.70f, -.3f),
                Vector3.one * .18f, guide);
            Part(route, PrimitiveType.Sphere, "Safe turn lower", new Vector3(-3.75f, -1.2f, -.3f),
                Vector3.one * .18f, guide);
            return Save(root, PrefabFolder + "/ForestVineSweep.prefab");
        }

        static void BuildVineStrand(Transform parent, string name, Vector3[] points,
            float thickness, Material main, Material alternate)
        {
            Transform strand = Child(parent, name);
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 from = points[i], to = points[i + 1];
                Vector3 delta = to - from;
                GameObject segment = Part(strand, PrimitiveType.Cylinder, "Stem " + i,
                    (from + to) * .5f, new Vector3(thickness, delta.magnitude * .52f, thickness),
                    i % 2 == 0 ? main : alternate);
                segment.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
                if (i == 0 || i == points.Length - 2)
                    Part(strand, PrimitiveType.Sphere, "Leaf " + i,
                        (i == 0 ? from : to) + new Vector3(0f, .12f, -.08f),
                        new Vector3(.34f, .13f, .1f), alternate).transform.localRotation =
                            Quaternion.Euler(0f, 0f, (i == 0 ? 1f : -1f) * 30f);
            }
        }

        static GameObject BuildSpaceGravitySweep()
        {
            GameObject root = new GameObject("Space Gravity Wake");
            root.layer = 31;
            Material voidMat = MakeMaterial("GravityWellVoid", new Color32(19, 13, 51, 255));
            Material ringMat = MakeMaterial("GravityWellRing", new Color32(179, 105, 246, 255));
            Material ringLight = MakeMaterial("GravityWellLight", new Color32(83, 230, 214, 255));
            Material saucerMat = MakeMaterial("GravityUfo", new Color32(72, 198, 188, 255));
            Material domeMat = MakeMaterial("GravityUfoDome", new Color32(255, 151, 194, 255));
            Material beamMat = MakeMaterial("GravityUfoBeam", new Color(124f / 255f, 235f / 255f, 219f / 255f, .32f), true);

            Part(root.transform, PrimitiveType.Sphere, "Singularity", Vector3.zero,
                new Vector3(1.48f, 1.48f, .25f), voidMat);
            Transform orbit = Child(root.transform, "Orbit");
            AddOrbit(orbit, "Inner orbit", 1.03f, 18, .12f, ringMat);
            AddOrbit(orbit, "Outer orbit", 1.72f, 24, .09f, ringLight);

            Transform ufo = Child(root.transform, "UFO");
            ufo.localPosition = new Vector3(0f, .4f, -.2f);
            Part(ufo, PrimitiveType.Sphere, "Saucer", Vector3.zero,
                new Vector3(1.45f, .32f, .8f), saucerMat);
            Part(ufo, PrimitiveType.Sphere, "Dome", new Vector3(0f, .22f, 0f),
                new Vector3(.65f, .5f, .55f), domeMat);
            for (int i = 0; i < 5; i++)
            {
                float x = -.56f + i * .28f;
                Part(ufo, PrimitiveType.Sphere, "Running light " + i,
                    new Vector3(x, -.11f, -.04f), new Vector3(.11f, .12f, .11f),
                    i % 2 == 0 ? ringLight : ringMat);
            }
            GameObject beam = Part(ufo, PrimitiveType.Cylinder, "Beam",
                new Vector3(0f, -1.02f, 0f), new Vector3(.68f, 1.52f, .58f), beamMat);
            beam.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);

            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * .25f;
                Part(root.transform, PrimitiveType.Sphere, "Antigravity mote " + i,
                    new Vector3(Mathf.Cos(angle) * 2.4f, Mathf.Sin(angle) * 1.25f, -.12f),
                    new Vector3(.12f, .12f, .12f), i % 2 == 0 ? ringMat : ringLight);
            }
            return Save(root, PrefabFolder + "/SpaceGravitySweep.prefab");
        }

        static void AddOrbit(Transform parent, string name, float radius, int count,
            float size, Material material)
        {
            Transform orbit = Child(parent, name);
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                float yScale = name.StartsWith("Outer") ? .72f : 1f;
                Part(orbit, PrimitiveType.Sphere, "Orbit bead " + i,
                    new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * yScale, -.1f),
                    Vector3.one * size, material);
            }
        }

        static Material MakeMaterial(string name, Color color, bool transparent = false)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Crazy Elevator/Storybook Surface");
                if (shader == null) throw new System.InvalidOperationException("Missing storybook surface shader.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                material.SetFloat("_SrcBlend", 5f);
                material.SetFloat("_DstBlend", 10f);
                material.SetFloat("_ZWrite", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = 3000;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject Part(Transform parent, PrimitiveType type, string name,
            Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.layer = 31;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
            return part;
        }

        static Transform Child(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.gameObject.layer = 31;
            child.SetParent(parent, false);
            return child;
        }

        static GameObject Save(GameObject root, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            if (prefab == null) throw new System.InvalidOperationException("Could not save " + path);
            return prefab;
        }

        static void WireEvents(GameObject vines, GameObject gravity)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ScenePrefabPath);
            try
            {
                foreach (ElevatorScene view in root.GetComponentsInChildren<ElevatorScene>(true))
                {
                    view.forestVineSweep = vines;
                    view.spaceGravitySweep = gravity;
                    EditorUtility.SetDirty(view);
                }
                PrefabUtility.SaveAsPrefabAsset(root, ScenePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var scene = EditorSceneManager.OpenScene(MainScenePath);
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                foreach (ElevatorScene view in sceneRoot.GetComponentsInChildren<ElevatorScene>(true))
                {
                    view.forestVineSweep = vines;
                    view.spaceGravitySweep = gravity;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                    EditorUtility.SetDirty(view);
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
