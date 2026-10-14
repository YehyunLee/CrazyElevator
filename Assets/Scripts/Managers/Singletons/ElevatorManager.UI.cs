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
            rewardPoints, rewardCaption, calloutNumber, calloutText, selectionArrow,
            encounterKicker, encounterTitle, encounterBody, encounterPrompt, encounterBadge;
        static readonly Color UiBlack = new Color32(9, 11, 16, 255);
        static readonly Color UiWhite = new Color32(255, 252, 241, 255);
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
        EncounterCard activeEncounterCard;
        float encounterCardTime;
        int encounterConfirmationConsumedFrame = -1;
        const int EncounterPreviewLayer = 30;
        const int EncounterPreviewWidth = 384;
        const int EncounterPreviewHeight = 256;
        Transform encounterPreviewStage;
        Transform encounterPreviewPivot;
        Camera encounterPreviewCamera;
        RenderTexture encounterPreviewTexture;
        GameObject encounterPreviewModel;
        EncounterCard renderedEncounterCard;

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
        void AccentBar(Rect r, Color color)
        {
            float width = Mathf.Clamp(r.width * .55f, 4f, 8f);
            Panel(new Rect(Mathf.Round(r.center.x - width * .5f), Mathf.Round(r.y),
                Mathf.Round(width), Mathf.Round(r.height)), color);
        }
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
        bool Button(Rect r, string text, Color color, bool enabled = true, bool selected = false)
        {
            bool was = GUI.enabled; GUI.enabled = was && enabled;
            if (selected) Panel(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), UiWhite);
            Panel(r, GUI.enabled ? color : new Color(.2f, .25f, .3f));
            var old = buttonStyle.normal.textColor; buttonStyle.normal.textColor = GUI.enabled ? Ink : Color.gray;
            bool pressed = GUI.Button(r, text, buttonStyle); buttonStyle.normal.textColor = old; GUI.enabled = was; return pressed;
        }

        // Summarize a passenger type for the UI.
        string ShortRequest(Rider p)
        {
            if (p.Kind == "HANDYMAN") return "Full power while aboard";
            if (p.Kind == "COURIER") return "2 spaces • 1 floor";
            if (p.Kind == "PREGNANT") return "Baby cries • 2 spaces";
            if (p.Kind == "INTERVIEW") return "Very urgent";
            if (p.Kind == "BOSS") return "Hold OPEN bonus";
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
            DiscoverWorld(WorldBand.Office);
        }

        void DiscoverWorld(WorldBand world)
        {
            if (IsNpc) return;
            QueueWorldDiscoveryCard(world);
        }

        void QueueWorldDiscoveryCard(WorldBand world)
        {
            if (IsNpc || !encounteredWorlds.Add(world) || completedWorldTutorials[Seat].Contains(world)) return;
            string titleText;
            string description;
            string badge;
            Color accent;
            switch (world)
            {
                case WorldBand.Candy:
                    titleText = "CANDY DIMENSION";
                    description = "Bigger groups and louder problems start appearing here.";
                    badge = "02";
                    accent = new Color32(236, 118, 188, 255);
                    break;
                case WorldBand.Water:
                    titleText = "UNDERWATER DIMENSION";
                    description = "Rust slows the elevator. A handyman keeps it moving.";
                    badge = "03";
                    accent = new Color32(48, 196, 210, 255);
                    break;
                default:
                    titleText = "OFFICE DIMENSION";
                    description = "Start simple. Match each passenger to the correct floor.";
                    badge = "01";
                    accent = new Color32(98, 132, 178, 255);
                    break;
            }
            EnqueueEncounterCard(new EncounterCard
            {
                Kicker = "NEW DIMENSION",
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
            if (IsNpc || rider == null || string.IsNullOrEmpty(rider.Kind)
                || !encounteredPassengerKinds.Add(rider.Kind)
                || completedPassengerTutorials[Seat].Contains(rider.Kind)) return;
            string titleText = rider.Name.ToUpperInvariant();
            string description;
            switch (rider.Kind)
            {
                case "ELDERLY":
                    titleText = "GRANDMA";
                    description = "Moves slowly and reduces your elevator speed.";
                    break;
                case "PREGNANT":
                    titleText = "MOM + BABY";
                    description = "The crying baby drains every onboard passenger's patience faster.";
                    break;
                case "BOSS":
                    titleText = "THE BOSS";
                    description = "Hold the door open to earn an extra bonus.";
                    break;
                case "GROUP":
                    titleText = "THE TRIO";
                    description = "Takes three spaces and boosts elevator speed.";
                    break;
                case "HANDYMAN":
                    titleText = "HANDYMAN";
                    description = "Prevents underwater rust from slowing the elevator.";
                    break;
                case "COURIER":
                    description = "Takes two spaces and wants a quick one floor trip.";
                    break;
                case "INTERVIEW":
                    description = "Very impatient. Deliver this passenger quickly.";
                    break;
                default:
                    description = string.IsNullOrEmpty(rider.Request)
                        ? "A new passenger has joined the queue." : rider.Request;
                    break;
            }
            EnqueueEncounterCard(new EncounterCard
            {
                Kicker = "NEW PASSENGER",
                Title = titleText,
                Description = description,
                PassengerKind = rider.Kind,
                Badge = string.IsNullOrEmpty(rider.Badge) ? "?" : rider.Badge,
                Accent = Palette[Mathf.Abs(rider.Color) % Palette.Length],
                PreviewSource = FindPassengerPreviewSource(rider),
                PreviewYaw = 0f
            });
        }

        GameObject FindPassengerPreviewSource(Rider rider)
        {
            if (rider == null) return null;
            if (rider.Data != null && rider.Data.prefab != null)
                return rider.Data.prefab.gameObject;
            if (sceneView == null || sceneView.passengers == null) return null;
            foreach (PassengerView passenger in sceneView.passengers)
                if (passenger != null && passenger.ResolvedKind == rider.Kind)
                    return passenger.gameObject;
            return null;
        }

        GameObject FindWorldPreviewSource(WorldBand world)
        {
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

        void EnsureEncounterPreview()
        {
            if (encounterPreviewStage != null && encounterPreviewCamera != null
                && encounterPreviewTexture != null) return;

            GameObject stageObject = new GameObject("Encounter 3D preview stage");
            stageObject.hideFlags = HideFlags.HideAndDontSave;
            stageObject.transform.position = new Vector3(10000f, -10000f, 10000f);
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
            encounterPreviewCamera.backgroundColor = UiBlack;
            encounterPreviewCamera.cullingMask = 1 << EncounterPreviewLayer;
            encounterPreviewCamera.orthographic = true;
            encounterPreviewCamera.allowHDR = false;
            encounterPreviewCamera.allowMSAA = true;
            encounterPreviewCamera.aspect = (float)EncounterPreviewWidth / EncounterPreviewHeight;
            encounterPreviewCamera.targetTexture = encounterPreviewTexture;
            encounterPreviewCamera.enabled = false;

            GameObject keyObject = new GameObject("Encounter preview key light");
            keyObject.hideFlags = HideFlags.HideAndDontSave;
            keyObject.transform.SetParent(encounterPreviewStage, false);
            keyObject.transform.localRotation = Quaternion.Euler(32f, -28f, 0f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.15f;
            key.color = new Color(1f, .96f, .88f);
            key.cullingMask = 1 << EncounterPreviewLayer;

            GameObject fillObject = new GameObject("Encounter preview fill light");
            fillObject.hideFlags = HideFlags.HideAndDontSave;
            fillObject.transform.SetParent(encounterPreviewStage, false);
            fillObject.transform.localPosition = new Vector3(2.8f, 2.2f, -3.5f);
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 18f;
            fill.intensity = 2.2f;
            fill.color = new Color(.58f, .82f, 1f);
            fill.cullingMask = 1 << EncounterPreviewLayer;
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

            PassengerView passenger = encounterPreviewModel.GetComponentInChildren<PassengerView>(true);
            if (passenger != null && passenger.jackets != null)
                foreach (Renderer jacket in passenger.jackets)
                    if (jacket != null) jacket.sharedMaterial = Mat(card.Accent);

            foreach (Transform child in encounterPreviewModel.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = EncounterPreviewLayer;
                child.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }
            foreach (MonoBehaviour behaviour in encounterPreviewModel.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;
            foreach (Collider collider in encounterPreviewModel.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (AudioSource source in encounterPreviewModel.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;
            foreach (Camera nestedCamera in encounterPreviewModel.GetComponentsInChildren<Camera>(true))
                nestedCamera.enabled = false;
            foreach (Light nestedLight in encounterPreviewModel.GetComponentsInChildren<Light>(true))
                nestedLight.enabled = false;
            foreach (TextMesh textMesh in encounterPreviewModel.GetComponentsInChildren<TextMesh>(true))
            {
                Renderer textRenderer = textMesh.GetComponent<Renderer>();
                if (textRenderer != null) textRenderer.enabled = false;
            }

            if (!TryGetEncounterPreviewBounds(encounterPreviewModel, out Bounds bounds))
            {
                ClearEncounterPreviewModel();
                return;
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
            encounterPreviewCamera.backgroundColor = Color.Lerp(UiBlack, card.Accent, .09f);
            encounterPreviewPivot.localRotation = Quaternion.Euler(0f, card.PreviewYaw, 0f);
            encounterPreviewCamera.enabled = true;
            renderedEncounterCard = card;
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
            if (card == null) return;
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

        void DrawEncounterCard(float viewWidth, float viewHeight)
        {
            if (activeEncounterCard == null || Event.current.type != EventType.Repaint) return;
            bool compact = Match != null;
            float width = Mathf.Min(compact ? 520f : 760f, viewWidth - (compact ? 20f : 48f));
            float height = compact ? 132f : 154f;
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
                encounterBadge.normal.textColor = activeEncounterCard.Accent;
                GUI.Label(preview, activeEncounterCard.Badge, encounterBadge);
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
                compact ? 28f : 36f), activeEncounterCard.Description, encounterBody);
            string confirmPrompt = Match == null ? "PRESS A / V TO CONTINUE"
                : Seat == 0 ? "P1: PRESS A / V" : "P2: PRESS A / PERIOD";
            GUI.Label(new Rect(contentX, card.y + card.height - 28f, contentWidth, 24f),
                confirmPrompt, encounterPrompt);
            encounterTitle.fontSize = oldTitleSize;
            encounterBody.fontSize = oldBodySize;
            encounterBadge.fontSize = oldBadgeSize;
        }

        // Draw menus or the active-shift HUD.
        void OnGUI()
        {
            // Mode select owns the first screen. Don't paint the intro curtain over it —
            // Update also freezes introTime while the menu is open, which would stick on black.
            if (round == null || eye == null || Match != null || MenuManager.IsOpen) return;
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
                return;
            }
            if (phase == Phase.Results || paused)
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
                    ? 1f - Mathf.Clamp01(phaseTime / DoorHoldDurationAtCurrentFloor()) : 0f;
                float margin = Mathf.Clamp(Screen.width * .025f, 24f, 36f);
                float width = Screen.width - margin * 2f;
                Panel(new Rect(margin, 18f, width, 12f), new Color(Ink.r, Ink.g, Ink.b, .55f));
                float remaining = width * fill;
                Panel(new Rect(margin + (width - remaining) * .5f, 18f, remaining, 12f), fill > .25f ? Teal : Coral);
                Panel(new Rect(Screen.width * .5f - 1f, 16f, 2f, 16f), Cream);
            }
            GUI.matrix = Matrix4x4.identity;
            if (phase != Phase.Results && !paused)
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
            }
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
            GUI.matrix = previous;
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
        void DrawCompactTopHUD()
        {
            int seconds = Mathf.CeilToInt(round.TimeLeft);
            string clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            const float totalWidth = 636f;
            const float y = 16f;
            const float x = 16f;

            Panel(new Rect(x - 5, y - 5, totalWidth + 10, 68), UiWhite);
            Panel(new Rect(x - 2, y - 2, totalWidth + 4, 62), UiBlack);
            float cursor = x;
            DrawHudTile(new Rect(cursor, y, 148, 48), WorldAccent(round.Floor),
                WorldName(round.Floor), "F" + round.Floor); cursor += 152;
            DrawHudTile(new Rect(cursor, y, 108, 48), Gold, "LOAD", round.Load + "/" + ElevatorRound.Capacity); cursor += 112;
            DrawHudTile(new Rect(cursor, y, 108, 48), Teal, "SCORE", round.Score.ToString()); cursor += 112;
            DrawHudTile(new Rect(cursor, y, 108, 48), Coral, "HAPPY", round.Happy.ToString()); cursor += 112;
            DrawHudTile(new Rect(cursor, y, 148, 48), Sky, "TIME", clock);
        }

        // Destination plaques sit at passengers' feet, leaving the space above
        // their heads for a clear selection arrow and occasional dialogue.
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

            Rider speaker = null;
            foreach (Rider rider in visible)
                if (rider.Boarded && (rider.HasFeature(PassengerFeature.CryingBaby)
                    || rider.Remaining <= rider.Patience * .25f
                    || rider.Destination == round.Floor || !string.IsNullOrEmpty(rider.Status)))
                { speaker = rider; break; }
            if (speaker == null && selectedRider != null && visible.Contains(selectedRider)) speaker = selectedRider;

            foreach (Rider rider in visible)
            {
                if (!figures.TryGetValue(rider, out Transform figure)) continue;
                Vector3 headPoint = eye.WorldToScreenPoint(figure.position + Vector3.up * 2.08f);
                Vector3 footPoint = eye.WorldToScreenPoint(PassengerDestinationAnchor(rider, figure));
                if (headPoint.z <= 0 || footPoint.z <= 0) continue;
                float localX = footPoint.x - view.x;
                float footY = view.y + view.height - footPoint.y;
                float headY = view.y + view.height - headPoint.y;
                float x = Mathf.Round(Mathf.Clamp(localX, 30f, viewWidth - 30f));
                float headX = Mathf.Round(Mathf.Clamp(headPoint.x - view.x, 30f, viewWidth - 30f));
                footY = Mathf.Round(Mathf.Clamp(footY, 96f, viewHeight - 38f));
                headY = Mathf.Round(Mathf.Clamp(headY, 72f, viewHeight - 116f));
                Color accent = Palette[rider.Color % Palette.Length];
                bool selected = rider == selectedRider || rider == draggedRider;

                // Extended cabin view uses the perspective-matched 3D floor
                // marker. Keep the old screen badge only for legacy layouts.
                if (!extendedInterior)
                {
                    Rect badge = new Rect(x - 18f, footY - 2f, 36f, 30f);
                    Color badgeBorder = selected
                        ? selectionTint == BoardingInvalid ? BoardingInvalid : BoardingValid
                        : new Color32(145, 153, 166, 255);
                    FramedPanel(badge, badgeBorder, 2f);
                    OutlinedLabel(new Rect(badge.x, badge.y - 1f, badge.width, badge.height),
                        rider.Destination.ToString(), calloutNumber);
                }

                // The selection arrow belongs above the passenger, not above
                // the lower-left floor marker—especially for multi-cell boxes.
                Rect arrow = new Rect(headX - 24f, headY - 26f, 48f, 40f);
                if (selected)
                {
                    Color arrowColor = selectionTint == BoardingInvalid ? BoardingInvalid : BoardingValid;
                    OutlinedLabel(arrow, "↓", selectionArrow, arrowColor);
                }

                if (rider != speaker) continue;
                string message = PassengerCalloutText(rider);
                if (string.IsNullOrEmpty(message)) continue;
                float width = Mathf.Clamp(calloutText.CalcSize(new GUIContent(message)).x + 22f, 112f, 220f);
                const float quoteHeight = 28f;
                float quoteX = headX;
                Rect quote = new Rect(Mathf.Clamp(quoteX - width * .5f, 12f, viewWidth - width - 12f),
                    (selected ? arrow.y : headY - 4f) - quoteHeight - 3f, width, quoteHeight);
                SpeechBubblePanel(quote, accent);
                GUI.Label(new Rect(quote.x + 6f, quote.y, quote.width - 12f, quote.height - 2f),
                    message, calloutText);
            }
        }

        // The cabin camera is much smaller during shaft travel, so the authored
        // world-space numbers become illegible. Redraw only the onboard riders'
        // destinations in screen space, sized for the preview itself.
        void DrawCabinPreviewCallouts()
        {
            if (!BuildingView || !showCabinPreview || !eye.enabled) return;

            Rect pixels = eye.pixelRect;
            Rect preview = new Rect(pixels.x, Screen.height - pixels.yMax, pixels.width, pixels.height);
            int previousSize = calloutNumber.fontSize;
            calloutNumber.fontSize = Mathf.Clamp(Mathf.RoundToInt(preview.height * .13f), 22, 30);

            var occupiedBadges = new List<Rect>();
            foreach (Rider rider in round.Riders)
            {
                if (!round.Owns(rider) || exiting.ContainsKey(rider)
                    || boardingTransfers.ContainsKey(rider)) continue;
                if (!figures.TryGetValue(rider, out Transform figure) || !figure.gameObject.activeInHierarchy) continue;

                Vector3 point = eye.WorldToScreenPoint(PassengerDestinationAnchor(rider, figure));
                if (point.z <= 0) continue;
                float x = Mathf.Round(Mathf.Clamp(point.x, preview.xMin + 18f, preview.xMax - 18f));
                float y = Mathf.Round(Mathf.Clamp(Screen.height - point.y, preview.yMin + 30f, preview.yMax - 22f));
                Rect badge = new Rect(x - 17f, y - 2f, 34f, 28f);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool overlaps = false;
                    foreach (Rect occupied in occupiedBadges)
                        if (badge.Overlaps(occupied)) { overlaps = true; break; }
                    if (!overlaps) break;
                    badge.y = Mathf.Max(preview.yMin + 28f, badge.y - 30f);
                }
                occupiedBadges.Add(badge);
                FramedPanel(badge, new Color32(145, 153, 166, 255), 2f);
                OutlinedLabel(badge, rider.Destination.ToString(), calloutNumber);
            }

            calloutNumber.fontSize = previousSize;
        }

        string PassengerCalloutText(Rider rider)
        {
            if (rider.Boarded && rider.Remaining <= 0) return "I'M MAD!";
            if (rider.Boarded && !string.IsNullOrEmpty(rider.Status)
                && rider.Status.IndexOf("passed my floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "MISSED MY FLOOR!";
            if (rider.Boarded && rider.Destination == round.Floor) return "MY STOP!";
            if (rider.Boarded && rider.HasFeature(PassengerFeature.CryingBaby))
                return round.BabyCalmedByGrandma ? "CALM! PATIENCE x0.5" : "WAAAH! PATIENCE x1.6";
            if (rider.Boarded && rider.Remaining <= rider.Patience * .25f) return "PLEASE HURRY!";
            return null;
        }

        // Same gameplay can be driven with buttons when a keyboard/controller is unavailable.
        void GetMouseControlRects(out Rect up, out Rect down, out Rect stop,
            out Rect close, out Rect hold, out Rect repair)
        {
            up = down = stop = close = hold = repair = new Rect();
            if (!extendedInterior || paused) return;
            bool split = Match != null;
            float width = split
                ? Mathf.Clamp(ViewWidth * .25f, 170f, 220f)
                : Mathf.Clamp(ViewWidth * .18f, 220f, 280f);
            float height = split ? 50f : 56f;
            float gap = split ? 8f : 10f;
            float margin = split ? 12f : 18f;
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
                int rows = showRepair ? 3 : 2;
                float y = ViewHeight - margin - height * rows - gap * (rows - 1);
                close = new Rect(x, y, width, height);
                hold = new Rect(x, y + height + gap, width, height);
                if (showRepair) repair = new Rect(x, y + (height + gap) * 2, width, height);
            }
        }

        void DrawMouseControls()
        {
            if (!extendedInterior || IsNpc) return;
            GetMouseControlRects(out var up, out var down, out var stop, out var close, out var hold, out var repair);
            DrawMouseControl(up, "HOLD  ↑  UP", Teal);
            DrawMouseControl(down, "HOLD  ↓  DOWN", Teal);
            DrawMouseControl(stop, CanStopAtFloor ? "STOP HERE" : "STOP • ALIGN FLOOR", Gold);
            DrawMouseControl(close, "CLOSE & TRAVEL", Coral);
            DrawMouseControl(hold, "HOLD DOOR", Teal);
            DrawMouseControl(repair, "HOLD TO REPAIR", Gold);
        }

        void DrawMouseControl(Rect rect, string text, Color accent)
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
            AngularPanel(rect, accent);
            if (pressed) Panel(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f), accent);
            Color previous = buttonStyle.normal.textColor;
            buttonStyle.normal.textColor = pressed ? UiBlack : UiWhite;
            Label(rect, text, buttonStyle);
            buttonStyle.normal.textColor = previous;
        }

        void DrawHudTile(Rect area, Color color, string caption, string value)
        {
            Panel(area, UiBlack);
            Panel(new Rect(area.x, area.yMax + 5f, area.width, 5f), color);
            AccentBar(new Rect(area.x + 4f, area.y + 7f, 7f, area.height - 18f), color);

            Color oldSmall = inkSmall.normal.textColor;
            Color oldLarge = inkLarge.normal.textColor;
            inkSmall.normal.textColor = new Color(UiWhite.r, UiWhite.g, UiWhite.b, .72f);
            inkLarge.normal.textColor = UiWhite;
            GUI.Label(new Rect(area.x + 18, area.y + 3, area.width - 22, 18), caption, inkSmall);
            GUI.Label(new Rect(area.x + 18, area.y + 18, area.width - 22, 27), value, inkLarge);
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
            Panel(new Rect(0, 104, 1440, 796), new Color(0, 0, 0, .82f));
            Panel(new Rect(316, 170, 824, 590), Color.black);
            Panel(new Rect(308, 162, 824, 590), UiBlack);
            Panel(new Rect(308, 162, 824, 7), Teal);
            AccentBar(new Rect(304, 186, 9, 522), Teal);
            string heading = paused ? "TAKE A BREATHER" : "SHIFT COMPLETE";
            Label(new Rect(354, 196, 700, 52), heading, title);
            string copy = paused
                ? "The clock is paused.\n\nResume the shift, return to the lobby, or go back to the main menu."
                : "FINAL SCORE: " + round.Score
                    + "\nHappy riders: " + round.Happy + "  •  Drop-offs: " + round.Delivered
                    + "\nMissed riders: " + round.Missed + "  •  Turned away: " + round.TurnedAway
                    + "\n\nYour 3-minute shift is over. Try again for a better mix!";
            Label(new Rect(356, 292, 690, 240), copy, body);
            if (paused)
            {
                if (Button(new Rect(350, 610, 724, 42), "RESUME SHIFT  /  START or ESC", Teal,
                    true, MenuManager.PauseSelection == 0))
                {
                    if (Match != null) Match.TogglePause();
                    else if (session != null) session.TogglePause();
                    else paused = false;
                }
                if (Button(new Rect(350, 664, 350, 42), "BACK TO LOBBY", Coral,
                    true, MenuManager.PauseSelection == 1))
                    MenuManager.ReturnToLobby();
                if (Button(new Rect(724, 664, 350, 42), "MAIN MENU", Gold,
                    true, MenuManager.PauseSelection == 2))
                    MenuManager.ReturnToMainMenu();
            }
            else
            {
                if (Button(new Rect(350, 700, 724, 48), "TRY AGAIN  /  CLICK or ENTER", Teal))
                    StartOrContinue();
            }
        }

    }
}
