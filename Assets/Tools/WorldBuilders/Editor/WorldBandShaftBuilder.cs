#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CrazyElevator.Shared;
using UnityEditor;
using UnityEngine;
using Game = CrazyElevator.Managers.ElevatorManager;

namespace CrazyElevator.Tools.WorldBuilders
{
    // Isolated SHAFT / ExteriorWorld rebuild. Never runs in gameplay.
    // Stacks the authored Office/Candy/Water band prefabs for every floor, hangs the
    // art team's FBX dioramas behind the candy and water bands, and bakes the
    // landings, signs, floor rails, side tracks and teleport gates as plain
    // editable GameObjects. Route layout comes from ElevatorManager.CrazyTravel.
    // Menu or Temp/WorldBandTasks/request.txt with "shaft".
    [InitializeOnLoad]
    public static class WorldBandShaftBuilder
    {
        const string ExteriorPath = "Assets/Prefabs/Resources/ExteriorWorld.prefab";
        const string BandFolder = "Assets/Static/WorldBands/Prefabs";
        const string MatFolder = "Assets/Static/WorldBands/Materials";
        const string CandyMatFolder = "Assets/Static/CottonCandy/Materials";
        const string Work = "Temp/WorldBandTasks";
        const string ShaftRootName = "Shaft World Bands";
        const string BackdropRootName = "World Backdrops";
        const string LegacyGroundTrackRootName = "Ground Track Rails";
        const string FloorRailRootName = "Floor Rails";
        const string SideTrackRootName = "Side Tracks";
        // ExteriorWorld and the three band prefabs are authored at this spacing.
        const float FloorHeight = 13.2f;
        const float CarLift = 1.4f;
        // Matches ElevatorManager.ExteriorLayer (the shaft camera layer).
        const int ExteriorLayer = 31;
        const float ShaftCenterX = 9f;
        // Band prefabs hold four floors each; stack them to fill a dimension.
        const int FloorsPerBand = 4;
        // CandyBand's landscape starts at its own floor 4.
        const int CandyBandAuthoredFloor = 4;
        // Diorama sky planes sit this far behind the building so wings read in front.
        const float BackdropSkyDepth = 60f;

        static int Floors => ElevatorRound.Floors;
        static int FloorsPerDimension => ElevatorRound.FloorsPerDimension;

        static readonly string[] RemoveNames =
        {
            ShaftRootName, BackdropRootName, LegacyGroundTrackRootName,
            FloorRailRootName, SideTrackRootName, Game.TeleportGateRootName,
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

        // Oversized band pieces that blocked the view; the FBX backdrops replace them.
        static readonly string[] CandyBandHidden = { "Mint Hill Far", "Mint Hill Near" };
        static readonly string[] WaterBandHidden = { "Deep Haze" };

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
            WorldArtMaterials.Apply();

            var root = PrefabUtility.LoadPrefabContents(ExteriorPath);
            try
            {
                ClearWeakDecor(root.transform);
                PlaceBands(root.transform);
                BuildFloorStack(root.transform);
                StretchShaft(root.transform);
                BuildFloorRails(root.transform);
                BuildSideTracks(root.transform);
                BuildTeleportGates(root.transform, Required(root.transform, "Sign • 0"));
                PlaceBackdrops(root.transform);
                foreach (string name in new[]
                    { FloorRailRootName, SideTrackRootName, Game.TeleportGateRootName, BackdropRootName })
                    SetLayer(root.transform.Find(name), ExteriorLayer);
                foreach (string name in new[] { FloorRailRootName, SideTrackRootName, "Left guide rail", "Right guide rail" })
                    SetLayer(root.transform.Find(name), 28); // Elevator tracks, separate from the cabin/world.
                PaintLandings(root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, ExteriorPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"Rebuilt ExteriorWorld shaft for {Floors} floors with FBX world backdrops.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void SetLayer(Transform root, int layer)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;
        }

        static float FloorY(float floor) => floor * FloorHeight;
        static float BoundaryY(int dimension) => FloorY(dimension * FloorsPerDimension - .5f) + CarLift;

        // ---------- Bands ----------

        static void PlaceBands(Transform root)
        {
            var bands = new GameObject(ShaftRootName).transform;
            bands.SetParent(root, false);
            var office = LoadBand("OfficeBand");
            var candy = LoadBand("CandyBand");
            var water = LoadBand("WaterBand");

            int stacks = Mathf.Max(1, FloorsPerDimension / FloorsPerBand);
            for (int i = 0; i < stacks; i++)
            {
                int officeFloor = i * FloorsPerBand;
                int candyFloor = FloorsPerDimension + i * FloorsPerBand - CandyBandAuthoredFloor;
                int waterFloor = FloorsPerDimension * 2 + i * FloorsPerBand;
                string suffix = i == 0 ? "" : " Tall" + (i > 1 ? " " + i : "");
                PlaceBandPair(office, bands, officeFloor, "Office Band" + suffix, null);
                PlaceBandPair(candy, bands, candyFloor, "Candy Band" + suffix, CandyBandHidden);
                PlaceBandPair(water, bands, waterFloor, "Water Band" + suffix, WaterBandHidden);
                if (i == stacks - 1)
                {
                    HideSpillover(bands.Find("Candy Band" + suffix), CandyTopFloor);
                    HideSpillover(bands.Find("Candy Band Right" + suffix), CandyTopFloor);
                }
            }
        }

        // Candy props sit ~11 units up each floor, on the next slab, and the sky puffs
        // hang just under the band top. On the top candy stack both would land on the
        // first water floor, so hide them there.
        const string CandyTopFloor = "Candy Floor 7";
        const float SpilloverHeight = 9f;

        static void HideSpillover(Transform band, string floorName)
        {
            if (band == null) return;
            foreach (Transform t in band.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Sky Puff")) t.gameObject.SetActive(false);
                if (t.name != floorName) continue;
                foreach (Transform prop in t)
                    if (prop.localPosition.y > SpilloverHeight) prop.gameObject.SetActive(false);
            }
        }

        static void PlaceBandPair(GameObject prefab, Transform parent, int floor, string name, string[] hidden)
        {
            // Mirror the authored world scenery across the central shaft.
            PlaceBand(prefab, parent, new Vector3(0f, FloorY(floor), 0f), name, false, hidden);
            string rightName = name.Replace(" Band", " Band Right");
            PlaceBand(prefab, parent, new Vector3(18f, FloorY(floor), 0f), rightName, true, hidden);
        }

        static GameObject LoadBand(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BandFolder}/{name}.prefab");
            if (prefab == null) throw new InvalidOperationException("Missing band prefab: " + name);
            return prefab;
        }

        static void PlaceBand(GameObject prefab, Transform parent, Vector3 localPos, string name,
            bool mirrored, string[] hidden)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.localPosition = localPos;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = mirrored ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            if (hidden == null) return;
            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true))
                if (Array.IndexOf(hidden, t.name) >= 0) t.gameObject.SetActive(false);
        }

        // ---------- Landings, signs, shaft ----------

        static void BuildFloorStack(Transform root)
        {
            var worldTemplate = Required(root, "World landing 0");
            var rightTemplate = Required(root, "World landing 0 right");
            var shaftTemplate = Required(root, "Shaft landing 0");
            var signTemplate = Required(root, "Sign • 0");
            for (int floor = 0; floor < Floors; floor++)
            {
                PlaceFloorPiece(root, worldTemplate, "World landing " + floor, floor);
                PlaceFloorPiece(root, rightTemplate, "World landing " + floor + " right", floor);
                PlaceFloorPiece(root, shaftTemplate, "Shaft landing " + floor, floor);
                var sign = PlaceFloorPiece(root, signTemplate, "Sign • " + floor, floor);
                var text = sign.GetComponent<TextMesh>();
                if (text != null) text.text = ElevatorRound.FloorNumber(floor).ToString();
            }
            RemoveFloorsAbove(root, Floors);
        }

        static Transform PlaceFloorPiece(Transform root, Transform template, string name, int floor)
        {
            Transform piece = root.Find(name);
            if (piece == null)
            {
                piece = UnityEngine.Object.Instantiate(template, root, false);
                piece.name = name;
            }
            Vector3 p = template.localPosition;
            piece.localPosition = new Vector3(p.x, p.y + FloorY(floor), p.z);
            return piece;
        }

        static void RemoveFloorsAbove(Transform root, int floors)
        {
            var stale = new List<GameObject>();
            foreach (Transform child in root)
            {
                if (!TryParseFloor(child.name, out int floor)) continue;
                if (floor >= floors) stale.Add(child.gameObject);
            }
            foreach (var go in stale) UnityEngine.Object.DestroyImmediate(go);
        }

        static bool TryParseFloor(string name, out int floor)
        {
            floor = -1;
            string[] prefixes = { "World landing ", "Shaft landing ", "Sign • " };
            foreach (string prefix in prefixes)
            {
                if (!name.StartsWith(prefix)) continue;
                return int.TryParse(name.Substring(prefix.Length).Split(' ')[0], out floor);
            }
            return false;
        }

        static void StretchShaft(Transform root)
        {
            float top = FloorY(Floors - 1) + 9f;
            foreach (string railName in new[] { "Left guide rail", "Right guide rail" })
            {
                var rail = Required(root, railName);
                rail.localScale = new Vector3(rail.localScale.x, top, rail.localScale.z);
                rail.localPosition = new Vector3(rail.localPosition.x, top * .5f - .8f, rail.localPosition.z);
            }
            var cap = Required(root, "Shaft top");
            cap.localPosition = new Vector3(cap.localPosition.x, top - .3f, cap.localPosition.z);
        }

        static Transform Required(Transform root, string name)
        {
            var t = root.Find(name);
            if (t == null) throw new InvalidOperationException("ExteriorWorld is missing " + name);
            return t;
        }

        // ---------- Route network (layout tables live in ElevatorManager.CrazyTravel) ----------

        static Material Girder => LoadMat("Assets/Static/ElevatorPersonas/Materials/DeepSeaMetal.mat");

        static Material WorldTrim(int floor)
        {
            int dimension = floor / FloorsPerDimension;
            return dimension == 0 ? CandyMat("Lemon") : dimension == 1 ? CandyMat("CandyFlossPink") : Mat("BubbleGlow");
        }

        // One rail along the front edge of every floor slab, across both wings.
        static void BuildFloorRails(Transform root)
        {
            var rails = new GameObject(FloorRailRootName).transform;
            rails.SetParent(root, false);
            float length = Game.GroundTrackSpan * 2f + 4f;
            for (int floor = 0; floor < Floors; floor++)
            {
                var rail = new GameObject("Floor rail " + floor).transform;
                rail.SetParent(rails, false);
                // Rail top sits just under the car body (car is 2.65 tall, centred on CarLift).
                rail.localPosition = new Vector3(ShaftCenterX, FloorY(floor) + CarLift - 1.45f, -2.15f);
                Material trim = WorldTrim(floor);
                Block(rail, "Rail", Vector3.zero, new Vector3(length, .22f, .22f), trim);
                Block(rail, "Girder", new Vector3(0f, -.3f, .05f), new Vector3(length, .4f, .3f), Girder);
                Block(rail, "Stop left", new Vector3(-length * .5f, .4f, 0f), new Vector3(.45f, 1.1f, .5f), trim);
                Block(rail, "Stop right", new Vector3(length * .5f, .4f, 0f), new Vector3(.45f, 1.1f, .5f), trim);
            }
        }

        // Short vertical tracks with bumpers where they break off.
        static void BuildSideTracks(Transform root)
        {
            var tracks = new GameObject(SideTrackRootName).transform;
            tracks.SetParent(root, false);
            Material[] columnTrims =
            {
                CandyMat("Mint"), CandyMat("Raspberry"), CandyMat("Blueberry"), CandyMat("Lemon")
            };
            var columns = new List<float>();
            for (int i = 0; i < Game.SideTracks.Length; i++)
            {
                Game.SideTrack side = Game.SideTracks[i];
                if (!columns.Contains(side.X)) columns.Add(side.X);
                Material trim = columnTrims[columns.IndexOf(side.X) % columnTrims.Length];
                float bottom = FloorY(side.Bottom) + CarLift - 1.45f;
                float top = FloorY(side.Top) + CarLift + 1.45f;
                var track = new GameObject($"Side track {i} F{side.Bottom}-F{side.Top}").transform;
                track.SetParent(tracks, false);
                track.localPosition = new Vector3(side.X, (bottom + top) * .5f, -.3f);
                float height = top - bottom;
                Block(track, "Left post", new Vector3(-1.5f, 0f, 0f), new Vector3(.22f, height, .24f), trim);
                Block(track, "Right post", new Vector3(1.5f, 0f, 0f), new Vector3(.22f, height, .24f), trim);
                Block(track, "Spine", new Vector3(0f, 0f, .9f), new Vector3(.35f, height, .2f), Girder);
                Block(track, "Top bumper", new Vector3(0f, height * .5f + .2f, 0f), new Vector3(3.4f, .4f, .7f), Girder);
                Block(track, "Bottom bumper", new Vector3(0f, -height * .5f - .2f, 0f), new Vector3(3.4f, .4f, .7f), Girder);
            }
        }

        // A portal frame around the car path with a big destination floor number.
        static void BuildTeleportGates(Transform root, Transform signTemplate)
        {
            var gates = new GameObject(Game.TeleportGateRootName).transform;
            gates.SetParent(root, false);
            Material portal = TeleportPortalMaterial();
            Material frame = CandyMat("Lemon");
            for (int i = 0; i < Game.TeleportGates.Length; i++)
            {
                Game.TeleportGate gate = Game.TeleportGates[i];
                var view = new GameObject(Game.TeleportGateName(i)).transform;
                view.SetParent(gates, false);
                view.localPosition = new Vector3(gate.X, FloorY(gate.Floor) + CarLift, -.8f);
                Block(view, "Portal", new Vector3(0f, 0f, 1.1f), new Vector3(3.4f, 3.4f, .1f), portal);
                Block(view, "Frame left", new Vector3(-1.95f, .1f, 0f), new Vector3(.45f, 3.9f, .7f), frame);
                Block(view, "Frame right", new Vector3(1.95f, .1f, 0f), new Vector3(.45f, 3.9f, .7f), frame);
                Block(view, "Frame top", new Vector3(0f, 2.05f, 0f), new Vector3(4.35f, .45f, .7f), frame);
                bool up = gate.Target > gate.Floor;
                GateText(view, signTemplate, "Destination", ElevatorRound.FloorNumber(gate.Target).ToString(),
                    new Vector3(0f, 4.4f, -.9f), .5f, new Color32(255, 222, 92, 255));
                GateText(view, signTemplate, "Label", up ? "WARP UP" : "WARP DOWN",
                    new Vector3(0f, 2.75f, -.9f), .14f, Color.white);
            }
        }

        static void GateText(Transform parent, Transform template, string name, string text,
            Vector3 position, float size, Color color)
        {
            var label = UnityEngine.Object.Instantiate(template, parent, false);
            label.name = name;
            label.localPosition = position;
            label.localRotation = Quaternion.identity;
            label.localScale = Vector3.one;
            var mesh = label.GetComponent<TextMesh>();
            mesh.text = text;
            mesh.characterSize = size;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontStyle = FontStyle.Bold;
            mesh.color = color;
        }

        static Material TeleportPortalMaterial()
        {
            const string path = MatFolder + "/TeleportPortal.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Crazy Elevator/Storybook Surface");
                if (shader == null) throw new InvalidOperationException("Missing Storybook Surface shader");
                mat = new Material(shader) { name = "TeleportPortal" };
                mat.SetColor("_BaseColor", new Color32(150, 92, 255, 255));
                mat.SetColor("_StripeColor", new Color32(110, 236, 255, 255));
                mat.SetFloat("_StripeScale", 26f);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        static void Block(Transform parent, string name, Vector3 position, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ---------- FBX world backdrops ----------

        static void PlaceBackdrops(Transform root)
        {
            var backdrops = new GameObject(BackdropRootName).transform;
            backdrops.SetParent(root, false);
            PlaceBackdrop(backdrops, WorldArtMaterials.CandyArt, "Candy Backdrop", 1, "BG",
                new[] { "BG", "pPlane4" }, new[] { "pPlane3" });
            PlaceBackdrop(backdrops, WorldArtMaterials.WaterArt, "Water Backdrop", 2, "pPlane1",
                new[] { "pPlane1", "pPlane2" }, null);
        }

        // The dioramas are authored for a camera looking down -Z (Maya front view).
        // Turn them 180° so they face our camera and fit the sky plane to one dimension band.
        static void PlaceBackdrop(Transform parent, string artPath, string name, int dimension,
            string skyPlane, string[] widen, string[] hidden)
        {
            var art = AssetDatabase.LoadAssetAtPath<GameObject>(artPath);
            if (art == null) throw new InvalidOperationException("Missing world art: " + artPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(art, parent);
            instance.name = name;
            Transform t = instance.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            var sky = t.Find(skyPlane);
            if (sky == null) throw new InvalidOperationException(artPath + " has no " + skyPlane);
            Bounds skyBounds = sky.GetComponent<Renderer>().bounds;

            float bottom = BoundaryY(dimension);
            float top = dimension + 1 < ElevatorRound.Dimensions ? BoundaryY(dimension + 1)
                : FloorY(Floors - 1) + CarLift + FloorHeight * 2f;
            float scale = (top - bottom) / skyBounds.size.y;
            Quaternion facing = Quaternion.Euler(0f, 180f, 0f);
            Vector3 wanted = new Vector3(ShaftCenterX, (top + bottom) * .5f, BackdropSkyDepth);
            t.localRotation = facing;
            t.localScale = Vector3.one * scale;
            t.localPosition = wanted - facing * (skyBounds.center * scale);

            // Wide sky planes melt into the camera sky so no frame edge shows.
            foreach (Transform child in t.GetComponentsInChildren<Transform>(true))
            {
                if (widen != null && Array.IndexOf(widen, child.name) >= 0)
                    child.localScale = new Vector3(child.localScale.x * 5f, child.localScale.y, child.localScale.z);
                if (hidden != null && Array.IndexOf(hidden, child.name) >= 0)
                    child.gameObject.SetActive(false);
            }
        }

        // ---------- Materials ----------

        static void ClearWeakDecor(Transform root)
        {
            var remove = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (ReferenceEquals(t, root)) continue;
                if (Array.IndexOf(RemoveNames, t.name) >= 0) remove.Add(t.gameObject);
            }
            foreach (var go in remove)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
        }

        static void PaintLandings(Transform root)
        {
            var officeTrim = Mat("OfficeTrim");
            var officeWall = Mat("OfficeWall");
            var vanilla = CandyMat("Vanilla");
            var pink = CandyMat("CandyFlossPink");
            var deep = AssetDatabase.LoadAssetAtPath<Material>("Assets/Static/ElevatorPersonas/Materials/DeepSeaMetal.mat");
            var sand = Mat("WaterSand");

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                string name = renderer.gameObject.name;
                Material mat = null;
                if (name == "Ground") mat = officeWall;
                else if (name.StartsWith("World landing ") && TryParseFloor(name, out int worldFloor))
                    mat = Pick(worldFloor, officeTrim, vanilla, sand);
                else if (name.StartsWith("Shaft landing ") && TryParseFloor(name, out int shaftFloor))
                    mat = Pick(shaftFloor, officeTrim, pink, deep);
                if (mat != null) renderer.sharedMaterial = mat;
            }
        }

        static Material Pick(int floor, Material office, Material candy, Material water)
        {
            int dimension = floor / FloorsPerDimension;
            return dimension == 0 ? office : dimension == 1 ? candy : water;
        }

        static Material Mat(string name) => LoadMat($"{MatFolder}/{name}.mat");
        static Material CandyMat(string name) => LoadMat($"{CandyMatFolder}/{name}.mat");

        static Material LoadMat(string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) throw new InvalidOperationException("Missing material: " + path);
            return mat;
        }
    }

    // The FBX files ship URP Lit materials tuned for Maya lighting, and the candy /
    // cane textures were not exported. Remap every FBX material to an editable
    // Storybook Surface material in Assets/Art/Worlds/Materials so the dioramas
    // match the rest of the game. Artists can tweak those .mat files directly.
    public static class WorldArtMaterials
    {
        public const string CandyArt = "Assets/Art/Worlds/Models/BG_candy_world.fbx";
        public const string WaterArt = "Assets/Art/Worlds/Models/BG_under_water.fbx";
        // Runtime copies loaded by the dimension reveal card.
        const string CandyArtRuntime = "Assets/Art/Worlds/Resources/BG_candy_world.fbx";
        const string WaterArtRuntime = "Assets/Art/Worlds/Resources/BG_under_water.fbx";
        const string Folder = "Assets/Art/Worlds/Materials";
        const string ShaderName = "Crazy Elevator/Storybook Surface";

        // Must match ElevatorManager.WorldSky so the diorama sky has no visible edge.
        public static readonly Color CandySky = new Color32(196, 172, 246, 255);
        public static readonly Color WaterSky = new Color32(30, 96, 146, 255);

        struct Look
        {
            public string source, asset;
            public Color baseColor, stripe;
            public float stripeScale;
            public bool sky;
        }

        static Look L(string source, string asset, Color32 c) =>
            new Look { source = source, asset = asset, baseColor = c };

        static Look Striped(string source, string asset, Color32 c, Color32 stripe, float scale) =>
            new Look { source = source, asset = asset, baseColor = c, stripe = stripe, stripeScale = scale };

        static Look Sky(string source, string asset, Color c) =>
            new Look { source = source, asset = asset, baseColor = c, sky = true };

        static Look[] CandyLooks => new[]
        {
            Sky("purple01", "Candy_Sky", CandySky),
            L("Cloud_beige", "Candy_Cloud", new Color32(255, 236, 222, 255)),
            L("blue", "Candy_Blue", new Color32(150, 205, 255, 255)),
            Striped("candy", "Candy_Lollipop", new Color32(255, 120, 170, 255), new Color32(255, 246, 250, 255), 260f),
            Striped("cane", "Candy_Cane", new Color32(236, 54, 88, 255), new Color32(255, 248, 248, 255), 70f),
            L("green", "Candy_Hill", new Color32(150, 222, 170, 255)),
            L("mint", "Candy_Mint", new Color32(160, 244, 214, 255)),
            L("openPBR_shader1", "Candy_Window", new Color32(255, 214, 120, 255)),
            L("pink", "Candy_Pink", new Color32(255, 128, 160, 255)),
            L("pink02", "Candy_Blush", new Color32(255, 214, 240, 255)),
        };

        static Look[] WaterLooks => new[]
        {
            Sky("lambert3", "Water_Sky", WaterSky),
            L("Blue02", "Water_Ruins", new Color32(34, 74, 122, 255)),
            L("deepblue", "Water_Deep", new Color32(26, 54, 98, 255)),
            L("Green01", "Water_Seaweed", new Color32(40, 168, 150, 255)),
            L("bubbe", "Water_Bubble", new Color32(200, 238, 255, 255)),
            L("coral_blue", "Water_CoralBlue", new Color32(120, 140, 255, 255)),
            L("coral_pink", "Water_CoralPink", new Color32(255, 128, 190, 255)),
            L("coral_pink02", "Water_CoralPlum", new Color32(196, 86, 160, 255)),
            L("openPBR_shader1", "Water_Rock", new Color32(186, 206, 224, 255)),
        };

        public static void Apply()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Art/Worlds", "Materials");
            var shader = Shader.Find(ShaderName);
            if (shader == null) throw new InvalidOperationException("Missing shader " + ShaderName);
            Remap(CandyLooks, shader, CandyArt, CandyArtRuntime);
            Remap(WaterLooks, shader, WaterArt, WaterArtRuntime);
        }

        static void Remap(Look[] looks, Shader shader, params string[] modelPaths)
        {
            var materials = new Material[looks.Length];
            for (int i = 0; i < looks.Length; i++) materials[i] = Ensure(looks[i], shader);
            foreach (string path in modelPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null) continue;
                bool changed = false;
                var existing = importer.GetExternalObjectMap();
                for (int i = 0; i < looks.Length; i++)
                {
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), looks[i].source);
                    if (existing.TryGetValue(id, out var current) && current == materials[i]) continue;
                    importer.AddRemap(id, materials[i]);
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        static Material Ensure(Look look, Shader shader)
        {
            string path = $"{Folder}/{look.asset}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = look.asset };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            // A camera-facing plane gets the shader's warm highlight; undo it for sky planes.
            Color baseColor = look.sky ? SkyBase(look.baseColor) : look.baseColor;
            mat.SetColor("_BaseColor", baseColor);
            mat.SetColor("_StripeColor", look.stripeScale > 0f ? look.stripe : baseColor);
            mat.SetFloat("_StripeScale", look.stripeScale);
            mat.SetFloat("_PatternStrength", look.sky ? 0f : .035f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Color SkyBase(Color sky)
        {
            Color linear = sky.linear;
            var highlight = new Color(1.03f, 1.01f, .96f);
            return new Color(linear.r / highlight.r, linear.g / highlight.g, linear.b / highlight.b).gamma;
        }
    }
}
#endif
