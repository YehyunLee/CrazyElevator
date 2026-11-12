using CrazyElevator.Shared;
using UnityEngine;

namespace CrazyElevator.Managers
{
    public sealed partial class MenuManager
    {
        const string TitlePreviewKey = "Title passengers";
        static readonly Rect[] TitleButtonRects =
        {
            new Rect(140, 522, 350, 88),
            new Rect(568, 585, 350, 88),
            new Rect(140, 659, 350, 88),
            new Rect(568, 722, 350, 88),
            new Rect(140, 796, 350, 88)
        };
        static readonly string[] TitleActions = { "START", "DIMENSION POOL", "GUIDE", "OPTIONS", "EXIT" };
        static readonly Color[] TitleActionColours =
        {
            MenuPresentation.Brass, new Color32(128, 188, 221, 255), MenuPresentation.Coral,
            new Color32(145, 190, 119, 255), new Color32(183, 150, 215, 255)
        };
        static readonly Color DoorwayGold = new Color32(255, 198, 103, 255);
        static readonly Color DoorwayEdge = new Color32(139, 72, 49, 255);
        static readonly Color DoorwayShade = new Color32(47, 33, 41, 255);
        [System.NonSerialized] GUIStyle titleWordmark, titleActionText, titleIndicator;

        void DrawModeSelect()
        {
            Rect screen = new Rect(0f, 0f, Screen.width, Screen.height);
            MenuPresentation.Paint(screen, MenuPresentation.Wall);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = MenuPresentation.Fit(screen, ArtWidth, ArtHeight);

            float transitionAmount = choosingMode ? Mathf.Clamp01(transition / TransitionSeconds) : 0f;
            if (transitionAmount >= 1f)
                DrawFixedTwoPlayerLobby();
            else
                DrawTitleScreen(transitionAmount);

            GUI.matrix = previous;
        }

        void DrawTitleScreen(float transitionAmount)
        {
            Rect art = new Rect(0, 0, ArtWidth, ArtHeight);
            MenuPresentation.Backdrop(art);
            MenuPresentation.Paint(new Rect(0, 908, ArtWidth, 174), new Color32(66, 49, 54, 255));
            MenuPresentation.Paint(new Rect(0, 908, ArtWidth, 7), new Color32(99, 71, 66, 255));
            MenuPresentation.Paint(new Rect(0, 1003, ArtWidth, 3), new Color32(56, 42, 48, 255));

            if (titleWordmark == null)
            {
                titleActionText = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 33
                };
                GameTypography.Apply(titleActionText, true);
                titleWordmark = new GUIStyle(titleActionText) { alignment = TextAnchor.MiddleLeft };
                titleIndicator = new GUIStyle(titleActionText) { fontSize = 55 };
            }

            DrawTitleWord(new Rect(122, 161, 826, 173), "CRAZY", 157, MenuPresentation.Teal);
            DrawTitleWord(new Rect(122, 330, 866, 123), "ELEVATORS", 105, MenuPresentation.Brass);
            DrawTitleElevator(transitionAmount);
            DrawTitlePostcard(new Rect(917, 173, 166, 129), WorldBand.Office, -8f);
            DrawTitlePostcard(new Rect(944, 326, 150, 117), WorldBand.Candy, 7f);
            DrawTitlePostcard(new Rect(1470, 774, 152, 119), WorldBand.Water, 8f);
            DrawTitlePostcard(new Rect(937, 475, 155, 121), WorldBand.Forest, -7f);
            DrawTitlePostcard(new Rect(1468, 112, 162, 126), WorldBand.Space, 9f);
            DrawTitleNavigation();

            if (transitionAmount > 0f)
                MenuPresentation.Paint(art, new Color(0f, 0f, 0f,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.32f, 1f, transitionAmount))));
        }

        void DrawTitleWord(Rect rect, string word, int size, Color edge)
        {
            titleWordmark.fontSize = size;
            Rect shadow = rect;
            shadow.position += new Vector2(7f, 13f);
            DrawLabel(shadow, word, titleWordmark, DoorwayShade);
            shadow.position -= new Vector2(5f, 5f);
            DrawLabel(shadow, word, titleWordmark, edge);
            DrawLabel(rect, word, titleWordmark, MenuPresentation.Paper);
        }

        void DrawTitleNavigation()
        {
            for (int i = 0; i < TitleButtonRects.Length; i++)
            {
                Rect rect = TitleButtonRects[i];
                Color accent = TitleActionColours[i];
                bool selected = i == titleSelection;
                if (selected) MenuPresentation.Paint(Expand(rect, 5f), MenuPresentation.Paper);
                Color edge = Color.Lerp(accent, MenuPresentation.Ink, .44f);
                MenuPresentation.Plaque(rect, accent, edge, selected ? 10f : 7f);
                Rect face = new Rect(rect.x + 7f, rect.y + 6f, rect.width - 14f, rect.height - 12f);
                MenuPresentation.Paint(face, Color.Lerp(accent, MenuPresentation.Paper, selected ? .82f : .67f));
                MenuPresentation.Fasteners(rect, edge);
                DrawLabel(rect, TitleActions[i], titleActionText, MenuPresentation.Ink);
            }
            if (choosingMode) return;

            DrawTitleActionHint();
            for (int i = 0; i < TitleButtonRects.Length; i++)
            {
                if (!GUI.Button(TitleButtonRects[i], GUIContent.none, GUIStyle.none)) continue;
                titleSelection = i;
                ActivateTitleSelection();
                break;
            }
        }

        void DrawTitleElevator(float transitionAmount)
        {
            Rect frame = new Rect(1035, 205, 556, 678);
            Rect bay = new Rect(1067, 280, 492, 535);
            MenuPresentation.Paint(new Rect(1048, 218, 556, 678), DoorwayShade);
            MenuPresentation.Plaque(frame, MenuPresentation.Brass, DoorwayEdge, 14f);
            MenuPresentation.Paint(new Rect(frame.x + 8f, frame.y + 8f, frame.width - 16f, 12f), DoorwayGold);
            MenuPresentation.Fasteners(frame, DoorwayEdge);

            MenuPresentation.Paint(bay, DoorwayShade);
            MenuPresentation.Paint(new Rect(bay.x + 13f, bay.y + 14f, bay.width - 26f, bay.height - 29f),
                new Color32(117, 75, 65, 255));
            MenuPresentation.Paint(new Rect(bay.x + 23f, bay.y + 24f, bay.width - 46f, 140),
                new Color32(148, 96, 72, 255));
            MenuPresentation.Paint(new Rect(bay.x + 13f, bay.yMax - 89f, bay.width - 26f, 74),
                new Color32(165, 117, 81, 255));
            MenuPresentation.Paint(new Rect(bay.x + 28f, bay.y + 30f, bay.width - 56f, 8f), MenuPresentation.Paper);

            if (directoryPreviewKind == TitlePreviewKey && directoryPreviewTexture != null)
            {
                MenuPresentation.DrawTexture(new Rect(bay.x + 9f, bay.y + 46f, bay.width - 18f, bay.width - 18f),
                    directoryPreviewTexture, ScaleMode.ScaleToFit);
            }
            DrawTitleDoors(bay, transitionAmount);

            Rect sign = new Rect(1231, 163, 164, 96);
            MenuPresentation.Plaque(sign, DoorwayEdge, DoorwayShade, 9f);
            Rect display = new Rect(sign.x + 10f, sign.y + 9f, sign.width - 20f, sign.height - 18f);
            MenuPresentation.Paint(display, MenuPresentation.Ink);
            DrawLabel(display, "↑", titleIndicator, new Color32(194, 232, 139, 255));

            Rect sill = new Rect(bay.x, bay.yMax, bay.width, 30f);
            MenuPresentation.Plaque(sill, DoorwayGold, DoorwayEdge);
            for (int stripe = 0; stripe < 8; stripe++)
                MenuPresentation.Paint(new Rect(sill.x + 10f + stripe * 60f, sill.y + 8f, 25f, 13f), DoorwayEdge);
            MenuPresentation.Plaque(new Rect(1140, 919, 348, 58), MenuPresentation.Teal, DoorwayShade);
        }

        void DrawTitleDoors(Rect bay, float transitionAmount)
        {
            if (transitionAmount <= 0f) return;
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(transitionAmount * 1.65f));
            float width = bay.width * .5f * slide;
            var left = new Rect(bay.x, bay.y, width, bay.height);
            var right = new Rect(bay.xMax - width, bay.y, width, bay.height);
            Color door = new Color32(167, 104, 65, 255);
            MenuPresentation.Paint(left, door);
            MenuPresentation.Paint(right, door);
            float seam = Mathf.Min(width, 5f);
            MenuPresentation.Paint(new Rect(left.xMax - seam, bay.y, seam, bay.height), DoorwayGold);
            MenuPresentation.Paint(new Rect(right.x, bay.y, seam, bay.height), DoorwayEdge);
        }

        void DrawTitlePostcard(Rect card, WorldBand world, float angle)
        {
            int dimension = (int)world;
            if (dimensionGuideImages == null || dimension >= dimensionGuideImages.Length
                || dimensionGuideImages[dimension] == null) return;
            Matrix4x4 previous = GUI.matrix;
            Vector3 pivot = card.center;
            GUI.matrix = previous * Matrix4x4.Translate(pivot)
                * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle)) * Matrix4x4.Translate(-pivot);
            MenuPresentation.Plaque(card, MenuPresentation.Paper, DoorwayShade, 5f);
            MenuPresentation.DrawTexture(new Rect(card.x + 8f, card.y + 8f, card.width - 16f, card.height - 24f),
                dimensionGuideImages[dimension], ScaleMode.ScaleAndCrop);
            MenuPresentation.Paint(new Rect(card.center.x - 5f, card.y - 4f, 10f, 13f), DoorwayGold);
            GUI.matrix = previous;
        }

        void SyncTitlePreview()
        {
            bool show = IsOpen && !showingControllerInstructions && !showingDimensionPool
                && !showingPassengerGuide && !showingOptions && (!choosingMode || transition < TransitionSeconds);
            if (!show)
            {
                if (directoryPreviewKind == TitlePreviewKey) ClearDirectoryPreviewModel();
                return;
            }
            if (directoryPreviewKind == TitlePreviewKey) return;

            ClearDirectoryPreviewModel();
            EnsureDirectoryPreviewStage();
            directoryPreviewModel = new GameObject(TitlePreviewKey) { hideFlags = HideFlags.HideAndDontSave };
            directoryPreviewModel.transform.SetParent(directoryPreviewStage, false);
            directoryPreviewKind = TitlePreviewKey;

            AddTitlePassenger("ASTRONAUT", new Vector3(-.59f, .46f, .38f), 1.15f, -13f);
            AddTitlePassenger("FAIRY", new Vector3(.58f, .51f, .37f), 1.29f, 4f);
            AddTitlePassenger("COURIER", new Vector3(-.60f, -.49f, 0f), 1.45f, 10f);
            AddTitlePassenger("GROUP", new Vector3(.60f, -.46f, .04f), 1.42f, -8f);
            directoryPreviewCamera.enabled = true;
        }

        void AddTitlePassenger(string kind, Vector3 offset, float height, float yaw)
        {
            PassengerView source = FindDirectoryPassenger(kind);
            if (source == null) return;
            GameObject model = Instantiate(source.gameObject, directoryPreviewModel.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.SetActive(true);
            foreach (TextMesh label in model.GetComponentsInChildren<TextMesh>(true))
                label.gameObject.SetActive(false);
            PrepareDirectoryPreviewModel(model);
            FitDirectoryModel(model.transform, height, 1.12f);
            model.transform.localPosition += offset;
        }
    }
}
