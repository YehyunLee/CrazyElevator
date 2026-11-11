using System;
using System.Collections.Generic;
using CrazyElevator.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Game = CrazyElevator.Managers.ElevatorManager;
using Object = UnityEngine.Object;

namespace CrazyElevator.EditorTools
{
    // Builds editable assets in the Editor; gameplay only toggles the saved art.
    public static class NewWorldsBuilder
    {
        const string ExteriorPath = "Assets/Prefabs/Resources/ExteriorWorld.prefab";
        const string ScenePrefabPath = "Assets/Prefabs/Resources/ElevatorScene.prefab";
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const float FloorHeight = Game.ShaftFloorHeight;
        static readonly string[] Kinds = { "FAIRY", "GOBLIN", "MUSHROOM", "ASTRONAUT", "ALIEN" };

        [MenuItem("Tools/Crazy Elevator/Build Forest and Space Worlds")]
        public static void Build()
        {
            PassengerPrefabBuilder.RebuildNewWorldPassengers();
            ExtendExterior();
            WirePassengerPrefabs();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Built the forest and space worlds and passenger prefabs.");
        }

        static void ExtendExterior()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ExteriorPath);
            try
            {
                Transform world = root.transform;
                Material forestFloor = Material("ForestLanding", new Color32(105, 129, 108, 255));
                Material forestShaft = Material("ForestShaft", new Color32(99, 80, 114, 255));
                Material spaceFloor = Material("SpaceLanding", new Color32(72, 65, 112, 255));
                Material spaceShaft = Material("SpaceShaft", new Color32(43, 38, 79, 255));
                Transform floorRails = Require(world, "Floor Rails");
                Transform railTemplate = Require(floorRails, "Floor rail 0");

                for (int floor = 24; floor < ElevatorRound.Floors; floor++)
                {
                    Material landing = floor < 32 ? forestFloor : spaceFloor;
                    Material shaft = floor < 32 ? forestShaft : spaceShaft;
                    CopyFloor(world, "World landing 0", "World landing " + floor, floor, landing);
                    CopyFloor(world, "World landing 0 right", "World landing " + floor + " right", floor, landing);
                    CopyFloor(world, "Shaft landing 0", "Shaft landing " + floor, floor, shaft);
                    Transform sign = CopyFloor(world, "Sign • 0", "Sign • " + floor, floor, null);
                    TextMesh label = sign.GetComponent<TextMesh>();
                    if (label != null) label.text = ElevatorRound.FloorNumber(floor).ToString();
                    Transform rail = floorRails.Find("Floor rail " + floor);
                    if (rail == null)
                    {
                        rail = Object.Instantiate(railTemplate, floorRails, false);
                        rail.name = "Floor rail " + floor;
                    }
                    rail.localPosition = railTemplate.localPosition + Vector3.up * (floor * FloorHeight);
                }

                ExtendSideTracks(Require(world, "Side Tracks"));
                float top = (ElevatorRound.Floors - 1) * FloorHeight + 9f;
                foreach (string name in new[] { "Left guide rail", "Right guide rail" })
                {
                    Transform rail = Require(world, name);
                    rail.localScale = new Vector3(rail.localScale.x, top, rail.localScale.z);
                    rail.localPosition = new Vector3(rail.localPosition.x, top * .5f - .8f, rail.localPosition.z);
                }
                Transform cap = Require(world, "Shaft top");
                cap.localPosition = new Vector3(cap.localPosition.x, top - .3f, cap.localPosition.z);
                Transform backdrops = Require(world, "World Backdrops");
                PlaceBackdrop(backdrops, "BG_fairy_world", "Forest Backdrop", 3);
                PlaceBackdrop(backdrops, "BG_space_world", "Space Backdrop", 4);
                PrefabUtility.SaveAsPrefabAsset(root, ExteriorPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Transform CopyFloor(Transform world, string sourceName, string targetName, int floor, Material material)
        {
            Transform source = Require(world, sourceName);
            Transform target = world.Find(targetName);
            if (target == null)
            {
                target = Object.Instantiate(source, world, false);
                target.name = targetName;
            }
            target.localPosition = source.localPosition + Vector3.up * (floor * FloorHeight);
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            return target;
        }

        static void ExtendSideTracks(Transform tracks)
        {
            var templates = new List<Transform>();
            foreach (Transform track in tracks)
                if (track.Find("Left post") != null && TrackBottom(track.name) >= 0
                    && TrackBottom(track.name) < 20) templates.Add(track);
            if (templates.Count == 0) throw new InvalidOperationException("No side-track templates found.");
            for (int i = 0; i < Game.SideTracks.Length; i++)
            {
                Game.SideTrack side = Game.SideTracks[i];
                if (side.Top < 24) continue;
                Transform source = templates.Find(t => Mathf.Abs(t.localPosition.x - side.X) < .01f) ?? templates[0];
                var stale = new List<GameObject>();
                foreach (Transform track in tracks)
                    if (Mathf.Abs(track.localPosition.x - side.X) < .01f && TrackBottom(track.name) == side.Bottom)
                        stale.Add(track.gameObject);
                foreach (GameObject old in stale) Object.DestroyImmediate(old);
                Transform copy = Object.Instantiate(source, tracks, false);
                copy.name = $"Side track {i} F{side.Bottom}-F{side.Top}";
                float bottom = side.Bottom * FloorHeight + 1.4f - 1.45f;
                float top = side.Top * FloorHeight + 1.4f + 1.45f;
                float height = top - bottom;
                copy.localPosition = new Vector3(side.X, (bottom + top) * .5f, source.localPosition.z);
                foreach (string name in new[] { "Left post", "Right post", "Spine" })
                {
                    Transform part = Require(copy, name);
                    part.localScale = new Vector3(part.localScale.x, height, part.localScale.z);
                }
                Transform upper = Require(copy, "Top bumper"), lower = Require(copy, "Bottom bumper");
                upper.localPosition = new Vector3(upper.localPosition.x, height * .5f + .2f, upper.localPosition.z);
                lower.localPosition = new Vector3(lower.localPosition.x, -height * .5f - .2f, lower.localPosition.z);
            }
        }

        static int TrackBottom(string name)
        {
            int index = name.LastIndexOf(" F", StringComparison.Ordinal);
            if (index < 0) return -1;
            int end = name.IndexOf('-', index + 2);
            return end > index && int.TryParse(name.Substring(index + 2, end - index - 2), out int floor)
                ? floor : -1;
        }

        static void PlaceBackdrop(Transform parent, string assetName, string name, int dimension)
        {
            Transform old = parent.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Worlds/Resources/" + assetName + ".fbx");
            if (asset == null) throw new InvalidOperationException("Missing world art " + assetName);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            instance.name = name;
            Transform t = instance.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            Renderer sky = Require(t, "pPlane1").GetComponent<Renderer>();
            if (sky == null || sky.bounds.size.y <= .001f) throw new InvalidOperationException("Missing sky plane in " + assetName);
            Bounds bounds = sky.bounds;
            float bottom = (dimension * ElevatorRound.FloorsPerDimension - .5f) * FloorHeight + 1.4f;
            float top = ((dimension + 1) * ElevatorRound.FloorsPerDimension - .5f) * FloorHeight + 1.4f;
            float scale = (top - bottom) / bounds.size.y;
            Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
            t.localRotation = facing;
            t.localScale = Vector3.one * scale;
            t.localPosition = new Vector3(9f, (top + bottom) * .5f, 60f) - facing * (bounds.center * scale);
            foreach (Transform child in t.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            instance.SetActive(false);
        }

        static Material Material(string name, Color color)
        {
            string path = "Assets/Art/Worlds/Materials/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Crazy Elevator/Storybook Surface");
            if (shader == null) throw new InvalidOperationException("Missing world material shader.");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void WirePassengerPrefabs()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ScenePrefabPath);
            try
            {
                foreach (ElevatorScene view in root.GetComponentsInChildren<ElevatorScene>(true)) AddPassengerPrefabs(view);
                PrefabUtility.SaveAsPrefabAsset(root, ScenePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var scene = EditorSceneManager.OpenScene(MainScenePath);
            bool changed = false;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                foreach (ElevatorScene view in sceneRoot.GetComponentsInChildren<ElevatorScene>(true))
                    if (AddPassengerPrefabs(view))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                        EditorUtility.SetDirty(view);
                        changed = true;
                    }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }

        static bool AddPassengerPrefabs(ElevatorScene view)
        {
            var passengers = new List<PassengerView>(view.passengers ?? Array.Empty<PassengerView>());
            bool changed = false;
            foreach (string kind in Kinds)
            {
                if (passengers.Exists(p => p != null && p.ResolvedKind == kind)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Resources/Passenger-" + kind + ".prefab");
                PassengerView passenger = prefab != null ? prefab.GetComponent<PassengerView>() : null;
                if (passenger == null) throw new InvalidOperationException("Missing passenger prefab " + kind);
                passengers.Add(passenger);
                changed = true;
            }
            if (changed) view.passengers = passengers.ToArray();
            return changed;
        }

        static Transform Require(Transform parent, string name)
        {
            Transform result = parent.Find(name);
            if (result == null) throw new InvalidOperationException(parent.name + " is missing " + name);
            return result;
        }
    }
}
