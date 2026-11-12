using CrazyElevator.Shared;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// --- from GameUI.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    // On-screen introduction, tutorial, timer and results. No scoring rules here.
    public sealed partial class ElevatorManager
    {
        // State owned by this part of the prototype.
        float scale, offsetX, offsetY;
        GUIStyle title, large, body, small, buttonStyle, inkBody, inkLarge, inkSmall, logo, stampWord,
            rewardPoints, rewardCaption, scoreFlightStyle, actionWord, calloutNumber, calloutText, selectionArrow,
            encounterKicker, encounterTitle, encounterBody, encounterPrompt, encounterBadge,
            manifestTitle, manifestDetail;
        static readonly Color UiBlack = new Color32(9, 11, 16, 255);
        static readonly Color UiWhite = new Color32(255, 252, 241, 255);
        const float ActionFeedbackSeconds = 1.2f;
        string actionFeedbackText;
        float actionFeedbackAt = -100f;
        bool actionFeedbackPositive, actionFeedbackEmphasized;
        readonly HashSet<WorldBand> encounteredWorlds = new HashSet<WorldBand>();
        readonly HashSet<string> encounteredPassengerKinds = new HashSet<string>();
        static readonly HashSet<WorldBand>[] completedWorldTutorials =
        {
            new HashSet<WorldBand>(), new HashSet<WorldBand>()
        };
        static readonly HashSet<string>[] completedPassengerTutorials =
        {
            new HashSet<string>(), new HashSet<string>()
        };
        readonly Queue<EncounterCard> encounterCards = new Queue<EncounterCard>();
        readonly Dictionary<string, Texture2D> passengerIconTextures = new Dictionary<string, Texture2D>();
        EncounterCard activeEncounterCard;
        float encounterCardTime;
        int encounterConfirmationConsumedFrame = -1;
        const int EncounterPreviewLayer = 30;
        const int EncounterPreviewWidth = 1920;
        const int EncounterPreviewHeight = 1080;
        Transform encounterPreviewStage;
        Transform encounterPreviewPivot;
        Camera encounterPreviewCamera;
        RenderTexture encounterPreviewTexture;
        GameObject encounterPreviewModel;
        EncounterCard renderedEncounterCard;
        Texture2D gameplayKeyIconTexture;

        sealed class EncounterCard
        {
            public string Kicker;
            public string Title;
            public string Description;
            public string Badge;
            public Color Accent;
            public GameObject PreviewSource;
            public float PreviewYaw;
            public bool IsWorldDiscovery;
            public WorldBand World;
            public string PassengerKind;
            public PassengerFeature Features;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEncounterTutorialProgress()
        {
            foreach (HashSet<WorldBand> worlds in completedWorldTutorials) worlds.Clear();
            foreach (HashSet<string> passengers in completedPassengerTutorials) passengers.Clear();
        }

        // Rebuild native GUI styles when returning to Play Mode without domain reload.
        void OnEnable() { body = null; }

        // Create reusable text styles on the first draw.
        void Styles()
        {
            if (body != null) return;
            body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
            GameTypography.Apply(body);
            body.normal.textColor = Cream;
            small = new GUIStyle(body) { fontSize = 12 };
            title = new GUIStyle(body) { fontSize = 36, fontStyle = FontStyle.Bold };
            GameTypography.Apply(title, true);
            manifestDetail = new GUIStyle(small)
            {
                padding = new RectOffset(), wordWrap = false, clipping = TextClipping.Clip
            };
            manifestTitle = new GUIStyle(manifestDetail);
            GameTypography.Apply(manifestTitle, true);
            large = new GUIStyle(body) { fontSize = 24, fontStyle = FontStyle.Bold };
            GameTypography.Apply(large, true);
            buttonStyle = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GameTypography.Apply(buttonStyle, true);
            inkBody = new GUIStyle(body); inkBody.normal.textColor = Ink;
            inkSmall = new GUIStyle(small); inkSmall.normal.textColor = Ink;
            inkLarge = new GUIStyle(large); inkLarge.normal.textColor = Ink;
            logo = new GUIStyle(body) { fontSize = 96, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GameTypography.Apply(logo, true);
            logo.normal.textColor = Cream;
            stampWord = new GUIStyle(body) { fontSize = 62, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            GameTypography.Apply(stampWord, true);
            stampWord.normal.textColor = Coral;
            rewardPoints = new GUIStyle(title) { fontSize = 48, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            GameTypography.Apply(rewardPoints, true);
            rewardCaption = new GUIStyle(buttonStyle) { fontSize = 17, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
            GameTypography.Apply(rewardCaption, true);
            scoreFlightStyle = new GUIStyle(rewardCaption) { fontSize = 18, fontStyle = FontStyle.Bold };
            GameTypography.Apply(scoreFlightStyle, true);
            actionWord = new GUIStyle(buttonStyle) { fontSize = 24, alignment = TextAnchor.MiddleCenter,
                wordWrap = false, clipping = TextClipping.Clip };
            GameTypography.Apply(actionWord, true);
            calloutNumber = new GUIStyle(buttonStyle) { fontSize = 25, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GameTypography.Apply(calloutNumber, true);
            calloutNumber.normal.textColor = UiWhite;
            calloutText = new GUIStyle(buttonStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
            GameTypography.Apply(calloutText, true);
            calloutText.normal.textColor = UiWhite;
            selectionArrow = new GUIStyle(buttonStyle) { fontSize = 32, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GameTypography.Apply(selectionArrow, true);
            selectionArrow.normal.textColor = Gold;
            encounterKicker = new GUIStyle(small) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
            GameTypography.Apply(encounterKicker, true);
            encounterTitle = new GUIStyle(title) { fontSize = 42, wordWrap = false, clipping = TextClipping.Clip };
            GameTypography.Apply(encounterTitle, true);
            encounterBody = new GUIStyle(body) { fontSize = 15, wordWrap = true };
            GameTypography.Apply(encounterBody);
            encounterPrompt = new GUIStyle(buttonStyle)
            { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            GameTypography.Apply(encounterPrompt, true);
            encounterPrompt.normal.textColor = Gold;
            encounterBadge = new GUIStyle(title) { fontSize = 44, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GameTypography.Apply(encounterBadge, true);
        }
        // Draw a solid panel and restore the GUI tint.
        void Panel(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }
        void AngularPanel(Rect r, Color accent)
        {
            FramedPanel(r, accent, 3f);
        }
        void SpeechBubblePanel(Rect r, Color accent)
        {
            FramedPanel(r, accent, 2f);
        }
        void FramedPanel(Rect r, Color accent, float border)
        {
            Rect outer = new Rect(
                Mathf.Round(r.x), Mathf.Round(r.y),
                Mathf.Max(1f, Mathf.Round(r.width)), Mathf.Max(1f, Mathf.Round(r.height)));
            Panel(outer, accent);
            Panel(new Rect(
                outer.x + border, outer.y + border,
                Mathf.Max(1f, outer.width - border * 2f),
                Mathf.Max(1f, outer.height - border * 2f)), UiBlack);
        }
        // Use the body style unless a custom style is supplied.
        void Label(Rect r, string text, GUIStyle style = null)
        {
            style = style ?? body;
            // Reapply after editor skin/domain changes, including Play Mode without domain reload.
            style.normal.textColor = style == inkBody || style == inkSmall || style == inkLarge ? Ink : Cream;
            GUI.Label(r, text, style);
        }
        void OutlinedLabel(Rect rect, string value, GUIStyle style)
        {
            OutlinedLabel(rect, value, style, UiWhite);
        }
        void OutlinedLabel(Rect rect, string value, GUIStyle style, Color foreground)
        {
            Color previous = style.normal.textColor;
            style.normal.textColor = UiBlack;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0)
                        GUI.Label(new Rect(rect.x + x, rect.y + y, rect.width, rect.height), value, style);
            style.normal.textColor = foreground;
            GUI.Label(rect, value, style);
            style.normal.textColor = previous;
        }
        // Draw a button and restore shared GUI state.
        bool Button(Rect r, string text, Color color, bool enabled = true, bool selected = false, bool menuColours = false)
        {
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && enabled;
            Color outline = menuColours ? MenuPresentation.DisplayColor(MenuPresentation.Paper) : UiWhite;
            Color face = GUI.enabled ? color : new Color(.2f, .25f, .3f);
            Color label = GUI.enabled ? Ink : Color.gray;
            if (menuColours)
            {
                face = MenuPresentation.DisplayColor(face);
                label = MenuPresentation.DisplayColor(MenuPresentation.Ink);
            }
            if (selected) Panel(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), outline);
            Panel(r, face);
            Color old = buttonStyle.normal.textColor;
            buttonStyle.normal.textColor = label;
            bool pressed = GUI.Button(r, text, buttonStyle);
            buttonStyle.normal.textColor = old;
            GUI.enabled = wasEnabled;
            return pressed;
        }

        // Summarize a passenger type for the UI.
        string ShortRequest(Rider p)
        {
            if (p.Kind == "HUNTER") return "Stuns monsters";
            if (p.Kind == "DIVER") return "Blocks tornadoes";
            if (p.Kind == "BOUNCER") return "Stronger collisions";
            if (p.Kind == "HANDYMAN") return "Full power while aboard";
            if (p.Kind == "COURIER") return "2 spaces • 1 floor";
            if (p.Kind == "PREGNANT") return "Baby cries • 2 spaces";
            if (p.Kind == "INTERVIEW") return "Very urgent";
            if (p.Kind == "BOSS") return "Premium delivery bonus.";
            if (p.Kind == "ELDERLY") return "Slow • big bonus";
            return "3 spaces together";
        }

        void ResetEncounterCards()
        {
            encounteredWorlds.Clear();
            encounteredPassengerKinds.Clear();
            encounterCards.Clear();
            activeEncounterCard = null;
            encounterCardTime = 0f;
            ClearEncounterPreviewModel();
        }

        void DiscoverWorld(WorldBand world)
        {
            // World / multiverse cards must only appear inside the timed
            // dimension reveal interlude — never when docking at a new band.
            _ = world;
        }

        void QueueWorldDiscoveryCard(WorldBand world)
        {
            if (IsNpc || MenuManager.TutorialActive || !encounteredWorlds.Add(world)
                || completedWorldTutorials[Seat].Contains(world)) return;
            string titleText;
            string description;
            string badge;
            Color accent;
            switch (world)
            {
                case WorldBand.Candy:
                    titleText = "CANDY MULTIVERSE";
                    description = "Bigger groups and louder problems start appearing here.";
                    badge = "02";
                    accent = new Color32(236, 118, 188, 255);
                    break;
                case WorldBand.Water:
                    titleText = "UNDERWATER MULTIVERSE";
                    description = "Rust slows the elevator. A handyman keeps it moving.";
                    badge = "03";
                    accent = new Color32(48, 196, 210, 255);
                    break;
                default:
                    titleText = "OFFICE MULTIVERSE";
                    description = "Start simple. Match each passenger to the correct floor.";
                    badge = "01";
                    accent = new Color32(98, 132, 178, 255);
                    break;
            }
            EnqueueEncounterCard(new EncounterCard
            {
                Kicker = "NEW MULTIVERSE",
                Title = titleText,
                Description = description,
                IsWorldDiscovery = true,
                World = world,
                Badge = badge,
                Accent = accent,
                PreviewSource = FindWorldPreviewSource(world),
                PreviewYaw = world == WorldBand.Office ? -12f : world == WorldBand.Candy ? 18f : -20f
            });
        }

        void DiscoverPassenger(Rider rider)
        {
            if (IsNpc || rider == null || string.IsNullOrEmpty(rider.Kind)) return;
            QueuePassengerDiscoveryCard(rider);
        }

        void QueuePassengerDiscoveryCard(Rider rider)
        {
            // Passenger types are covered by the full-screen preview before
            // every round, keeping the active round free of tutorial popups.
            _ = rider;
        }

        GameObject FindWorldPreviewSource(WorldBand world)
        {
            // Prefer the art-team FBX backgrounds shipped for dimension intros.
            if (world == WorldBand.Candy)
            {
                GameObject art = Resources.Load<GameObject>("BG_candy_world");
                if (art != null) return art;
            }
            else if (world == WorldBand.Water)
            {
                GameObject art = Resources.Load<GameObject>("BG_under_water");
                if (art != null) return art;
            }
            else if (world == WorldBand.Forest || world == WorldBand.Space)
            {
                GameObject art = Resources.Load<GameObject>(world == WorldBand.Forest
                    ? "BG_fairy_world" : "BG_space_world");
                if (art != null) return art;
            }
            string wanted = world == WorldBand.Office ? "Office Band"
                : world == WorldBand.Candy ? "Candy Band" : "Water Band";
            string featuredSection = world == WorldBand.Office ? "Office Floor 1"
                : world == WorldBand.Candy ? "Candy Floor 4" : "Reef Floor 8";
            if (!gameObject.scene.IsValid()) return null;
            foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
            {
                foreach (Transform candidate in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate == null) continue;
                    string candidateName = candidate.name.Replace("(Clone)", string.Empty).Trim();
                    if (candidateName != wanted) continue;
                    foreach (Transform section in candidate.GetComponentsInChildren<Transform>(true))
                    {
                        string sectionName = section.name.Replace("(Clone)", string.Empty).Trim();
                        if (sectionName == featuredSection) return section.gameObject;
                    }
                    return candidate.gameObject;
                }
            }
            return null;
        }

        // Dimension interludes use the same isolated 3D renderer as the old
        // encounter cards, but the match HUD presents the texture on one
        // unified screen instead of one card per player.
        public void PrepareDimensionPreview(WorldBand world)
        {
            Color accent = DimensionIntroAccent(world);
            GameObject source = FindWorldPreviewSource(world);
            float yaw = world == WorldBand.Office ? -12f : world == WorldBand.Candy ? 18f : -20f;
            // The art-team FBX dioramas face +Z (Maya front view) with single-sided
            // planes; turn them toward the preview camera and keep only a slight angle.
            if (source != null && source.name.StartsWith("BG_")) yaw = 180f + yaw * .4f;
            EncounterCard card = new EncounterCard
            {
                Kicker = "ROUND PREVIEW",
                Title = DimensionIntroTitle(world),
                Description = string.Empty,
                Badge = string.Empty,
                Accent = accent,
                PreviewSource = source,
                PreviewYaw = yaw,
                IsWorldDiscovery = true,
                World = world
            };
            PrepareEncounterPreview(card);
        }

        public Texture DimensionRevealTexture => encounterPreviewTexture;

        public bool PrepareTutorialCharacter(string kind)
        {
            PassengerData data = passengerCatalog != null ? passengerCatalog.FindKind(kind) : null;
            GameObject source = data != null && data.prefab != null ? data.prefab.gameObject
                : sceneView != null && sceneView.passengers != null ? FindPassengerTemplate(kind) : null;
            PrepareEncounterPreview(new EncounterCard
            {
                Title = kind, PreviewSource = source, Accent = Teal, PreviewYaw = 0f
            });
            if (encounterPreviewModel != null)
                encounterPreviewCamera.backgroundColor = MenuPresentation.Wall;
            return encounterPreviewModel != null;
        }

        public void AnimateTutorialCharacter(float seconds)
        {
            if (encounterPreviewPivot != null && encounterPreviewModel != null)
                encounterPreviewPivot.localRotation = Quaternion.Euler(0f,
                    Mathf.Sin(seconds * .85f) * 28f, 0f);
        }

        public void FinishTutorialCharacter() => ClearEncounterPreviewModel();

        void EnsureEncounterPreview()
        {
            if (encounterPreviewStage != null && encounterPreviewCamera != null
                && encounterPreviewTexture != null) return;

            GameObject stageObject = new GameObject("Encounter 3D preview stage");
            stageObject.hideFlags = HideFlags.HideAndDontSave;
            stageObject.transform.position = new Vector3(10000f + Seat * 100f, -10000f, 10000f);
            encounterPreviewStage = stageObject.transform;

            GameObject pivotObject = new GameObject("Preview model pivot");
            pivotObject.hideFlags = HideFlags.HideAndDontSave;
            pivotObject.transform.SetParent(encounterPreviewStage, false);
            encounterPreviewPivot = pivotObject.transform;

            encounterPreviewTexture = new RenderTexture(EncounterPreviewWidth, EncounterPreviewHeight, 24,
                RenderTextureFormat.ARGB32)
            {
                name = "Encounter 3D preview",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            encounterPreviewTexture.Create();

            GameObject cameraObject = new GameObject("Encounter 3D preview camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(encounterPreviewStage, false);
            encounterPreviewCamera = cameraObject.AddComponent<Camera>();
            encounterPreviewCamera.clearFlags = CameraClearFlags.SolidColor;
            encounterPreviewCamera.backgroundColor = MenuPresentation.Wall;
            encounterPreviewCamera.cullingMask = 1 << EncounterPreviewLayer;
            encounterPreviewCamera.orthographic = true;
            encounterPreviewCamera.allowHDR = false;
            encounterPreviewCamera.allowMSAA = true;
            encounterPreviewCamera.aspect = (float)EncounterPreviewWidth / EncounterPreviewHeight;
            encounterPreviewCamera.targetTexture = encounterPreviewTexture;
            encounterPreviewCamera.enabled = false;

            MenuPresentation.ExcludePreviewLayer(sceneView != null ? sceneView.lights : null, EncounterPreviewLayer);
            MenuPresentation.LightPreview(encounterPreviewStage, Vector3.zero, EncounterPreviewLayer);
        }

        void PrepareEncounterPreview(EncounterCard card)
        {
            ClearEncounterPreviewModel();
            if (card == null || card.PreviewSource == null) return;
            EnsureEncounterPreview();

            encounterPreviewModel = Instantiate(card.PreviewSource, encounterPreviewPivot, false);
            encounterPreviewModel.name = card.Title + " encounter preview";
            encounterPreviewModel.hideFlags = HideFlags.HideAndDontSave;
            encounterPreviewModel.transform.localPosition = Vector3.zero;
            encounterPreviewModel.transform.localRotation = Quaternion.identity;
            encounterPreviewModel.transform.localScale = Vector3.one;
            encounterPreviewModel.SetActive(true);
            MakeStillPreviewModel(encounterPreviewModel, card.Accent);

            if (!TryGetEncounterPreviewBounds(encounterPreviewModel, out Bounds bounds))
            {
                ClearEncounterPreviewModel();
                return;
            }

            // World FBX sets are large environment meshes — shrink them so the
            // dimension card shows the whole scene instead of a clipped corner.
            if (card.IsWorldDiscovery)
            {
                float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (largest > 6f)
                {
                    float scale = 5.2f / largest;
                    encounterPreviewModel.transform.localScale = Vector3.one * scale;
                    if (!TryGetEncounterPreviewBounds(encounterPreviewModel, out bounds))
                    {
                        ClearEncounterPreviewModel();
                        return;
                    }
                }
            }

            Vector3 centerInPivot = encounterPreviewPivot.InverseTransformPoint(bounds.center);
            encounterPreviewModel.transform.localPosition -= centerInPivot;
            float horizontalRadius = Mathf.Sqrt(bounds.extents.x * bounds.extents.x
                + bounds.extents.z * bounds.extents.z);
            float fittedHalfHeight = Mathf.Max(bounds.extents.y,
                horizontalRadius / encounterPreviewCamera.aspect);
            encounterPreviewCamera.orthographicSize = Mathf.Max(.7f, fittedHalfHeight * 1.16f);
            float distance = Mathf.Max(7f, bounds.extents.magnitude * 2.4f);
            encounterPreviewCamera.transform.localPosition = new Vector3(0f, 0f, -distance);
            encounterPreviewCamera.transform.localRotation = Quaternion.identity;
            encounterPreviewCamera.nearClipPlane = .03f;
            encounterPreviewCamera.farClipPlane = distance * 3f + 20f;
            encounterPreviewCamera.backgroundColor = Color.Lerp(MenuPresentation.Wall, card.Accent, .09f);
            encounterPreviewPivot.localRotation = Quaternion.Euler(0f, card.PreviewYaw, 0f);
            encounterPreviewCamera.enabled = true;
            renderedEncounterCard = card;
        }

        // A preview copy only renders: no scripts, clicks, sound, labels, or own cameras.
        void MakeStillPreviewModel(GameObject model, Color accent)
        {
            PassengerView passenger = model.GetComponentInChildren<PassengerView>(true);
            if (passenger != null && passenger.jackets != null)
                foreach (Renderer jacket in passenger.jackets)
                    if (jacket != null) jacket.sharedMaterial = Mat(accent);

            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = EncounterPreviewLayer;
                child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (AudioSource source in model.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;
            foreach (Camera nestedCamera in model.GetComponentsInChildren<Camera>(true))
                nestedCamera.enabled = false;
            foreach (Light nestedLight in model.GetComponentsInChildren<Light>(true))
                nestedLight.enabled = false;
            foreach (TextMesh textMesh in model.GetComponentsInChildren<TextMesh>(true))
            {
                Renderer textRenderer = textMesh.GetComponent<Renderer>();
                if (textRenderer != null) textRenderer.enabled = false;
            }
        }

        bool TryGetEncounterPreviewBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            if (model == null) return false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        void ClearEncounterPreviewModel()
        {
            if (encounterPreviewModel != null)
            {
                encounterPreviewModel.SetActive(false);
                Destroy(encounterPreviewModel);
            }
            encounterPreviewModel = null;
            renderedEncounterCard = null;
            if (encounterPreviewCamera != null) encounterPreviewCamera.enabled = false;
        }

        void ReleaseEncounterPreview()
        {
            ClearEncounterPreviewModel();
            ReleaseDimensionLineup();
            if (encounterPreviewCamera != null) encounterPreviewCamera.targetTexture = null;
            if (encounterPreviewTexture != null)
            {
                encounterPreviewTexture.Release();
                Destroy(encounterPreviewTexture);
            }
            if (encounterPreviewStage != null) Destroy(encounterPreviewStage.gameObject);
            encounterPreviewStage = null;
            encounterPreviewPivot = null;
            encounterPreviewCamera = null;
            encounterPreviewTexture = null;
        }

        void EnqueueEncounterCard(EncounterCard card)
        {
            if (card == null || MenuManager.TutorialActive) return;
            if (activeEncounterCard == null)
            {
                ActivateEncounterCard(card);
            }
            else encounterCards.Enqueue(card);
        }

        void ActivateEncounterCard(EncounterCard card)
        {
            activeEncounterCard = card;
            encounterCardTime = 0f;
            PrepareEncounterPreview(card);
        }

        void TickEncounterCards(float dt)
        {
            if (activeEncounterCard == null || dt <= 0f) return;
            if (encounterPreviewPivot != null && encounterPreviewModel != null)
            {
                encounterPreviewPivot.localRotation = Quaternion.Euler(0f,
                    activeEncounterCard.PreviewYaw + Mathf.Sin(encounterCardTime * 1.15f) * 13f, 0f);
            }
            encounterCardTime += dt;
        }

        public void AdvanceEncounterCardAfterConfirmation()
        {
            if (activeEncounterCard == null) return;
            if (activeEncounterCard.IsWorldDiscovery)
                completedWorldTutorials[Seat].Add(activeEncounterCard.World);
            if (!string.IsNullOrEmpty(activeEncounterCard.PassengerKind))
                completedPassengerTutorials[Seat].Add(activeEncounterCard.PassengerKind);
            activeEncounterCard = null;
            encounterCardTime = 0f;
            ClearEncounterPreviewModel();
            if (encounterCards.Count > 0)
                ActivateEncounterCard(encounterCards.Dequeue());
        }

        public void SuppressEncounterCards()
        {
            encounterCards.Clear();
            activeEncounterCard = null;
            encounterCardTime = 0f;
            ClearEncounterPreviewModel();
        }

        void DrawEncounterCard(float viewWidth, float viewHeight)
        {
            if (MenuManager.TutorialActive || activeEncounterCard == null
                || Event.current.type != EventType.Repaint) return;
            bool compact = Match != null;
            float width = Mathf.Min(compact ? 520f : 760f, viewWidth - (compact ? 20f : 48f));
            // Leave enough footer room for the enlarged confirmation keycap.
            float height = compact ? 164f : 154f;
            float targetX = (viewWidth - width) * .5f;
            float enter = EaseOut(Mathf.Clamp01(encounterCardTime / .24f));
            float x = Mathf.Lerp(viewWidth + 24f, targetX, enter);
            float y = compact ? 78f : 94f;
            Rect card = new Rect(Mathf.Round(x), y, width, height);
            Panel(new Rect(card.x + 8f, card.y + 9f, card.width, card.height), new Color(0f, 0f, 0f, .35f));
            FramedPanel(card, activeEncounterCard.Accent, 4f);
            float previewWidth = compact ? 142f : 184f;
            Rect preview = new Rect(card.x + 4f, card.y + 4f, previewWidth, card.height - 8f);
            Panel(preview, Color.Lerp(UiBlack, activeEncounterCard.Accent, .16f));
            if (renderedEncounterCard == activeEncounterCard && encounterPreviewTexture != null
                && encounterPreviewModel != null)
            {
                Color previousGuiColor = GUI.color;
                GUI.color = Color.white;
                GUI.DrawTexture(preview, encounterPreviewTexture, ScaleMode.ScaleToFit, false);
                GUI.color = previousGuiColor;
            }
            else
            {
                if (string.IsNullOrEmpty(activeEncounterCard.PassengerKind))
                {
                    encounterBadge.normal.textColor = activeEncounterCard.Accent;
                    GUI.Label(preview, activeEncounterCard.Badge, encounterBadge);
                }
            }

            if (!string.IsNullOrEmpty(activeEncounterCard.PassengerKind))
            {
                const float iconSize = 62f;
                DrawPassengerBadgeIcon(new Rect(preview.xMax - iconSize - 10f,
                    preview.y + 10f, iconSize, iconSize), PassengerIconKind(activeEncounterCard),
                    activeEncounterCard.Accent);
            }

            int oldTitleSize = encounterTitle.fontSize;
            int oldBodySize = encounterBody.fontSize;
            int oldBadgeSize = encounterBadge.fontSize;
            encounterTitle.fontSize = compact ? 29 : 42;
            encounterBody.fontSize = compact ? 13 : 16;
            encounterBadge.fontSize = compact ? 34 : 46;
            encounterKicker.normal.textColor = activeEncounterCard.Accent;
            encounterTitle.normal.textColor = UiWhite;
            encounterBody.normal.textColor = new Color(UiWhite.r, UiWhite.g, UiWhite.b, .82f);
            float contentX = preview.xMax + (compact ? 14f : 20f);
            float contentWidth = card.xMax - contentX - 14f;
            GUI.Label(new Rect(contentX, card.y + 9f, contentWidth, 18f),
                activeEncounterCard.Kicker, encounterKicker);
            GUI.Label(new Rect(contentX, card.y + (compact ? 25f : 27f), contentWidth,
                compact ? 40f : 52f), activeEncounterCard.Title, encounterTitle);
            GUI.Label(new Rect(contentX, card.y + (compact ? 68f : 82f), contentWidth,
                compact ? 40f : 36f), activeEncounterCard.Description, encounterBody);
            const float promptSize = 52f;
            Rect prompt = new Rect(contentX, card.y + card.height - promptSize, promptSize, promptSize);
            DrawGameplayKeyHint(prompt, GameplayConfirmKey);
            GUI.Label(new Rect(prompt.x + promptSize + 12f, prompt.y + 12f,
                contentWidth - promptSize - 12f, 28f),
                "CONTINUE", encounterPrompt);
            encounterTitle.fontSize = oldTitleSize;
            encounterBody.fontSize = oldBodySize;
            encounterBadge.fontSize = oldBadgeSize;
        }

        // Draw menus or the active-shift HUD.
        void OnGUI()
        {
            // Mode select owns the first screen. Don't paint the intro curtain over it —
            // Update also freezes introTime while the menu is open, which would stick on black.
            if (round == null || eye == null || MenuManager.IsOpen
                || MenuManager.RoundPassengerIntroductionActive) return;
            if (paused && MenuManager.PauseOpen) return;
            if (paused && Match == null && !MenuManager.TutorialActive)
            {
                Styles();
                scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
                offsetX = (Screen.width - 1440 * scale) / 2; offsetY = (Screen.height - 900 * scale) / 2;
                GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0), Quaternion.identity, Vector3.one * scale);
                Overlay();
                GUI.matrix = Matrix4x4.identity;
                return;
            }
            if (dimensionRevealActive)
            {
                if (Match == null)
                {
                    GUI.matrix = Matrix4x4.identity;
                    DrawDimensionRevealOverlay(new Rect(0f, 0f, Screen.width, Screen.height), dimensionRevealTime);
                    DrawSessionFade(sessionFadeAlpha);
                }
                return;
            }
            if (phase == Phase.Countdown)
            {
                if (!paused && (Match == null || !IsNpc)) DrawNoTutorialCountdown();
                return;
            }
            if (Match != null || MenuManager.FullScreenOverlayActive) return;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.enabled = true;
            GameTypography.ApplyToSkin(GUI.skin);
            Styles();
            if (phase == Phase.Intro)
            {
                scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
                offsetX = (Screen.width - 1440 * scale) / 2; offsetY = (Screen.height - 900 * scale) / 2;
                GUI.matrix = Matrix4x4.identity;
                if (introTime < IntroRevealStart)
                {
                    Panel(new Rect(0, 0, Screen.width, Screen.height), Color.black);
                }
                else
                {
                    // Mask only the two moving halves. The centre is deliberately
                    // left undrawn so the live 3D elevator is actually revealed.
                    float reveal = EaseOut(Mathf.Clamp01((introTime - IntroRevealStart) / IntroRevealDuration));
                    float curtainWidth = Screen.width * .5f * (1 - reveal);
                    Panel(new Rect(0, 0, curtainWidth, Screen.height), Color.black);
                    Panel(new Rect(Screen.width - curtainWidth, 0, curtainWidth, Screen.height), Color.black);
                    float edgeAlpha = 1f - Mathf.Clamp01(reveal * .85f);
                    Panel(new Rect(curtainWidth - 6, 0, 6, Screen.height), new Color(Cream.r, Cream.g, Cream.b, edgeAlpha));
                    Panel(new Rect(Screen.width - curtainWidth, 0, 6, Screen.height), new Color(Cream.r, Cream.g, Cream.b, edgeAlpha));
                }
                GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0), Quaternion.identity, Vector3.one * scale);
                IntroOverlay();
                GUI.matrix = Matrix4x4.identity;
                DrawSessionFade(sessionFadeAlpha);
                return;
            }
            // The tutorial holds the car behind its cards; that is not the pause menu.
            if (phase == Phase.Results || paused && !MenuManager.TutorialActive)
            {
                scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
                offsetX = (Screen.width - 1440 * scale) / 2; offsetY = (Screen.height - 1000 * scale) / 2;
                GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0), Quaternion.identity, Vector3.one * scale);
                Overlay();
            }
            else if (!extendedInterior && (phase == Phase.Opening || phase == Phase.Boarding || phase == Phase.Closing))
            {
                // The remaining time contracts symmetrically toward the centre,
                // echoing the physical elevator doors closing from both sides.
                float fill = phase == Phase.Opening ? 1f : phase == Phase.Boarding
                    ? 1f - Mathf.Clamp01(phaseTime / BoardingDurationAtCurrentFloor()) : 0f;
                float margin = Mathf.Clamp(Screen.width * .025f, 24f, 36f);
                float width = Screen.width - margin * 2f;
                Panel(new Rect(margin, 18f, width, 12f), new Color(Ink.r, Ink.g, Ink.b, .55f));
                float remaining = width * fill;
                Panel(new Rect(margin + (width - remaining) * .5f, 18f, remaining, 12f), fill > .25f ? Teal : Coral);
                Panel(new Rect(Screen.width * .5f - 1f, 16f, 2f, 16f), Cream);
            }
            GUI.matrix = Matrix4x4.identity;
            if (phase != Phase.Results && (!paused || MenuManager.TutorialActive))
            {
                DrawTravelView();
                float hudScale = extendedInterior ? Mathf.Max(1, Screen.height / 900f) : 1;
                GUI.matrix = Matrix4x4.Scale(Vector3.one * hudScale);
                if (extendedInterior && compactTopHud) DrawCompactTopHUD();
                else DrawRoundHUD();
                DrawInteriorHUD();
                if (extendedInterior) DrawElevatorSpeech(Screen.height / hudScale, Screen.width / hudScale);
                GUI.matrix = Matrix4x4.identity;
                if (extendedInterior && Match == null) DrawPassengerCallouts();
                DrawMouseControls();
                DrawDeliveryPopups(Screen.width, Screen.height);
                DrawEncounterCard(Screen.width, Screen.height);
                DrawActionFeedback(Screen.width, Screen.height);
                DrawScoreFlights(Screen.width, Screen.height, SoloScoreHudAnchor());
            }
            DrawSessionFade(sessionFadeAlpha);
            MenuManager.DrawTutorialCardsAboveHud();
        }

        void DrawSessionFade(float alpha)
        {
            if (alpha <= .001f) return;
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(alpha));
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawNoTutorialCountdown()
        {
            GameTypography.ApplyToSkin(GUI.skin);
            Styles();
            int previousDepth = GUI.depth;
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.depth = -1000;
            Rect view = new Rect(0f, 0f, Screen.width, Screen.height);
            MenuPresentation.Backdrop(view);
            GUI.matrix = previousMatrix * MenuPresentation.Fit(view, 1200f, 640f);

            var progress = GameManager.Instance;
            string roundLabel = "ROUND " + (progress != null ? progress.RoundNumber : 1);
            string worldName = progress != null ? progress.ActiveDimensionName : "OFFICE";
            string countdown = (3 - Mathf.FloorToInt(countdownTime)).ToString();
            Color accent = WorldAccent(round != null ? round.Floor : 0);
            Color edge = Color.Lerp(accent, MenuPresentation.Ink, .58f);
            Rect board = new Rect(357f, 94f, 486f, 442f);
            MenuPresentation.Plaque(board, MenuPresentation.Paper, edge, 10f);
            MenuPresentation.Fasteners(board, edge);

            Rect badge = new Rect(505f, 120f, 190f, 44f);
            MenuPresentation.Plaque(badge, accent, edge, 4f);
            var centered = new GUIStyle(title)
                { fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            centered.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Ink);
            GUI.Label(badge, roundLabel, centered);
            centered.fontSize = 35;
            GUI.Label(new Rect(380f, 176f, 440f, 70f), worldName, centered);

            Rect display = new Rect(479f, 262f, 242f, 234f);
            MenuPresentation.Plaque(display, MenuPresentation.Wall, edge, 6f);
            MenuPresentation.Fasteners(display, MenuPresentation.Brass);
            centered.fontSize = 144;
            centered.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            GUI.Label(display, countdown, centered);

            GUI.matrix = previousMatrix;
            GUI.depth = previousDepth;
        }

        // Match HUD is drawn by ElevatorMatch so the solo UI remains unchanged.
        public void DrawMatchViewGUI()
        {
            if (round == null || eye == null) return;
            Styles();
            if (IsNpc)
            {
                Rect npcView = Match.ViewRect(Seat);
                Matrix4x4 npcMatrix = GUI.matrix;
                GUI.matrix = Matrix4x4.Translate(new Vector3(npcView.x, npcView.y, 0));
                if (extendedInterior) DrawElevatorSpeech(npcView.height, npcView.width);
                if (extendedInterior) DrawPassengerCallouts();
                DrawDeliveryPopups(npcView.width, npcView.height);
                DrawActionFeedback(npcView.width, npcView.height);
                GUI.matrix = npcMatrix;
                return;
            }
            DrawTravelView();
            Rect view = Match.ViewRect(Seat);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Translate(new Vector3(view.x, view.y, 0));
            if (extendedInterior) DrawElevatorSpeech(view.height, view.width);
            if (extendedInterior) DrawPassengerCallouts();
            DrawMouseControls();
            DrawDeliveryPopups(view.width, view.height);
            DrawEncounterCard(view.width, view.height);
            DrawActionFeedback(view.width, view.height);
            GUI.matrix = previous;
        }

        Vector2 SoloScoreHudAnchor()
        {
            float hudScale = extendedInterior ? Mathf.Max(1f, Screen.height / 900f) : 1f;
            if (extendedInterior && compactTopHud)
            {
                Rect hud = SoloHudRect();
                return new Vector2(hud.x + hud.width * .57f, hud.y + 37f) * hudScale;
            }

            float x = Mathf.Max(12f, Screen.width / hudScale - 258f);
            return new Vector2((x + 123f) * hudScale, 92f * hudScale);
        }

        // Reward tokens fly from the delivery point to this player's score tile.
        public void DrawScoreFlights(float viewWidth, float viewHeight, Vector2 scoreAnchor)
        {
            float now = Time.unscaledTime;
            for (int i = scoreFlights.Count - 1; i >= 0; i--)
                if (now - scoreFlights[i].StartedAt >= ScoreFlightDuration)
                    scoreFlights.RemoveAt(i);
            if (scoreFlights.Count == 0 || Event.current.type != EventType.Repaint) return;

            Styles();
            float uiScale = Mathf.Clamp(viewHeight / 900f, .72f, 1.15f);
            for (int i = 0; i < scoreFlights.Count; i++)
            {
                ScoreFlight flight = scoreFlights[i];
                float age = now - flight.StartedAt;
                if (age < 0f) continue;

                float t = Mathf.Clamp01(age / ScoreFlightDuration);
                float eased = EaseOut(t);
                Vector2 start = new Vector2(flight.Viewport.x * viewWidth,
                    (1f - flight.Viewport.y) * viewHeight);
                Vector2 position = Vector2.Lerp(start, scoreAnchor, eased);
                position.y -= Mathf.Sin(t * Mathf.PI) * Mathf.Min(58f * uiScale, viewHeight * .1f);
                position.x += Mathf.Sin(t * Mathf.PI * 2f + flight.Serial * .73f) * 9f * (1f - t) * uiScale;

                float alpha = Mathf.Clamp01(age / .08f)
                    * (1f - Mathf.Clamp01((t - .82f) / .18f));
                float tokenScale = Mathf.Lerp(1.08f, .76f, eased);
                float width = 78f * uiScale * tokenScale;
                float height = 32f * uiScale * tokenScale;
                Color accent = Match != null ? (Seat == 0
                    ? new Color32(255, 124, 104, 255) : new Color32(75, 226, 202, 255)) : Teal;

                // A short dotted trail makes the flight direction readable without
                // leaving a persistent line across the playfield.
                for (int trail = 3; trail >= 1; trail--)
                {
                    float trailT = Mathf.Clamp01(t - trail * .055f);
                    Vector2 trailPosition = Vector2.Lerp(start, scoreAnchor, EaseOut(trailT));
                    trailPosition.y -= Mathf.Sin(trailT * Mathf.PI)
                        * Mathf.Min(58f * uiScale, viewHeight * .1f);
                    float dotSize = (5f - trail) * uiScale;
                    Color dotColor = accent;
                    dotColor.a = alpha * (1f - trail * .2f);
                    Panel(new Rect(trailPosition.x - dotSize * .5f,
                        trailPosition.y - dotSize * .5f, dotSize, dotSize), dotColor);
                }

                Rect token = new Rect(position.x - width * .5f, position.y - height * .5f,
                    width, height);
                Color panel = new Color(UiBlack.r, UiBlack.g, UiBlack.b, .94f * alpha);
                Panel(token, panel);
                Panel(new Rect(token.x, token.y, 3f * uiScale, token.height),
                    new Color(accent.r, accent.g, accent.b, alpha));
                int previousFontSize = scoreFlightStyle.fontSize;
                Color previousTextColor = scoreFlightStyle.normal.textColor;
                scoreFlightStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(18f * uiScale), 13, 20);
                scoreFlightStyle.normal.textColor = new Color(Gold.r, Gold.g, Gold.b, alpha);
                GUI.Label(token, "+" + flight.Points, scoreFlightStyle);
                scoreFlightStyle.fontSize = previousFontSize;
                scoreFlightStyle.normal.textColor = previousTextColor;
            }
        }

        // One short cue per car, so mistakes and steals stay readable in split screen.
        public void ShowActionFeedback(string label, bool positive, bool emphasized = false)
        {
            if (string.IsNullOrEmpty(label) || round == null || round.Finished) return;
            // Ability introductions pause game time; keep the cue for the first playable frame after them.
            float now = Time.time;
            float age = now - actionFeedbackAt;
            if (actionFeedbackEmphasized && age < .7f && !emphasized) return;
            bool automaticBoardingBlocked = label == "NO ROOM!" || label == "NO OPEN TILES!";
            float repeatDelay = automaticBoardingBlocked ? 2.2f : .4f;
            if (actionFeedbackText == label && age < repeatDelay) return;
            actionFeedbackText = label;
            actionFeedbackPositive = positive;
            actionFeedbackEmphasized = emphasized;
            actionFeedbackAt = now;
        }

        void ClearActionFeedback()
        {
            actionFeedbackText = null;
            actionFeedbackAt = -100f;
            actionFeedbackEmphasized = false;
        }

        void DrawActionFeedback(float viewWidth, float viewHeight)
        {
            if (string.IsNullOrEmpty(actionFeedbackText) || Event.current.type != EventType.Repaint) return;
            float age = Time.time - actionFeedbackAt;
            if (age < 0f || age >= ActionFeedbackSeconds) return;

            float scale = Mathf.Clamp(viewHeight / 900f, .7f, 1.1f);
            float entrance = EaseOut(Mathf.Clamp01(age / .15f));
            float alpha = Mathf.Clamp01((ActionFeedbackSeconds - age) / .27f) * entrance;
            float width = Mathf.Min(viewWidth - 24f, (actionFeedbackEmphasized ? 330f : 270f) * scale);
            float height = (actionFeedbackEmphasized ? 56f : 44f) * scale;
            // Clear the match collision banner and any passenger introduction card.
            float minimumY = activeEncounterCard != null ? 260f : 154f;
            float y = Mathf.Max(minimumY, viewHeight * .18f) + (1f - entrance) * 12f * scale;
            Rect cue = new Rect((viewWidth - width) * .5f, y, width, height);
            Color accent = actionFeedbackPositive ? Teal : Coral;
            Panel(cue, new Color(UiBlack.r, UiBlack.g, UiBlack.b, .84f * alpha));
            Panel(new Rect(cue.x, cue.y, cue.width, 4f * scale),
                new Color(accent.r, accent.g, accent.b, alpha));
            if (actionFeedbackEmphasized)
                Panel(new Rect(cue.x + 4f * scale, cue.yMax - 4f * scale,
                    cue.width - 8f * scale, 2f * scale),
                    new Color(accent.r, accent.g, accent.b, .7f * alpha));

            actionWord.fontSize = Mathf.RoundToInt((actionFeedbackEmphasized ? 26f : 21f) * scale);
            actionWord.normal.textColor = new Color(UiWhite.r, UiWhite.g, UiWhite.b, alpha);
            GUI.Label(new Rect(cue.x + 8f * scale, cue.y + 4f * scale,
                cue.width - 16f * scale, cue.height - 8f * scale), actionFeedbackText, actionWord);
        }

        // Screen-space points stay readable while the 3D passenger exits.
        void DrawDeliveryPopups(float viewWidth, float viewHeight)
        {
            float now = Time.unscaledTime;
            for (int i = deliveryPopups.Count - 1; i >= 0; i--)
                if (now - deliveryPopups[i].StartedAt >= DeliveryPopupDuration)
                    deliveryPopups.RemoveAt(i);
            if (deliveryPopups.Count == 0 || Event.current.type != EventType.Repaint) return;

            float uiScale = Mathf.Clamp(viewHeight / 900f, .72f, 1.15f);
            for (int i = 0; i < deliveryPopups.Count; i++)
            {
                DeliveryPopup popup = deliveryPopups[i];
                float age = now - popup.StartedAt;
                float progress = Mathf.Clamp01(age / DeliveryPopupDuration);
                float entrance = Mathf.Clamp01(age / .16f);
                float settle = Mathf.Clamp01((age - .16f) / .2f);
                float popScale = entrance < 1f
                    ? Mathf.Lerp(.62f, 1.16f, EaseOut(entrance))
                    : Mathf.Lerp(1.16f, 1f, EaseOut(settle));
                float alpha = 1f - Mathf.Clamp01((progress - .7f) / .3f);
                float rise = EaseOut(progress) * 76f * uiScale;
                float jitter = Mathf.Sin(age * 32f + popup.Serial * 1.7f) * 2.5f * (1f - progress);

                float width = Mathf.Min(290f * uiScale * popScale, viewWidth - 24f);
                float centerX = Mathf.Clamp(popup.Viewport.x * viewWidth + jitter,
                    width * .5f + 12f, viewWidth - width * .5f - 12f);
                float centerY = (1f - popup.Viewport.y) * viewHeight - rise;
                Color accent = popup.Combo > 1 ? Coral : Teal;

                float burst = EaseOut(Mathf.Clamp01(age / .48f));
                for (int spark = 0; spark < 6; spark++)
                {
                    float angle = (spark / 6f) * Mathf.PI * 2f + popup.Serial * .37f;
                    float radius = Mathf.Lerp(24f, 78f, burst) * uiScale;
                    float size = Mathf.Lerp(11f, 3f, burst) * uiScale;
                    Vector2 position = new Vector2(centerX + Mathf.Cos(angle) * radius,
                        centerY + Mathf.Sin(angle) * radius);
                    Color sparkColor = spark % 2 == 0 ? Gold : accent;
                    sparkColor.a = alpha * (1f - burst * .45f);
                    Panel(new Rect(position.x - size * .5f, position.y - size * .5f, size, size), sparkColor);
                }

                int previousPointSize = rewardPoints.fontSize;
                int previousCaptionSize = rewardCaption.fontSize;
                Color previousPointColor = rewardPoints.normal.textColor;
                Color previousCaptionColor = rewardCaption.normal.textColor;
                rewardPoints.fontSize = Mathf.Max(24, Mathf.RoundToInt(48f * uiScale * popScale));
                rewardCaption.fontSize = Mathf.Max(12, Mathf.RoundToInt(17f * uiScale));

                Rect pointsRect = new Rect(centerX - width * .5f, centerY - 48f * uiScale,
                    width, 62f * uiScale * popScale);
                rewardPoints.normal.textColor = new Color(Ink.r, Ink.g, Ink.b, alpha * .65f);
                GUI.Label(new Rect(pointsRect.x + 3f, pointsRect.y + 4f, pointsRect.width, pointsRect.height),
                    "+" + popup.Points, rewardPoints);
                rewardPoints.normal.textColor = new Color(Gold.r, Gold.g, Gold.b, alpha);
                GUI.Label(pointsRect, "+" + popup.Points, rewardPoints);

                string caption = popup.Combo > 1 ? "QUICK COMBO  x" + popup.Combo
                    : popup.Happy ? "PERFECT DROP!" : "RIGHT FLOOR!";
                bool floatingPerfect = popup.Happy && popup.Combo <= 1;
                if (floatingPerfect)
                {
                    // Perfect drops use the same floating 3D treatment as the
                    // points, so the reward does not get buried in another box.
                    rewardCaption.fontSize = Mathf.Max(14, Mathf.RoundToInt(21f * uiScale));
                    Rect captionRect = new Rect(centerX - width * .5f, centerY + 14f * uiScale,
                        width, 30f * uiScale);
                    rewardCaption.normal.textColor = new Color(Ink.r, Ink.g, Ink.b, alpha * .8f);
                    GUI.Label(new Rect(captionRect.x + 3f, captionRect.y + 3f,
                        captionRect.width, captionRect.height), caption, rewardCaption);
                    rewardCaption.normal.textColor = new Color(Cream.r, Cream.g, Cream.b, alpha);
                    GUI.Label(captionRect, caption, rewardCaption);
                }
                else
                {
                    float bannerWidth = Mathf.Min(width * .82f, 230f * uiScale);
                    Rect banner = new Rect(centerX - bannerWidth * .5f, centerY + 14f * uiScale,
                        bannerWidth, 30f * uiScale);
                    FramedPanel(banner, accent, 2f);
                    rewardCaption.normal.textColor = new Color(Cream.r, Cream.g, Cream.b, alpha);
                    GUI.Label(new Rect(banner.x + 6f, banner.y, banner.width - 12f, banner.height),
                        caption, rewardCaption);
                }

                rewardPoints.fontSize = previousPointSize;
                rewardCaption.fontSize = previousCaptionSize;
                rewardPoints.normal.textColor = previousPointColor;
                rewardCaption.normal.textColor = previousCaptionColor;
            }
        }

        public void DrawMatchIntroGUI()
        {
            Styles();
            float introScale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
            float introX = (Screen.width - 1440f * introScale) * .5f;
            float introY = (Screen.height - 900f * introScale) * .5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(introX, introY, 0), Quaternion.identity, Vector3.one * introScale);
            IntroOverlay();
        }

        // A single compact strip keeps essential information readable without
        // covering the world with separate status boxes.
        Rect SoloHudRect()
        {
            float hudScale = Mathf.Max(1f, Screen.height / 900f);
            return new Rect(12f, 8f, Mathf.Min(424f, Screen.width / hudScale - 24f), 58f);
        }

        void DrawCompactTopHUD()
        {
            int seconds = Mathf.CeilToInt(round.TimeLeft);
            string clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            Rect hud = SoloHudRect();
            MenuPresentation.HudPlate(hud, MenuPresentation.Teal);
            Rect floor = new Rect(hud.x, hud.y, hud.width * .42f, hud.height);
            Rect points = new Rect(floor.xMax, hud.y, hud.width * .30f, hud.height);
            Rect time = new Rect(points.xMax, hud.y, hud.width * .28f, hud.height);
            DrawHudTile(floor, MenuPresentation.Paper, WorldName(round.Floor),
                "F" + ElevatorRound.FloorNumber(round.Floor));
            DrawHudTile(points, MenuPresentation.Brass, "SCORE", round.Score.ToString());
            DrawHudTile(time, seconds <= 10 ? MenuPresentation.Coral : MenuPresentation.Paper, "TIME", clock);
        }

        readonly List<Rect> passengerBadgeObstacles = new List<Rect>();

        // Draw pickup information in screen space, inside the owning camera pane.
        void DrawPassengerCallouts()
        {
            if (BuildingView) return;
            Rect view = Match != null ? Match.ViewRect(Seat) : new Rect(0, 0, Screen.width, Screen.height);
            float viewWidth = view.width;
            float viewHeight = view.height;
            var visible = new List<Rider>();
            foreach (Rider rider in round.Riders)
                if (IsVisibleSpeaker(rider)) visible.Add(rider);
            if (visible.Count == 0) return;

            passengerBadgeObstacles.Clear();
            if (ShowBoardingDoorHints)
                for (int slot = 0; slot < 3; slot++)
                    passengerBadgeObstacles.Add(BoardingDoorHintRect(BoardingDoorAtScreenSlot(slot), slot));
            Rect badgeArea = new Rect(0f, 64f, viewWidth, viewHeight - 64f);

            Rider speaker = null;
            foreach (Rider rider in visible)
                if (rider.Boarded && (rider.HasFeature(PassengerFeature.CryingBaby)
                    || rider.Remaining <= rider.Patience * .25f
                    || round.AtDestination(rider) || !string.IsNullOrEmpty(rider.Status)))
                { speaker = rider; break; }
            if (speaker == null && selectedRider != null && visible.Contains(selectedRider)) speaker = selectedRider;

            foreach (Rider rider in visible)
            {
                if (!figures.TryGetValue(rider, out Transform figure)) continue;
                Vector3 headPoint = eye.WorldToScreenPoint(figure.position + Vector3.up * 2.08f);
                Vector3 footPoint = eye.WorldToScreenPoint(PassengerDestinationAnchor(rider, figure));
                if (headPoint.z <= 0 || footPoint.z <= 0) continue;
                float projectedHeadX = headPoint.x - view.x;
                if (projectedHeadX < -24f || projectedHeadX > viewWidth + 24f) continue;
                float localX = footPoint.x - view.x;
                float footY = view.y + view.height - footPoint.y;
                float headY = view.y + view.height - headPoint.y;
                float x = Mathf.Round(Mathf.Clamp(localX, 30f, viewWidth - 30f));
                float headX = Mathf.Round(Mathf.Clamp(projectedHeadX, 30f, viewWidth - 30f));
                footY = Mathf.Round(Mathf.Clamp(footY, 96f, viewHeight - 38f));
                headY = Mathf.Round(Mathf.Clamp(headY, 72f, viewHeight - 116f));
                Color accent = Palette[rider.Color % Palette.Length];
                bool selected = rider == selectedRider || rider == draggedRider;

                // Keep the waiting destination beside the passenger's feet,
                // leaving the selection arrow and ability cue near the head.
                if (!rider.Boarded && !rider.Resolved && round.IsOffered(rider))
                    DrawPickupDestination(rider, headX, footY, badgeArea, selected);

                // The selection arrow belongs above the passenger, not above
                // the lower-left floor marker—especially for multi-cell boxes.
                Rect arrow = new Rect(headX - 24f, headY - 26f, 48f, 40f);
                if (selected)
                {
                    Color arrowColor = selectionTint == BoardingInvalid ? BoardingInvalid : BoardingValid;
                    OutlinedLabel(arrow, "↓", selectionArrow, arrowColor);

                    // Keep the kick action beside the selected passenger so
                    // it remains visible even when the lower HUD is covered by
                    // the travel/door controls.
                    if (rider == selectedRider && rider.Boarded && draggedRider == null && phase == Phase.Boarding)
                    {
                        float hintX = Mathf.Clamp(x + 24f, 8f, viewWidth - 120f);
                        float hintY = Mathf.Clamp(footY - 48f, 72f, viewHeight - 54f);
                        Rect hint = new Rect(hintX, hintY, 42f, 42f);
                        DrawGameplayKeyHint(hint, GameplayKickoutKey);
                        Color previousHintText = calloutText.normal.textColor;
                        calloutText.normal.textColor = UiWhite;
                        GUI.Label(new Rect(hint.x + 50f, hint.y + 6f, 62f, 30f),
                            "KICK", calloutText);
                        calloutText.normal.textColor = previousHintText;
                    }
                }

                string cue = PassengerVisualCue(rider, selected);
                if (!string.IsNullOrEmpty(cue))
                {
                    float cueWidth = Mathf.Clamp(small.CalcSize(new GUIContent(cue)).x + 18f, 76f, 166f);
                    float cueX = headX + 12f + cueWidth <= viewWidth - 8f
                        ? headX + 12f : headX - cueWidth - 12f;
                    Rect cueRect = new Rect(Mathf.Clamp(cueX, 8f, viewWidth - cueWidth - 8f),
                        headY + 2f, cueWidth, 22f);
                    FramedPanel(cueRect, PassengerAbilityAccent(rider), 2f);
                    Label(new Rect(cueRect.x + 8f, cueRect.y + 2f, cueRect.width - 12f, 18f), cue, small);
                }

                if (rider != speaker) continue;
                bool mad = rider.Boarded && rider.Remaining <= 0;
                string message = PassengerCalloutText(rider);
                if (!mad && string.IsNullOrEmpty(message)) continue;
                float width = mad ? 64f
                    : Mathf.Clamp(calloutText.CalcSize(new GUIContent(message)).x + 22f, 112f, 220f);
                float quoteHeight = mad ? 64f : 28f;
                float quoteX = headX;
                Rect quote = new Rect(Mathf.Clamp(quoteX - width * .5f, 12f, viewWidth - width - 12f),
                    (selected ? arrow.y : headY - 4f) - quoteHeight - 3f, width, quoteHeight);
                if (mad)
                {
                    // MAD uses the simple standalone angry face. It should
                    // read as a passenger mood icon, not as a boxed message.
                    DrawPassengerAngryIcon(quote);
                }
                else
                {
                    SpeechBubblePanel(quote, accent);
                    GUI.Label(new Rect(quote.x + 6f, quote.y, quote.width - 12f, quote.height - 2f),
                        message, calloutText);
                }
            }
        }

        void DrawPickupDestination(Rider rider, float headX, float footY, Rect view, bool selected)
        {
            string abilityIcon = PassengerAbilityIconKind(rider);
            bool showAbility = !string.IsNullOrEmpty(abilityIcon);
            const float badgeHeight = 42f;
            float iconWidth = showAbility ? 36f : 0f;
            float vipHeight = rider.IsVip ? 24f : 0f;
            string destination = DestinationTag(rider);
            float badgeWidth = Mathf.Max(52f,
                Mathf.Ceil(calloutNumber.CalcSize(new GUIContent(destination)).x) + iconWidth + 12f);
            Rect area = PlacePassengerBadge(view, new Rect(headX - badgeWidth * .5f,
                footY - badgeHeight - vipHeight, badgeWidth, badgeHeight + vipHeight));
            passengerBadgeObstacles.Add(area);
            Rect badge = new Rect(area.x, area.y + vipHeight, area.width, badgeHeight);
            Color abilityAccent = PassengerAbilityAccent(rider);
            Color border = rider.IsVip ? Gold : showAbility ? abilityAccent : selected
                ? selectionTint == BoardingInvalid ? BoardingInvalid : BoardingValid : Teal;
            FramedPanel(badge, border, 3f);
            if (showAbility)
                DrawPassengerBadgeIcon(new Rect(badge.x + 3f, badge.y + 3f, iconWidth, iconWidth),
                    abilityIcon, abilityAccent);
            OutlinedLabel(new Rect(badge.x + iconWidth + 6f, badge.y, badge.width - iconWidth - 12f,
                badge.height), destination, calloutNumber);
            if (rider.IsVip)
                OutlinedLabel(new Rect(area.x, area.y, area.width, vipHeight), "VIP", calloutText, Gold);
        }

        Rect PlacePassengerBadge(Rect view, Rect badge)
        {
            badge = ClampInto(view, badge);
            if (!PassengerBadgeOverlaps(badge)) return badge;

            const float gap = 4f;
            Rect best = badge;
            float bestDistance = float.PositiveInfinity;
            foreach (Rect obstacle in passengerBadgeObstacles)
            {
                ConsiderPosition(obstacle.yMin - badge.height - gap);
                ConsiderPosition(obstacle.yMax + gap);
            }
            return best;

            void ConsiderPosition(float y)
            {
                float distance = Mathf.Abs(badge.y - y);
                if (distance >= bestDistance || y < view.yMin + 6f
                    || y + badge.height > view.yMax - 6f) return;
                Rect candidate = new Rect(badge.x, y, badge.width, badge.height);
                if (PassengerBadgeOverlaps(candidate)) return;
                best = candidate;
                bestDistance = distance;
            }
        }

        bool PassengerBadgeOverlaps(Rect badge)
        {
            foreach (Rect obstacle in passengerBadgeObstacles)
                if (badge.Overlaps(obstacle)) return true;
            return false;
        }

        string PassengerVisualCue(Rider rider, bool selected)
        {
            if (rider.Kind == "HUNTER") return "MONSTER STUN";
            if (rider.Kind == "DIVER") return "TORNADO SHIELD";
            if (rider.Kind == "BOUNCER") return "STRONG BUMP";
            if (rider.Kind == "FAIRY") return "FIREFLY SCOUT";
            if (rider.Kind == "GOBLIN") return "VINE SHORTCUT";
            if (rider.Kind == "MUSHROOM") return "SPORE GUARD";
            if (rider.Kind == "ASTRONAUT") return "GRAVITY TETHER";
            if (rider.Kind == "ALIEN") return "UFO LINK";
            if (!rider.Boarded)
            {
                if (rider.HasFeature(PassengerFeature.SpeedSlow)) return "SPEED DOWN";
                if (rider.HasFeature(PassengerFeature.SpeedBoost)) return "SPEED UP";
                if (rider.HasFeature(PassengerFeature.CryingBaby)) return "PATIENCE DRAIN";
                if (rider.HasFeature(PassengerFeature.ClearsRust)) return "CLEARS RUST";
                if (rider.HasFeature(PassengerFeature.UrgencySpeech)) return "SHORT PATIENCE";
                if (rider.Kind == "BOSS") return "DELIVERY BONUS";
                return selected && rider.Space > 1 ? rider.Space + " SPACES" : null;
            }
            if (!round.Owns(rider)) return null;
            if (rider.HasFeature(PassengerFeature.SpeedSlow))
                return round.BabyCalmedByGrandma ? "SPEED DOWN + CALM" : "SPEED DOWN";
            if (rider.HasFeature(PassengerFeature.SpeedBoost)) return "SPEED UP";
            if (rider.HasFeature(PassengerFeature.CryingBaby))
                return round.BabyCalmedByGrandma ? "BABY CALM" : "PATIENCE DRAIN";
            if (rider.HasFeature(PassengerFeature.ClearsRust)) return "CLEARS RUST";
            if (rider.HasFeature(PassengerFeature.UrgencySpeech)) return "SHORT PATIENCE";
            if (rider.Kind == "BOSS") return "DELIVERY BONUS";
            return null;
        }

        static string PassengerAbilityIconKind(Rider rider)
        {
            if (rider == null) return null;
            switch (rider.Kind)
            {
                case "ELDERLY": return "ELDERLY";
                case "PREGNANT": return "PREGNANT";
                case "GROUP": return "GROUP";
                case "HANDYMAN": return "HANDYMAN";
                case "HUNTER": return "HUNTER";
                case "DIVER": return "DIVER";
                case "BOUNCER": return "BOUNCER";
                case "BOSS": return "BOSS";
                case "INTERVIEW": return "INTERVIEW";
                case "COURIER": return "COURIER";
                case "FAIRY": return "FAIRY";
                case "GOBLIN": return "GOBLIN";
                case "MUSHROOM": return "MUSHROOM";
                case "ASTRONAUT": return "ASTRONAUT";
                case "ALIEN": return "ALIEN";
            }
            if (rider.HasFeature(PassengerFeature.SpeedSlow)) return "ELDERLY";
            if (rider.HasFeature(PassengerFeature.CryingBaby)) return "PREGNANT";
            if (rider.HasFeature(PassengerFeature.SpeedBoost)) return "SPEEDBOOST";
            if (rider.HasFeature(PassengerFeature.ClearsRust)) return "HANDYMAN";
            if (rider.HasFeature(PassengerFeature.MultiSpace)) return "COURIER";
            return null;
        }

        static Color PassengerAbilityAccent(Rider rider)
        {
            if (rider == null) return Teal;
            switch (rider.Kind)
            {
                case "ELDERLY": return new Color32(255, 66, 65, 255);
                case "PREGNANT": return new Color32(255, 128, 34, 255);
                case "GROUP": return new Color32(255, 211, 55, 255);
                case "HANDYMAN": return new Color32(42, 225, 210, 255);
                case "HUNTER": return new Color32(130, 255, 74, 255);
                case "DIVER": return new Color32(51, 171, 255, 255);
                case "BOUNCER": return new Color32(255, 89, 179, 255);
                case "BOSS": return new Color32(255, 205, 62, 255);
                case "INTERVIEW": return new Color32(255, 238, 86, 255);
                case "COURIER": return new Color32(255, 164, 55, 255);
                case "FAIRY": return new Color32(235, 117, 216, 255);
                case "GOBLIN": return new Color32(152, 214, 105, 255);
                case "MUSHROOM": return new Color32(255, 127, 122, 255);
                case "ASTRONAUT": return new Color32(137, 191, 255, 255);
                case "ALIEN": return new Color32(90, 235, 208, 255);
            }
            if (rider.HasFeature(PassengerFeature.SpeedSlow)) return new Color32(255, 66, 65, 255);
            if (rider.HasFeature(PassengerFeature.CryingBaby)) return new Color32(255, 128, 34, 255);
            if (rider.HasFeature(PassengerFeature.SpeedBoost)) return new Color32(255, 211, 55, 255);
            return Teal;
        }

        readonly List<Rider> manifestRiders = new List<Rider>();
        readonly Dictionary<Rider, RenderTexture> manifestRiderPortraits = new Dictionary<Rider, RenderTexture>();
        Vector2 manifestScroll;
        const float ManifestHeaderHeight = 35f;
        const float ManifestRowHeight = 60f;
        const float ManifestRowGap = 4f;
        const float ManifestInset = 6f;
        const float ManifestShadow = 3f;
        static readonly Color ManifestPortraitBacking = new Color32(166, 152, 197, 255);
        static readonly Color ManifestUrgentText = new Color32(148, 44, 55, 255);

        void UpdateManifestPortraits()
        {
            manifestRiderPortraits.Clear();
            MenuManager menu = MenuManager.Current;
            if (menu == null) return;

            foreach (Rider rider in round.Riders)
            {
                if (!round.Owns(rider)) continue;
                PassengerView prefab = FindPassengerArt(rider);
                if (prefab != null)
                    manifestRiderPortraits[rider] = menu.GetManifestPortrait(prefab, PassengerJacket(rider));
            }
        }

        Rect ManifestView => Match != null ? Match.ViewRect(Seat) : new Rect(0, 0, Screen.width, Screen.height);

        static float ManifestScale(Rect view) => Mathf.Min(Mathf.Clamp(view.height / 900f, .8f, 1.4f),
            view.width / 650f);

        Rect ManifestBoardRect(Rect view, float uiScale, int riderCount)
        {
            float width = Mathf.Clamp(view.width * passengerListWidth, 200f * uiScale, 240f * uiScale);
            float contentHeight = riderCount == 0 ? 26f : riderCount * ManifestRowHeight - ManifestRowGap;
            // Keep clear of the rival marker above and the travel controls below.
            float top = Mathf.Min(view.height * .4f, Mathf.Max(Match != null ? 140f : 102f, view.height * .14f));
            float availableHeight = Mathf.Max(76f * uiScale, view.height - top - 205f);
            float height = Mathf.Min(Mathf.Min(view.height * .52f, availableHeight),
                (ManifestHeaderHeight + contentHeight + ManifestInset + ManifestShadow) * uiScale);
            return new Rect(view.xMax - width - 14f * uiScale, view.y + top, width, height);
        }

        Rect PassengerListRect()
        {
            Rect view = ManifestView;
            int count = 0;
            foreach (Rider rider in round.Riders) if (round.Owns(rider)) count++;
            return ManifestBoardRect(view, ManifestScale(view), count);
        }

        void DrawPassengerList()
        {
            if (!showPassengerList || !BuildingView || cameraViewBlend < .99f) return;
            manifestRiders.Clear();
            foreach (Rider rider in round.Riders)
                if (round.Owns(rider)) manifestRiders.Add(rider);
            // Stable insertion sort preserves boarding order when urgency ties.
            for (int i = 1; i < manifestRiders.Count; i++)
            {
                Rider rider = manifestRiders[i];
                int j = i - 1;
                while (j >= 0 && manifestRiders[j].Remaining > rider.Remaining)
                { manifestRiders[j + 1] = manifestRiders[j]; j--; }
                manifestRiders[j + 1] = rider;
            }
            Rect view = ManifestView;
            float uiScale = ManifestScale(view);
            int count = manifestRiders.Count;
            Rect board = ManifestBoardRect(view, uiScale, count);
            int stopFloor = phase == Phase.Moving ? GlideStopFloor()
                : phase == Phase.Docking ? destination : round.Floor;
            int stopSide = phase == Phase.Moving ? NearestStop(RouteLateralX)
                : phase == Phase.Docking ? dockingStop : round.Stop;
            int dropOffCount = 0;
            foreach (Rider rider in manifestRiders)
                if (rider.Destination == stopFloor && rider.DestinationStop == stopSide) dropOffCount++;
            DrawManifestHeader(board, count, dropOffCount, uiScale);

            float inset = ManifestInset * uiScale;
            float headerHeight = ManifestHeaderHeight * uiScale;
            Rect scroll = new Rect(board.x + inset, board.y + headerHeight, board.width - 2f * inset,
                board.height - headerHeight - inset - ManifestShadow * uiScale);
            if (count == 0)
            {
                manifestScroll = Vector2.zero;
                ManifestText(manifestDetail, scroll, "NO PASSENGERS YET", 13f * uiScale,
                    MenuPresentation.Paper, TextAnchor.MiddleCenter);
                return;
            }

            float rowHeight = ManifestRowHeight * uiScale;
            float rowGap = ManifestRowGap * uiScale;
            float contentHeight = count * rowHeight - rowGap;
            bool overflow = contentHeight - scroll.height > .01f;
            GUIStyle scrollbar = GUI.skin.verticalScrollbar;
            float scrollbarWidth = Mathf.Max(17f, scrollbar.fixedWidth + scrollbar.margin.horizontal);
            float rowWidth = scroll.width - (overflow ? scrollbarWidth : 0f);
            manifestScroll.y = Mathf.Clamp(manifestScroll.y, 0f, Mathf.Max(0f, contentHeight - scroll.height));
            manifestScroll = GUI.BeginScrollView(scroll, manifestScroll,
                new Rect(0, 0, rowWidth, overflow ? contentHeight : scroll.height), false, overflow);
            for (int i = 0; i < count; i++)
            {
                Rider rider = manifestRiders[i];
                bool atStop = rider.Destination == stopFloor && rider.DestinationStop == stopSide;
                DrawManifestRow(new Rect(0, i * rowHeight, rowWidth, rowHeight - rowGap), rider, uiScale, atStop);
            }
            GUI.EndScrollView();
        }

        void DrawManifestHeader(Rect board, int count, int dropOffCount, float uiScale)
        {
            float shadow = ManifestShadow * uiScale;
            float radius = 9f * uiScale;
            PaintManifestShape(new Rect(board.x, board.y + shadow, board.width, board.height - shadow),
                MenuPresentation.Ink, radius);
            PaintManifestShape(new Rect(board.x, board.y, board.width, board.height - shadow),
                MenuPresentation.Wall, radius);

            float inset = ManifestInset * uiScale;
            Rect header = new Rect(board.x + inset, board.y + inset, board.width - 2f * inset, 24f * uiScale);
            PaintManifestShape(header, MenuPresentation.Brass, 5f * uiScale);
            Rect counter = new Rect(header.xMax - 27f * uiScale, header.y + 3f * uiScale, 24f * uiScale, 18f * uiScale);
            PaintManifestShape(counter, MenuPresentation.Ink, 5f * uiScale);
            ManifestText(manifestTitle, counter, count.ToString(), 14f * uiScale, MenuPresentation.Paper,
                TextAnchor.MiddleCenter);
            Rect title = Rect.MinMaxRect(header.x + 7f * uiScale, header.y,
                counter.x - 4f * uiScale, header.yMax);
            if (dropOffCount > 0)
            {
                Rect dropOff = new Rect(title.xMax - 70f * uiScale, title.y, 70f * uiScale, title.height);
                title.xMax = dropOff.x - 4f * uiScale;
                ManifestText(manifestDetail, dropOff, dropOffCount + (phase == Phase.Opening ? " AT STOP" : " NEXT STOP"),
                    11f * uiScale, MenuPresentation.Ink, TextAnchor.MiddleRight);
            }
            ManifestText(manifestTitle, title, "ON BOARD", 16f * uiScale, MenuPresentation.Ink);
        }

        void DrawManifestRow(Rect row, Rider rider, float uiScale, bool atStop)
        {
            float patience = Mathf.Clamp01(rider.Remaining / Mathf.Max(.001f, rider.Patience));
            bool urgent = patience <= .25f;
            PaintManifestShape(row, MenuPresentation.Paper, 6f * uiScale);

            float frameSize = 48f * uiScale;
            float thickness = 3f * uiScale;
            Rect frame = new Rect(row.x + 5f * uiScale, row.center.y - frameSize * .5f, frameSize, frameSize);
            Color patienceColour = urgent ? MenuPresentation.Coral
                : patience <= .5f ? MenuPresentation.Brass : MenuPresentation.Teal;
            DrawManifestPatienceFrame(frame, thickness, patience, patienceColour);
            Rect portrait = Rect.MinMaxRect(frame.x + thickness, frame.y + thickness,
                frame.xMax - thickness, frame.yMax - thickness);
            MenuPresentation.Paint(portrait, ManifestPortraitBacking);
            DrawManifestPortrait(portrait, rider);

            float textX = frame.xMax + 7f * uiScale;
            float textRight = row.xMax - 6f * uiScale;
            ManifestText(manifestTitle, new Rect(textX, row.y + 7f * uiScale, textRight - textX, 20f * uiScale),
                PassengerRoleName(rider.Kind), 16f * uiScale, MenuPresentation.Ink);

            float badgeWidth = 42f * uiScale;
            Rect destination = new Rect(textRight - badgeWidth, row.y + 29f * uiScale, badgeWidth, 19f * uiScale);
            ManifestText(manifestDetail, Rect.MinMaxRect(textX, destination.y, destination.x - 4f * uiScale, destination.yMax),
                Mathf.CeilToInt(Mathf.Max(0f, rider.Remaining)) + "s · +" + round.BaseDeliveryPoints(rider),
                13f * uiScale, urgent ? ManifestUrgentText : MenuPresentation.Wall);
            PaintManifestShape(destination, atStop ? MenuPresentation.Brass : MenuPresentation.Teal, 5f * uiScale);
            string destinationText = (rider.DestinationStop == CenterStop ? "F" : "") + DestinationTag(rider);
            ManifestText(manifestTitle, destination, destinationText, 14f * uiScale,
                MenuPresentation.Ink, TextAnchor.MiddleCenter);
        }

        static void DrawManifestPatienceFrame(Rect frame, float thickness, float patience, Color colour)
        {
            MenuPresentation.Paint(frame, MenuPresentation.Wall);
            // Fill clockwise from the top left, counting each corner as part of one edge.
            float progress = patience * 4f;
            float edge = frame.width - thickness;
            if (progress > 0f)
                MenuPresentation.Paint(new Rect(frame.x, frame.y, edge * Mathf.Clamp01(progress), thickness), colour);
            if (progress > 1f)
                MenuPresentation.Paint(new Rect(frame.xMax - thickness, frame.y, thickness,
                    edge * Mathf.Clamp01(progress - 1f)), colour);
            if (progress > 2f)
            {
                float length = edge * Mathf.Clamp01(progress - 2f);
                MenuPresentation.Paint(new Rect(frame.xMax - length, frame.yMax - thickness, length, thickness), colour);
            }
            if (progress > 3f)
            {
                float length = edge * Mathf.Clamp01(progress - 3f);
                MenuPresentation.Paint(new Rect(frame.x, frame.yMax - length, thickness, length), colour);
            }
        }

        static void PaintManifestShape(Rect rect, Color colour, float radius)
            => GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
                MenuPresentation.DisplayColor(colour), 0f, radius);

        static void ManifestText(GUIStyle style, Rect rect, string text, float fontSize, Color colour,
            TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            GUIContent content = new GUIContent(text);
            style.alignment = alignment;
            style.fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize));
            while (style.fontSize > 8 && style.CalcSize(content).x > rect.width)
                style.fontSize--;
            MenuPresentation.Label(rect, content, style, colour);
        }

        static string PassengerRoleName(string kind)
        {
            switch (kind)
            {
                case "COURIER": return "COURIER";
                case "INTERVIEW": return "INTERVIEWEE";
                case "ELDERLY": return "GRANDMA";
                case "PREGNANT": return "MOM + BABY";
                case "GROUP": return "TRIO";
                case "SPEEDBOOST": return "CLOWN";
                case "HANDYMAN": return "HANDYMAN";
                case "HUNTER": return "HUNTER";
                case "DIVER": return "DIVER";
                case "BOUNCER": return "BOUNCER";
                case "BOSS": return "BOSS";
                case "FAIRY": return "FAIRY";
                case "GOBLIN": return "GOBLIN";
                case "MUSHROOM": return "MUSHROOM";
                case "ASTRONAUT": return "ASTRONAUT";
                case "ALIEN": return "ALIEN";
                default: return "WORKER";
            }
        }

        void DrawManifestPortrait(Rect rect, Rider rider)
        {
            if (manifestRiderPortraits.TryGetValue(rider, out RenderTexture portrait) && portrait != null)
                MenuPresentation.DrawTexture(rect, portrait);
            else
                DrawPassengerBadgeIcon(rect, rider.Kind, PassengerAbilityAccent(rider));
        }

        string PassengerCalloutText(Rider rider)
        {
            if (rider.Boarded && rider.Remaining <= 0) return "I'M MAD!";
            if (rider.Boarded && !string.IsNullOrEmpty(rider.Status)
                && rider.Status.IndexOf("passed my floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "MISSED MY FLOOR!";
            if (rider.Boarded && round.AtDestination(rider)) return "MY STOP!";
            if (rider.Boarded && rider.HasFeature(PassengerFeature.CryingBaby))
                return round.BabyCalmedByGrandma ? "CALM! PATIENCE x0.5" : "WAAAH! PATIENCE x1.6";
            if (rider.Boarded && rider.Remaining <= rider.Patience * .25f) return "PLEASE HURRY!";
            return null;
        }

        void DrawPassengerBadgeIcon(Rect rect, string kind, Color accent)
        {
            if (string.IsNullOrEmpty(kind)) return;
            string key = kind + ColorUtility.ToHtmlStringRGBA(accent);
            if (!passengerIconTextures.TryGetValue(key, out Texture2D texture) || texture == null)
            {
                texture = PassengerIconLibrary.CreatePassengerBadge(kind, accent, UiBlack, UiWhite);
                passengerIconTextures[key] = texture;
            }

            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        void DrawPassengerAngryIcon(Rect rect)
        {
            const string kind = "MAD_FACE";
            string key = kind + ColorUtility.ToHtmlStringRGBA(Coral);
            if (!passengerIconTextures.TryGetValue(key, out Texture2D texture) || texture == null)
            {
                texture = PassengerIconLibrary.CreateAngryFace(Coral, UiBlack);
                passengerIconTextures[key] = texture;
            }

            Color previous = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
            GUI.color = previous;
        }

        static PassengerFeature PassengerFeatures(Rider rider)
        {
            if (rider == null) return PassengerFeature.None;
            PassengerFeature features = PassengerFeature.None;
            PassengerFeature[] all =
            {
                PassengerFeature.SlowArrival,
                PassengerFeature.MultiSpace, PassengerFeature.UrgencySpeech,
                PassengerFeature.ClearsRust, PassengerFeature.GroupParty,
                PassengerFeature.SpeedBoost, PassengerFeature.SpeedSlow,
                PassengerFeature.CryingBaby, PassengerFeature.CalmsBaby
            };
            foreach (PassengerFeature feature in all)
                if (rider.HasFeature(feature)) features |= feature;
            return features;
        }

        static string PassengerIconKind(EncounterCard card)
        {
            if (card == null) return string.Empty;
            if ((card.Features & PassengerFeature.SpeedBoost) != 0) return "SPEEDBOOST";
            if ((card.Features & PassengerFeature.ClearsRust) != 0) return "HANDYMAN";
            if ((card.Features & PassengerFeature.CryingBaby) != 0) return "PREGNANT";
            if ((card.Features & (PassengerFeature.SpeedSlow | PassengerFeature.SlowArrival)) != 0)
                return "ELDERLY";
            if ((card.Features & PassengerFeature.UrgencySpeech) != 0) return "INTERVIEW";
            if ((card.Features & (PassengerFeature.GroupParty | PassengerFeature.MultiSpace)) != 0)
                return "GROUP";
            return card.PassengerKind;
        }

        // Same gameplay can be driven with buttons when a keyboard/controller is unavailable.
        void GetMouseControlRects(out Rect up, out Rect down, out Rect stop,
            out Rect close, out Rect kickout, out Rect repair)
        {
            up = down = stop = close = kickout = repair = new Rect();
            if (!extendedInterior || paused) return;
            bool split = Match != null && Match.SplitScreen;
            float margin = split ? 12f : 18f;
            float preferredWidth = split ? Mathf.Clamp(ViewWidth * .28f, 156f, 210f)
                : Mathf.Clamp(ViewWidth * .18f, 204f, 254f);
            float width = Mathf.Min(preferredWidth, ViewWidth - margin * 2f);
            float height = split ? 44f : 50f;
            float gap = 6f;
            float x = ViewWidth - width - margin;
            if (phase == Phase.Moving)
            {
                float y = ViewHeight - margin - height * 3 - gap * 2;
                up = new Rect(x, y, width, height);
                down = new Rect(x, y + height + gap, width, height);
                stop = new Rect(x, y + (height + gap) * 2, width, height);
            }
            else if (phase == Phase.Boarding)
            {
                bool showRepair = !FriendlyInterior && repairedFor <= 0 && !HasHandyman;
                bool showKickout = HasSelectedBoardedPassenger;
                int rows = 1 + (showKickout ? 1 : 0) + (showRepair ? 1 : 0);
                float y = ViewHeight - margin - height * rows - gap * (rows - 1);
                close = new Rect(x, y, width, height);
                int row = 1;
                if (showKickout)
                {
                    kickout = new Rect(x, y + (height + gap) * row, width, height);
                    row++;
                }
                if (showRepair) repair = new Rect(x, y + (height + gap) * row, width, height);
            }
        }

        void DrawMouseControls()
        {
            if (!extendedInterior || IsNpc) return;
            DrawBoardingDoorCue();
            GetMouseControlRects(out var up, out var down, out var stop, out var close, out var kickout, out var repair);
            DrawMouseControl(up, "HOLD TO GO UP", MenuPresentation.Teal, GameplayUpKey);
            DrawMouseControl(down, "HOLD TO GO DOWN", MenuPresentation.Teal, GameplayDownKey);
            DrawMouseControl(stop, phase == Phase.Moving ? "STOP AT F" + ElevatorRound.FloorNumber(GlideStopFloor()) : "STOP",
                MenuPresentation.Brass, GameplayConfirmKey);
            DrawMouseControl(close, "CLOSE & TRAVEL", MenuPresentation.Brass, GameplayCloseKey);
            DrawMouseControl(kickout, "KICK OUT", MenuPresentation.Coral, GameplayKickoutKey);
            DrawMouseControl(repair, "HOLD TO REPAIR", MenuPresentation.Teal, GameplayRepairKey);
        }

        bool ReadDoorClick(InputManager.SeatInput input)
        {
            if (!input.PointerPressed || draggedRider != null || IsOverMouseControl(input.Pointer, false)
                || !TryPickBoardingDoor(input.Pointer, out BoardingDoor door)) return false;
            OpenBoardingDoor(door);
            return true;
        }

        void DrawBoardingDoorCue()
        {
            if (!ShowBoardingDoorHints) return;
            for (int slot = 0; slot < 3; slot++)
            {
                BoardingDoor door = BoardingDoorAtScreenSlot(slot);
                string direction = GameplayUsesWasd
                    ? (slot == 0 ? "A" : slot == 1 ? "W" : "D")
                    : (slot == 0 ? "←" : slot == 1 ? "↑" : "→");
                DrawGameplayKeyHint(BoardingDoorHintRect(door, slot), direction,
                    MenuPresentation.Teal, boardingDoorOpen[(int)door]);
            }
        }

        bool ShowBoardingDoorHints => !IsNpc && phase == Phase.Boarding
            && !round.Finished && !paused && selectedRider == null;

        // Screen-relative left / up / right open the corresponding door,
        // even when the cabin is viewed from the opposite side.
        Rect BoardingDoorHintRect(BoardingDoor door, int slot)
        {
            Vector3 anchor = BoardingDoorThreshold(door);
            anchor.y = 3.35f;
            Vector3 point = eye.WorldToScreenPoint(stage.TransformPoint(anchor));
            Vector2 cursor;
            if (point.z > 0f)
            {
                cursor = new Vector2(point.x, Screen.height - point.y);
                if (Match != null) cursor -= Match.ViewRect(Seat).position;
            }
            else
            {
                // A doorway behind the camera still needs a selectable hint.
                cursor = new Vector2(ViewWidth * (slot == 0 ? .1f : slot == 1 ? .5f : .9f),
                    ViewHeight * .5f);
            }
            float size = Mathf.Clamp(ViewHeight * .042f, 28f, 38f);
            float inset = size * 1.15f;
            // Side doorways may be partially offscreen. Keep their direction
            // hints visible at the corresponding edge of this player's pane.
            float minX = slot == 0 ? inset : slot == 1 ? ViewWidth * .36f : ViewWidth * .70f;
            float maxX = slot == 0 ? ViewWidth * .30f : slot == 1 ? ViewWidth * .64f : ViewWidth - inset;
            cursor.x = Mathf.Clamp(cursor.x, minX, maxX);
            cursor.y = Mathf.Clamp(cursor.y, 70f + size, ViewHeight - size * 2f);

            return new Rect(cursor.x - size * .5f, cursor.y - size, size, size);
        }

        void DrawMouseControl(Rect rect, string text, Color accent, string key)
        {
            if (rect.width <= 0) return;
            var mouse = Mouse.current;
            Vector2 cursor = mouse == null ? Vector2.zero
                : new Vector2(mouse.position.ReadValue().x, Screen.height - mouse.position.ReadValue().y);
            if (Match != null)
            {
                Rect view = Match.ViewRect(Seat);
                cursor.x -= view.x;
                cursor.y -= view.y;
            }
            bool pressed = mouse != null && mouse.leftButton.isPressed && rect.Contains(cursor);
            if (pressed) MenuPresentation.Plaque(rect, MenuPresentation.Paper, MenuPresentation.Metal, 2f);
            else MenuPresentation.HudPlate(rect, accent);
            int previousFontSize = buttonStyle.fontSize;
            TextAnchor previousAlignment = buttonStyle.alignment;
            bool previousWordWrap = buttonStyle.wordWrap;
            TextClipping previousClipping = buttonStyle.clipping;
            buttonStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * .34f), 14, 20);
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.wordWrap = false;
            buttonStyle.clipping = TextClipping.Clip;
            Rect labelRect = rect;
            if (!string.IsNullOrEmpty(key))
            {
                float keySize = Mathf.Min(rect.height - 8f, 42f);
                const float keyInset = 9f;
                const float keyGap = 8f;
                float maxLabelWidth = Mathf.Max(1f,
                    rect.width - keyInset - keySize - keyGap - 8f);
                while (buttonStyle.fontSize > 12 &&
                       buttonStyle.CalcSize(new GUIContent(text)).x > maxLabelWidth)
                {
                    buttonStyle.fontSize--;
                }
                Rect hint = new Rect(rect.x + keyInset,
                    rect.y + (rect.height - keySize) * .5f,
                    keySize, keySize);
                DrawGameplayKeyHint(hint, key, accent, pressed);
                buttonStyle.alignment = TextAnchor.MiddleLeft;
                labelRect = new Rect(hint.xMax + keyGap, rect.y,
                    maxLabelWidth, rect.height);
            }
            MenuPresentation.Label(labelRect, new GUIContent(text), buttonStyle,
                pressed ? MenuPresentation.Ink : MenuPresentation.Paper);
            buttonStyle.fontSize = previousFontSize;
            buttonStyle.alignment = previousAlignment;
            buttonStyle.wordWrap = previousWordWrap;
            buttonStyle.clipping = previousClipping;
        }

        bool GameplayUsesKeyboard => MenuManager.KeyboardHintsEnabled;
        bool GameplayUsesWasd => GameplayUsesKeyboard && Seat == 0;
        string GameplayConfirmKey => GameplayUsesKeyboard ? (Seat == 1 ? "." : "C") : "A";
        string GameplayHorizontalKeys => GameplayUsesWasd ? "A/D" : "←/→";
        string GameplayVerticalKeys => GameplayUsesWasd ? "W/S" : "↑/↓";
        string GameplayDoorKeys => GameplayUsesWasd ? "A/W/D" : "←/↑/→";
        string GameplayUpKey => GameplayUsesWasd ? "W" : "↑";
        string GameplayDownKey => GameplayUsesWasd ? "S" : "↓";
        string GameplayCloseKey => GameplayConfirmKey;
        string GameplayKickoutKey => GameplayUsesKeyboard ? (Seat == 1 ? "," : "I") : "B";
        string GameplayRepairKey => GameplayUsesKeyboard ? (Seat == 1 ? ";" : "R") : "RB";
        string GameplayBoostKey => GameplayUsesKeyboard ? (Seat == 1 ? "RIGHT SHIFT" : "SHIFT") : "LB";

        void DrawGameplayKeyHint(Rect rect, string key)
        {
            DrawGameplayKeyHint(rect, key, MenuPresentation.Teal);
        }

        void DrawGameplayKeyHint(Rect rect, string key, Color accent, bool active = false)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (active) accent = MenuPresentation.Brass;
            if (gameplayKeyIconTexture == null)
                gameplayKeyIconTexture = CreateGameplayKeyIcon();

            Color previousGuiColor = GUI.color;
            GUI.color = MenuPresentation.DisplayColor(accent);
            GUI.DrawTexture(rect, gameplayKeyIconTexture);
            GUI.color = previousGuiColor;

            int previousFontSize = buttonStyle.fontSize;
            TextAnchor previousAlignment = buttonStyle.alignment;
            Color previousTextColor = buttonStyle.normal.textColor;
            buttonStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * .42f), 11, 20);
            buttonStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            var label = new GUIContent(key);
            while (buttonStyle.fontSize > 9 && buttonStyle.CalcSize(label).x > rect.width - 5f)
                buttonStyle.fontSize--;
            GUI.Label(rect, label, buttonStyle);
            buttonStyle.fontSize = previousFontSize;
            buttonStyle.alignment = previousAlignment;
            buttonStyle.normal.textColor = previousTextColor;
        }

        static Texture2D CreateGameplayKeyIcon()
        {
            const int size = 64;
            const float radius = 29f;
            const float ring = 3f;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * .5f, size * .5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center);
                float edge = Mathf.Clamp01(radius + 1f - distance);
                bool inside = distance <= radius;
                bool outline = inside && distance >= radius - ring;
                byte alpha = (byte)(edge * 255f);
                pixels[y * size + x] = outline
                    ? new Color32(255, 255, 255, alpha)
                    : inside ? new Color32(0, 0, 0, (byte)(alpha * .78f))
                    : new Color32(255, 255, 255, 0);
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void DrawHudTile(Rect area, Color color, string caption, string value)
        {
            MenuPresentation.Paint(new Rect(area.x, area.y + 11f, 1f, area.height - 22f), MenuPresentation.Ink);
            Color oldSmall = inkSmall.normal.textColor;
            Color oldLarge = inkLarge.normal.textColor;
            inkSmall.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            inkLarge.normal.textColor = MenuPresentation.DisplayColor(color);
            GUI.Label(new Rect(area.x + 10f, area.y + 5f, area.width - 15f, 18f), caption, inkSmall);
            GUI.Label(new Rect(area.x + 10f, area.y + 21f, area.width - 15f, 30f), value, inkLarge);
            inkSmall.normal.textColor = oldSmall;
            inkLarge.normal.textColor = oldLarge;
        }

        // Show remaining time and current score.
        void DrawRoundHUD()
        {
            // Round up so zero is shown only when time has actually expired.
            int seconds = Mathf.CeilToInt(round.TimeLeft);
            string clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            float hudScale = extendedInterior ? Mathf.Max(1, Screen.height / 900f) : 1;
            float x = Mathf.Max(12f, Screen.width / hudScale - 258f);
            AngularPanel(new Rect(x, 44, 246, 66), Teal);
            Label(new Rect(x + 12, 50, 222, 28), clock + " LEFT", large);
            Label(new Rect(x + 12, 80, 222, 24), "SCORE  " + round.Score + "  |  HAPPY  " + round.Happy, small);
        }

        // Animate the opening title.
        void IntroOverlay()
        {
            float logoProgress = EaseOut(Mathf.Clamp01((introTime - .18f) / 1.05f));
            float logoY = Mathf.Lerp(-150, 328, logoProgress);
            float stampProgress = Mathf.Clamp01((introTime - 1.12f) / .24f);
            float wordAlpha = Mathf.Clamp01((introTime - 1.12f) / .18f);
            float fade = 1 - Mathf.Clamp01((introTime - 2.48f) / .57f);
            if (fade <= 0) return;
            Color markColor = new Color(Teal.r, Teal.g, Teal.b, fade);
            Panel(new Rect(650, logoY - 65, 140, 130), new Color(Ink.r, Ink.g, Ink.b, fade));
            Panel(new Rect(659, logoY - 54, 58, 108), markColor);
            Panel(new Rect(723, logoY - 54, 58, 108), markColor);
            Panel(new Rect(669, logoY - 43, 102, 86), new Color(45 / 255f, 58 / 255f, 97 / 255f, fade));
            // Draw the lift pictogram from primitives instead of relying on an
            // emoji font, which is not bundled consistently in standalone builds.
            Color cream = new Color(Cream.r, Cream.g, Cream.b, fade);
            Panel(new Rect(718, logoY - 43, 4, 86), cream);
            Panel(new Rect(684, logoY - 19, 13, 13), cream);
            Panel(new Rect(682, logoY - 3, 17, 31), cream);
            Panel(new Rect(743, logoY - 19, 13, 13), cream);
            Panel(new Rect(741, logoY - 3, 17, 31), cream);
            if (wordAlpha > 0)
            {
                Matrix4x4 oldMatrix = GUI.matrix;
                Vector2 stampPivot = new Vector2(620, logoY - 86);
                GUIUtility.RotateAroundPivot(-9f, stampPivot);
                float stampScale = 1.22f - .22f * EaseOut(stampProgress);
                GUIUtility.ScaleAroundPivot(new Vector2(stampScale, stampScale), stampPivot);
                GUI.color = new Color(0, 0, 0, wordAlpha * fade * .45f);
                DrawStampWord(430, logoY - 126);
                GUI.color = new Color(1, 1, 1, wordAlpha * fade);
                DrawStampWord(425, logoY - 132);
                GUI.matrix = oldMatrix;
                GUI.color = Color.white;
            }
        }

        // Draw the intro stamp at the given position.
        void DrawStampWord(float x, float y)
        {
            const float letterWidth = 78;
            const string word = "CRAZY";
            for (int i = 0; i < word.Length; i++)
                Label(new Rect(x + i * letterWidth, y, letterWidth, 82), word[i].ToString(), stampWord);
        }

        // Show pause or final results.
        void Overlay()
        {
            MenuPresentation.Paint(new Rect(0, 104, 1440, 796), new Color(0, 0, 0, .82f));
            MenuPresentation.Paint(new Rect(316, 170, 824, 590), Color.black);
            MenuPresentation.Paint(new Rect(308, 162, 824, 590), MenuPresentation.Wall);
            MenuPresentation.Paint(new Rect(308, 162, 824, 7), MenuPresentation.Teal);
            MenuPresentation.Paint(new Rect(304, 186, 9, 522), MenuPresentation.Teal);
            string heading = paused ? "TAKE A BREATHER" : "ROUND COMPLETE";
            var overlayTitle = new GUIStyle(title);
            var overlayBody = new GUIStyle(body);
            overlayTitle.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            overlayBody.normal.textColor = MenuPresentation.DisplayColor(MenuPresentation.Paper);
            GUI.Label(new Rect(354, 196, 700, 52), heading, overlayTitle);
            string copy = paused
                ? "The clock is paused.\n\nResume the shift, return to the lobby, or go back to the main menu."
                : "FINAL SCORE: " + round.Score
                    + "\nHappy riders: " + round.Happy + "  •  Drop-offs: " + round.Delivered
                    + "\nMissed riders: " + round.Missed + "  •  Turned away: " + round.TurnedAway
                    + "\n\nNext world: "
                    + (session != null ? session.NextDimensionName : "CANDY") + ".";
            GUI.Label(new Rect(356, 292, 690, 240), copy, overlayBody);
            if (paused)
            {
                string resumeKey = GameplayUsesKeyboard ? "ESC" : "START";
                if (Button(new Rect(350, 610, 724, 42), "RESUME SHIFT  /  " + resumeKey, MenuPresentation.Teal,
                    true, MenuManager.PauseSelection == 0, menuColours: true))
                {
                    if (Match != null) Match.TogglePause();
                    else if (session != null) session.TogglePause();
                    else paused = false;
                }
                if (Button(new Rect(350, 664, 350, 42), "BACK TO LOBBY", MenuPresentation.Coral,
                    true, MenuManager.PauseSelection == 1, menuColours: true))
                    MenuManager.ReturnToLobby();
                if (Button(new Rect(724, 664, 350, 42), "MAIN MENU", MenuPresentation.Brass,
                    true, MenuManager.PauseSelection == 2, menuColours: true))
                    MenuManager.ReturnToMainMenu();
            }
            else
            {
                string nextRoundKey = GameplayUsesKeyboard ? "ENTER" : "A";
                if (Button(new Rect(350, 700, 724, 48), "NEXT ROUND  /  " + nextRoundKey, MenuPresentation.Teal, menuColours: true))
                    StartOrContinue();
            }
        }

    }
}
