using CrazyElevator.Shared;
using UnityEngine;

namespace CrazyElevator.Managers
{
    public sealed partial class ElevatorManager
    {
        public const float DimensionRevealConfirmDelay = .85f;
        bool dimensionRevealConfirmClicked;
        const float DimensionDoorCloseSeconds = .85f;
        bool dimensionRevealActive;
        float dimensionRevealTime;
        int dimensionRevealIndex = -1;
        Phase dimensionResumePhase;
        float dimensionResumePhaseTime;
        float dimensionResumeDoors;

        public bool DimensionRevealActive => dimensionRevealActive;
        public int DimensionRevealIndex => dimensionRevealIndex;
        public static WorldBand DimensionWorld(int index)
        {
            return (WorldBand)Mathf.Clamp(index, 0, ElevatorRound.Dimensions - 1);
        }

        public void BeginDimensionReveal(int index)
        {
            index = Mathf.Clamp(index, 0, ElevatorRound.Dimensions - 1);
            if (dimensionRevealActive) return;
            dimensionRevealIndex = index;
            dimensionRevealTime = 0f;
            dimensionRevealConfirmClicked = false;
            dimensionResumePhase = phase;
            dimensionResumePhaseTime = phaseTime;
            dimensionResumeDoors = doors;
            dimensionRevealActive = true;
            MarkDimensionPassengersIntroduced(index);
            // One camera pair serves the shared preview in a local duel.
            if (Match == null || Seat == 0)
            {
                PrepareDimensionPreview(DimensionWorld(index));
                PrepareDimensionLineup(DimensionWorld(index));
            }
            notice = "ROUND " + (session != null ? session.RoundNumber : 1)
                + ": " + DimensionIntroTitle(DimensionWorld(index));
        }

        void MarkDimensionPassengersIntroduced(int index)
        {
            // Cover the world's roster here so its round can run uninterrupted.
            completedWorldTutorials[Seat].Add(DimensionWorld(index));
            if (round == null) return;
            foreach (var rider in round.Riders)
                if (rider != null && !string.IsNullOrEmpty(rider.Kind)
                    && rider.Origin / ElevatorRound.FloorsPerDimension == index)
                    completedPassengerTutorials[Seat].Add(rider.Kind);
        }

        public void FinishDimensionReveal()
        {
            if (!dimensionRevealActive) return;
            dimensionRevealActive = false;
            phase = dimensionResumePhase;
            phaseTime = dimensionResumePhaseTime;
            doors = dimensionResumeDoors;
            SetDoors(doors);
            ClearEncounterPreviewModel();
            ClearDimensionLineup();
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            dimensionRevealConfirmClicked = false;
            if (Match == null && !MenuManager.BeginRoundPassengerIntroduction(this, null))
                BeginRoundCountdown();
        }

        public void ResetDimensionReveal()
        {
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            dimensionRevealConfirmClicked = false;
            ClearEncounterPreviewModel();
            ClearDimensionLineup();
        }

        void TickDimensionReveal(float dt)
        {
            if (paused || MenuManager.IsOpen || Match != null && Match.Paused) return;
            dimensionRevealTime += Mathf.Max(0f, dt);
            AnimateDimensionLineup(dimensionRevealTime);
            float close = Mathf.Clamp01(dimensionRevealTime / DimensionDoorCloseSeconds);
            SetDoors(Mathf.Lerp(dimensionResumeDoors, 0f, close));
            // ElevatorMatch ends the shared preview for both cars together.
            if (Match == null && dimensionRevealTime >= DimensionRevealConfirmDelay
                && DimensionRevealConfirmed) FinishDimensionReveal();
        }

        public bool DimensionRevealConfirmed => dimensionRevealConfirmClicked
            || (!IsNpc && SeatControls.ConfirmPressed);

        const int LineupSlots = 3;
        const float LineupSlotWidth = 5.6f;
        const float LineupSlotHeight = 3.15f;
        Transform dimensionLineupStage;
        Camera dimensionLineupCamera;
        RenderTexture dimensionLineupTexture;
        readonly Transform[] dimensionLineupSlots = new Transform[LineupSlots];
        readonly Transform[] dimensionHazardModels = new Transform[LineupSlots];
        readonly Transform[] dimensionDemoSubjects = new Transform[LineupSlots];
        readonly StarfishImpairmentView[] dimensionRustGauges = new StarfishImpairmentView[LineupSlots];

        enum HazardDemo { Scanner, Monster, Tornado, Syrup, Current, Rust, Vines, Gravity, Ufo }

        public static string DimensionIntroTitle(WorldBand world) => world == WorldBand.Office ? "OFFICE"
            : world == WorldBand.Candy ? "COTTON CANDY" : world == WorldBand.Water ? "UNDERWATER"
            : world == WorldBand.Forest ? "MYSTICAL FOREST" : "SPACE";

        public static Color DimensionIntroAccent(WorldBand world) => world == WorldBand.Office
            ? new Color32(98, 132, 178, 255) : world == WorldBand.Candy
                ? new Color32(236, 118, 188, 255) : world == WorldBand.Water
                ? new Color32(48, 196, 210, 255) : world == WorldBand.Forest
                ? new Color32(145, 204, 144, 255) : new Color32(174, 143, 242, 255);

        (HazardDemo Effect, string Name, string Caption, GameObject Prefab)[] DimensionHazards(WorldBand world)
        {
            var monster = (HazardDemo.Monster, "MONSTER", "Knocks you back", sceneView.monster);
            var tornado = (HazardDemo.Tornado, "TORNADO", "Spins and flings you", sceneView.tornado);
            switch (world)
            {
                case WorldBand.Office:
                    return new[] { (HazardDemo.Scanner, "SECURITY SCAN", "Limits your speed", sceneView.securitySweep),
                        monster, tornado };
                case WorldBand.Candy:
                    return new[] { (HazardDemo.Syrup, "SYRUP", "Sticks and slows you", sceneView.syrupSurge),
                        monster, tornado };
                case WorldBand.Water:
                    return new[] { (HazardDemo.Current, "CURRENT", "Pushes you and adds rust", sceneView.oceanCurrent),
                        (HazardDemo.Rust, "RUST", "Builds up and slows you", impairmentGaugePrefab != null
                            ? impairmentGaugePrefab.gameObject : null), tornado };
                case WorldBand.Forest:
                    return new[] { (HazardDemo.Vines, "VINES", "Catch and shove you", sceneView.forestVineSweep) };
                default:
                    return new[] { (HazardDemo.Gravity, "BLACK HOLE", "Pulls you off course", sceneView.spaceGravitySweep),
                        (HazardDemo.Ufo, "UFO", "Moves a waiting rider", sceneView.spaceGravitySweep) };
            }
        }

        (HazardDemo Effect, string Name, string Caption, GameObject Prefab)[] dimensionHazards;

        void PrepareDimensionLineup(WorldBand world)
        {
            ClearDimensionLineup();
            if (sceneView == null) return;
            dimensionHazards = DimensionHazards(world);
            EnsureDimensionLineup();
            for (int i = 0; i < dimensionHazards.Length; i++)
            {
                var hazard = dimensionHazards[i];
                Transform slot = dimensionLineupSlots[i];
                bool ufo = hazard.Effect == HazardDemo.Ufo;
                GameObject subject = ufo ? FindPassengerTemplate("ALIEN") : sceneView.exteriorCar.gameObject;
                dimensionDemoSubjects[i] = CreatePreviewActor(slot, subject, ufo ? "Waiting rider" : "Elevator",
                    ufo ? .82f : 1.15f, ufo ? 1.15f : 1.9f, model =>
                    {
                        if (!ufo) SetUpPreviewCar(model);
                    });
                if (hazard.Prefab == null) continue;
                dimensionHazardModels[i] = CreatePreviewActor(slot, hazard.Prefab, hazard.Name, 1.65f, 2.4f, model =>
                {
                    if (hazard.Effect == HazardDemo.Gravity || ufo)
                        SetSpacePreviewMode(model.transform, ufo);
                    Transform safeRoute = model.transform.Find("Fairy safe route");
                    if (safeRoute != null) safeRoute.gameObject.SetActive(false);
                    var gauge = model.GetComponent<StarfishImpairmentView>();
                    if (gauge == null) return;
                    model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    gauge.SetSeverity(0);
                    dimensionRustGauges[i] = gauge;
                });
            }
            dimensionLineupCamera.backgroundColor = Color.Lerp(MenuPresentation.Ink, DimensionIntroAccent(world), .22f);
            dimensionLineupCamera.enabled = true;
            AnimateDimensionLineup(0f);
        }

        void AnimateDimensionLineup(float elapsed)
        {
            if (dimensionHazards == null || dimensionLineupCamera == null || !dimensionLineupCamera.enabled) return;
            float time = Mathf.Repeat(elapsed / 3.15f, 1f);
            for (int i = 0; i < dimensionHazards.Length; i++)
                PoseHazardDemo(i, dimensionHazards[i].Effect, time);
        }

        void PoseHazardDemo(int slot, HazardDemo effect, float time)
        {
            Transform subject = dimensionDemoSubjects[slot];
            Transform hazard = dimensionHazardModels[slot];
            float approach = PreviewEase(time, .02f, .36f);
            float hit = PreviewEase(time, .36f, .62f);
            float exit = PreviewEase(time, .64f, .94f);
            float jolt = Mathf.Sin(hit * Mathf.PI);
            Vector3 position = new Vector3(Mathf.Lerp(-1.8f, -.18f, approach), -.28f, -.23f);
            Vector3 hazardPosition = new Vector3(.8f, .1f, .28f);
            Vector3 hazardAngles = Vector3.zero;
            Vector3 hazardScale = Vector3.one;
            float tilt = 0f;
            switch (effect)
            {
                case HazardDemo.Scanner:
                    position.x += hit * .24f + exit * 1.62f;
                    if (hazard != null) PoseScanner(hazard.GetChild(0), time * 10f, jolt);
                    break;
                case HazardDemo.Syrup:
                    position.x += hit * .26f + exit * 1.32f;
                    hazardPosition.x = Mathf.Lerp(.8f, position.x + .30f, hit);
                    hazardScale = new Vector3(1f + .24f * jolt, 1f - .36f * hit, 1f);
                    tilt = -8f * jolt;
                    break;
                case HazardDemo.Monster:
                    hazardPosition.x = .98f - .59f * approach + .45f * hit;
                    hazardAngles.z = 13f * jolt;
                    position.x -= hit * 1.55f;
                    tilt = 25f * jolt;
                    break;
                case HazardDemo.Tornado:
                    hazardAngles.y = time * 720f;
                    hazardPosition.x = .55f + .23f * Mathf.Sin(time * 7f);
                    position += new Vector3(-1.66f * hit, .95f * hit, 0f);
                    tilt = 360f * hit + 18f * exit;
                    break;
                case HazardDemo.Current:
                    hazardPosition.x = .12f + .82f * hit;
                    position += new Vector3(1.78f * hit, -.32f * hit, 0f);
                    tilt = -18f * jolt;
                    if (hazard != null)
                    {
                        PoseCurrentStream(hazard.GetChild(0), "Current upper", 0f, .45f, time * 3f, 0f);
                        PoseCurrentStream(hazard.GetChild(0), "Current lower", 0f, -.44f, time * 3f, 2f);
                    }
                    break;
                case HazardDemo.Rust:
                    position.x += .68f * hit + .45f * exit;
                    hazardPosition = new Vector3(position.x, .76f, -.5f);
                    if (dimensionRustGauges[slot] != null)
                        dimensionRustGauges[slot].SetSeverity(Mathf.Min(3, Mathf.FloorToInt(time * 5f)));
                    break;
                case HazardDemo.Vines:
                    float shove = PreviewEase(time, .53f, .84f);
                    hazardPosition = new Vector3(.54f - .55f * approach + 1.35f * shove, -.05f, -.85f);
                    hazardScale = new Vector3(1f - .48f * jolt, 1f + .40f * jolt, 1f);
                    position.x += 1.85f * shove;
                    tilt = -19f * Mathf.Sin(shove * Mathf.PI);
                    break;
                case HazardDemo.Gravity:
                    float pull = PreviewEase(time, .29f, .84f);
                    position += new Vector3(1.40f * pull, .45f * pull, 0f);
                    tilt = -25f * hit;
                    if (hazard != null)
                    {
                        Transform orbit = hazard.GetChild(0).Find("Orbit");
                        if (orbit != null) orbit.localRotation = Quaternion.Euler(0f, 0f, time * 420f);
                    }
                    break;
                case HazardDemo.Ufo:
                    float lift = PreviewEase(time, .22f, .54f);
                    float transfer = PreviewEase(time, .49f, .83f);
                    position = new Vector3(Mathf.Lerp(-1.32f, 1.24f, transfer),
                        -.92f + lift * 1.52f - .45f * transfer, -.18f);
                    hazardPosition = new Vector3(position.x, .24f + lift * .26f, .15f);
                    hazardAngles.z = -10f * Mathf.Sin(transfer * Mathf.PI);
                    if (hazard != null)
                    {
                        Transform beam = hazard.GetChild(0).Find("UFO/Beam");
                        if (beam != null) beam.gameObject.SetActive(time >= .14f && time <= .88f);
                    }
                    break;
            }

            if (subject != null)
            {
                subject.localPosition = position;
                subject.localRotation = Quaternion.Euler(0f, effect == HazardDemo.Ufo ? 0f : -13f, tilt);
            }
            if (hazard != null)
            {
                hazard.localPosition = hazardPosition;
                hazard.localRotation = Quaternion.Euler(hazardAngles);
                hazard.localScale = hazardScale;
            }
        }

        static float PreviewEase(float time, float from, float to)
            => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, time));

        GameObject FindPassengerTemplate(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            foreach (PassengerView passenger in sceneView.passengers)
                if (passenger != null && passenger.ResolvedKind == kind) return passenger.gameObject;
            return Resources.Load<GameObject>("Passenger-" + kind);
        }

        void EnsureDimensionLineup()
        {
            if (dimensionLineupStage != null) return;
            // Uses the same preview layer with its own local lighting.
            EnsureEncounterPreview();
            var stageObject = new GameObject("Dimension hazard lineup") { hideFlags = HideFlags.HideAndDontSave };
            dimensionLineupStage = stageObject.transform;
            dimensionLineupStage.position = new Vector3(-10000f - Seat * 100f, -10000f, 10000f);
            for (int i = 0; i < LineupSlots; i++)
            {
                var slot = new GameObject("Lineup slot " + i) { hideFlags = HideFlags.HideAndDontSave };
                slot.transform.SetParent(dimensionLineupStage, false);
                slot.transform.localPosition = new Vector3((i - 1) * LineupSlotWidth, 0f, 0f);
                dimensionLineupSlots[i] = slot.transform;
            }

            dimensionLineupTexture = new RenderTexture(480 * LineupSlots, 270, 24, RenderTextureFormat.ARGB32)
            {
                name = "Dimension hazard lineup",
                antiAliasing = 2,
                hideFlags = HideFlags.HideAndDontSave
            };
            dimensionLineupTexture.Create();

            var cameraObject = new GameObject("Dimension lineup camera") { hideFlags = HideFlags.HideAndDontSave };
            cameraObject.transform.SetParent(dimensionLineupStage, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -12f);
            dimensionLineupCamera = cameraObject.AddComponent<Camera>();
            dimensionLineupCamera.clearFlags = CameraClearFlags.SolidColor;
            dimensionLineupCamera.backgroundColor = Color.clear;
            dimensionLineupCamera.cullingMask = 1 << EncounterPreviewLayer;
            dimensionLineupCamera.orthographic = true;
            dimensionLineupCamera.orthographicSize = LineupSlotHeight * .5f;
            dimensionLineupCamera.aspect = LineupSlots * 16f / 9f;
            dimensionLineupCamera.nearClipPlane = .1f;
            dimensionLineupCamera.farClipPlane = 30f;
            dimensionLineupCamera.allowHDR = false;
            dimensionLineupCamera.targetTexture = dimensionLineupTexture;
            dimensionLineupCamera.enabled = false;
            MenuPresentation.LightPreview(dimensionLineupStage, Vector3.zero, EncounterPreviewLayer);
        }

        void ClearDimensionLineup()
        {
            for (int i = 0; i < LineupSlots; i++)
            {
                dimensionHazardModels[i] = dimensionDemoSubjects[i] = null;
                dimensionRustGauges[i] = null;
                if (dimensionLineupSlots[i] == null) continue;
                foreach (Transform model in dimensionLineupSlots[i])
                {
                    model.gameObject.SetActive(false);
                    Destroy(model.gameObject);
                }
            }
            dimensionHazards = null;
            if (dimensionLineupCamera != null) dimensionLineupCamera.enabled = false;
        }

        void ReleaseDimensionLineup()
        {
            ClearDimensionLineup();
            if (dimensionLineupTexture != null)
            {
                if (dimensionLineupCamera != null) dimensionLineupCamera.targetTexture = null;
                dimensionLineupTexture.Release();
                Destroy(dimensionLineupTexture);
            }
            if (dimensionLineupStage != null) Destroy(dimensionLineupStage.gameObject);
            dimensionLineupStage = null;
            dimensionLineupCamera = null;
            dimensionLineupTexture = null;
        }

        void DrawDimensionHazard(Rect area, int slot, float alpha, float elapsed)
        {
            if (dimensionLineupTexture == null || dimensionDemoSubjects[slot] == null) return;
            float cycle = Mathf.Repeat(elapsed / 3.15f, 1f);
            float fade = Mathf.Min(PreviewEase(cycle, 0f, .065f), 1f - PreviewEase(cycle, .93f, 1f));
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha * fade);
            MenuPresentation.DrawTextureUV(area, dimensionLineupTexture,
                new Rect((float)slot / LineupSlots, 0f, 1f / LineupSlots, 1f));
            GUI.color = old;
        }

        public void DrawDimensionRevealOverlay(Rect view, float elapsed)
        {
            MenuPresentation.Backdrop(view);
            float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / DimensionRevealConfirmDelay));
            if (alpha <= .001f) return;

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = previousMatrix * MenuPresentation.Fit(view, 1200f, 640f);
            GUI.color = previousColor * new Color(1f, 1f, 1f, alpha);

            WorldBand world = DimensionWorld(dimensionRevealIndex);
            Color accent = DimensionIntroAccent(world);
            Color accentEdge = Color.Lerp(accent, MenuPresentation.Ink, .48f);
            Color recess = Color.Lerp(MenuPresentation.Wall, accent, .22f);
            Color paper = MenuPresentation.Paper;
            Color ink = MenuPresentation.Ink;
            Rect postcard = new Rect(42f, 44f, 430f, 510f);
            MenuPresentation.Plaque(postcard, paper, accentEdge, 10f);
            MenuPresentation.Fasteners(postcard, accentEdge);

            Rect badge = new Rect(postcard.center.x - 92f, 64f, 184f, 42f);
            MenuPresentation.Plaque(badge, accent, accentEdge, 4f);
            var heading = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19, alignment = TextAnchor.MiddleCenter, wordWrap = false
            };
            GameTypography.Apply(heading, true);
            heading.normal.textColor = MenuPresentation.DisplayColor(ink);
            var progress = GameManager.Instance;
            GUI.Label(badge, "ROUND " + (progress != null ? progress.RoundNumber : 1), heading);

            Rect picture = new Rect(62f, 126f, 390f, 278f);
            MenuPresentation.Plaque(picture, recess, accentEdge, 5f);
            if (DimensionRevealTexture != null)
                MenuPresentation.DrawTexture(new Rect(66f, 130f, 382f, 270f), DimensionRevealTexture, ScaleMode.ScaleToFit);

            heading.fontSize = 34;
            GUI.Label(new Rect(60f, 426f, 394f, 63f), DimensionIntroTitle(world), heading);
            var description = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19, alignment = TextAnchor.UpperCenter, wordWrap = true
            };
            GameTypography.Apply(description);
            description.normal.textColor = MenuPresentation.DisplayColor(ink);
            heading.fontSize = 26;
            heading.alignment = TextAnchor.MiddleLeft;
            description.fontSize = 20;
            description.alignment = TextAnchor.UpperLeft;
            int hazardCount = dimensionHazards?.Length ?? 0;
            float ticketHeight = hazardCount == 1 ? 274f : hazardCount == 2 ? 242f : 158f;
            float ticketsTop = hazardCount == 1 ? postcard.center.y - ticketHeight * .5f : postcard.y;
            for (int i = 0; i < hazardCount; i++)
            {
                Rect ticket = new Rect(496f, ticketsTop + i * (ticketHeight + 16f), 662f, ticketHeight);
                MenuPresentation.Plaque(ticket, paper, accentEdge, 8f);
                MenuPresentation.Paint(new Rect(ticket.x, ticket.y, 8f, ticket.height), accent);

                float previewWidth = hazardCount < 3 ? 317f : 233f;
                float previewHeight = previewWidth * 9f / 16f;
                Rect preview = new Rect(ticket.x + 20f, ticket.center.y - previewHeight * .5f,
                    previewWidth, previewHeight);
                MenuPresentation.Plaque(preview, recess, accentEdge, 3f);
                DrawDimensionHazard(preview, i, alpha, elapsed);
                float copyX = preview.xMax + 24f;
                float copyWidth = ticket.xMax - copyX - 20f;
                GUI.Label(new Rect(copyX, ticket.center.y - 47f, copyWidth, 42f),
                    dimensionHazards[i].Name, heading);
                GUI.Label(new Rect(copyX, ticket.center.y + 3f, copyWidth, 61f),
                    dimensionHazards[i].Caption, description);
            }

            if (elapsed >= DimensionRevealConfirmDelay)
            {
                Rect confirm = new Rect(450f, 572f, 300f, 50f);
                Color face = confirm.Contains(Event.current.mousePosition) ? paper : MenuPresentation.Teal;
                MenuPresentation.Plaque(confirm, face, ink, 5f);
                heading.alignment = TextAnchor.MiddleCenter;
                heading.fontSize = 19;
                GUI.Label(confirm, "CONTINUE", heading);
                DrawGameplayKeyHint(new Rect(confirm.xMax + 12f, confirm.y, 50f, 50f), GameplayConfirmKey);
                if (GUI.Button(confirm, GUIContent.none, GUIStyle.none))
                    dimensionRevealConfirmClicked = true;
            }

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }
    }
}
