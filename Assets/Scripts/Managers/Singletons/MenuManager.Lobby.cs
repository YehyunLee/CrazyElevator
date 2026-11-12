using System.Collections.Generic;
using CrazyElevator.Shared;
using UnityEngine;

namespace CrazyElevator.Managers
{
    public sealed partial class MenuManager
    {
        static readonly string[] LobbyCharacters =
        {
            "COURIER", "BOUNCER", "DIVER", "FAIRY", "ASTRONAUT"
        };
        static readonly int[] lobbyCharacterSelection = { 0, 1 };
        static readonly Color LobbyPortraitBacking = new Color32(62, 49, 67, 255);
        static readonly Color LobbyShadow = new Color32(38, 29, 44, 255);
        readonly Dictionary<string, RenderTexture> lobbyPortraits = new Dictionary<string, RenderTexture>();
        readonly Dictionary<(PassengerView prefab, Color jacket), RenderTexture> manifestPortraits =
            new Dictionary<(PassengerView prefab, Color jacket), RenderTexture>();
        readonly int[] lobbyHeldDirection = new int[2];
        readonly int[] lobbyConfirmFrame = { -1, -1 };

        static void ResetLobbyCharacterSelection()
        {
            lobbyCharacterSelection[0] = 0;
            lobbyCharacterSelection[1] = 1;
        }

        bool IsLobbySeatReady(int seat) => seat == 0 ? playerOneReady : playerTwoReady;

        void ConfirmLobbySeat(int seat)
        {
            if (lobbyConfirmFrame[seat] == Time.frameCount) return;
            lobbyConfirmFrame[seat] = Time.frameCount;
            HandleLobbyConfirm(seat);
        }

        void UpdateLobbyCharacterNavigation(int seat, float horizontal)
        {
            int direction = Mathf.Abs(horizontal) >= .55f ? (horizontal < 0f ? -1 : 1) : 0;
            if (direction != 0 && direction != lobbyHeldDirection[seat])
                StepLobbyCharacter(seat, direction);
            lobbyHeldDirection[seat] = direction;
        }

        void StepLobbyCharacter(int seat, int direction)
        {
            if (launching || IsLobbySeatReady(seat)) return;
            int count = LobbyCharacters.Length;
            lobbyCharacterSelection[seat] = (lobbyCharacterSelection[seat] + direction + count) % count;
        }

        void UpdateLobbyPortraits()
        {
            for (int seat = 0; seat < lobbyCharacterSelection.Length; seat++)
            {
                string kind = LobbyCharacters[lobbyCharacterSelection[seat]];
                if (lobbyPortraits.ContainsKey(kind)) continue;
                PassengerView prefab = FindDirectoryPassenger(kind);
                if (prefab != null) lobbyPortraits.Add(kind, CapturePassengerPortrait(prefab, 320, LobbyPortraitBacking));
            }
        }

        internal RenderTexture GetManifestPortrait(PassengerView prefab, Material jacket)
        {
            var key = (prefab, jacket.color);
            if (manifestPortraits.TryGetValue(key, out RenderTexture portrait)) return portrait;

            portrait = CapturePassengerPortrait(prefab, 128, Color.clear, jacket);
            manifestPortraits.Add(key, portrait);
            return portrait;
        }

        // Capture a still from the same front-facing preview stage used by the guide.
        RenderTexture CapturePassengerPortrait(PassengerView prefab, int size, Color background,
            Material jacket = null)
        {
            EnsureDirectoryPreviewStage();
            PassengerView model = Instantiate(prefab, directoryPreviewStage, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, -14f, 0f);
            model.gameObject.SetActive(true);
            if (jacket != null && model.jackets != null)
                foreach (Renderer renderer in model.jackets)
                    if (renderer != null) renderer.sharedMaterial = jacket;
            foreach (TextMesh label in model.GetComponentsInChildren<TextMesh>(true))
                label.gameObject.SetActive(false);
            PrepareDirectoryPreviewModel(model.gameObject);
            FitDirectoryModel(model.transform, 2.06f, 2.08f, centerHorizontally: true);

            var portrait = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32)
            {
                name = prefab.ResolvedKind + " passenger portrait",
                antiAliasing = 2,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            portrait.Create();
            RenderTexture previousTexture = directoryPreviewCamera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            Color previousBackground = directoryPreviewCamera.backgroundColor;
            try
            {
                directoryPreviewCamera.targetTexture = portrait;
                directoryPreviewCamera.backgroundColor = background;
                directoryPreviewCamera.Render();
            }
            finally
            {
                directoryPreviewCamera.targetTexture = previousTexture;
                directoryPreviewCamera.backgroundColor = previousBackground;
                RenderTexture.active = previousActive;
                model.gameObject.SetActive(false);
                Destroy(model.gameObject);
            }
            return portrait;
        }

        void ReleasePassengerPortraits()
        {
            foreach (RenderTexture portrait in lobbyPortraits.Values)
                Destroy(portrait);
            foreach (RenderTexture portrait in manifestPortraits.Values)
                Destroy(portrait);
            lobbyPortraits.Clear();
            manifestPortraits.Clear();
        }

        void DrawFixedTwoPlayerLobby()
        {
            Rect view = new Rect(0f, 0f, ArtWidth, ArtHeight);
            GUI.BeginGroup(view);
            MenuPresentation.Backdrop(view);
            if (!launching && DrawLobbyBack())
            {
                GUI.EndGroup();
                choosingMode = false;
                transition = 0f;
                playerOneReady = playerTwoReady = false;
                SyncTitleButtons();
                return;
            }

            Rect header = new Rect(540f, 144f, 620f, 78f);
            MenuPresentation.Plaque(header, MenuPresentation.Paper, MenuPresentation.Metal, 8f);
            MenuPresentation.Fasteners(header, MenuPresentation.Metal);
            DrawLabel(header, "CHOOSE CHARACTER", menuLabelText, MenuPresentation.Ink);
            float launch = launching ? Mathf.Clamp01(launchTimer / LaunchSeconds) : 0f;
            float descent = launch * launch * (ArtHeight + 150f);
            DrawLobbySeat(0, playerOneButton, playerOneReadyButton, MenuPresentation.Coral, descent);
            DrawLobbySeat(1, rivalButton, playerTwoReadyButton, MenuPresentation.Teal, descent);

            Rect versus = new Rect(814f, 497f, 72f, 58f);
            MenuPresentation.Plaque(versus, MenuPresentation.Brass, MenuPresentation.Metal);
            DrawLabel(versus, "VS", readyLabelText, MenuPresentation.Ink);
            MenuPresentation.Plaque(startButton,
                launching ? MenuPresentation.Brass : LobbyPortraitBacking, LobbyShadow);
            DrawLabel(startButton, launching ? "START" : "WAITING", readyLabelText,
                launching ? MenuPresentation.Ink : MenuPresentation.Paper);

            if (launching)
                MenuPresentation.Paint(view, new Color(0f, 0f, 0f, Mathf.Clamp01((launch - .45f) * 1.82f)));
            GUI.EndGroup();
        }

        bool DrawLobbyBack()
        {
            MenuPresentation.Plaque(backButton, MenuPresentation.Paper, MenuPresentation.Metal);
            DrawLabel(backButton, "BACK", readyLabelText, MenuPresentation.Ink);
            DrawKeyHint(new Rect(backButton.xMax + 14f, backButton.y + 2f, 54f, 54f),
                KeyboardHintsEnabled ? "ESC" : "B");
            return GUI.Button(backButton, GUIContent.none, GUIStyle.none);
        }

        void DrawLobbySeat(int seat, Rect card, Rect confirm, Color accent, float descent)
        {
            card.y += descent;
            confirm.y += descent;
            MenuPresentation.Plaque(card, MenuPresentation.Brass, MenuPresentation.Metal, 10f);
            MenuPresentation.Paint(Expand(card, -10f), LobbyShadow);
            MenuPresentation.Fasteners(card, MenuPresentation.Metal);

            Rect seatPlate = new Rect(card.center.x - 124f, card.y - 25f, 248f, 54f);
            MenuPresentation.Plaque(seatPlate, accent, MenuPresentation.Metal);
            DrawLabel(seatPlate, seat == 0 ? "PLAYER 1" : "PLAYER 2", readyLabelText, MenuPresentation.Ink);

            string character = LobbyCharacters[lobbyCharacterSelection[seat]];
            float portraitSize = Mathf.Max(1f, Mathf.Min(card.width - 156f, confirm.y - card.y - 106f));
            Rect portrait = new Rect(card.center.x - portraitSize * .5f, card.y + 44f,
                portraitSize, portraitSize);
            MenuPresentation.Paint(Expand(portrait, 4f), MenuPresentation.Metal);
            MenuPresentation.Paint(portrait, LobbyPortraitBacking);
            if (lobbyPortraits.TryGetValue(character, out RenderTexture texture))
                MenuPresentation.DrawTexture(portrait, texture, ScaleMode.ScaleToFit);
            Rect name = new Rect(portrait.x - 24f, portrait.yMax + 6f, portrait.width + 48f, 47f);
            DrawLabel(name, character, cardTitle, MenuPresentation.Paper);

            bool ready = IsLobbySeatReady(seat);
            if (!ready && !launching)
            {
                Rect left = new Rect(card.x + 24f, portrait.center.y - 29f, 54f, 58f);
                Rect right = new Rect(card.xMax - 78f, left.y, left.width, left.height);
                DrawLobbyArrow(left, seat, -1, accent);
                DrawLobbyArrow(right, seat, 1, accent);
            }

            MenuPresentation.Plaque(confirm, ready ? accent : MenuPresentation.Paper, MenuPresentation.Metal);
            DrawLabel(confirm, ready ? "READY" : "CONFIRM", readyLabelText, MenuPresentation.Ink);
            if (launching) return;
            string key = KeyboardHintsEnabled ? (seat == 0 ? "C" : ".") : "A";
            DrawKeyHint(new Rect(confirm.xMax - 66f, confirm.center.y - 27f, 54f, 54f), key);
            if (GUI.Button(confirm, GUIContent.none, GUIStyle.none))
                ConfirmLobbySeat(seat);
        }

        void DrawLobbyArrow(Rect rect, int seat, int direction, Color accent)
        {
            MenuPresentation.Plaque(rect, accent, MenuPresentation.Metal, 4f);
            if (direction < 0) DrawChoiceArrow(rect, false, MenuPresentation.Ink, ref leftChoiceArrowTexture);
            else DrawChoiceArrow(rect, true, MenuPresentation.Ink, ref rightChoiceArrowTexture);
            if (KeyboardHintsEnabled)
            {
                string key = seat == 0 ? (direction < 0 ? "A" : "D") : (direction < 0 ? "←" : "→");
                DrawKeyHint(new Rect(rect.center.x - 18f, rect.yMax + 6f, 36f, 32f), key);
            }
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) StepLobbyCharacter(seat, direction);
        }
    }
}
