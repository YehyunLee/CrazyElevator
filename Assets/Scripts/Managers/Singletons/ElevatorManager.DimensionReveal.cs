using CrazyElevator.Shared;
using UnityEngine;

namespace CrazyElevator.Managers
{
    public sealed partial class ElevatorManager
    {
        // The reveal waits for confirm; this is only the CPU-vs-CPU fallback.
        public const float DimensionRevealSeconds = 6.8f;
        // Ignore confirm briefly so a press meant for gameplay doesn't skip the card.
        public const float DimensionRevealConfirmDelay = 1.2f;
        public const float DimensionRevealFadeSeconds = .6f;
        const float DimensionDoorCloseSeconds = .85f;
        bool dimensionRevealActive;
        float dimensionRevealTime;
        float dimensionRevealExitTime = -1f;
        int dimensionRevealIndex = -1;
        Phase dimensionResumePhase;
        float dimensionResumePhaseTime;
        float dimensionResumeDoors;

        public bool DimensionRevealActive => dimensionRevealActive;
        public int DimensionRevealIndex => dimensionRevealIndex;
        public static WorldBand DimensionWorld(int index)
        {
            return index <= 0 ? WorldBand.Office
                : index == 1 ? WorldBand.Candy : WorldBand.Water;
        }

        public void BeginDimensionReveal(int index)
        {
            index = Mathf.Clamp(index, 0, ElevatorRound.Dimensions - 1);
            if (dimensionRevealActive) return;
            dimensionRevealIndex = index;
            dimensionRevealTime = 0f;
            dimensionRevealExitTime = -1f;
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
            dimensionRevealExitTime = -1f;
            // The confirm that closed the card must not also act in the cabin.
            encounterConfirmationConsumedFrame = Time.frameCount;
        }

        public void ResetDimensionReveal()
        {
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            dimensionRevealExitTime = -1f;
            ClearEncounterPreviewModel();
            ClearDimensionLineup();
        }

        void TickDimensionReveal(float dt)
        {
            if (paused || MenuManager.IsOpen || Match != null && Match.Paused) return;
            dimensionRevealTime += Mathf.Max(0f, dt);
            float close = Mathf.Clamp01(dimensionRevealTime / DimensionDoorCloseSeconds);
            SetDoors(Mathf.Lerp(dimensionResumeDoors, 0f, close));
            // In 1v1, ElevatorMatch waits for every human player instead.
            if (Match != null) return;
            if (dimensionRevealExitTime < 0f)
            {
                if (dimensionRevealTime >= DimensionRevealConfirmDelay && SeatControls.ConfirmPressed)
                {
                    dimensionRevealExitTime = 0f;
                    Play(click);
                }
                return;
            }
            dimensionRevealExitTime += Mathf.Max(0f, dt);
            if (dimensionRevealExitTime >= DimensionRevealFadeSeconds) FinishDimensionReveal();
        }

        // Shared by the solo and 1v1 reveal screens.
        public static float DimensionRevealAlpha(float time, float exitTime)
        {
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / .85f));
            float fadeOut = exitTime < 0f ? 1f
                : Mathf.SmoothStep(0f, 1f, 1f - Mathf.Clamp01(exitTime / DimensionRevealFadeSeconds));
            return Mathf.Min(fadeIn, fadeOut);
        }

        // Blinks gently once confirm is accepted.
        public static float DimensionPromptAlpha(float time) => time < DimensionRevealConfirmDelay ? 0f
            : Mathf.Lerp(.55f, 1f, .5f + .5f * Mathf.Sin((time - DimensionRevealConfirmDelay) * 4f));

        // ---------- Passenger models for the reveal rows ----------
        // The authored passenger prefabs render side by side into one texture;
        // each reveal row draws its own third of it. Riders are authored facing the
        // cabin doors (-Z), which is toward the lineup camera, so they show their front.

        const int LineupSlots = 3;
        const float LineupSlotWidth = 3f;
        const float LineupModelHeight = 2.5f;
        Transform dimensionLineupStage;
        Camera dimensionLineupCamera;
        RenderTexture dimensionLineupTexture;
        readonly Transform[] dimensionLineupSlots = new Transform[LineupSlots];
        readonly bool[] dimensionLineupFilled = new bool[LineupSlots];

        // Same order as the reveal rows.
        static string[] DimensionPassengerKinds(WorldBand world) => world == WorldBand.Office
            ? new[] { "COURIER", "INTERVIEW", "ELDERLY" }
            : world == WorldBand.Candy ? new[] { "PREGNANT", "BOSS", "GROUP" }
            : new[] { "HANDYMAN", "RUST", "REPAIR" };

        public static string DimensionIntroTitle(WorldBand world) => world == WorldBand.Office ? "OFFICE"
            : world == WorldBand.Candy ? "COTTON CANDY" : "UNDERWATER";

        public static Color DimensionIntroAccent(WorldBand world) => world == WorldBand.Office
            ? new Color32(98, 132, 178, 255) : world == WorldBand.Candy
                ? new Color32(236, 118, 188, 255) : new Color32(48, 196, 210, 255);

        public static string DimensionIntroSummary(WorldBand world) => world == WorldBand.Office
            ? "Plan your route. Keep your passengers happy."
            : world == WorldBand.Candy ? "Bigger groups. Louder passengers. Faster rides."
            : "Watch the rust. Keep your elevator moving.";

        public static string[] DimensionIntroNames(WorldBand world) => world == WorldBand.Office
            ? new[] { "COURIER", "INTERVIEW", "GRANDMA" }
            : world == WorldBand.Candy ? new[] { "MOM + BABY", "THE BOSS", "THE TRIO" }
            : new[] { "HANDYMAN", "RUST", "REPAIR" };

        public static string[] DimensionIntroLines(WorldBand world) => world == WorldBand.Office
            ? new[] { "Takes two spaces. Leave room for the box.", "Short patience. Make this stop a priority.",
                "Wait for her to board. Slows the elevator." }
            : world == WorldBand.Candy
                ? new[] { "Crying drains patience. Grandma calms the baby.", "On-time delivery pays a bonus.",
                    "Three spaces. Speeds the elevator up." }
                : new[] { "Clears rust while aboard.", "Slows the elevator over time.", "Hold repair to patch it back up." };

        void PrepareDimensionLineup(WorldBand world)
        {
            ClearDimensionLineup();
            // The shared round preview also uses this car in CPU matchups.
            if (sceneView == null || sceneView.passengers == null) return;
            string[] kinds = DimensionPassengerKinds(world);
            bool any = false;
            EnsureDimensionLineup();
            for (int i = 0; i < LineupSlots; i++)
            {
                GameObject model = InstantiateLineupModel(kinds[i], dimensionLineupSlots[i]);
                if (model == null) continue;
                model.name = kinds[i] + " reveal preview";
                model.SetActive(true);
                MakeStillPreviewModel(model, Palette[(i + 2) % Palette.Length]);
                dimensionLineupFilled[i] = FitLineupModel(model, dimensionLineupSlots[i]);
                any |= dimensionLineupFilled[i];
            }
            if (dimensionLineupCamera == null) return;
            // Transparent, but tinted like the reveal row in case alpha is dropped.
            Color accent = DimensionIntroAccent(world);
            Color row = Color.Lerp(Ink, accent, .22f);
            dimensionLineupCamera.backgroundColor = new Color(row.r, row.g, row.b, 0f);
            dimensionLineupCamera.enabled = any;
        }

        // Rust and repair have no passenger, so they show the authored starfish gauge and shovel.
        GameObject InstantiateLineupModel(string kind, Transform slot)
        {
            if (kind == "RUST")
            {
                if (impairmentGaugePrefab == null) return null;
                StarfishImpairmentView gauge = Instantiate(impairmentGaugePrefab, slot, false);
                gauge.SetSeverity(3);
                // The starfish faces +Z (toward the cabin camera); the lineup camera looks from -Z.
                gauge.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                return gauge.gameObject;
            }
            if (kind == "REPAIR")
            {
                if (handymanShovelPrefab == null) return null;
                Transform shovel = Instantiate(handymanShovelPrefab, slot, false);
                shovel.localRotation = Quaternion.Euler(0f, 0f, -25f);
                return shovel.gameObject;
            }
            GameObject source = FindPassengerTemplate(kind);
            return source != null ? Instantiate(source, slot, false) : null;
        }

        GameObject FindPassengerTemplate(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            foreach (PassengerView passenger in sceneView.passengers)
                if (passenger != null && passenger.ResolvedKind == kind) return passenger.gameObject;
            return null;
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
            // Shares the encounter stage's preview layer and key light.
            EnsureEncounterPreview();
            var stageObject = new GameObject("Dimension passenger lineup") { hideFlags = HideFlags.HideAndDontSave };
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
                name = "Dimension passenger lineup",
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

        // Draws one passenger as a square inside a reveal row (both solo and 1v1 screens).
        public void DrawDimensionPassenger(Rect area, int slot, float alpha)
        {
            if (dimensionLineupTexture == null || slot < 0 || slot >= LineupSlots
                || !dimensionLineupFilled[slot]) return;
            float size = Mathf.Min(area.width, area.height);
            if (size < 8f) return;
            Rect square = new Rect(area.center.x - size * .5f, area.center.y - size * .5f, size, size);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(square, dimensionLineupTexture,
                new Rect((float)slot / LineupSlots, 0f, 1f / LineupSlots, 1f));
            GUI.color = old;
        }

        void DrawDimensionRevealOverlay(Rect view)
        {
            Styles();
            float contentAlpha = DimensionRevealAlpha(dimensionRevealTime, dimensionRevealExitTime);
            Panel(view, new Color(0f, 0f, 0f, .96f));
            if (contentAlpha <= .001f) return;

            WorldBand world = DimensionWorld(dimensionRevealIndex);
            Color accent = DimensionIntroAccent(world);
            float width = Mathf.Min(view.width - 52f, 980f);
            float height = Mathf.Min(view.height - 120f, 620f);
            Rect card = new Rect(view.x + (view.width - width) * .5f,
                view.y + (view.height - height) * .5f, width, height);
            Panel(new Rect(card.x + 10f, card.y + 12f, card.width, card.height),
                new Color(0f, 0f, 0f, .38f * contentAlpha));
            FramedPanel(card, new Color(accent.r, accent.g, accent.b, contentAlpha), 5f);

            if (DimensionRevealTexture != null)
            {
                Rect image = new Rect(card.x + 22f, card.y + 18f,
                    card.width - 44f, card.height * .26f);
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, contentAlpha);
                GUI.DrawTexture(image, DimensionRevealTexture, ScaleMode.ScaleToFit, true);
                GUI.color = old;
            }

            Color oldText = title.normal.textColor;
            int oldSize = title.fontSize;
            TextAnchor oldAlign = title.alignment;
            title.alignment = TextAnchor.MiddleCenter;
            title.fontSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .015f, 14f, 20f));
            title.normal.textColor = new Color(accent.r, accent.g, accent.b, contentAlpha);
            GUI.Label(new Rect(card.x + 24f, card.y + card.height * .30f, card.width - 48f, 26f),
                "ROUND " + (session != null ? session.RoundNumber : 1), title);
            title.fontSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .04f, 30f, 54f));
            title.normal.textColor = new Color(UiWhite.r, UiWhite.g, UiWhite.b, contentAlpha);
            GUI.Label(new Rect(card.x + 24f, card.y + card.height * .35f,
                card.width - 48f, 52f), DimensionIntroTitle(world), title);
            title.fontSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .014f, 13f, 18f));
            GUI.Label(new Rect(card.x + 24f, card.y + card.height * .435f,
                card.width - 48f, 28f), DimensionIntroSummary(world), title);

            string[] names = DimensionIntroNames(world);
            string[] lines = DimensionIntroLines(world);

            float rowTop = card.y + card.height * .50f;
            float rowWidth = (card.width - 72f) / 3f;
            int nameSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .02f, 16f, 26f));
            int lineSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .014f, 13f, 18f));
            for (int i = 0; i < 3; i++)
            {
                Rect row = new Rect(card.x + 24f + i * (rowWidth + 12f), rowTop, rowWidth, card.height * .36f);
                Panel(row, new Color(accent.r, accent.g, accent.b, .22f * contentAlpha));
                title.fontSize = nameSize;
                title.normal.textColor = new Color(UiWhite.r, UiWhite.g, UiWhite.b, contentAlpha);
                GUI.Label(new Rect(row.x + 8f, row.y + 10f, row.width - 16f, 32f), names[i], title);
                title.fontSize = lineSize;
                title.normal.textColor = new Color(accent.r, accent.g, accent.b, contentAlpha);
                GUI.Label(new Rect(row.x + 10f, row.y + 42f, row.width - 20f, 66f), lines[i], title);
                DrawDimensionPassenger(new Rect(row.x, row.y + 104f, row.width, row.height - 108f), i, contentAlpha);
            }

            if (dimensionRevealExitTime < 0f)
            {
                title.fontSize = nameSize;
                title.normal.textColor = new Color(Gold.r, Gold.g, Gold.b,
                    contentAlpha * DimensionPromptAlpha(dimensionRevealTime));
                GUI.Label(new Rect(card.x, rowTop + card.height * .36f + 4f, card.width,
                    card.yMax - rowTop - card.height * .36f - 8f), "PRESS " + GameplayConfirmKey + " TO CONTINUE", title);
            }

            title.fontSize = oldSize;
            title.normal.textColor = oldText;
            title.alignment = oldAlign;
        }
    }
}
