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
            PrepareDimensionPreview(DimensionWorld(index));
            PrepareDimensionLineup(DimensionWorld(index));
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
            float close = Mathf.Clamp01(dimensionRevealTime / DimensionDoorCloseSeconds);
            SetDoors(Mathf.Lerp(dimensionResumeDoors, 0f, close));
            // ElevatorMatch ends the shared preview for both cars together.
            if (Match == null && dimensionRevealTime >= DimensionRevealConfirmDelay
                && DimensionRevealConfirmed) FinishDimensionReveal();
        }

        public bool DimensionRevealConfirmed => dimensionRevealConfirmClicked
            || (!IsNpc && SeatControls.ConfirmPressed);

        const int LineupSlots = 3;
        const float LineupSlotWidth = 3f;
        const float LineupModelHeight = 2.5f;
        Transform dimensionLineupStage;
        Camera dimensionLineupCamera;
        RenderTexture dimensionLineupTexture;
        readonly Transform[] dimensionLineupSlots = new Transform[LineupSlots];
        readonly bool[] dimensionLineupFilled = new bool[LineupSlots];

        public static string DimensionIntroTitle(WorldBand world) => world == WorldBand.Office ? "OFFICE"
            : world == WorldBand.Candy ? "COTTON CANDY" : world == WorldBand.Water ? "UNDERWATER"
            : world == WorldBand.Forest ? "MYSTICAL FOREST" : "SPACE";

        public static Color DimensionIntroAccent(WorldBand world) => world == WorldBand.Office
            ? new Color32(98, 132, 178, 255) : world == WorldBand.Candy
                ? new Color32(236, 118, 188, 255) : world == WorldBand.Water
                ? new Color32(48, 196, 210, 255) : world == WorldBand.Forest
                ? new Color32(145, 204, 144, 255) : new Color32(174, 143, 242, 255);

        (string Name, string Description, GameObject Prefab)[] DimensionHazards(WorldBand world)
        {
            switch (world)
            {
                case WorldBand.Office:
                    return new[] { ("SECURITY SCAN", "Slows the elevator.", sceneView.securitySweep),
                        ("MONSTER", "Bounces the elevator back.", sceneView.monster),
                        ("TORNADO", "Spins and flings the elevator.", sceneView.tornado) };
                case WorldBand.Candy:
                    return new[] { ("SYRUP", "Slows the elevator.", sceneView.syrupSurge),
                        ("MONSTER", "Bounces the elevator back.", sceneView.monster),
                        ("TORNADO", "Spins and flings the elevator.", sceneView.tornado) };
                case WorldBand.Water:
                    return new[] { ("CURRENT", "Pushes the elevator sideways and adds rust.", sceneView.oceanCurrent),
                        ("RUST", "Builds up underwater and slows the elevator.", (GameObject)null),
                        ("TORNADO", "Spins and flings the elevator.", sceneView.tornado) };
                case WorldBand.Forest:
                    return new[] { ("VINES", "Catch the elevator and shove it sideways.", sceneView.forestVineSweep) };
                default:
                    return new[] { ("BLACK HOLE", "Pulls the elevator sideways.", sceneView.spaceGravitySweep),
                        ("UFO", "Beams a waiting passenger to another floor.", sceneView.spaceGravitySweep) };
            }
        }

        (string Name, string Description, GameObject Prefab)[] dimensionHazards;

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
                GameObject model;
                if (hazard.Prefab != null)
                    model = Instantiate(hazard.Prefab, slot, false);
                else if (hazard.Name == "RUST" && impairmentGaugePrefab != null)
                {
                    var gauge = Instantiate(impairmentGaugePrefab, slot, false);
                    gauge.SetSeverity(3);
                    gauge.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    model = gauge.gameObject;
                }
                else continue;

                if (world == WorldBand.Space)
                    foreach (Transform part in model.transform)
                        part.gameObject.SetActive((part.name == "UFO") == (hazard.Name == "UFO"));
                if (world == WorldBand.Forest)
                {
                    Transform safeRoute = model.transform.Find("Fairy safe route");
                    if (safeRoute != null) safeRoute.gameObject.SetActive(false);
                }
                model.SetActive(true);
                MakeStillPreviewModel(model, Palette[(i + 2) % Palette.Length]);
                dimensionLineupFilled[i] = FitLineupModel(model, slot);
            }
            Color row = Color.Lerp(MenuPresentation.Wall, DimensionIntroAccent(world), .22f);
            dimensionLineupCamera.backgroundColor = new Color(row.r, row.g, row.b, 0f);
            dimensionLineupCamera.enabled = true;
        }

        GameObject FindPassengerTemplate(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            foreach (PassengerView passenger in sceneView.passengers)
                if (passenger != null && passenger.ResolvedKind == kind) return passenger.gameObject;
            return Resources.Load<GameObject>("Passenger-" + kind);
        }

        bool FitLineupModel(GameObject model, Transform slot)
        {
            if (!TryGetEncounterPreviewBounds(model, out Bounds bounds)) return false;
            float scale = Mathf.Min(LineupModelHeight / Mathf.Max(.01f, bounds.size.y),
                (LineupSlotWidth * .9f) / Mathf.Max(.01f, Mathf.Max(bounds.size.x, bounds.size.z)));
            model.transform.localScale *= scale;
            if (!TryGetEncounterPreviewBounds(model, out bounds)) return false;
            model.transform.localPosition -= slot.InverseTransformPoint(bounds.center);
            return true;
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

            dimensionLineupTexture = new RenderTexture(320 * LineupSlots, 320, 24, RenderTextureFormat.ARGB32)
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
            dimensionLineupCamera.orthographicSize = LineupSlotWidth * .5f;
            dimensionLineupCamera.aspect = LineupSlots;
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
                dimensionLineupFilled[i] = false;
                if (dimensionLineupSlots[i] == null) continue;
                foreach (Transform model in dimensionLineupSlots[i])
                {
                    model.gameObject.SetActive(false);
                    Destroy(model.gameObject);
                }
            }
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

        void DrawDimensionHazard(Rect area, int slot, float alpha)
        {
            if (dimensionLineupTexture == null || slot < 0 || slot >= LineupSlots
                || !dimensionLineupFilled[slot]) return;
            float size = Mathf.Min(area.width, area.height);
            if (size < 8f) return;
            Rect square = new Rect(area.center.x - size * .5f, area.center.y - size * .5f, size, size);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            MenuPresentation.DrawTextureUV(square, dimensionLineupTexture,
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
            heading.fontSize = 24;
            heading.alignment = TextAnchor.MiddleLeft;
            description.fontSize = 17;
            description.alignment = TextAnchor.UpperLeft;
            for (int i = 0; dimensionHazards != null && i < dimensionHazards.Length; i++)
            {
                Rect ticket = new Rect(496f, 44f + i * 174f, 662f, 158f);
                MenuPresentation.Plaque(ticket, paper, accentEdge, 8f);
                MenuPresentation.Paint(new Rect(ticket.x, ticket.y, 9f, ticket.height), accent);
                MenuPresentation.Fasteners(ticket, accentEdge);
                float copyX = ticket.x + 30f;
                if (dimensionLineupFilled[i])
                {
                    Rect portrait = new Rect(ticket.x + 24f, ticket.y + 15f, 138f, 138f);
                    MenuPresentation.Plaque(portrait, recess, accentEdge, 3f);
                    DrawDimensionHazard(new Rect(portrait.x + 4f, portrait.y + 4f, 130f, 130f), i, alpha);
                    copyX = portrait.xMax + 25f;
                }
                float copyWidth = ticket.xMax - copyX - 28f;
                GUI.Label(new Rect(copyX, ticket.y + 27f, copyWidth, 44f), dimensionHazards[i].Name, heading);
                GUI.Label(new Rect(copyX, ticket.y + 78f, copyWidth, 73f), dimensionHazards[i].Description, description);
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
