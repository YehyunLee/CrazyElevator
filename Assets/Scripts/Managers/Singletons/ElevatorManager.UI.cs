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
            rewardPoints, rewardCaption, calloutNumber, calloutText;
        static readonly Color UiBlack = new Color32(9, 11, 16, 255);
        static readonly Color UiWhite = new Color32(255, 252, 241, 255);

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
            calloutNumber = new GUIStyle(buttonStyle) { fontSize = 31, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GameTypography.Apply(calloutNumber, true);
            calloutNumber.normal.textColor = UiWhite;
            calloutText = new GUIStyle(buttonStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip };
            GameTypography.Apply(calloutText, true);
            calloutText.normal.textColor = UiWhite;
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
            // Overlap the left edge by one pixel so fractional screen-space
            // rectangles cannot leave a pale seam between the accent and panel.
            float left = Mathf.Floor(r.x) - 1f;
            float width = Mathf.Ceil(r.xMax) - left;
            Panel(new Rect(left, r.y, width, r.height), UiBlack);
            Panel(new Rect(left, r.yMax - 4f, width, 4f), accent);
            AccentBar(new Rect(left, r.y + 2f, 10f, r.height - 6f), accent);
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
            Color previous = style.normal.textColor;
            style.normal.textColor = UiBlack;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0)
                        GUI.Label(new Rect(rect.x + x, rect.y + y, rect.width, rect.height), value, style);
            style.normal.textColor = UiWhite;
            GUI.Label(rect, value, style);
            style.normal.textColor = previous;
        }
        // Draw a button and restore shared GUI state.
        bool Button(Rect r, string text, Color color, bool enabled = true)
        {
            bool was = GUI.enabled; GUI.enabled = was && enabled;
            Panel(r, GUI.enabled ? color : new Color(.2f, .25f, .3f));
            var old = buttonStyle.normal.textColor; buttonStyle.normal.textColor = GUI.enabled ? Ink : Color.gray;
            bool pressed = GUI.Button(r, text, buttonStyle); buttonStyle.normal.textColor = old; GUI.enabled = was; return pressed;
        }

        // Summarize a passenger type for the UI.
        string ShortRequest(Rider p)
        {
            if (p.Kind == "HANDYMAN") return "Full power while aboard";
            if (p.Kind == "COURIER") return "2 spaces • 1 floor";
            if (p.Kind == "PREGNANT") return "2 spaces";
            if (p.Kind == "INTERVIEW") return "Very urgent";
            if (p.Kind == "BOSS") return "Hold OPEN bonus";
            if (p.Kind == "ELDERLY") return "Slow • big bonus";
            return "3 spaces together";
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
            if (phase == Phase.Welcome || phase == Phase.Tutorial || phase == Phase.Results || paused)
            {
                scale = Mathf.Min(Screen.width / 1440f, Screen.height / 900f);
                offsetX = (Screen.width - 1440 * scale) / 2; offsetY = (Screen.height - 900 * scale) / 2;
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
            if (phase != Phase.Welcome && phase != Phase.Tutorial && phase != Phase.Results && !paused)
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

                float bannerWidth = Mathf.Min(width * .82f, 230f * uiScale);
                Rect banner = new Rect(centerX - bannerWidth * .5f, centerY + 14f * uiScale,
                    bannerWidth, 30f * uiScale);
                Panel(banner, new Color(Ink.r, Ink.g, Ink.b, alpha * .9f));
                Panel(new Rect(banner.x, banner.y, 5f * uiScale, banner.height),
                    new Color(accent.r, accent.g, accent.b, alpha));
                rewardCaption.normal.textColor = new Color(Cream.r, Cream.g, Cream.b, alpha);
                string caption = popup.Combo > 1 ? "QUICK COMBO  x" + popup.Combo
                    : popup.Happy ? "PERFECT DROP!" : "RIGHT FLOOR!";
                GUI.Label(banner, caption, rewardCaption);

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

            Panel(new Rect(x - 5, y - 5, totalWidth + 10, 58), UiWhite);
            Panel(new Rect(x - 2, y - 2, totalWidth + 4, 52), UiBlack);
            float cursor = x;
            DrawHudTile(new Rect(cursor, y, 148, 48), WorldAccent(round.Floor),
                WorldName(round.Floor), "F" + round.Floor); cursor += 152;
            DrawHudTile(new Rect(cursor, y, 108, 48), Gold, "LOAD", round.Load + "/" + ElevatorRound.Capacity); cursor += 112;
            DrawHudTile(new Rect(cursor, y, 108, 48), Teal, "SCORE", round.Score.ToString()); cursor += 112;
            DrawHudTile(new Rect(cursor, y, 108, 48), Coral, "HAPPY", round.Happy.ToString()); cursor += 112;
            DrawHudTile(new Rect(cursor, y, 148, 48), Sky, "TIME", clock);
        }

        // Screen-space passenger tags stay crisp and never disappear behind
        // the 3D models. Only one rider speaks at a time to keep the cabin calm.
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
                if (rider.Boarded && (rider.Remaining <= rider.Patience * .25f
                    || rider.Destination == round.Floor || !string.IsNullOrEmpty(rider.Status)))
                { speaker = rider; break; }
            if (speaker == null && selectedRider != null && visible.Contains(selectedRider)) speaker = selectedRider;

            var occupiedBadges = new List<Rect>();
            foreach (Rider rider in visible)
            {
                // The character and its world-space patience meter animate in
                // different Unity phases. Hide head UI during the brief walk
                // instead of letting the layers visibly trail one another.
                if (boardingTransfers.ContainsKey(rider)) continue;
                if (!figures.TryGetValue(rider, out Transform figure)) continue;
                Vector3 point = eye.WorldToScreenPoint(figure.position + Vector3.up * 2.08f);
                if (point.z <= 0) continue;
                float localX = point.x - view.x;
                float localY = view.y + view.height - point.y;
                float x = Mathf.Round(Mathf.Clamp(localX, 30f, viewWidth - 30f));
                float y = Mathf.Round(Mathf.Clamp(localY, 82f, viewHeight - 94f));
                Color accent = Palette[rider.Color % Palette.Length];

                Rect badge = new Rect(x - 17f, y - 16f, 34f, 34f);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    bool overlaps = false;
                    foreach (Rect occupied in occupiedBadges)
                        if (badge.Overlaps(occupied)) { overlaps = true; break; }
                    if (!overlaps) break;
                    badge.y = Mathf.Max(84f, badge.y - 42f);
                }
                occupiedBadges.Add(badge);
                OutlinedLabel(badge, rider.Destination.ToString(), calloutNumber);

                if (rider != speaker) continue;
                string message = PassengerCalloutText(rider);
                if (string.IsNullOrEmpty(message)) continue;
                float width = Mathf.Clamp(76f + message.Length * 5f, 120f, 176f);
                Rect quote = new Rect(Mathf.Clamp(x - width * .5f, 12f, viewWidth - width - 12f),
                    badge.y - 33f, width, 27f);
                AngularPanel(quote, accent);
                GUI.Label(new Rect(quote.x + 9f, quote.y - 1f, quote.width - 15f, quote.height - 2f), message, calloutText);
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
                if (!rider.Boarded || rider.Resolved || exiting.ContainsKey(rider)
                    || boardingTransfers.ContainsKey(rider)) continue;
                if (!figures.TryGetValue(rider, out Transform figure) || !figure.gameObject.activeInHierarchy) continue;

                Vector3 point = eye.WorldToScreenPoint(figure.position + Vector3.up * 2.08f);
                if (point.z <= 0) continue;
                float x = Mathf.Round(Mathf.Clamp(point.x, preview.xMin + 18f, preview.xMax - 18f));
                float y = Mathf.Round(Mathf.Clamp(Screen.height - point.y, preview.yMin + 30f, preview.yMax - 22f));
                Rect badge = new Rect(x - 18f, y - 17f, 36f, 34f);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool overlaps = false;
                    foreach (Rect occupied in occupiedBadges)
                        if (badge.Overlaps(occupied)) { overlaps = true; break; }
                    if (!overlaps) break;
                    badge.y = Mathf.Max(preview.yMin + 28f, badge.y - 30f);
                }
                occupiedBadges.Add(badge);
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
            if (pressed) Panel(new Rect(rect.x + 6f, rect.y + 5f, rect.width - 10f, rect.height - 12f), accent);
            Color previous = buttonStyle.normal.textColor;
            buttonStyle.normal.textColor = pressed ? UiBlack : UiWhite;
            Label(rect, text, buttonStyle);
            buttonStyle.normal.textColor = previous;
        }

        void DrawHudTile(Rect area, Color color, string caption, string value)
        {
            Panel(area, UiBlack);
            Panel(new Rect(area.x, area.yMax - 5f, area.width, 5f), color);
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

        // Show welcome, tutorial, pause, or final results.
        void Overlay()
        {
            Panel(new Rect(0, 104, 1440, 796), new Color(0, 0, 0, .82f));
            Panel(new Rect(338, 230, 780, 456), Color.black);
            Panel(new Rect(330, 222, 780, 456), UiBlack);
            Panel(new Rect(330, 222, 780, 7), Teal);
            AccentBar(new Rect(326, 246, 9, 382), Teal);
            string heading = paused ? "TAKE A BREATHER" : phase == Phase.Welcome ? "YOUR SHIFT. THEIR CHAOS." : phase == Phase.Tutorial ? "HOW TO PLAY" : "SHIFT COMPLETE";
            Label(new Rect(372, 256, 700, 52), heading, title);
            string copy;
            if (paused) copy = "The clock is paused.\n\nPress Start / Escape or resume when you are ready.";
            else if (phase == Phase.Welcome) copy = "3 minutes. Drop riders at their floors for points.\n\nOffice (0–3) → Candy (4–7) → Underwater (8–11).\n\nDrag to board or kick. CLOSE & TRAVEL, then hold UP/DOWN and STOP near a floor.";
            else if (phase == Phase.Tutorial) copy = "Three things to know.\n\nThe bold number above each rider is their destination floor.";
            else copy = "FINAL SCORE: " + round.Score
                + "\nHappy riders: " + round.Happy + "  •  Drop-offs: " + round.Delivered
                + "\nMissed riders: " + round.Missed + "  •  Turned away: " + round.TurnedAway
                + "\n\nYour 3-minute shift is over. Try again for a better mix!";
            Label(new Rect(374, 322, 690, 240), copy, body);
            if (phase == Phase.Tutorial)
            {
                Panel(new Rect(374, 418, 210, 112), UiWhite); Panel(new Rect(374, 418, 210, 7), Teal);
                Panel(new Rect(602, 418, 210, 112), UiWhite); Panel(new Rect(602, 418, 210, 7), Gold);
                Panel(new Rect(830, 418, 210, 112), UiWhite); Panel(new Rect(830, 418, 210, 7), Coral);
                Label(new Rect(392, 432, 174, 78), "1  BOARD\nDrag a rider inside.\nDrag onboard riders to move.", inkBody);
                Label(new Rect(620, 432, 174, 78), "2  TRAVEL\nClick CLOSE & TRAVEL.\nHold UP / DOWN to move.", inkBody);
                Label(new Rect(848, 432, 174, 78), "3  STOP / EJECT\nClick STOP near a floor.\nDrag rider out to eject.", inkBody);
                Label(new Rect(374, 542, 690, 48), "Office → Candy → Underwater. Shift+↑↓ builds speed. FIX handyman clears rust.", small);
            }
            string action = paused ? "RESUME SHIFT  /  CLICK, START or ESC"
                : phase == Phase.Welcome ? "SHOW ME HOW  /  CLICK or ENTER"
                : phase == Phase.Tutorial ? "START SHIFT  /  CLICK or ENTER" : "TRY AGAIN  /  CLICK or ENTER";
            if (Button(new Rect(374, 600, 692, 48), action, Teal))
            {
                if (paused)
                {
                    if (Match != null) Match.TogglePause();
                    else if (session != null) session.TogglePause();
                    else paused = false;
                }
                else StartOrContinue();
            }
        }

    }
}
