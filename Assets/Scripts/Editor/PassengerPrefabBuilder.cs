using System;
using System.Collections.Generic;
using CrazyElevator.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CrazyElevator.EditorTools
{
    // Rebuilds the illustrated roly-poly passenger family as editable prefab art.
    // This is Editor-only: gameplay instantiates the saved prefabs and never creates
    // passenger geometry at runtime.
    public static class PassengerPrefabBuilder
    {
        const string ResourcePrefabFolder = "Assets/Prefabs/Resources";
        const string PersonaFolder = "Assets/Static/ElevatorPersonas";
        const string ConceptPrefabFolder = PersonaFolder + "/Prefabs/PassengerConcepts";
        const string MaterialFolder = PersonaFolder + "/Materials/Passengers";
        const string ElevatorScenePath = PersonaFolder + "/Prefabs/ExtendedElevatorScene.prefab";
        const string StorybookShaderName = "Crazy Elevator/Storybook Surface";

        static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

        static readonly Color Ink = C(29, 35, 49);
        static readonly Color White = C(247, 245, 235);
        static readonly Color Navy = C(38, 53, 91);
        static readonly Color OfficeBlue = C(68, 82, 105);
        static readonly Color ShirtBlue = C(216, 226, 229);
        static readonly Color SkinDark = C(133, 82, 54);
        static readonly Color SkinWarm = C(218, 151, 108);
        static readonly Color SkinPink = C(250, 183, 183);
        static readonly Color CandyPink = C(239, 75, 137);
        static readonly Color CandyBlue = C(105, 126, 238);
        static readonly Color CandyLilac = C(148, 151, 240);
        static readonly Color Maroon = C(101, 31, 61);
        static readonly Color Brown = C(92, 52, 36);
        static readonly Color GrannyBrown = C(133, 80, 57);
        static readonly Color Gray = C(194, 195, 197);
        static readonly Color Coral = C(255, 102, 92);
        static readonly Color Orange = C(243, 123, 54);
        static readonly Color Yellow = C(246, 193, 75);
        static readonly Color Green = C(64, 176, 108);
        static readonly Color Teal = C(60, 145, 146);
        static readonly Color Aqua = C(104, 204, 198);
        static readonly Color OceanBlue = C(65, 127, 164);
        static readonly Color OceanDark = C(35, 85, 119);
        static readonly Color Seaweed = C(66, 139, 69);
        static readonly Color Sand = C(215, 202, 162);
        static readonly Color Gold = C(218, 170, 69);

        [MenuItem("Tools/Crazy Elevator/Rebuild Illustrated Passenger Prefabs")]
        public static void Rebuild()
        {
            EnsureFolder(ConceptPrefabFolder);
            EnsureFolder(MaterialFolder);
            materials.Clear();

            // The two clean bases from the reference sheet are useful starting
            // points for designers making more passengers in Prefab Mode.
            Build("BASE-ROUND", ConceptPrefabFolder + "/Passenger-BASE-ROUND.prefab", BuildRoundBase, .48f);
            Build("BASE-SQUARE", ConceptPrefabFolder + "/Passenger-BASE-SQUARE.prefab", BuildSquareBase, .48f);

            // Active game kinds. Their gameplay identities stay unchanged while
            // their silhouettes and clothing follow the supplied character sheet.
            Build("COURIER", ResourcePrefabFolder + "/Passenger-COURIER.prefab", BuildOfficeBadge, .5f);
            Build("PREGNANT", ResourcePrefabFolder + "/Passenger-PREGNANT.prefab", BuildCandyParty, .52f);
            Build("INTERVIEW", ResourcePrefabFolder + "/Passenger-INTERVIEW.prefab", BuildOfficeTie, .5f);
            Build("BOSS", ResourcePrefabFolder + "/Passenger-BOSS.prefab", BuildCandyGentleman, .55f);
            Build("ELDERLY", ResourcePrefabFolder + "/Passenger-ELDERLY.prefab", BuildGranny, .53f);
            Build("GROUP", ResourcePrefabFolder + "/Passenger-GROUP.prefab", BuildClownTrio, .82f);
            Build("HANDYMAN", PersonaFolder + "/Prefabs/Passenger-HANDYMAN.prefab", BuildAtlanteanMechanic, .62f);

            // Standalone concept exports retain the two underwater ideas even
            // though only the mechanic is currently wired into the round catalog.
            Build("ATLANTEAN", ConceptPrefabFolder + "/Passenger-ATLANTEAN.prefab", BuildAtlantean, .6f);
            Build("DOLPHIN", ConceptPrefabFolder + "/Passenger-DOLPHIN.prefab", BuildDolphinPassenger, .65f);

            BuildShowcase();
            WirePassengerReferences();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Rebuilt illustrated 3D passenger prefabs and refreshed ElevatorScene references.");
        }

        [MenuItem("Tools/Crazy Elevator/Apply Matte Storybook Style")]
        public static void ApplyMatteStorybookStyle()
        {
            Shader storybook = Shader.Find(StorybookShaderName);
            if (storybook == null)
                throw new InvalidOperationException("Missing shader: " + StorybookShaderName);

            string[] roots =
            {
                "Assets/Prefabs/Resources",
                "Assets/Static/WorldBands/Materials",
                PersonaFolder + "/Materials"
            };

            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null || mat.shader.name == "CrazyElevator/Interior World Backdrop")
                    continue;

                bool isPassenger = path.StartsWith(MaterialFolder + "/", StringComparison.Ordinal);
                bool supported = mat.shader.name == StorybookShaderName
                    || mat.shader.name == "Universal Render Pipeline/Lit"
                    || mat.shader.name == "Universal Render Pipeline/Simple Lit"
                    || mat.shader.name == "Standard";
                if (!supported) continue;

                // Keep authored stripes and paper patterns on materials that
                // were already using the storybook shader. Passenger colours
                // are the exception because their builder owns these values.
                bool alreadyStorybook = mat.shader == storybook;
                if (alreadyStorybook && !isPassenger) continue;

                Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
                if (!alreadyStorybook)
                {
                    mat.shader = storybook;
                    changed++;
                }

                mat.SetColor("_BaseColor", baseColor);
                mat.SetColor("_StripeColor", baseColor);
                mat.SetFloat("_StripeScale", 0);
                mat.SetColor("_PatternColor", Color.Lerp(baseColor, Ink, .72f));
                mat.SetFloat("_PatternScale", 4.5f);
                mat.SetFloat("_PatternStrength", isPassenger ? 0f : .018f);
                mat.SetFloat("_PatternMode", 0);
                mat.SetFloat("_ToonSteps", 3);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Applied the matte storybook style to {changed} lit materials.");
        }

        [MenuItem("Tools/Crazy Elevator/Open Illustrated Passenger Preview")]
        public static void OpenPreview()
        {
            GameObject preview = AssetDatabase.LoadAssetAtPath<GameObject>(
                ConceptPrefabFolder + "/Passenger-SHOWCASE.prefab");
            Selection.activeObject = preview;
            AssetDatabase.OpenAsset(preview);
        }

        static void BuildShowcase()
        {
            string[] paths =
            {
                ResourcePrefabFolder + "/Passenger-INTERVIEW.prefab",
                ResourcePrefabFolder + "/Passenger-COURIER.prefab",
                ResourcePrefabFolder + "/Passenger-BOSS.prefab",
                ResourcePrefabFolder + "/Passenger-PREGNANT.prefab",
                ResourcePrefabFolder + "/Passenger-ELDERLY.prefab",
                ResourcePrefabFolder + "/Passenger-GROUP.prefab",
                ConceptPrefabFolder + "/Passenger-ATLANTEAN.prefab",
                PersonaFolder + "/Prefabs/Passenger-HANDYMAN.prefab",
                ConceptPrefabFolder + "/Passenger-DOLPHIN.prefab"
            };

            var root = new GameObject("Passenger-SHOWCASE");
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                GameObject passenger = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                passenger.transform.SetParent(root.transform, false);
                passenger.transform.localPosition = new Vector3((i - (paths.Length - 1) * .5f) * 1.65f, 0, 0);
                foreach (TextMesh label in passenger.GetComponentsInChildren<TextMesh>(true))
                    label.gameObject.SetActive(false);
            }
            PrefabUtility.SaveAsPrefabAsset(root, ConceptPrefabFolder + "/Passenger-SHOWCASE.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        static void Build(string kind, string path, Action<Transform, List<Renderer>> art, float radius)
        {
            var root = new GameObject("Passenger-" + kind);
            var hit = root.AddComponent<CapsuleCollider>();
            hit.radius = radius;
            hit.height = 1.92f;
            hit.center = new Vector3(0, .91f, 0);

            var accents = new List<Renderer>();
            art(root.transform, accents);

            TextMesh speech = AddLabel(root.transform, "Speech bubble", new Vector3(0, 2.42f, 0), "", .022f);
            TextMesh destination = AddLabel(root.transform, "Destination badge", new Vector3(0, 2.08f, 0), kind + "  1", .023f);
            destination.fontStyle = FontStyle.Bold;

            var view = root.AddComponent<PassengerView>();
            view.kind = kind;
            view.speech = speech;
            view.destination = destination;
            // Only tiny accent pieces are recoloured by the existing gameplay
            // palette; authored clothes and faces keep their concept colours.
            view.jackets = accents.ToArray();

            bool success;
            PrefabUtility.SaveAsPrefabAsset(root, path, out success);
            UnityEngine.Object.DestroyImmediate(root);
            if (!success) throw new InvalidOperationException("Could not save passenger prefab: " + path);
        }

        static void BuildRoundBase(Transform root, List<Renderer> accents)
        {
            RolyBody(root, White, White);
            Sphere(root, "Round head", V(0, 1.42f, 0), V(.72f, .72f, .66f), White);
            SimpleFace(root, 1.43f, .34f, Ink);
        }

        static void BuildSquareBase(Transform root, List<Renderer> accents)
        {
            RolyBody(root, White, White);
            Cube(root, "Square head", V(0, 1.42f, 0), V(.72f, .62f, .62f), White);
            SimpleFace(root, 1.42f, .34f, Ink);
        }

        static void BuildOfficeTie(Transform root, List<Renderer> accents)
        {
            RolyBody(root, ShirtBlue, Ink);
            Cube(root, "Square head", V(0, 1.43f, 0), V(.72f, .64f, .62f), SkinDark);
            SimpleFace(root, 1.44f, .34f, Ink, closed: true);

            // White collar, navy tie, belt and the little pocket from the sheet.
            Cube(root, "Left collar", V(-.13f, 1.02f, -.39f), V(.22f, .16f, .035f), White, V(0, 0, -35));
            Cube(root, "Right collar", V(.13f, 1.02f, -.39f), V(.22f, .16f, .035f), White, V(0, 0, 35));
            Cube(root, "Tie", V(0, .79f, -.43f), V(.13f, .43f, .045f), Navy);
            Sphere(root, "Tie knot", V(0, 1.00f, -.44f), V(.15f, .12f, .07f), Navy);
            Cube(root, "Belt", V(0, .45f, -.42f), V(.78f, .09f, .05f), Brown);
            Cube(root, "Shirt pocket", V(.24f, .79f, -.43f), V(.19f, .13f, .035f), White);
            accents.Add(Sphere(root, "Palette pin", V(.24f, .82f, -.46f), V(.055f, .055f, .035f), OfficeBlue));
        }

        static void BuildOfficeBadge(Transform root, List<Renderer> accents)
        {
            RolyBody(root, OfficeBlue, Ink);
            Cube(root, "Square head", V(0, 1.43f, 0), V(.72f, .64f, .62f), SkinWarm);
            SimpleFace(root, 1.44f, .34f, Ink);
            Cube(root, "White shirt panel", V(0, .83f, -.39f), V(.28f, .55f, .04f), White);

            Capsule(root, "Left lanyard", V(-.10f, .97f, -.43f), V(.035f, .30f, .035f), Ink, V(0, 0, -18));
            Capsule(root, "Right lanyard", V(.10f, .97f, -.43f), V(.035f, .30f, .035f), Ink, V(0, 0, 18));
            Renderer badge = Cube(root, "ID badge", V(0, .72f, -.46f), V(.22f, .16f, .045f), White);
            accents.Add(Cube(root, "ID stripe", V(0, .72f, -.49f), V(.15f, .035f, .02f), OceanBlue));

            // Courier identity remains readable without losing the office design.
            Cube(root, "Parcel", V(.52f, .68f, -.04f), V(.32f, .32f, .32f), Sand, V(0, 0, -8));
            Cube(root, "Parcel tape", V(.52f, .68f, -.21f), V(.055f, .32f, .015f), Brown, V(0, 0, -8));
            if (badge != null) badge.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildCandyGentleman(Transform root, List<Renderer> accents)
        {
            RolyBody(root, C(250, 205, 215), CandyLilac);
            Sphere(root, "Round head", V(0, 1.43f, 0), V(.72f, .72f, .66f), SkinPink);
            SimpleFace(root, 1.45f, .34f, Ink);

            Cylinder(root, "Top-hat brim", V(0, 1.82f, 0), V(.76f, .055f, .60f), C(249, 203, 219));
            Cylinder(root, "Top-hat crown", V(0, 2.01f, 0), V(.54f, .22f, .47f), C(249, 203, 219));
            Cube(root, "Hat ribbon", V(0, 1.88f, -.25f), V(.54f, .09f, .035f), CandyPink);
            for (int i = -2; i <= 2; i++)
                Cube(root, "Blue hat dash", V(i * .105f, 1.88f, -.275f), V(.052f, .075f, .02f), CandyBlue, V(0, 0, -20));

            Sphere(root, "Left moustache curl", V(-.15f, 1.29f, -.36f), V(.33f, .16f, .09f), CandyPink, V(0, 0, -14));
            Sphere(root, "Right moustache curl", V(.15f, 1.29f, -.36f), V(.33f, .16f, .09f), CandyPink, V(0, 0, 14));
            Bow(root, V(0, 1.06f, -.40f), CandyBlue, CandyPink, .22f);
            Cube(root, "Left tailcoat", V(-.33f, .73f, -.36f), V(.22f, .58f, .08f), Maroon, V(0, 0, 8));
            Cube(root, "Right tailcoat", V(.33f, .73f, -.36f), V(.22f, .58f, .08f), Maroon, V(0, 0, -8));
            CandyStripeSkirt(root);
            Bow(root, V(0, .42f, -.46f), CandyPink, CandyPink, .20f);
            accents.Add(Sphere(root, "Palette jewel", V(0, .42f, -.50f), V(.12f, .12f, .06f), CandyPink));
        }

        static void BuildCandyParty(Transform root, List<Renderer> accents)
        {
            RolyBody(root, CandyBlue, CandyLilac);
            Sphere(root, "Round head", V(0, 1.43f, 0), V(.72f, .72f, .66f), SkinPink);
            SimpleFace(root, 1.45f, .34f, Ink);
            Sphere(root, "Left rosy cheek", V(-.29f, 1.35f, -.34f), V(.20f, .20f, .08f), CandyPink);
            Sphere(root, "Right rosy cheek", V(.29f, 1.35f, -.34f), V(.20f, .20f, .08f), CandyPink);

            // Layered party hat and pom-pom.
            for (int i = 0; i < 5; i++)
            {
                float y = 1.78f + i * .095f;
                float width = .42f - i * .065f;
                Cylinder(root, i % 2 == 0 ? "Pink party-hat band" : "Blue party-hat band",
                    V(0, y, 0), V(width, .065f, width), i % 2 == 0 ? CandyPink : CandyBlue);
            }
            Sphere(root, "Party-hat pom", V(0, 2.28f, 0), V(.15f, .15f, .15f), CandyLilac);

            Bow(root, V(0, 1.04f, -.42f), CandyPink, CandyPink, .22f);
            Cube(root, "Overall bib", V(0, .78f, -.43f), V(.56f, .43f, .05f), CandyBlue);
            Sphere(root, "Left overall button", V(-.25f, .98f, -.46f), V(.08f, .08f, .045f), Gold);
            Sphere(root, "Right overall button", V(.25f, .98f, -.46f), V(.08f, .08f, .045f), Gold);
            Cube(root, "Belly pocket", V(0, .66f, -.48f), V(.31f, .16f, .04f), CandyBlue);
            CandyStripeSkirt(root);
            accents.Add(Cube(root, "Pocket accent", V(0, .71f, -.51f), V(.18f, .025f, .015f), CandyLilac));
        }

        static void BuildGranny(Transform root, List<Renderer> accents)
        {
            RolyBody(root, GrannyBrown, Navy);
            Sphere(root, "Round head", V(0, 1.43f, 0), V(.70f, .68f, .64f), SkinWarm);
            SimpleFace(root, 1.43f, .34f, Ink, closed: true);

            // Cloud-like gray hair cap.
            for (int i = 0; i < 9; i++)
            {
                float angle = Mathf.Lerp(205f, -25f, i / 8f) * Mathf.Deg2Rad;
                Sphere(root, "Gray hair curl", V(Mathf.Cos(angle) * .37f, 1.65f + Mathf.Sin(angle) * .24f, .01f),
                    V(.25f, .25f, .25f), Gray);
            }

            // Small silver glasses, bridge and dark brows.
            Cylinder(root, "Left glasses lens", V(-.16f, 1.46f, -.35f), V(.17f, .025f, .17f), White, V(90, 0, 0));
            Cylinder(root, "Right glasses lens", V(.16f, 1.46f, -.35f), V(.17f, .025f, .17f), White, V(90, 0, 0));
            Sphere(root, "Left pupil", V(-.16f, 1.46f, -.385f), V(.055f, .055f, .035f), Ink);
            Sphere(root, "Right pupil", V(.16f, 1.46f, -.385f), V(.055f, .055f, .035f), Ink);
            Cube(root, "Glasses bridge", V(0, 1.46f, -.38f), V(.12f, .025f, .025f), Ink);
            Cube(root, "Left brow", V(-.17f, 1.58f, -.37f), V(.22f, .035f, .03f), Ink, V(0, 0, -5));
            Cube(root, "Right brow", V(.17f, 1.58f, -.37f), V(.22f, .035f, .03f), Ink, V(0, 0, 5));

            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Lerp(205f, 335f, i / 8f) * Mathf.Deg2Rad;
                Sphere(root, "Pearl necklace", V(Mathf.Cos(a) * .30f, 1.05f + Mathf.Sin(a) * .17f, -.40f),
                    V(.075f, .075f, .055f), White);
            }
            for (int i = 0; i < 3; i++)
                Sphere(root, "Cardigan button", V(0, .88f - i * .18f, -.44f), V(.045f, .045f, .03f), Ink);
            accents.Add(Sphere(root, "Pearl accent", V(0, 1.02f, -.44f), V(.08f, .08f, .055f), White));

            // Cane follows the slow-arrival passenger silhouette.
            Capsule(root, "Cane", V(.50f, .58f, -.03f), V(.055f, .48f, .055f), Brown, V(0, 0, -7));
            Sphere(root, "Cane handle", V(.43f, 1.03f, -.03f), V(.20f, .075f, .075f), Brown);
        }

        static void BuildClownTrio(Transform root, List<Renderer> accents)
        {
            BuildClown(root, V(0, 0, 0), 1f);
            BuildCandyFriend(root, "Left candy friend", V(-.64f, .04f, .10f), .60f, CandyPink, CandyBlue);
            BuildCandyFriend(root, "Right candy friend", V(.64f, .04f, .10f), .60f, Aqua, Yellow);
            accents.Add(Sphere(root, "Palette clown nose", V(0, 1.43f, -.40f), V(.17f, .17f, .10f), Coral));
        }

        static void BuildClown(Transform root, Vector3 offset, float s)
        {
            Sphere(root, "Clown lower body", offset + V(0, .48f, 0) * s, V(.70f, .67f, .58f) * s, Yellow);
            Capsule(root, "Clown torso", offset + V(0, .87f, 0) * s, V(.52f, .40f, .42f) * s, Green);
            Sphere(root, "Clown white face", offset + V(0, 1.43f, 0) * s, V(.72f, .70f, .66f) * s, White);
            SimpleFace(root, offset.y + 1.45f * s, .35f * s - offset.z, Ink, false, offset.x, s);

            // Scalloped red hair surrounding the face.
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                Sphere(root, "Red clown curl", offset + V(Mathf.Cos(a) * .42f, 1.48f + Mathf.Sin(a) * .38f, .03f) * s,
                    V(.25f, .25f, .22f) * s, Coral);
            }
            Sphere(root, "Left clown cheek", offset + V(-.29f, 1.36f, -.36f) * s, V(.19f, .19f, .08f) * s, Coral);
            Sphere(root, "Right clown cheek", offset + V(.29f, 1.36f, -.36f) * s, V(.19f, .19f, .08f) * s, Coral);
            Sphere(root, "Clown nose", offset + V(0, 1.43f, -.40f) * s, V(.17f, .17f, .10f) * s, Coral);

            for (int i = 0; i < 8; i++)
            {
                float a = Mathf.Lerp(195f, 345f, i / 7f) * Mathf.Deg2Rad;
                Sphere(root, "Ruffle collar", offset + V(Mathf.Cos(a) * .35f, 1.05f + Mathf.Sin(a) * .16f, -.39f) * s,
                    V(.16f, .12f, .07f) * s, White);
            }
            for (int i = -1; i <= 1; i++)
                Cube(root, "Green suit stripe", offset + V(i * .26f, .55f, -.43f) * s,
                    V(.13f, .60f, .045f) * s, Green);
            for (int i = 0; i < 3; i++)
                Sphere(root, "Orange clown button", offset + V(0, .90f - i * .24f, -.48f) * s,
                    V(.12f, .12f, .065f) * s, Orange);
        }

        static void BuildCandyFriend(Transform root, string name, Vector3 offset, float s, Color face, Color outfit)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(root, false);
            pivot.localPosition = offset;
            Sphere(pivot, "Body", V(0, .48f, .08f) * s, V(.62f, .72f, .52f) * s, outfit);
            Sphere(pivot, "Head", V(0, 1.30f, 0) * s, V(.64f, .64f, .58f) * s, face);
            SimpleFace(pivot, 1.31f * s, .31f * s, Ink, false, 0, s);
            Cylinder(pivot, "Candy hat", V(0, 1.70f, 0) * s, V(.34f, .17f, .30f) * s, outfit);
        }

        static void BuildAtlanteanMechanic(Transform root, List<Renderer> accents)
        {
            BuildAtlanteanBody(root, true);
            BuildMiniDolphin(root, V(.65f, 1.10f, .10f), .42f);

            // Direct children with these names are animated by the existing repair code.
            Capsule(root, "Arm", V(-.43f, .86f, -.02f), V(.13f, .43f, .13f), Teal, V(0, 0, -12));
            Capsule(root, "Arm", V(.43f, .86f, -.02f), V(.13f, .43f, .13f), Teal, V(0, 0, 12));
            Capsule(root, "Wrench shaft", V(-.55f, .72f, -.18f), V(.045f, .35f, .045f), Gray, V(0, 0, -24));
            Sphere(root, "Wrench head", V(-.68f, 1.02f, -.18f), V(.16f, .11f, .055f), Gray, V(0, 0, -24));
            Cube(root, "Wrench jaw", V(-.76f, 1.08f, -.18f), V(.11f, .055f, .06f), Ink, V(0, 0, -24));
            Cube(root, "Toolbox", V(.50f, .55f, -.03f), V(.38f, .32f, .30f), Brown);
            Cube(root, "Toolbox lid", V(.50f, .73f, -.03f), V(.42f, .08f, .33f), Gold);
            Capsule(root, "Toolbox handle", V(.50f, .88f, -.03f), V(.16f, .12f, .045f), Gold, V(0, 0, 90));
            accents.Add(Sphere(root, "Palette shell badge", V(.23f, .80f, -.48f), V(.11f, .11f, .065f), Gold));
        }

        static void BuildAtlantean(Transform root, List<Renderer> accents)
        {
            BuildAtlanteanBody(root, false);
            accents.Add(Sphere(root, "Palette moon badge", V(0, .80f, -.48f), V(.12f, .12f, .065f), White));
        }

        static void BuildAtlanteanBody(Transform root, bool mechanic)
        {
            Sphere(root, "Ocean lower body", V(0, .46f, 0), V(.71f, .66f, .58f), Navy);
            Capsule(root, "Ocean torso", V(0, .88f, 0), V(.52f, .42f, .42f), mechanic ? OceanBlue : Teal);
            Sphere(root, "Round ocean head", V(0, 1.47f, 0), V(.75f, .72f, .68f), mechanic ? OceanBlue : Teal);
            SimpleFace(root, 1.47f, .36f, Ink);
            Sphere(root, "Dark face stripe", V(0, 1.57f, -.35f), V(.26f, .48f, .05f), OceanDark);

            // Twin horns and three-pronged side fins.
            Capsule(root, "Left horn", V(-.24f, 1.90f, 0), V(.095f, .31f, .095f), OceanDark, V(0, 0, -10));
            Capsule(root, "Right horn", V(.24f, 1.90f, 0), V(.095f, .31f, .095f), OceanDark, V(0, 0, 10));
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = -1; i <= 1; i++)
                {
                    float angle = side * (58f + i * 22f);
                    Capsule(root, side < 0 ? "Left aqua fin" : "Right aqua fin",
                        V(side * (.53f + Mathf.Abs(i) * .06f), 1.47f + i * .13f, .02f),
                        V(.10f, .31f, .035f), Aqua, V(0, 0, angle));
                }
                Capsule(root, side < 0 ? "Left fin rib" : "Right fin rib",
                    V(side * .51f, 1.47f, -.015f), V(.035f, .34f, .025f), OceanDark, V(0, 0, side * 65));
            }

            Cylinder(root, "Gold collar", V(0, 1.10f, 0), V(.56f, .055f, .45f), Gold);
            Cube(root, "White wrap top", V(0, .93f, -.40f), V(.70f, .22f, .045f), White);
            Cube(root, "Sand sash", V(-.02f, .52f, -.43f), V(.78f, .22f, .045f), Sand, V(0, 0, -14));
            Cube(root, "Brown rope belt", V(0, .70f, -.45f), V(.78f, .09f, .05f), Brown);
            for (int i = -3; i <= 3; i++)
                Sphere(root, "Rope knot", V(i * .11f, .70f, -.49f), V(.065f, .065f, .04f), Brown);

            // Seaweed tuft from the second Atlantis design.
            Capsule(root, "Seaweed hair", V(.35f, 1.96f, .04f), V(.10f, .35f, .08f), Seaweed, V(0, 0, 28));
            Capsule(root, "Seaweed hair", V(.47f, 2.00f, .04f), V(.09f, .31f, .075f), Seaweed, V(0, 0, 7));
            Capsule(root, "Seaweed hair", V(.55f, 1.93f, .04f), V(.08f, .28f, .07f), Seaweed, V(0, 0, -18));

            if (!mechanic)
            {
                Sphere(root, "Crescent pendant", V(0, .83f, -.49f), V(.13f, .16f, .055f), White);
                Sphere(root, "Pendant cutout", V(.05f, .86f, -.53f), V(.10f, .12f, .045f), Teal);
            }
        }

        static void BuildDolphinPassenger(Transform root, List<Renderer> accents)
        {
            BuildMiniDolphin(root, V(0, .82f, 0), 1f);
            accents.Add(Sphere(root, "Palette dolphin cheek", V(.18f, 1.48f, -.39f), V(.12f, .07f, .05f), Coral));
        }

        static void BuildMiniDolphin(Transform root, Vector3 offset, float s)
        {
            Sphere(root, "Dolphin body", offset + V(0, .25f, 0) * s, V(.78f, .45f, .45f) * s, OceanBlue, V(0, 0, -18));
            Sphere(root, "Dolphin head", offset + V(-.35f, .56f, 0) * s, V(.52f, .42f, .42f) * s, OceanBlue);
            Sphere(root, "Dolphin snout", offset + V(-.68f, .48f, -.02f) * s, V(.45f, .17f, .20f) * s, OceanBlue);
            Sphere(root, "Dolphin eye", offset + V(-.42f, .66f, -.36f) * s, V(.09f, .11f, .06f) * s, Ink);
            Sphere(root, "Dolphin smile", offset + V(-.67f, .48f, -.20f) * s, V(.09f, .045f, .035f) * s, Coral);
            Capsule(root, "Dolphin top fin", offset + V(.05f, .64f, .04f) * s, V(.10f, .28f, .06f) * s, OceanBlue, V(0, 0, -24));
            Capsule(root, "Dolphin left flipper", offset + V(-.10f, .10f, -.23f) * s, V(.10f, .30f, .06f) * s, OceanBlue, V(55, 0, 35));
            Capsule(root, "Dolphin right flipper", offset + V(.05f, .12f, .20f) * s, V(.10f, .30f, .06f) * s, OceanBlue, V(-55, 0, -25));
            Capsule(root, "Dolphin tail stem", offset + V(.47f, -.05f, .02f) * s, V(.16f, .40f, .14f) * s, OceanBlue, V(0, 0, -36));
            Capsule(root, "Dolphin upper tail", offset + V(.70f, -.34f, .02f) * s, V(.11f, .28f, .07f) * s, OceanBlue, V(0, 0, -62));
            Capsule(root, "Dolphin lower tail", offset + V(.55f, -.46f, .02f) * s, V(.11f, .28f, .07f) * s, OceanBlue, V(0, 0, 18));
        }

        static void RolyBody(Transform root, Color top, Color bottom)
        {
            Sphere(root, "Rounded lower body", V(0, .48f, 0), V(.70f, .67f, .58f), bottom);
            Capsule(root, "Tapered torso", V(0, .88f, 0), V(.52f, .40f, .42f), top);
        }

        static void CandyStripeSkirt(Transform root)
        {
            for (int i = -2; i <= 2; i++)
            {
                Color color = i % 2 == 0 ? CandyPink : CandyLilac;
                Cube(root, "Candy skirt stripe", V(i * .14f, .35f, -.46f), V(.12f, .42f, .045f), color, V(0, 0, i * 3f));
            }
        }

        static void Bow(Transform root, Vector3 position, Color wing, Color knot, float size)
        {
            Sphere(root, "Left bow wing", position + V(-size * .72f, 0, 0), V(size, size * .68f, size * .34f), wing, V(0, 0, 18));
            Sphere(root, "Right bow wing", position + V(size * .72f, 0, 0), V(size, size * .68f, size * .34f), wing, V(0, 0, -18));
            Sphere(root, "Bow knot", position + V(0, 0, -.015f), V(size * .62f, size * .62f, size * .38f), knot);
        }

        static void SimpleFace(Transform root, float y, float frontZ, Color color, bool closed = false, float x = 0, float scale = 1)
        {
            if (closed)
            {
                Cube(root, "Left closed eye", V(x - .16f * scale, y, -frontZ), V(.18f, .035f, .035f) * scale, color, V(0, 0, -3));
                Cube(root, "Right closed eye", V(x + .16f * scale, y, -frontZ), V(.18f, .035f, .035f) * scale, color, V(0, 0, 3));
            }
            else
            {
                Capsule(root, "Left eye", V(x - .16f * scale, y, -frontZ), V(.045f, .11f, .035f) * scale, color);
                Capsule(root, "Right eye", V(x + .16f * scale, y, -frontZ), V(.045f, .11f, .035f) * scale, color);
            }
        }

        static TextMesh AddLabel(Transform parent, string name, Vector3 position, string text, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 48;
            label.characterSize = size;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = White;
            return label;
        }

        static Renderer Sphere(Transform parent, string name, Vector3 position, Vector3 scale, Color color, Vector3 euler = default)
            => Part(parent, name, PrimitiveType.Sphere, position, scale, color, euler);

        static Renderer Cube(Transform parent, string name, Vector3 position, Vector3 scale, Color color, Vector3 euler = default)
            => Part(parent, name, PrimitiveType.Cube, position, scale, color, euler);

        static Renderer Capsule(Transform parent, string name, Vector3 position, Vector3 scale, Color color, Vector3 euler = default)
            => Part(parent, name, PrimitiveType.Capsule, position, scale, color, euler);

        static Renderer Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Color color, Vector3 euler = default)
            => Part(parent, name, PrimitiveType.Cylinder, position, scale, color, euler);

        static Renderer Part(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale, Color color, Vector3 euler)
        {
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = Mat(ColorName(color), color);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        static Material Mat(string name, Color color)
        {
            if (materials.TryGetValue(name, out Material cached)) return cached;
            string path = MaterialFolder + "/Passenger-" + name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader storybook = Shader.Find(StorybookShaderName);
            if (storybook == null)
                throw new InvalidOperationException("Missing shader: " + StorybookShaderName);
            if (mat == null)
            {
                mat = new Material(storybook) { name = "Passenger " + name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != storybook)
            {
                mat.shader = storybook;
            }

            mat.SetColor("_BaseColor", color);
            mat.SetColor("_StripeColor", color);
            mat.SetFloat("_StripeScale", 0);
            mat.SetColor("_PatternColor", Color.Lerp(color, Ink, .72f));
            mat.SetFloat("_PatternScale", 4.5f);
            mat.SetFloat("_PatternStrength", 0);
            mat.SetFloat("_PatternMode", 0);
            mat.SetFloat("_ToonSteps", 3);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            materials[name] = mat;
            return mat;
        }

        static void WirePassengerReferences()
        {
            string[] paths =
            {
                ResourcePrefabFolder + "/Passenger-COURIER.prefab",
                ResourcePrefabFolder + "/Passenger-PREGNANT.prefab",
                ResourcePrefabFolder + "/Passenger-INTERVIEW.prefab",
                ResourcePrefabFolder + "/Passenger-BOSS.prefab",
                ResourcePrefabFolder + "/Passenger-ELDERLY.prefab",
                ResourcePrefabFolder + "/Passenger-GROUP.prefab",
                PersonaFolder + "/Prefabs/Passenger-HANDYMAN.prefab"
            };
            var views = new PassengerView[paths.Length];
            for (int i = 0; i < paths.Length; i++)
                views[i] = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]).GetComponent<PassengerView>();

            GameObject sceneRoot = PrefabUtility.LoadPrefabContents(ElevatorScenePath);
            try
            {
                ElevatorScene scene = sceneRoot.GetComponent<ElevatorScene>();
                if (scene == null) throw new InvalidOperationException("ExtendedElevatorScene is missing ElevatorScene.");
                scene.passengers = views;
                EditorUtility.SetDirty(scene);
                PrefabUtility.SaveAsPrefabAsset(sceneRoot, ElevatorScenePath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(sceneRoot);
            }

            if (!AssetDatabase.IsValidFolder("Assets/Data/Passengers")) return;
            foreach (string guid in AssetDatabase.FindAssets("t:PassengerData", new[] { "Assets/Data/Passengers" }))
            {
                PassengerData data = AssetDatabase.LoadAssetAtPath<PassengerData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                for (int i = 0; i < views.Length; i++)
                {
                    if (views[i] == null || views[i].ResolvedKind != data.kind) continue;
                    data.prefab = views[i];
                    EditorUtility.SetDirty(data);
                    break;
                }
            }
        }

        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static string ColorName(Color color)
        {
            Color32 c = color;
            return c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
        }

        static Color C(byte r, byte g, byte b) => new Color32(r, g, b, 255);
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    }
}
