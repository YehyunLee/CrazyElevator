#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrazyElevator.Tools.WorldBuilders
{
    // Editor-only snapshots of world art and the shaft map, rendered from the
    // gameplay camera angle. Never runs in gameplay and never edits assets.
    // Menu or Temp/MapTasks/request.txt with "inspect" / "shots" / "refresh".
    [InitializeOnLoad]
    public static class WorldMapPreview
    {
        const string Work = "Temp/MapTasks";
        const string ExteriorPath = "Assets/Prefabs/Resources/ExteriorWorld.prefab";
        const float FloorHeight = 13.2f;
        const float ShaftCenterX = 9f;
        static readonly string[] WorldArt =
        {
            "Assets/Art/Worlds/Models/BG_candy_world.fbx",
            "Assets/Art/Worlds/Models/BG_under_water.fbx"
        };

        static WorldMapPreview() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string request = Work + "/request.txt";
            if (!File.Exists(request)) return;
            string action = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                string[] words = action.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0 && words[0] == "describe") DescribeAsset(words);
                else if (words.Length > 0 && words[0] == "render") RenderAsset(words);
                else if (action == "inspect") InspectWorldArt();
                else if (action == "shots") ShaftShots();
                // Lets an external editor pick up script/asset edits while
                // Unity is in the background (auto refresh only runs on focus).
                else if (action == "refresh") AssetDatabase.Refresh();
                else throw new InvalidOperationException("Unknown request (use inspect|shots|refresh): " + action);
                File.WriteAllText(Work + "/result.txt", "PASS " + action + " " + DateTime.Now.ToString("o"));
            }
            catch (Exception e)
            {
                File.WriteAllText(Work + "/result.txt", e.ToString());
                Debug.LogException(e);
            }
        }

        [MenuItem("Crazy Elevator/World / Preview World Art (Temp/MapTasks)")]
        public static void InspectWorldArt()
        {
            Directory.CreateDirectory(Work);
            var report = new StringBuilder();
            foreach (string path in WorldArt)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) { report.AppendLine("MISSING " + path); continue; }
                string name = Path.GetFileNameWithoutExtension(path);
                Bounds bounds = Describe(asset, report, path);
                Vector3 c = bounds.center;
                float r = Mathf.Max(bounds.extents.magnitude, .5f);
                Color bg = new Color(.12f, .13f, .17f);
                Render(asset, $"{Work}/{name}_front.png", c + new Vector3(0, 0, -r * 2.2f), c, 45f, bg);
                Render(asset, $"{Work}/{name}_back.png", c + new Vector3(0, 0, r * 2.2f), c, 45f, bg);
                Render(asset, $"{Work}/{name}_side.png", c + new Vector3(r * 2.2f, 0, 0), c, 45f, bg);
                Render(asset, $"{Work}/{name}_top.png", c + new Vector3(0, r * 2.2f, -.01f), c, 45f, bg);
                Render(asset, $"{Work}/{name}_angle.png", c + new Vector3(-r * 1.3f, r * .8f, -r * 1.6f), c, 45f, bg);
            }
            File.WriteAllText(Work + "/inspect.txt", report.ToString());
        }

        [MenuItem("Crazy Elevator/World / Shaft Map Shots (Temp/MapTasks)")]
        public static void ShaftShots()
        {
            Directory.CreateDirectory(Work);
            var exterior = AssetDatabase.LoadAssetAtPath<GameObject>(ExteriorPath);
            if (exterior == null) throw new InvalidOperationException("Missing " + ExteriorPath);
            int[] floors = { 0, 4, 7, 9, 12, 15, 17, 20, 23 };
            foreach (int floor in floors)
                RenderShaftAt(exterior, $"shaft_floor_{floor:00}", ShaftCenterX, floor);
            // Ground-track ends: the camera follows the car across the full rail.
            RenderShaftAt(exterior, "rail_left_f2", ShaftCenterX - 20f, 2f);
            RenderShaftAt(exterior, "rail_right_f9", ShaftCenterX + 20f, 9f);
            RenderShaftAt(exterior, "rail_left_f14", ShaftCenterX - 14f, 14f);
            RenderShaftAt(exterior, "rail_right_f20", ShaftCenterX + 22f, 20f);
            var report = new StringBuilder();
            Bounds b = Describe(exterior, report, ExteriorPath, 2);
            File.WriteAllText(Work + "/exterior.txt", report.ToString());
            Vector3 overview = new Vector3(b.center.x, b.center.y, b.center.z - b.extents.y * 2.6f);
            Render(exterior, $"{Work}/shaft_overview.png", overview, b.center, 50f, new Color(.6f, .7f, .85f), 900, 1600);
        }

        // Mirrors ElevatorManager.UpdateCrazyTravelCamera.
        static void RenderShaftAt(GameObject exterior, string name, float x, float floor)
        {
            float carY = floor * FloorHeight + 1.4f;
            Vector3 cam = new Vector3(x, carY + 5.2f, -34f);
            Vector3 target = new Vector3(x, carY + .9f, -.8f);
            // Same far clip and culling mask as the Exterior travel camera in ElevatorScene.prefab.
            Render(exterior, $"{Work}/{name}.png", cam, target, 61f, Sky(Mathf.RoundToInt(floor)),
                farClip: 150f, cullingMask: 1 << 31);
        }

        // describe <assetPath> <depth> <outName>
        static void DescribeAsset(string[] words)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(words[1]);
            if (asset == null) throw new InvalidOperationException("Missing " + words[1]);
            var report = new StringBuilder();
            Describe(asset, report, words[1], int.Parse(words[2]));
            File.WriteAllText($"{Work}/{words[3]}.txt", report.ToString());
        }

        // render <assetPath> <outName> camX camY camZ targetX targetY targetZ fov [r g b]
        static void RenderAsset(string[] words)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(words[1]);
            if (asset == null) throw new InvalidOperationException("Missing " + words[1]);
            float F(int i) => float.Parse(words[i], System.Globalization.CultureInfo.InvariantCulture);
            Color bg = words.Length >= 13 ? new Color(F(10), F(11), F(12)) : new Color(.12f, .13f, .17f);
            Render(asset, $"{Work}/{words[2]}.png", new Vector3(F(3), F(4), F(5)),
                new Vector3(F(6), F(7), F(8)), F(9), bg);
        }

        static Color Sky(int floor)
        {
            if (floor >= 16) return WorldArtMaterials.WaterSky;
            if (floor >= 8) return WorldArtMaterials.CandySky;
            return new Color32(176, 198, 224, 255);
        }

        static Bounds Describe(GameObject asset, StringBuilder report, string label, int maxDepth = 6)
        {
            report.AppendLine("=== " + label);
            bool found = false;
            Bounds total = default;
            int renderers = 0, vertices = 0;
            foreach (Transform t in asset.GetComponentsInChildren<Transform>(true))
            {
                int depth = 0;
                for (Transform p = t; p != asset.transform; p = p.parent) depth++;
                var mf = t.GetComponent<MeshFilter>();
                var mr = t.GetComponent<Renderer>();
                if (mr != null)
                {
                    renderers++;
                    if (!found) { total = mr.bounds; found = true; }
                    else total.Encapsulate(mr.bounds);
                    if (mf != null && mf.sharedMesh != null) vertices += mf.sharedMesh.vertexCount;
                }
                if (depth > maxDepth) continue;
                var line = new StringBuilder();
                line.Append(new string(' ', depth * 2)).Append(t.name)
                    .Append("  pos=").Append(t.localPosition.ToString("0.##"))
                    .Append(" rot=").Append(t.localEulerAngles.ToString("0"))
                    .Append(" scl=").Append(t.localScale.ToString("0.##"));
                if (mr != null)
                {
                    line.Append("  bounds c=").Append(mr.bounds.center.ToString("0.#"))
                        .Append(" size=").Append(mr.bounds.size.ToString("0.#"));
                    if (mf != null && mf.sharedMesh != null)
                        line.Append(" verts=").Append(mf.sharedMesh.vertexCount)
                            .Append(" colors=").Append(mf.sharedMesh.colors32.Length > 0);
                    foreach (var m in mr.sharedMaterials)
                    {
                        if (m == null) { line.Append(" [null mat]"); continue; }
                        line.Append(" [").Append(m.name).Append(" / ").Append(m.shader.name);
                        if (m.HasProperty("_BaseColor")) line.Append(" ").Append(m.GetColor("_BaseColor").ToString("0.##"));
                        else if (m.HasProperty("_Color")) line.Append(" ").Append(m.GetColor("_Color").ToString("0.##"));
                        Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")
                            : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                        if (tex != null) line.Append(" tex=").Append(tex.name);
                        line.Append("]");
                    }
                }
                report.AppendLine(line.ToString());
            }
            report.AppendLine($"TOTAL renderers={renderers} verts={vertices} bounds c={total.center:0.#} size={total.size:0.#}");
            report.AppendLine();
            return total;
        }

        static void Render(GameObject asset, string file, Vector3 position, Vector3 target, float fov,
            Color background, int width = 1280, int height = 720, float farClip = 0f, int cullingMask = ~0)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            RenderTexture rt = null;
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                instance.transform.position = Vector3.zero;

                var key = new GameObject("Preview key light");
                SceneManager.MoveGameObjectToScene(key, scene);
                key.transform.rotation = Quaternion.Euler(38f, -28f, 0f);
                var keyLight = key.AddComponent<Light>();
                keyLight.type = LightType.Directional;
                keyLight.intensity = 1.25f;

                var fill = new GameObject("Preview fill light");
                SceneManager.MoveGameObjectToScene(fill, scene);
                fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
                var fillLight = fill.AddComponent<Light>();
                fillLight.type = LightType.Directional;
                fillLight.intensity = .55f;

                var cameraObject = new GameObject("Preview camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
                camera.cullingMask = cullingMask;
                camera.fieldOfView = fov;
                float distance = Vector3.Distance(position, target);
                camera.nearClipPlane = Mathf.Max(.03f, distance * .002f);
                camera.farClipPlane = farClip > 0f ? farClip : distance * 20f + 500f;
                camera.transform.position = position;
                camera.transform.rotation = Quaternion.LookRotation(target - position, Vector3.up);

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                camera.targetTexture = rt;
                camera.Render();

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(file, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                camera.targetTexture = null;
            }
            finally
            {
                if (rt != null) rt.Release();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
#endif
