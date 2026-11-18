using System.Collections.Generic;
using CrazyElevator.Shared;
using SharedRider = CrazyElevator.Shared.Rider;
using UnityEngine;

namespace CrazyElevator.Managers
{
    public sealed partial class ElevatorManager
    {
        const float AbilityIntroDuration = 3.5f;
        public const float VipIntroDuration = 1.65f;
        Transform abilityIntroPassenger, abilityIntroOpponent, abilityIntroOwnCar, abilityIntroBaby;
        StarfishImpairmentView abilityIntroRepairGauge;
        bool abilityIntroInGallery;
        string abilityIntroKind;
        SharedRider abilityIntroVip;
        Texture2D abilityIntroShield;
        Camera abilityIntroVipCamera;
        RenderTexture abilityIntroVipTexture;
        Texture2D abilityIntroVipShade;
        Transform abilityIntroVipFigure;
        Transform abilityIntroVipDestination;
        Quaternion abilityIntroVipDestinationRotation;
        Bounds abilityIntroVipBounds;
        Vector3 abilityIntroVipFocus;
        Transform abilityIntroVipWorld;
        bool abilityIntroVipFigureWasActive;
        readonly List<Renderer> abilityIntroVipOccluders = new List<Renderer>();
        readonly List<bool> abilityIntroVipOccluderStates = new List<bool>();

        // Reuse the existing offscreen preview camera and the editable game
        // prefabs, so this animation never touches the live car or passengers.
        public bool PrepareAbilityIntro(string kind, SharedRider vip = null)
            => PrepareAbilityPreview(kind, vip, false);

        public bool PrepareRoundPassengerDemo(string kind)
            => PrepareAbilityPreview(kind, null, true) || PrepareTutorialCharacter(kind);

        public void FinishRoundPassengerDemo() => FinishAbilityIntro();

        public void AnimateRoundPassengerDemo(float elapsed)
        {
            if (abilityIntroInGallery) AnimateAbilityIntro(Mathf.Repeat(elapsed, AbilityIntroDuration));
            else AnimateTutorialCharacter(elapsed);
        }

        bool PrepareAbilityPreview(string kind, SharedRider vip, bool gallery)
        {
            if (sceneView == null || sceneView.passengers == null) return false;
            FinishAbilityIntro();
            if (kind == "VIP")
            {
                ClearEncounterPreviewModel();
                abilityIntroKind = kind;
                abilityIntroVip = vip;
                if (!PrepareVipLocationShot(vip))
                {
                    FinishAbilityIntro();
                    return false;
                }
                AnimateAbilityIntro(0f);
                return true;
            }

            string passengerKind = kind == "CALM" ? "ELDERLY" : kind;
            PassengerData data = passengerCatalog != null ? passengerCatalog.FindKind(passengerKind) : null;
            GameObject passenger = data != null && data.prefab != null
                ? data.prefab.gameObject : FindPassengerTemplate(passengerKind);
            GameObject car = sceneView.exteriorCar != null ? sceneView.exteriorCar.gameObject : null;
            GameObject opponent = kind == "HUNTER" ? sceneView.monster
                : kind == "DIVER" ? sceneView.tornado
                : kind == "FAIRY" || kind == "GOBLIN" || kind == "MUSHROOM" ? sceneView.forestVineSweep
                : kind == "ALIEN" ? sceneView.spaceGravitySweep
                : kind == "HANDYMAN" && impairmentGaugePrefab != null ? impairmentGaugePrefab.gameObject
                : kind == "CALM" ? FindPassengerTemplate("PREGNANT")
                : kind == "PREGNANT" && !gallery ? FindPassengerTemplate("ELDERLY")
                : gallery || kind == "BOUNCER" || kind == "GROUP" || kind == "ELDERLY" || kind == "ASTRONAUT"
                    ? car : null;
            if (passenger == null || opponent == null) return false;

            EnsureEncounterPreview();
            encounterPreviewModel = new GameObject("Passenger ability introduction");
            encounterPreviewModel.hideFlags = HideFlags.HideAndDontSave;
            encounterPreviewModel.transform.SetParent(encounterPreviewPivot, false);
            encounterPreviewPivot.localRotation = Quaternion.identity;
            Transform stage = encounterPreviewModel.transform;
            abilityIntroPassenger = CreatePreviewActor(stage, passenger, "Passenger", 2.3f, 2.85f);
            float opponentHeight = opponent == car && gallery ? 1.34f : 2.3f;
            abilityIntroOpponent = CreatePreviewActor(stage, opponent, "Opponent", opponentHeight, 2.85f, model =>
            {
                if (opponent == car) SetUpPreviewCar(model);
                if (kind == "ALIEN") SetSpacePreviewMode(model.transform, true);
                if (kind == "FAIRY")
                {
                    Transform route = model.transform.Find("Fairy safe route");
                    if (route != null) route.gameObject.SetActive(true);
                }
                abilityIntroRepairGauge = model.GetComponent<StarfishImpairmentView>();
                if (abilityIntroRepairGauge == null) return;
                abilityIntroRepairGauge.SetSeverity(gallery ? 3 : 0);
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            });
            if (kind == "BOUNCER")
                abilityIntroOwnCar = CreatePreviewActor(stage, car, "Your elevator",
                    gallery ? 1.34f : 2.5f, 2.85f, SetUpPreviewCar);
            if (gallery && kind == "ELDERLY")
                abilityIntroBaby = CreatePreviewActor(stage, FindPassengerTemplate("PREGNANT"), "Baby", 1.55f, 2f);
            if (abilityIntroPassenger == null || abilityIntroOpponent == null)
            {
                FinishAbilityIntro();
                return false;
            }
            abilityIntroKind = kind;
            abilityIntroInGallery = gallery;
            abilityIntroVip = null;
            if (kind == "DIVER" && abilityIntroShield == null)
                abilityIntroShield = PassengerIconLibrary.CreatePassengerBadge("DIVER", Teal, Ink, Cream);
            encounterPreviewCamera.backgroundColor = kind == "HUNTER" || kind == "ELDERLY" ? new Color(.12f, .17f, .26f)
                : kind == "DIVER" || kind == "HANDYMAN" ? new Color(.04f, .24f, .30f)
                : kind == "FAIRY" || kind == "GOBLIN" || kind == "MUSHROOM" ? new Color(.10f, .19f, .21f)
                : kind == "ASTRONAUT" || kind == "ALIEN" ? new Color(.09f, .06f, .20f)
                : new Color(.30f, .16f, .27f);
            encounterPreviewCamera.nearClipPlane = .03f;
            encounterPreviewCamera.farClipPlane = 80f;
            encounterPreviewCamera.enabled = false; // Render once after the pose is set.
            AnimateAbilityIntro(0f);
            return true;
        }

        Transform CreatePreviewActor(Transform parent, GameObject source, string label, float height,
            float maxWidth, System.Action<GameObject> configure = null)
        {
            if (source == null) return null;
            var slot = new GameObject(label + " preview pose");
            slot.hideFlags = HideFlags.HideAndDontSave;
            slot.transform.SetParent(parent, false);
            var model = Instantiate(source, slot.transform, false);
            model.name = label + " preview model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.SetActive(true);
            configure?.Invoke(model);
            MakeStillPreviewModel(model, Teal);
            if (!TryGetEncounterPreviewBounds(model, out Bounds bounds))
            {
                Destroy(slot);
                return null;
            }
            float width = Mathf.Max(bounds.size.x, bounds.size.z * .75f);
            model.transform.localScale *= Mathf.Min(height / Mathf.Max(.01f, bounds.size.y),
                maxWidth / Mathf.Max(.01f, width));
            TryGetEncounterPreviewBounds(model, out bounds);
            model.transform.localPosition -= slot.transform.InverseTransformPoint(bounds.center);
            return slot.transform;
        }

        static void SetUpPreviewCar(GameObject model)
        {
            // Face the cutaway toward the preview camera so the open cabin is visible.
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            foreach (PassengerView occupant in model.GetComponentsInChildren<PassengerView>(true))
                occupant.gameObject.SetActive(false);
            foreach (PassengerPatienceBar bar in model.GetComponentsInChildren<PassengerPatienceBar>(true))
                bar.gameObject.SetActive(false);
        }

        static void SetSpacePreviewMode(Transform model, bool showUfo)
        {
            foreach (Transform part in model)
                part.gameObject.SetActive((part.name == "UFO") == showUfo);
        }

        bool PrepareVipLocationShot(SharedRider vip)
        {
            ElevatorManager owner = WorldOwner;
            if (vip == null || owner == null || owner.exteriorCamera == null
                || owner.exteriorCar == null || owner.exteriorCar.parent == null) return false;

            abilityIntroVipWorld = owner.exteriorCar.parent;
            bool hadFigure = owner.crowd.TryGetValue(vip, out CrowdFigure existing)
                && existing.Root != null;
            abilityIntroVipFigureWasActive = hadFigure && existing.Root.gameObject.activeSelf;
            PassengerView passenger = owner.WaitingPassengerView(vip);
            if (passenger == null) return false;
            abilityIntroVipFigure = passenger.transform;
            CacheVipCinematicRenderers(owner, abilityIntroVipFigure);
            if (passenger.destination != null)
            {
                abilityIntroVipDestination = passenger.destination.transform;
                abilityIntroVipDestinationRotation = abilityIntroVipDestination.rotation;
            }
            abilityIntroVipFigure.gameObject.SetActive(true);
            abilityIntroVipFocus = abilityIntroVipFigure.position + abilityIntroVipWorld.up * 1.1f;
            abilityIntroVipBounds = new Bounds(abilityIntroVipFocus, new Vector3(1.4f, 2.2f, 1.4f));

            int textureWidth = Mathf.Clamp(Mathf.Max(1920, Screen.width), 1920, 2560);
            int textureHeight = Mathf.RoundToInt(textureWidth * 9f / 16f);
            abilityIntroVipTexture = new RenderTexture(textureWidth, textureHeight, 24,
                RenderTextureFormat.ARGB32)
            {
                name = "VIP shaft cinematic",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            abilityIntroVipTexture.Create();

            GameObject cameraObject = new GameObject("VIP shaft focus camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            abilityIntroVipCamera = cameraObject.AddComponent<Camera>();
            abilityIntroVipCamera.CopyFrom(owner.exteriorCamera);
            abilityIntroVipCamera.enabled = false;
            abilityIntroVipCamera.targetTexture = abilityIntroVipTexture;
            abilityIntroVipCamera.rect = new Rect(0f, 0f, 1f, 1f);
            abilityIntroVipCamera.aspect = (float)textureWidth / textureHeight;
            abilityIntroVipCamera.orthographic = false;
            abilityIntroVipCamera.nearClipPlane = .05f;
            abilityIntroVipCamera.farClipPlane = Mathf.Max(owner.exteriorCamera.farClipPlane, 120f);

            abilityIntroVipShade = CreateVipSpotlightTexture();
            return abilityIntroVipShade != null;
        }

        void CacheVipCinematicRenderers(ElevatorManager owner, Transform vipFigure)
        {
            abilityIntroVipOccluders.Clear();
            AddVipElevatorRenderers(abilityIntroVipWorld, vipFigure);
            AddVipElevatorRenderers(owner.exteriorCar, vipFigure);
            if (owner.Match != null && owner.Match.Rival != null)
                AddVipElevatorRenderers(owner.Match.Rival.exteriorCar, vipFigure);
        }

        void AddVipElevatorRenderers(Transform root, Transform vipFigure)
        {
            if (root == null) return;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer != null && !renderer.transform.IsChildOf(vipFigure)
                    && !abilityIntroVipOccluders.Contains(renderer))
                    abilityIntroVipOccluders.Add(renderer);
        }

        Texture2D CreateVipSpotlightTexture()
        {
            const int width = 512;
            const int height = 288;
            float frameHeight = Mathf.Max(1f, Screen.height * .79f);
            float aspect = Screen.width / frameHeight;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "VIP focus shade",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float ny = (y + .5f) / height - .5f;
                for (int x = 0; x < width; x++)
                {
                    float nx = ((x + .5f) / width - .5f) * aspect;
                    float distance = Mathf.Sqrt(nx * nx + ny * ny);
                    float shade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.22f, .28f, distance)) * .70f;
                    pixels[y * width + x] = new Color(0f, 0f, 0f, shade);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        void AnimateVipLocationShot(float time)
        {
            if (abilityIntroVipCamera == null || abilityIntroVipWorld == null
                || abilityIntroVipTexture == null) return;

            float zoom = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .85f, time));
            const float exteriorDistance = 36f;
            Vector3 cameraPosition = abilityIntroVipFocus
                - abilityIntroVipWorld.forward * exteriorDistance
                + abilityIntroVipWorld.up * (exteriorDistance * .12f);
            abilityIntroVipCamera.transform.SetPositionAndRotation(cameraPosition,
                Quaternion.LookRotation(abilityIntroVipFocus - cameraPosition, abilityIntroVipWorld.up));
            abilityIntroVipCamera.fieldOfView = Mathf.Lerp(55f, 12f, zoom);
            abilityIntroVipCamera.ResetProjectionMatrix();
            if (abilityIntroVipDestination != null)
                abilityIntroVipDestination.rotation = Quaternion.LookRotation(
                    abilityIntroVipDestination.position - cameraPosition, abilityIntroVipWorld.up);
            Ray shot = new Ray(cameraPosition, abilityIntroVipFocus - cameraPosition);
            float targetDistance = Vector3.Distance(cameraPosition, abilityIntroVipFocus);
            Transform rivalCar = Match != null && Match.Rival != null
                ? Match.Rival.exteriorCar : null;
            abilityIntroVipOccluderStates.Clear();
            foreach (Renderer renderer in abilityIntroVipOccluders)
            {
                bool wasEnabled = renderer != null && renderer.enabled;
                abilityIntroVipOccluderStates.Add(wasEnabled);
                if (renderer == null || !wasEnabled) continue;

                // The landing and car can sit directly between the cinematic
                // camera and the highlighted passenger. Hide only geometry
                // crossed by this shot, rather than letting a wall or platform
                // fill the VIP spotlight. All renderers are restored below.
                if (IsVipElevatorRenderer(renderer.transform, exteriorCar)
                    || IsVipElevatorRenderer(renderer.transform, rivalCar)
                    || (renderer.bounds.IntersectRay(shot, out float hitDistance)
                        && hitDistance < targetDistance - .02f))
                    renderer.enabled = false;
            }
            try
            {
                abilityIntroVipCamera.Render();
            }
            finally
            {
                for (int i = 0; i < abilityIntroVipOccluders.Count; i++)
                    if (abilityIntroVipOccluders[i] != null)
                        abilityIntroVipOccluders[i].enabled = abilityIntroVipOccluderStates[i];
            }
        }

        static bool IsVipElevatorRenderer(Transform renderer, Transform car)
            => renderer != null && car != null && renderer.IsChildOf(car);

        void DrawVipSpotlight(Rect frame, float time, float fade)
        {
            if (abilityIntroVipCamera == null || abilityIntroVipTexture == null
                || abilityIntroVipShade == null) return;

            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.DrawTexture(frame, abilityIntroVipShade, ScaleMode.StretchToFill, true);

            float scale = Mathf.Max(frame.width / abilityIntroVipTexture.width,
                frame.height / abilityIntroVipTexture.height);
            float drawWidth = abilityIntroVipTexture.width * scale;
            float drawHeight = abilityIntroVipTexture.height * scale;
            float drawX = frame.center.x - drawWidth * .5f;
            float drawY = frame.center.y - drawHeight * .5f;
            Vector3[] corners =
            {
                abilityIntroVipBounds.min,
                abilityIntroVipBounds.max,
                new Vector3(abilityIntroVipBounds.min.x, abilityIntroVipBounds.min.y, abilityIntroVipBounds.max.z),
                new Vector3(abilityIntroVipBounds.min.x, abilityIntroVipBounds.max.y, abilityIntroVipBounds.min.z),
                new Vector3(abilityIntroVipBounds.max.x, abilityIntroVipBounds.min.y, abilityIntroVipBounds.min.z),
                new Vector3(abilityIntroVipBounds.max.x, abilityIntroVipBounds.max.y, abilityIntroVipBounds.min.z),
                new Vector3(abilityIntroVipBounds.max.x, abilityIntroVipBounds.min.y, abilityIntroVipBounds.max.z),
                new Vector3(abilityIntroVipBounds.min.x, abilityIntroVipBounds.max.y, abilityIntroVipBounds.max.z)
            };
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector3 corner in corners)
            {
                Vector3 point = abilityIntroVipCamera.WorldToViewportPoint(corner);
                if (point.z <= 0f) continue;
                float x = drawX + point.x * drawWidth;
                float y = drawY + (1f - point.y) * drawHeight;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            if (minX != float.MaxValue)
            {
                float diameter = Mathf.Clamp(Mathf.Max(maxX - minX, maxY - minY) * 1.3f + 24f,
                    frame.height * .13f, frame.height * .48f);
                Rect ring = new Rect((minX + maxX - diameter) * .5f,
                    (minY + maxY - diameter) * .5f, diameter, diameter);
                float pulse = 1f + Mathf.Sin(time * 4.5f) * .025f;
                ring = new Rect(ring.center.x - diameter * pulse * .5f,
                    ring.center.y - diameter * pulse * .5f, diameter * pulse, diameter * pulse);
                float ringFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.32f, .8f, time));
                DrawVipFocusRing(ring, Gold, fade * ringFade);
            }
            GUI.color = oldColor;
        }

        static void DrawVipFocusRing(Rect bounds, Color color, float alpha)
        {
            const int segments = 64;
            float radius = bounds.width * .5f;
            float thickness = Mathf.Clamp(bounds.width * .018f, 2f, 6f);
            Vector2 centre = bounds.center;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                Vector2 from = centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
                Vector2 to = centre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                Vector2 midpoint = (from + to) * .5f;
                float length = Vector2.Distance(from, to) + 1f;
                float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;
                GUIUtility.RotateAroundPivot(angle, midpoint);
                GUI.DrawTexture(new Rect(midpoint.x - length * .5f,
                    midpoint.y - thickness * .5f, length, thickness), Texture2D.whiteTexture);
                GUI.matrix = oldMatrix;
            }
            GUI.color = oldColor;
        }

        public void AnimateAbilityIntro(float time)
        {
            if (abilityIntroKind == "VIP")
            {
                AnimateVipLocationShot(time);
                return;
            }
            if (abilityIntroPassenger == null || encounterPreviewCamera == null) return;
            if (abilityIntroOpponent == null) return;
            float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.75f, 1.5f, time));
            float impact = Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(1.5f, 2.15f, time)) * Mathf.PI);
            float recoil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.55f, 2.2f, time));
            abilityIntroPassenger.localPosition = new Vector3(-1.25f + impact * .32f, impact * .10f,
                abilityIntroOwnCar != null ? -.8f : 0f);
            abilityIntroPassenger.localRotation = Quaternion.Euler(0f, 10f + impact * 18f, -impact * 8f);
            if (abilityIntroOwnCar != null)
            {
                abilityIntroOwnCar.localPosition = new Vector3(-1.25f + impact * .32f, -.15f, .9f);
                abilityIntroOwnCar.localRotation = Quaternion.Euler(0f, 0f, -impact * 5f);
            }
            bool speed = abilityIntroKind == "GROUP" || abilityIntroKind == "ELDERLY";
            float opponentX = abilityIntroKind == "GROUP" ? Mathf.Lerp(.7f, 2.1f, approach)
                : abilityIntroKind == "ELDERLY"
                    ? Mathf.Lerp(.9f, 2.05f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, 1.1f, time)))
                        - .75f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.05f, 1.8f, time))
                    : 2.2f - approach * .75f + recoil * .5f;
            abilityIntroOpponent.localPosition = new Vector3(opponentX, impact * .12f, 0f);
            abilityIntroOpponent.localRotation = abilityIntroKind == "DIVER"
                ? Quaternion.Euler(0f, time * 260f, impact * 18f)
                : Quaternion.Euler(0f, -12f, abilityIntroKind == "ELDERLY" ? -recoil * 9f
                    : speed || abilityIntroKind == "CALM" ? 0f : recoil * 22f);

            if (abilityIntroKind == "FAIRY")
            {
                abilityIntroPassenger.localPosition = new Vector3(-1.1f + approach * .4f,
                    .18f + .22f * Mathf.Sin(time * 5f), 0f);
                abilityIntroOpponent.localPosition = new Vector3(.9f, .05f, 0f);
                abilityIntroOpponent.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 4f) * 8f);
            }
            else if (abilityIntroKind == "GOBLIN")
            {
                abilityIntroPassenger.localPosition = new Vector3(-1f + impact * .65f, -.1f, 0f);
                abilityIntroPassenger.localRotation = Quaternion.Euler(0f, 0f, -impact * 28f);
                abilityIntroOpponent.localPosition = new Vector3(1.4f + approach * .5f, .05f, 0f);
                abilityIntroOpponent.localRotation = Quaternion.Euler(0f, 0f, -approach * 28f);
            }
            else if (abilityIntroKind == "MUSHROOM")
            {
                abilityIntroPassenger.localPosition = new Vector3(-1.05f, .05f + impact * .2f, 0f);
                abilityIntroOpponent.localPosition = new Vector3(.85f + impact * .24f, .05f, 0f);
                abilityIntroOpponent.localRotation = Quaternion.Euler(0f, 0f, impact * 8f);
            }
            else if (abilityIntroKind == "ASTRONAUT")
            {
                abilityIntroPassenger.localPosition = new Vector3(-1.2f, -.05f, 0f);
                abilityIntroPassenger.localRotation = Quaternion.Euler(0f, 8f, -approach * 16f + impact * 20f);
                abilityIntroOpponent.localPosition = new Vector3(.7f - approach * .3f, .04f, .5f);
                abilityIntroOpponent.localRotation = Quaternion.Euler(0f, 0f, impact * 12f - recoil * 12f);
            }
            else if (abilityIntroKind == "ALIEN")
            {
                abilityIntroPassenger.localPosition = new Vector3(-1.15f, .05f + .1f * Mathf.Sin(time * 6f), 0f);
                abilityIntroPassenger.localRotation = Quaternion.Euler(0f, 10f, Mathf.Sin(time * 4f) * 8f);
                abilityIntroOpponent.localPosition = new Vector3(.9f + .18f * Mathf.Sin(time * 4f), .2f, 0f);
                abilityIntroOpponent.localRotation = Quaternion.Euler(0f, 0f, time * 60f);
            }
            if (abilityIntroInGallery) PoseGalleryAbility(time);

            // Start on the passenger, then pivot/zoom out to reveal the threat.
            float reveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, 1.2f, time));
            encounterPreviewCamera.orthographicSize = Mathf.Lerp(1.2f, 2.1f, reveal);
            float yaw = Mathf.Lerp(4f, 0f, reveal);
            float focusX = Mathf.Lerp(-1.1f, .12f, reveal);
            encounterPreviewCamera.transform.localPosition = new Vector3(
                focusX - 12f * Mathf.Tan(yaw * Mathf.Deg2Rad), 0f, -12f);
            encounterPreviewCamera.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            encounterPreviewCamera.Render();
        }

        void PoseGalleryAbility(float time)
        {
            float effect = PreviewEase(time, 1.35f, 2.1f);
            float after = PreviewEase(time, 2.15f, 2.95f);
            switch (abilityIntroKind)
            {
                case "HANDYMAN":
                    if (abilityIntroRepairGauge != null)
                    {
                        if (effect < .01f) abilityIntroRepairGauge.SetSeverity(3);
                        else if (after < .5f) abilityIntroRepairGauge.AnimateRemoval(effect);
                        else abilityIntroRepairGauge.SetSeverity(0);
                    }
                    break;
                case "ELDERLY":
                    if (abilityIntroBaby == null) break;
                    bool calming = time >= 1.90f;
                    abilityIntroOpponent.gameObject.SetActive(!calming);
                    abilityIntroBaby.gameObject.SetActive(calming);
                    abilityIntroBaby.localPosition = new Vector3(1.18f - .60f * after, -.14f
                        + Mathf.Sin(time * 15f) * .11f * (1f - after), 0f);
                    break;
                case "PREGNANT":
                    abilityIntroOpponent.localPosition = new Vector3(1.12f, -.22f, .6f);
                    abilityIntroPassenger.localPosition = new Vector3(-.45f + .82f * effect,
                        Mathf.Sin(time * 16f) * .04f * effect, -.35f);
                    break;
                case "GOBLIN":
                    abilityIntroOpponent.localScale = Vector3.one * (1f - .7f * effect);
                    break;
                case "MUSHROOM":
                    abilityIntroOpponent.localPosition = new Vector3(.93f - .30f * effect + 1.12f * after, .05f, 0f);
                    break;
                case "ALIEN":
                    abilityIntroOpponent.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 3.8f) * 7f);
                    abilityIntroOpponent.localPosition = new Vector3(.95f, .48f, .10f);
                    abilityIntroPassenger.localPosition = new Vector3(Mathf.Lerp(-1.15f, .95f, effect),
                        Mathf.Lerp(-.47f, .33f, effect) + .51f * after, -.25f);
                    break;
                case "HUNTER":
                case "DIVER":
                case "BOUNCER":
                case "GROUP":
                case "FAIRY":
                case "ASTRONAUT":
                    break; // The live cutaway already shows these interactions.
                case "SPEEDBOOST":
                    abilityIntroOpponent.localPosition = new Vector3(.40f + 2.25f * effect, -.25f, .42f);
                    break;
                default:
                    // Ordinary riders show the same pickup and delivery flow players use in the round.
                    float board = PreviewEase(time, .5f, 1.48f);
                    float ride = PreviewEase(time, 1.48f, 2.48f);
                    float leave = PreviewEase(time, 2.48f, 3.22f);
                    abilityIntroOpponent.localPosition = new Vector3(.72f, -.55f + .87f * ride, .44f);
                    abilityIntroPassenger.localPosition = new Vector3(-1.40f + 1.75f * board + 1.42f * leave,
                        -.37f + .87f * ride, -.33f);
                    break;
            }
        }

        public string RoundPassengerDemoCaption(string kind)
        {
            switch (kind)
            {
                case "ELDERLY": return "Slows the car. Calms babies.";
                case "PREGNANT": return "Crying drains patience";
                case "BOUNCER": return "Stronger bumps";
                case "GROUP": return "Speed boost and sticky trail";
                case "HANDYMAN": return "Clears rust";
                case "HUNTER": return "Stuns monsters";
                case "DIVER": return "Blocks tornadoes";
                case "FAIRY": return "Reveals a route past vines";
                case "GOBLIN": return "Cuts through vines";
                case "MUSHROOM": return "Blocks vines";
                case "ASTRONAUT": return "Tethers the car against gravity";
                case "ALIEN": return "Uses the UFO beam as a shortcut";
                case "BOSS": return "Bonus points on delivery";
                case "INTERVIEW": return "Short patience";
            }
            foreach (SharedRider rider in AllRiders)
            {
                if (rider == null || rider.Kind != kind) continue;
                if (rider.HasFeature(PassengerFeature.SpeedBoost)) return "Speeds up the car";
                if (rider.Space > 1) return "Needs " + rider.Space + " spaces";
                if (rider.Bonus > 0) return "Bonus points on delivery";
                break;
            }
            return "Deliver to their floor";
        }

        public void DrawRoundPassengerDemo(Rect frame, float elapsed)
        {
            if (encounterPreviewTexture == null) return;
            float time = Mathf.Repeat(elapsed, AbilityIntroDuration);
            float fade = abilityIntroInGallery
                ? Mathf.Min(PreviewEase(time, 0f, .16f), 1f - PreviewEase(time, 3.25f, AbilityIntroDuration)) : 1f;
            Color old = GUI.color;
            GUI.color = old * new Color(1f, 1f, 1f, fade);
            MenuPresentation.DrawTexture(frame, encounterPreviewTexture, ScaleMode.ScaleToFit);
            GUI.color = old;
            if (!abilityIntroInGallery) return;
            DrawAbilityVisuals(frame, time, fade);
            DrawGalleryAbilityVisuals(frame, time, fade);
            GUI.color = old;
        }

        void DrawGalleryAbilityVisuals(Rect frame, float time, float fade)
        {
            if (abilityIntroPassenger == null || abilityIntroOpponent == null) return;
            Vector2 passenger = PreviewGuiPoint(frame, abilityIntroPassenger.position);
            Vector2 target = PreviewGuiPoint(frame, abilityIntroOpponent.position);
            float effect = PreviewEase(time, 1.25f, 1.65f) * fade;
            if (abilityIntroKind == "PREGNANT" || abilityIntroKind == "INTERVIEW")
            {
                // An illustrated gauge demonstrates impatience without another sentence.
                float patience = 1f - PreviewEase(time, 1.05f, 2.38f);
                Rect gauge = new Rect(target.x - 38f, Mathf.Clamp(target.y - 63f, frame.y + 10f, frame.yMax - 15f),
                    76f, 10f);
                MenuPresentation.Paint(gauge, new Color(Ink.r, Ink.g, Ink.b, fade));
                gauge.width *= patience;
                MenuPresentation.Paint(gauge, Color.Lerp(Coral, Teal, patience) * new Color(1f, 1f, 1f, fade));
                if (abilityIntroKind == "PREGNANT" && effect > 0f)
                    DrawGallerySymbol(new Rect(passenger.x - 54f, passenger.y, 42f, 40f), "!", Coral, effect);
            }
            else if (abilityIntroKind == "ELDERLY" && time > 1.9f && abilityIntroBaby != null)
            {
                Vector2 baby = PreviewGuiPoint(frame, abilityIntroBaby.position);
                DrawGallerySymbol(new Rect(baby.x - 20f, baby.y - 79f - 10f * Mathf.Sin(time * 6f), 40f, 40f),
                    "♥", Coral, effect);
            }
            else if (abilityIntroKind == "ASTRONAUT")
            {
                Vector2 direction = target - passenger;
                Matrix4x4 old = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, passenger);
                MenuPresentation.Paint(new Rect(passenger.x, passenger.y + 15f, direction.magnitude, 4f),
                    new Color(Teal.r, Teal.g, Teal.b, effect));
                GUI.matrix = old;
            }
            else if (abilityIntroKind == "MUSHROOM")
            {
                float size = 60f + 42f * Mathf.Sin(Mathf.Clamp01((time - 1.25f) / 1.5f) * Mathf.PI);
                DrawVipFocusRing(new Rect(passenger.x - size * .5f, passenger.y - size * .5f, size, size),
                    Teal, effect);
            }
            else if (abilityIntroKind == "BOSS" && time >= 2.48f)
                DrawGallerySymbol(new Rect(passenger.x - 32f, passenger.y - 75f, 64f, 38f), "+", Gold, effect);
            else if (abilityIntroKind == "GROUP")
                for (int i = 1; i <= 3; i++)
                    DrawGallerySymbol(new Rect(target.x - i * 19f - 16f, target.y + 12f + i % 2 * 5f, 22f, 24f),
                        "●", Coral, effect * (1f - i * .17f));
        }

        Vector2 PreviewGuiPoint(Rect frame, Vector3 point)
        {
            Vector3 uv = encounterPreviewCamera.WorldToViewportPoint(point);
            return new Vector2(frame.x + frame.width * uv.x, frame.yMax - frame.height * uv.y);
        }

        void DrawGallerySymbol(Rect rect, string value, Color color, float alpha)
        {
            Styles();
            var symbol = new GUIStyle(large) { alignment = TextAnchor.MiddleCenter, fontSize = 34 };
            symbol.normal.textColor = MenuPresentation.DisplayColor(new Color(color.r, color.g, color.b, alpha));
            GUI.Label(rect, value, symbol);
        }

        public void DrawAbilityIntro(float time, Rect view, bool showPlayer, bool playerPane)
        {
            Texture introTexture = abilityIntroKind == "VIP"
                ? abilityIntroVipTexture : encounterPreviewTexture;
            if (introTexture == null) return;
            float duration = abilityIntroKind == "VIP" ? VipIntroDuration : AbilityIntroDuration;
            float fade = Mathf.Min(Mathf.Clamp01(time / .25f), Mathf.Clamp01((duration - time) / .35f));
            Color old = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -2000;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(view, Texture2D.whiteTexture);
            float bar = view.height * .105f;
            Rect frame;
            if (playerPane)
            {
                // Fit the landscape preview inside one pane without cropping
                // either the passenger or the approaching hazard.
                float height = Mathf.Min(view.height - bar * 2f, view.width * 9f / 16f);
                frame = new Rect(view.x, view.center.y - height * .5f, view.width, height);
            }
            else frame = new Rect(view.x, view.y + bar, view.width, view.height - bar * 2f);
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.DrawTexture(frame, introTexture,
                playerPane ? ScaleMode.ScaleToFit : ScaleMode.ScaleAndCrop);
            if (abilityIntroKind == "VIP")
                DrawVipSpotlight(frame, time, fade);

            DrawAbilityVisuals(frame, time, fade);
            GUI.color = new Color(1f, 1f, 1f, fade);
            GameTypography.ApplyToSkin(GUI.skin);
            Styles();
            var textStyle = new GUIStyle(large) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(view.height * .038f, view.width * .048f)), 18, 42), wordWrap = false };
            string message = abilityIntroKind == "VIP" && abilityIntroVip != null
                ? "F" + StopTag(abilityIntroVip.Origin, abilityIntroVip.OriginStop) + "    ·    +250"
                : abilityIntroKind == "HUNTER" ? "STUNS MONSTERS"
                : abilityIntroKind == "DIVER" ? "BLOCKS TORNADOES"
                : abilityIntroKind == "BOUNCER" ? "STRONGER BUMPS"
                : abilityIntroKind == "GROUP" ? "SPEED + STICKY TRAIL"
                : abilityIntroKind == "ELDERLY" ? "SLOWS ELEVATOR"
                : abilityIntroKind == "PREGNANT" ? "PATIENCE DRAINS FASTER"
                : abilityIntroKind == "CALM" ? "CALMS THE BABY"
                : abilityIntroKind == "FAIRY" ? "FIREFLIES REVEAL A SAFE ROUTE"
                : abilityIntroKind == "GOBLIN" ? "CUTS THROUGH VINES"
                : abilityIntroKind == "MUSHROOM" ? "SPORES GUARD AGAINST VINES"
                : abilityIntroKind == "ASTRONAUT" ? "TETHERS AGAINST GRAVITY"
                : abilityIntroKind == "ALIEN" ? "RIDES THE UFO BEAM TO A SHORTCUT" : "CLEARS RUST";
            float promptHeight = Mathf.Clamp(view.height * .04f, 22f, 36f);
            Rect messageArea = new Rect(view.x, frame.yMax, view.width,
                Mathf.Max(0f, view.yMax - frame.yMax - promptHeight));
            GUI.Label(messageArea, message, textStyle);
            var skipHint = new GUIStyle(textStyle)
            {
                fontSize = Mathf.Clamp(Mathf.RoundToInt(view.height * .022f), 14, 24),
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(view.x, view.yMax - promptHeight, view.width, promptHeight),
                "PRESS Y TO SKIP", skipHint);
            textStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.height * .022f), 14, 24);
            string heading = showPlayer ? (IsNpc ? "CPU  ·  " : "PLAYER " + (Seat + 1) + "  ·  ") : "";
            string name = abilityIntroKind == "VIP" ? "VIP" : abilityIntroKind == "GROUP" ? "THE TRIO" : abilityIntroKind == "ELDERLY" ? "GRANDMA"
                : abilityIntroKind == "PREGNANT" ? "MOM + BABY" : abilityIntroKind == "CALM" ? "GRANDMA + BABY"
                : abilityIntroKind;
            GUI.Label(new Rect(view.x, view.y, view.width, frame.y - view.y), heading + name, textStyle);
            GUI.color = old;
            GUI.matrix = oldMatrix;
            GUI.depth = oldDepth;
        }

        void DrawAbilityVisuals(Rect frame, float time, float fade)
        {
            Color old = GUI.color;
            // A visible strike/shield complements the moving 3D characters.
            float effect = Mathf.Clamp01(Mathf.Min((time - 1.35f) / .15f, (2.25f - time) / .25f));
            if (effect > 0f && abilityIntroKind == "DIVER" && abilityIntroShield != null)
            {
                float size = frame.height * .32f;
                GUI.color = new Color(1f, 1f, 1f, effect * fade);
                GUI.DrawTexture(new Rect(frame.center.x - size * .70f, frame.center.y - size * .5f, size, size), abilityIntroShield);
            }
            else if (effect > 0f && abilityIntroKind == "HUNTER")
            {
                GUI.color = new Color(.55f, 1f, .9f, effect * fade);
                GUI.DrawTexture(new Rect(frame.center.x - frame.width * .12f, frame.center.y - 5f,
                    frame.width * .25f, 10f), Texture2D.whiteTexture);
            }
            else if (effect > 0f && (abilityIntroKind == "CALM" || abilityIntroKind == "PREGNANT"))
            {
                GameTypography.ApplyToSkin(GUI.skin);
                Styles();
                var symbol = new GUIStyle(large) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(frame.height * .20f), 28, 110) };
                symbol.normal.textColor = abilityIntroKind == "CALM" ? Coral : Gold;
                GUI.color = new Color(1f, 1f, 1f, effect * fade);
                GUI.Label(new Rect(frame.center.x - frame.width * .1f, frame.y, frame.width * .2f, frame.height * .55f),
                    abilityIntroKind == "CALM" ? "♥" : "!", symbol);
            }
            GUI.color = old;
        }

        void IntroduceBoardedPassenger(CrazyElevator.Shared.Rider rider)
        {
            if (rider == null || round == null || !round.Owns(rider)) return;
            if (rider.HasFeature(PassengerFeature.CryingBaby) && !round.BabyCalmedByGrandma)
                GameManager.Instance?.RequestAbilityIntro(this, "PREGNANT");
            if (round.BabyCalmedByGrandma
                && (rider.HasFeature(PassengerFeature.CryingBaby)
                    || rider.HasFeature(PassengerFeature.CalmsBaby)))
                GameManager.Instance?.RequestAbilityIntro(this, "CALM");
            if (rider.HasFeature(PassengerFeature.SpeedBoost))
                GameManager.Instance?.RequestAbilityIntro(this, "GROUP");
            if (rider.HasFeature(PassengerFeature.SpeedSlow))
                GameManager.Instance?.RequestAbilityIntro(this, "ELDERLY");
            if (rider.HasFeature(PassengerFeature.ForestScout))
                GameManager.Instance?.RequestAbilityIntro(this, "FAIRY");
            if (rider.HasFeature(PassengerFeature.ForestShortcut))
                GameManager.Instance?.RequestAbilityIntro(this, "GOBLIN");
            if (rider.HasFeature(PassengerFeature.ForestSporeGuard))
                GameManager.Instance?.RequestAbilityIntro(this, "MUSHROOM");
        }

        public void FinishAbilityIntro()
        {
            ReleaseVipLocationShot();
            abilityIntroPassenger = abilityIntroOpponent = abilityIntroOwnCar = null;
            abilityIntroBaby = null;
            abilityIntroRepairGauge = null;
            abilityIntroInGallery = false;
            abilityIntroKind = null;
            abilityIntroVip = null;
            ClearEncounterPreviewModel();
        }

        void ReleaseAbilityIntro()
        {
            ReleaseVipLocationShot();
            if (abilityIntroShield != null) Destroy(abilityIntroShield);
            abilityIntroShield = null;
        }

        void ReleaseVipLocationShot()
        {
            if (abilityIntroVipFigure != null)
                abilityIntroVipFigure.gameObject.SetActive(abilityIntroVipFigureWasActive);
            abilityIntroVipFigure = null;
            abilityIntroVipOccluders.Clear();
            abilityIntroVipOccluderStates.Clear();
            if (abilityIntroVipDestination != null)
                abilityIntroVipDestination.rotation = abilityIntroVipDestinationRotation;
            abilityIntroVipDestination = null;
            abilityIntroVipDestinationRotation = Quaternion.identity;
            abilityIntroVipWorld = null;
            abilityIntroVipFocus = Vector3.zero;
            abilityIntroVipBounds = default;
            abilityIntroVipFigureWasActive = false;

            if (abilityIntroVipCamera != null)
                Destroy(abilityIntroVipCamera.gameObject);
            abilityIntroVipCamera = null;
            if (abilityIntroVipTexture != null)
            {
                abilityIntroVipTexture.Release();
                Destroy(abilityIntroVipTexture);
            }
            abilityIntroVipTexture = null;
            if (abilityIntroVipShade != null) Destroy(abilityIntroVipShade);
            abilityIntroVipShade = null;
        }
    }
}
