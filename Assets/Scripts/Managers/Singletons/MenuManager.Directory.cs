using System.Collections.Generic;
using CrazyElevator.Shared;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CrazyElevator.Managers
{
    public sealed partial class MenuManager
    {
        enum DirectoryEntryType { Dimension, Quirk, Passenger }

        sealed class DirectoryEntry
        {
            public DirectoryEntryType Type;
            public int Dimension;
            public string Title;
            public string Description;
            public string PassengerKind;
        }

        [Header("Dimension guide images")]
        [SerializeField] Texture2D[] dimensionGuideImages = new Texture2D[ElevatorRound.Dimensions];

        readonly List<DirectoryEntry> directoryEntries = new List<DirectoryEntry>();
        bool showingDimensionPool;
        bool showingPassengerGuide;
        int poolSelection;
        int directorySelection;
        int directoryButtonSelection;
        bool poolMoveHeld;
        bool directoryMoveHeld;
        float poolRepeatTimer;
        float directoryRepeatTimer;
        string poolNotice;
        float poolNoticeUntil;

        const int DirectoryPreviewLayer = 30;
        const int DirectoryPreviewSize = 512;
        static readonly Rect DirectoryPreviousButtonRect = new Rect(797, 468, 106, 64);
        static readonly Rect DirectoryNextButtonRect = new Rect(797, 552, 106, 64);
        Transform directoryPreviewStage;
        Camera directoryPreviewCamera;
        RenderTexture directoryPreviewTexture;
        GameObject directoryPreviewModel;
        string directoryPreviewKind;

        void OpenDimensionPool()
        {
            titleSelection = 1;
            showingDimensionPool = true;
            showingPassengerGuide = false;
            showingOptions = false;
            poolSelection = Mathf.Clamp(poolSelection, 0, ElevatorRound.Dimensions - 1);
            poolMoveHeld = false;
            poolRepeatTimer = 0f;
            SyncTitleButtons();
        }

        void OpenPassengerGuide()
        {
            titleSelection = 2;
            showingPassengerGuide = true;
            showingDimensionPool = false;
            showingOptions = false;
            BuildDirectoryEntries();
            directorySelection = 0;
            directoryButtonSelection = 0;
            directoryMoveHeld = false;
            directoryRepeatTimer = 0f;
            UpdateDirectoryPreview();
            SyncTitleButtons();
        }

        void CloseDimensionPool()
        {
            showingDimensionPool = false;
            titleSelection = 1;
            SyncTitleButtons();
        }

        void ClosePassengerGuide()
        {
            showingPassengerGuide = false;
            titleSelection = 2;
            ClearDirectoryPreviewModel();
            SyncTitleButtons();
        }

        void UpdateDimensionPoolNavigation()
        {
            ReadDirectoryNavigation(out Vector2 move, out bool confirm, out bool back);
            bool moving = Mathf.Abs(move.y) >= .55f;
            poolRepeatTimer -= Time.unscaledDeltaTime;
            if (moving && (!poolMoveHeld || poolRepeatTimer <= 0f))
            {
                poolSelection = (poolSelection + (move.y < 0f ? 1 : ElevatorRound.Dimensions - 1))
                    % ElevatorRound.Dimensions;
                poolRepeatTimer = poolMoveHeld ? .12f : .28f;
            }
            poolMoveHeld = moving;

            if (confirm) TogglePoolSelection();
            if (back) CloseDimensionPool();
        }

        void UpdatePassengerGuideNavigation()
        {
            ReadDirectoryNavigation(out Vector2 move, out bool confirm, out bool back);
            if (TryGetMenuArtPointer(out Vector2 pointer))
            {
                if (DirectoryPreviousButtonRect.Contains(pointer)) directoryButtonSelection = 0;
                else if (DirectoryNextButtonRect.Contains(pointer)) directoryButtonSelection = 1;
            }

            bool moving = Mathf.Abs(move.y) >= .55f;
            directoryRepeatTimer -= Time.unscaledDeltaTime;
            if (moving && (!directoryMoveHeld || directoryRepeatTimer <= 0f))
            {
                directoryButtonSelection = move.y > 0f ? 0 : 1;
                directoryRepeatTimer = directoryMoveHeld ? .12f : .28f;
            }
            directoryMoveHeld = moving;
            if (confirm) StepDirectory(directoryButtonSelection == 0 ? -1 : 1);
            if (back) ClosePassengerGuide();
        }

        void UpdateDirectoryPreviewAnimation()
        {
            if (directoryPreviewModel == null || directoryEntries.Count == 0) return;
            DirectoryEntry entry = directoryEntries[Mathf.Clamp(directorySelection, 0, directoryEntries.Count - 1)];
            if (entry.Type == DirectoryEntryType.Passenger)
                directoryPreviewModel.transform.localRotation = Quaternion.Euler(0f,
                    180f + Mathf.Sin(Time.unscaledTime * 1.2f) * 5f, 0f);
        }

        static void ReadDirectoryNavigation(out Vector2 move, out bool confirm, out bool back)
        {
            move = Vector2.zero;
            confirm = false;
            back = false;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                move.x = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
                move.y = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                    - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);
                confirm = keyboard.cKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame;
                back = keyboard.escapeKey.wasPressedThisFrame;
            }

            foreach (Gamepad pad in Gamepad.all)
            {
                if (pad == null) continue;
                Vector2 padMove = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
                if (padMove.sqrMagnitude > move.sqrMagnitude) move = padMove;
                confirm |= pad.buttonSouth.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
            }
        }

        void TogglePoolSelection()
        {
            GameManager game = GameManager.Instance;
            if (game == null) return;
            bool enabled = game.IsDimensionEnabled(poolSelection);
            if (!game.SetDimensionEnabled(poolSelection, !enabled))
            {
                poolNotice = "KEEP AT LEAST ONE DIMENSION IN THE POOL";
                poolNoticeUntil = Time.unscaledTime + 2f;
            }
            else poolNotice = null;
        }

        void DrawDimensionPoolScreen()
        {
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);
            Panel(new Rect(0, 0, Screen.width, Screen.height), new Color(.25f, .08f, .20f, .38f));

            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUI.Label(new Rect(250, 70, 1200, 84), "DIMENSION POOL", poolHeaderText);
            GUI.Label(new Rect(290, 154, 1120, 46),
                "Choose the worlds that can appear in future rounds.", poolInstructionText);

            GameManager game = GameManager.Instance;
            string[] names = { "OFFICE", "CANDYLAND", "UNDERWATER", "MYSTICAL FOREST", "SPACE" };
            for (int i = 0; i < ElevatorRound.Dimensions; i++)
            {
                Rect row = new Rect(440, 220 + i * 108, 820, 94);
                bool enabled = game == null || game.IsDimensionEnabled(i);
                Color accent = ElevatorManager.DimensionIntroAccent((WorldBand)i);
                DrawDirectoryCard(row, accent, i == poolSelection);
                GUI.Label(new Rect(row.x + 34, row.y + 8, 500, row.height - 16), names[i], poolDimensionNameText);
                Rect toggle = new Rect(row.xMax - 244, row.y + 14, 220, 66);
                Panel(toggle, enabled ? accent : new Color32(81, 85, 96, 255));
                GUI.Label(toggle, enabled ? "IN POOL  ✓" : "EXCLUDED", poolToggleLabel);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    poolSelection = i;
                    TogglePoolSelection();
                }
            }

            if (!string.IsNullOrEmpty(poolNotice) && Time.unscaledTime < poolNoticeUntil)
                GUI.Label(new Rect(350, 770, 1000, 42), poolNotice, poolNoticeText);
            int count = game != null ? game.EnabledDimensionCount : ElevatorRound.Dimensions;
            GUI.Label(new Rect(250, 810, 1200, 38),
                "ROUNDS CYCLE THROUGH " + count + " ENABLED " + (count == 1 ? "WORLD" : "WORLDS") + ".", poolCycleCaption);

            Rect back = new Rect(615, 910, 470, 76);
            DrawDirectoryCard(back, Cream, poolSelection == ElevatorRound.Dimensions);
            GUI.Label(back, "BACK", cardTitle);
            DrawKeyHint(new Rect(back.xMax + 18, back.y + 10, 54, 54), "B");
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) CloseDimensionPool();
            GUI.matrix = oldMatrix;
        }

        void BuildDirectoryEntries()
        {
            directoryEntries.Clear();
            AddDimensionEntry(0, "OFFICE", "0.85 floors per second.");
            AddQuirkEntry(0, "SHAFT HAZARDS", "Monsters and tornadoes roam the shaft. Office hazards wake after 12 seconds.");
            AddPassengerEntry(0, "COURIER", "THE COURIER", "Needs two cabin spaces. Make room for the package.");
            AddPassengerEntry(0, "INTERVIEW", "THE INTERVIEWEE", "Short patience. Get them to the interview on time.");
            AddPassengerEntry(0, "HUNTER", "THE HUNTER", "Stuns nearby monsters while aboard.");
            AddPassengerEntry(0, "ELDERLY", "GRANDMA", "Slows the elevator and takes a little longer to reach the door.");

            AddDimensionEntry(1, "CANDYLAND", "1.25 floors per second.");
            AddPassengerEntry(1, "PREGNANT", "MOM + BABY", "Takes two spaces. The crying baby makes passenger patience drain faster.");
            AddPassengerEntry(1, "BOSS", "THE BOSS", "An on-time delivery earns a bonus.");
            AddPassengerEntry(1, "BOUNCER", "THE BOUNCER", "Takes two spaces and strengthens elevator collision rebounds in 1v1.");
            AddPassengerEntry(1, "GROUP", "THE TRIO", "Three spaces. The group speeds up the elevator. In 1v1, a boarded Trio leaves candy behind. Rivals who hit it lose speed.");
            AddPassengerEntry(1, "ELDERLY", "GRANDMA", "Slows the elevator and can calm the baby while both are aboard.");

            AddDimensionEntry(2, "UNDERWATER", "0.6 floors per second.");
            AddQuirkEntry(2, "RUST", "Rust builds while you travel in water. A Handyman clears the impairment.");
            AddPassengerEntry(2, "DIVER", "THE DIVER", "Blocks tornado hits while aboard.");
            AddPassengerEntry(2, "HANDYMAN", "THE HANDYMAN", "Repairs rust while aboard and restores full elevator power.");
            AddPassengerEntry(2, "ELDERLY", "GRANDMA", "Slows the elevator and takes a little longer to reach the door.");

            AddDimensionEntry(3, "MYSTICAL FOREST", "Meet the forest passengers.");
            AddQuirkEntry(3, "WILD GROWTH", "Glowing vines sweep across a track. Switch tracks or use a forest passenger's ability.");
            AddPassengerEntry(3, "FAIRY", "THE FAIRY", "Fireflies reveal a safe route. A clean dodge gives the elevator a short speed boost.");
            AddPassengerEntry(3, "GOBLIN", "THE GOBLIN", "Cuts through the vines and rides the side track as a shortcut.");
            AddPassengerEntry(3, "MUSHROOM", "THE MUSHROOM", "A spore burst blocks a vine hit.");
            AddDimensionEntry(4, "SPACE", "A UFO beam moves a waiting passenger. Watch the new floor.");
            AddQuirkEntry(4, "GRAVITY WAKE", "A black hole distorts the shaft. The UFO beam pulls the elevator sideways.");
            AddPassengerEntry(4, "ASTRONAUT", "THE ASTRONAUT", "Tethers the elevator to the track to resist the gravity pull.");
            AddPassengerEntry(4, "ALIEN", "THE ALIEN", "Uses the UFO beam to slingshot the elevator onto a side track.");
        }

        void AddDimensionEntry(int dimension, string titleText, string description)
        {
            directoryEntries.Add(new DirectoryEntry
            {
                Type = DirectoryEntryType.Dimension,
                Dimension = dimension,
                Title = titleText,
                Description = description
            });
        }

        void AddQuirkEntry(int dimension, string titleText, string description)
        {
            directoryEntries.Add(new DirectoryEntry
            {
                Type = DirectoryEntryType.Quirk,
                Dimension = dimension,
                Title = titleText,
                Description = description
            });
        }

        void AddPassengerEntry(int dimension, string kind, string titleText, string description)
        {
            directoryEntries.Add(new DirectoryEntry
            {
                Type = DirectoryEntryType.Passenger,
                Dimension = dimension,
                PassengerKind = kind,
                Title = titleText,
                Description = description
            });
        }

        void StepDirectory(int amount)
        {
            if (directoryEntries.Count == 0) return;
            directorySelection = (directorySelection + amount + directoryEntries.Count) % directoryEntries.Count;
            UpdateDirectoryPreview();
        }

        void DrawPassengerGuideScreen()
        {
            if (directoryEntries.Count == 0) BuildDirectoryEntries();
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);
            Panel(new Rect(0, 0, Screen.width, Screen.height), new Color(.30f, .07f, .22f, .30f));

            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            DrawShaft(ScaleRectAboutCenter(new Rect(42, 155, 560, 730), 1.5f),
                1f, DirectoryElevatorDescent);
            DrawShaft(ScaleRectAboutCenter(new Rect(1098, 155, 560, 730), 1.5f),
                .78f, DirectoryElevatorDescent);

            DirectoryEntry entry = directoryEntries[Mathf.Clamp(directorySelection, 0, directoryEntries.Count - 1)];
            DirectoryEntry previousEntry = directoryEntries[
                (directorySelection + directoryEntries.Count - 1) % directoryEntries.Count];
            DirectoryEntry nextEntry = directoryEntries[(directorySelection + 1) % directoryEntries.Count];
            Rect window = ScaleRectAboutCenter(new Rect(169, 330, 306, 350), 1.55f);
            Panel(new Rect(window.x - 9, window.y - 9, window.width + 18, window.height + 18),
                new Color32(236, 118, 188, 255));
            Panel(window, new Color32(20, 18, 34, 255));
            DrawDirectoryImage(entry, window);

            Rect info = new Rect(740, 215, 220, 210);
            Panel(info, new Color32(255, 250, 234, 245));
            GUI.Label(new Rect(info.x + 8, info.y + 27, info.width - 16, 36),
                previousEntry.Title, guideNeighborItemText);
            GUI.Label(new Rect(info.x + 8, info.y + 76, info.width - 16, 52),
                entry.Title, guideCurrentItemText);
            GUI.Label(new Rect(info.x + 8, info.y + 137, info.width - 16, 36),
                nextEntry.Title, guideNeighborItemText);

            Rect previous = DirectoryPreviousButtonRect;
            Rect next = DirectoryNextButtonRect;
            DrawDirectoryCard(previous, Teal, directoryButtonSelection == 0);
            GUI.Label(previous, "\u25B2", guideArrowText);
            DrawDirectoryCard(next, Coral, directoryButtonSelection == 1);
            GUI.Label(next, "\u25BC", guideArrowText);
            if (GUI.Button(previous, GUIContent.none, GUIStyle.none))
            {
                directoryButtonSelection = 0;
                StepDirectory(-1);
            }
            if (GUI.Button(next, GUIContent.none, GUIStyle.none))
            {
                directoryButtonSelection = 1;
                StepDirectory(1);
            }
            Rect selectedArrow = directoryButtonSelection == 0 ? previous : next;
            DrawKeyHint(new Rect(selectedArrow.xMax + 16f, selectedArrow.y + 5f, 54f, 54f), "A", true);

            GUI.Label(new Rect(700, 650, 300, 34),
                (directorySelection + 1) + "  /  " + directoryEntries.Count, guideCaptionText);

            Rect nextCard = new Rect(1170, 340, 420, 360);
            Panel(nextCard, new Color(9f / 255f, 11f / 255f, 16f / 255f, .88f));
            GUI.Label(new Rect(nextCard.x + 24, nextCard.y + 24, nextCard.width - 48, 44),
                entry.Title, guideNextStopHeaderText);
            GUI.Label(new Rect(nextCard.x + 24, nextCard.y + 78, nextCard.width - 48, 250),
                entry.Description, guideNextStopDescriptionText);

            Rect back = new Rect(732.5f, 812, 235, 70);
            DrawDirectoryCard(back, Cream, false);
            GUI.Label(back, "BACK", guideCardTitleText);
            DrawKeyHint(new Rect(back.xMax + 18f, back.y + 8f, 54f, 54f), "B", true);
            if (GUI.Button(back, GUIContent.none, GUIStyle.none)) ClosePassengerGuide();

            GUI.Label(new Rect(245, 35, 1210, 70), "PASSENGER & DIMENSION GUIDE", guideHeaderText);
            GUI.Label(new Rect(320, 100, 1060, 36), "THE CRAZY ELEVATOR DIRECTORY", guideCaptionText);
            GUI.matrix = oldMatrix;
        }

        void DrawDirectoryImage(DirectoryEntry entry, Rect window)
        {
            Texture image = null;
            if (HasDirectoryModelPreview(entry))
                image = directoryPreviewTexture;
            else if (dimensionGuideImages != null && entry.Dimension >= 0
                && entry.Dimension < dimensionGuideImages.Length)
                image = dimensionGuideImages[entry.Dimension];

            Rect inner = new Rect(window.x + 12, window.y + 12, window.width - 24, window.height - 24);
            if (image != null) GUI.DrawTexture(inner, image, ScaleMode.ScaleToFit, true);
            else GUI.Label(inner, "PREVIEW", guidePreviewText);
            GUI.Label(new Rect(window.x, window.yMax - 42f, window.width, 34f),
                "PREVIEW", guidePreviewText);
        }

        static bool IsShaftHazardEntry(DirectoryEntry entry)
            => entry != null && entry.Type == DirectoryEntryType.Quirk && entry.Title == "SHAFT HAZARDS";

        static bool HasDirectoryModelPreview(DirectoryEntry entry)
            => entry != null && (entry.Type == DirectoryEntryType.Passenger || IsShaftHazardEntry(entry)
                || entry.Type == DirectoryEntryType.Dimension && entry.Dimension >= 3);

        void UpdateDirectoryPreview()
        {
            if (directoryEntries.Count == 0) return;
            DirectoryEntry entry = directoryEntries[Mathf.Clamp(directorySelection, 0, directoryEntries.Count - 1)];
            if (!HasDirectoryModelPreview(entry))
            {
                ClearDirectoryPreviewModel();
                return;
            }
            string previewKey = IsShaftHazardEntry(entry) ? "SHAFT HAZARDS"
                : entry.Type == DirectoryEntryType.Dimension ? entry.Title : entry.PassengerKind;
            if (directoryPreviewKind == previewKey && directoryPreviewModel != null)
            {
                if (directoryPreviewCamera != null) directoryPreviewCamera.enabled = true;
                return;
            }
            ClearDirectoryPreviewModel();
            EnsureDirectoryPreviewStage();
            if (IsShaftHazardEntry(entry))
                CreateShaftHazardDirectoryPreview(previewKey);
            else if (entry.Type == DirectoryEntryType.Dimension)
            {
                GameObject world = Resources.Load<GameObject>(entry.Dimension == 3 ? "BG_fairy_world" : "BG_space_world");
                if (world != null)
                {
                    directoryPreviewModel = Instantiate(world, directoryPreviewStage, false);
                    directoryPreviewModel.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }
            else
                CreatePassengerDirectoryPreview(entry, previewKey);
            if (directoryPreviewModel == null) return;
            directoryPreviewKind = previewKey;
            PrepareDirectoryPreviewModel(directoryPreviewModel);
            FitDirectoryModel(directoryPreviewModel.transform, 2.5f, 2.5f);
            directoryPreviewCamera.enabled = true;
        }

        void CreatePassengerDirectoryPreview(DirectoryEntry entry, string previewKey)
        {
            if (entry.PassengerKind == "GROUP")
            {
                CreateTrioDirectoryPreview(previewKey);
                return;
            }

            PassengerView source = FindDirectoryPassenger(entry.PassengerKind);
            if (source == null) return;
            directoryPreviewModel = Instantiate(source.gameObject, directoryPreviewStage, false);
            directoryPreviewModel.name = previewKey + " directory preview";
            directoryPreviewModel.transform.localPosition = Vector3.zero;
            directoryPreviewModel.transform.localRotation = Quaternion.identity;
            directoryPreviewModel.transform.localScale = source.transform.localScale;
            directoryPreviewModel.SetActive(true);
            foreach (TextMesh label in directoryPreviewModel.GetComponentsInChildren<TextMesh>(true))
                label.gameObject.SetActive(false);
        }

        void CreateTrioDirectoryPreview(string previewKey)
        {
            PassengerView clown = FindDirectoryPassenger("GROUP");
            PassengerView boss = FindDirectoryPassenger("BOSS");
            PassengerView momAndBaby = FindDirectoryPassenger("PREGNANT");
            if (clown == null && boss == null && momAndBaby == null) return;

            directoryPreviewModel = new GameObject(previewKey + " directory preview");
            directoryPreviewModel.transform.SetParent(directoryPreviewStage, false);
            directoryPreviewModel.transform.localPosition = Vector3.zero;
            directoryPreviewModel.transform.localRotation = Quaternion.identity;
            directoryPreviewModel.transform.localScale = Vector3.one;

            AddTrioMemberPreview(clown, Vector3.zero, 1f, 0f);
            AddTrioMemberPreview(boss, new Vector3(-.85f, 0f, .12f), .72f, -12f);
            AddTrioMemberPreview(momAndBaby, new Vector3(.85f, 0f, .12f), .72f, 12f);
        }

        void AddTrioMemberPreview(PassengerView source, Vector3 localPosition, float scale, float yaw)
        {
            if (source == null) return;
            GameObject member = Instantiate(source.gameObject, directoryPreviewModel.transform, false);
            member.name = source.ResolvedKind + " trio preview member";
            member.transform.localPosition = localPosition;
            member.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            member.transform.localScale = source.transform.localScale * scale;
            if (source.ResolvedKind == "PREGNANT")
            {
                foreach (Transform child in member.GetComponentsInChildren<Transform>(true))
                    if (child != member.transform && child.name == "Baby") child.gameObject.SetActive(false);
            }
            foreach (TextMesh label in member.GetComponentsInChildren<TextMesh>(true))
                label.gameObject.SetActive(false);
        }

        void CreateShaftHazardDirectoryPreview(string previewKey)
        {
            ElevatorManager player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            ElevatorScene scene = player != null ? player.sceneView : null;
            if (scene == null || scene.tornado == null && scene.monster == null) return;

            directoryPreviewModel = new GameObject(previewKey + " directory preview");
            directoryPreviewModel.transform.SetParent(directoryPreviewStage, false);
            directoryPreviewModel.transform.localPosition = Vector3.zero;
            directoryPreviewModel.transform.localRotation = Quaternion.identity;
            directoryPreviewModel.transform.localScale = Vector3.one;
            AddDirectoryHazardModel(scene.tornado, new Vector3(-.65f, 0f, 0f), 18f);
            AddDirectoryHazardModel(scene.monster, new Vector3(.65f, 0f, 0f), -18f);
        }

        void AddDirectoryHazardModel(GameObject prefab, Vector3 localPosition, float yaw)
        {
            if (prefab == null) return;
            GameObject model = Instantiate(prefab, directoryPreviewModel.transform, false);
            model.name = prefab.name + " directory preview";
            model.transform.localPosition = localPosition;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = prefab.transform.localScale;
        }

        static void PrepareDirectoryPreviewModel(GameObject model)
        {
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (AudioSource source in model.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            SetDirectoryLayer(model.transform, DirectoryPreviewLayer);
        }

        PassengerView FindDirectoryPassenger(string kind)
        {
            ElevatorManager player = GameManager.Instance != null ? GameManager.Instance.Player : null;
            PassengerView[] passengers = player != null ? player.PassengerPrefabs : null;
            if (passengers != null)
                foreach (PassengerView passenger in passengers)
                    if (passenger != null && passenger.ResolvedKind == kind) return passenger;
            GameObject resource = Resources.Load<GameObject>("Passenger-" + kind);
            return resource != null ? resource.GetComponent<PassengerView>() : null;
        }

        void EnsureDirectoryPreviewStage()
        {
            if (directoryPreviewStage != null) return;
            GameObject stage = new GameObject("Passenger directory preview stage")
                { hideFlags = HideFlags.HideAndDontSave };
            directoryPreviewStage = stage.transform;
            directoryPreviewStage.position = new Vector3(-10000f, -10000f, -10000f);

            GameObject cameraObject = new GameObject("Passenger directory preview camera")
                { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(directoryPreviewStage, false);
            cameraObject.transform.localPosition = new Vector3(0f, 1f, -6f);
            cameraObject.transform.localRotation = Quaternion.identity;
            directoryPreviewCamera = cameraObject.AddComponent<Camera>();
            directoryPreviewCamera.enabled = false;
            directoryPreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            directoryPreviewCamera.backgroundColor = Color.clear;
            directoryPreviewCamera.cullingMask = 1 << DirectoryPreviewLayer;
            directoryPreviewCamera.orthographic = true;
            directoryPreviewCamera.orthographicSize = 1.3f;
            directoryPreviewCamera.aspect = 1f;
            directoryPreviewCamera.nearClipPlane = .1f;
            directoryPreviewCamera.farClipPlane = 20f;
            directoryPreviewCamera.allowHDR = false;

            directoryPreviewTexture = new RenderTexture(DirectoryPreviewSize, DirectoryPreviewSize, 24,
                RenderTextureFormat.ARGB32)
            {
                name = "Passenger directory preview",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            directoryPreviewTexture.Create();
            directoryPreviewCamera.targetTexture = directoryPreviewTexture;

            GameObject lightObject = new GameObject("Passenger directory preview light")
                { hideFlags = HideFlags.HideAndDontSave };
            lightObject.transform.SetParent(directoryPreviewStage, false);
            lightObject.transform.localRotation = Quaternion.Euler(28f, -28f, 0f);
            Light key = lightObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.4f;
            key.color = Color.white;
            key.cullingMask = 1 << DirectoryPreviewLayer;
        }

        static void SetDirectoryLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetDirectoryLayer(child, layer);
        }

        void FitDirectoryModel(Transform model, float targetHeight, float maxWidth)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(model.position, Vector3.zero);
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds) return;
            float fitScale = targetHeight / Mathf.Max(.01f, bounds.size.y);
            if (maxWidth > 0f)
                fitScale = Mathf.Min(fitScale, maxWidth / Mathf.Max(.01f, bounds.size.x));
            model.localScale *= fitScale;
            bounds = new Bounds(model.position, Vector3.zero);
            hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (hasBounds)
                model.position += directoryPreviewStage.up * (1f - (bounds.center.y - directoryPreviewStage.position.y));
        }

        void ClearDirectoryPreviewModel()
        {
            if (directoryPreviewCamera != null) directoryPreviewCamera.enabled = false;
            if (directoryPreviewModel != null) Destroy(directoryPreviewModel);
            directoryPreviewModel = null;
            directoryPreviewKind = null;
        }

        void ReleaseDirectoryPreview()
        {
            ClearDirectoryPreviewModel();
            if (directoryPreviewCamera != null) directoryPreviewCamera.targetTexture = null;
            if (directoryPreviewStage != null) Destroy(directoryPreviewStage.gameObject);
            else if (directoryPreviewCamera != null) Destroy(directoryPreviewCamera.gameObject);
            if (directoryPreviewTexture != null)
            {
                directoryPreviewTexture.Release();
                Destroy(directoryPreviewTexture);
            }
            directoryPreviewCamera = null;
            directoryPreviewStage = null;
            directoryPreviewTexture = null;
        }

        void DrawDirectoryCard(Rect rect, Color accent, bool selected)
        {
            if (selected) Panel(new Rect(rect.x - 5, rect.y - 5, rect.width + 10, rect.height + 10), Cream);
            Panel(rect, accent);
            Panel(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6),
                new Color32(255, 250, 234, 245));
        }

        static Rect ScaleRectAboutCenter(Rect rect, float scale)
        {
            Vector2 center = rect.center;
            rect.width *= scale;
            rect.height *= scale;
            rect.center = center;
            return rect;
        }

        void OnDestroyDirectoryPreview() => ReleaseDirectoryPreview();
    }
}
