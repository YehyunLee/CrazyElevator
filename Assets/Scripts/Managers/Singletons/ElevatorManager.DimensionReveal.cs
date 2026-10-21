using CrazyElevator.Shared;
using UnityEngine;

namespace CrazyElevator.Managers
{
    public sealed partial class ElevatorManager
    {
        public const float DimensionRevealSeconds = 6.8f;
        const float DimensionDoorCloseSeconds = .85f;
        bool dimensionRevealActive;
        float dimensionRevealTime;
        int dimensionRevealIndex = -1;
        readonly bool[] dimensionRevealed = new bool[ElevatorRound.Dimensions];
        Phase dimensionResumePhase;
        float dimensionResumePhaseTime;
        float dimensionResumeDoors;

        public bool DimensionRevealActive => dimensionRevealActive;
        public int DimensionRevealIndex => dimensionRevealIndex;
        public float DimensionRevealProgress => Mathf.Clamp01(dimensionRevealTime / DimensionRevealSeconds);
        public static WorldBand DimensionWorld(int index)
        {
            return index <= 0 ? WorldBand.Office
                : index == 1 ? WorldBand.Candy : WorldBand.Water;
        }

        public void BeginDimensionReveal(int index)
        {
            index = Mathf.Clamp(index, 1, ElevatorRound.Dimensions - 1);
            if (dimensionRevealActive) return;
            dimensionRevealed[index] = true;
            dimensionRevealIndex = index;
            dimensionRevealTime = 0f;
            dimensionResumePhase = phase;
            dimensionResumePhaseTime = phaseTime;
            dimensionResumeDoors = doors;
            dimensionRevealActive = true;
            MarkDimensionPassengersIntroduced(index);
            PrepareDimensionPreview(DimensionWorld(index));
            notice = DimensionWorld(index) == WorldBand.Candy
                ? "NEW MULTIVERSE: COTTON CANDY"
                : "NEW MULTIVERSE: UNDERWATER";
        }

        void MarkDimensionPassengersIntroduced(int index)
        {
            // Those passenger types are covered by the dimension interlude, so
            // their separate "NEW PASSENGER" cards must not appear later.
            if (index == 1)
            {
                completedPassengerTutorials[Seat].Add("PREGNANT");
                completedPassengerTutorials[Seat].Add("BOSS");
                completedPassengerTutorials[Seat].Add("GROUP");
                completedWorldTutorials[Seat].Add(WorldBand.Candy);
            }
            else if (index == 2)
            {
                completedPassengerTutorials[Seat].Add("HANDYMAN");
                completedWorldTutorials[Seat].Add(WorldBand.Water);
            }
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
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
        }

        public void ResetDimensionReveal()
        {
            dimensionRevealActive = false;
            dimensionRevealIndex = -1;
            dimensionRevealTime = 0f;
            for (int i = 0; i < dimensionRevealed.Length; i++) dimensionRevealed[i] = false;
            ClearEncounterPreviewModel();
        }

        void TickDimensionReveal(float dt)
        {
            dimensionRevealTime += Mathf.Max(0f, dt);
            float close = Mathf.Clamp01(dimensionRevealTime / DimensionDoorCloseSeconds);
            SetDoors(Mathf.Lerp(dimensionResumeDoors, 0f, close));
            // Solo mode has no shared match clock to drive the hand-off.
            if (Match == null && dimensionRevealTime >= DimensionRevealSeconds)
                FinishDimensionReveal();
        }

        bool TryBeginSoloDimensionReveal()
        {
            if (round == null) return false;
            for (int index = 1; index < ElevatorRound.Dimensions; index++)
            {
                if (dimensionRevealed[index]
                    || round.Elapsed < ElevatorRound.DimensionUnlockInterval * index) continue;
                BeginDimensionReveal(index);
                return true;
            }
            return false;
        }

        void DrawDimensionRevealOverlay(Rect view)
        {
            Styles();
            float time = dimensionRevealTime;
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / .85f));
            float fadeOut = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((DimensionRevealSeconds - time) / .85f));
            float contentAlpha = Mathf.Min(fadeIn, fadeOut);
            Panel(view, new Color(0f, 0f, 0f, .96f));
            if (contentAlpha <= .001f) return;

            WorldBand world = DimensionWorld(dimensionRevealIndex);
            Color accent = world == WorldBand.Candy
                ? new Color32(236, 118, 188, 255)
                : new Color32(48, 196, 210, 255);
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
                    card.width - 44f, card.height * .42f);
                Color old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, contentAlpha);
                GUI.DrawTexture(image, DimensionRevealTexture, ScaleMode.ScaleToFit, true);
                GUI.color = old;
            }

            Color oldText = title.normal.textColor;
            int oldSize = title.fontSize;
            TextAnchor oldAlign = title.alignment;
            title.fontSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .04f, 30f, 54f));
            title.alignment = TextAnchor.MiddleCenter;
            title.normal.textColor = new Color(UiWhite.r, UiWhite.g, UiWhite.b, contentAlpha);
            GUI.Label(new Rect(card.x + 24f, card.y + card.height * .52f,
                card.width - 48f, 52f), world == WorldBand.Candy ? "COTTON CANDY" : "UNDERWATER", title);

            string[] names;
            string[] lines;
            if (world == WorldBand.Candy)
            {
                names = new[] { "MOM + BABY", "THE BOSS", "THE TRIO" };
                lines = new[]
                {
                    "Crying drains everyone's patience.",
                    "On-time delivery pays a bonus.",
                    "Three spaces. Speeds the elevator up."
                };
            }
            else
            {
                names = new[] { "HANDYMAN", "RUST", "REPAIR" };
                lines = new[]
                {
                    "Clears rust while aboard.",
                    "Slows the elevator over time.",
                    "Hold to patch it back up."
                };
            }

            float rowTop = card.y + card.height * .64f;
            float rowWidth = (card.width - 72f) / 3f;
            int nameSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .02f, 16f, 26f));
            int lineSize = Mathf.RoundToInt(Mathf.Clamp(view.width * .014f, 13f, 18f));
            for (int i = 0; i < 3; i++)
            {
                Rect row = new Rect(card.x + 24f + i * (rowWidth + 12f), rowTop, rowWidth, card.height * .28f);
                Panel(row, new Color(accent.r, accent.g, accent.b, .22f * contentAlpha));
                title.fontSize = nameSize;
                title.normal.textColor = new Color(UiWhite.r, UiWhite.g, UiWhite.b, contentAlpha);
                GUI.Label(new Rect(row.x + 8f, row.y + 10f, row.width - 16f, 32f), names[i], title);
                title.fontSize = lineSize;
                title.normal.textColor = new Color(accent.r, accent.g, accent.b, contentAlpha);
                GUI.Label(new Rect(row.x + 10f, row.y + 44f, row.width - 20f, row.height - 52f), lines[i], title);
            }

            title.fontSize = oldSize;
            title.normal.textColor = oldText;
            title.alignment = oldAlign;
        }
    }
}
