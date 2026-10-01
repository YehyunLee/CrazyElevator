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
        GUIStyle title, large, body, small, buttonStyle, inkBody, inkLarge, inkSmall, logo, stampWord;

        // Rebuild native GUI styles when returning to Play Mode without domain reload.
        void OnEnable() { body = null; }

        // Create reusable text styles on the first draw.
        void Styles()
        {
            if (body != null) return;
            body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = false };
            body.normal.textColor = Cream;
            small = new GUIStyle(body) { fontSize = 12 };
            title = new GUIStyle(body) { fontSize = 36, fontStyle = FontStyle.Bold };
            large = new GUIStyle(body) { fontSize = 24, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            inkBody = new GUIStyle(body); inkBody.normal.textColor = Ink;
            inkSmall = new GUIStyle(small); inkSmall.normal.textColor = Ink;
            inkLarge = new GUIStyle(large); inkLarge.normal.textColor = Ink;
            logo = new GUIStyle(body) { fontSize = 96, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            logo.normal.textColor = Cream;
            stampWord = new GUIStyle(body) { fontSize = 62, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            stampWord.normal.textColor = Coral;
        }
        // Draw a solid panel and restore the GUI tint.
        void Panel(Rect r, Color c) { var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old; }
        // Use the body style unless a custom style is supplied.
        void Label(Rect r, string text, GUIStyle style = null)
        {
            style = style ?? body;
            // Reapply after editor skin/domain changes, including Play Mode without domain reload.
            style.normal.textColor = style == inkBody || style == inkSmall || style == inkLarge ? Ink : Cream;
            GUI.Label(r, text, style);
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
            if (round == null || eye == null || Match != null) return;
            // Mode select owns the first screen. Don't paint the intro curtain over it —
            // Update also freezes introTime while the menu is open, which would stick on black.
            if (MenuManager.IsOpen) return;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GUI.enabled = true;
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
                GUI.matrix = Matrix4x4.identity;
                DrawMouseControls();
            }
        }

        // Match HUD is drawn by ElevatorMatch so the solo UI remains unchanged.
        public void DrawMatchViewGUI()
        {
            if (round == null || eye == null || IsNpc) return;
            Styles();
            DrawTravelView();
            Rect view = Match.ViewRect(Seat);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Translate(new Vector3(view.x, view.y, 0));
            DrawMouseControls();
            GUI.matrix = previous;
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
            float canvasWidth = Screen.width / Mathf.Max(1, Screen.height / 900f);
            const float totalWidth = 780f;
            const float y = 14f;
            float x = Mathf.Max(12f, (canvasWidth - totalWidth) * .5f);

            Panel(new Rect(x - 5, y - 5, totalWidth + 10, 58), new Color(Ink.r, Ink.g, Ink.b, .96f));
            float cursor = x;
            DrawHudTile(new Rect(cursor, y, 190, 48), WorldAccent(round.Floor),
                WorldName(round.Floor), "FLOOR " + round.Floor); cursor += 192;
            DrawHudTile(new Rect(cursor, y, 144, 48), Gold, "LOAD", round.Load + "/" + ElevatorRound.Capacity); cursor += 146;
            DrawHudTile(new Rect(cursor, y, 150, 48), new Color32(94, 188, 156, 255), "SCORE", round.Score.ToString()); cursor += 152;
            DrawHudTile(new Rect(cursor, y, 132, 48), Coral, "HAPPY", round.Happy.ToString()); cursor += 134;
            DrawHudTile(new Rect(cursor, y, 156, 48), Sky, "TIME LEFT", clock);
        }

        // Same gameplay can be driven with buttons when a keyboard/controller is unavailable.
        void GetMouseControlRects(out Rect up, out Rect down, out Rect stop,
            out Rect close, out Rect hold, out Rect repair)
        {
            up = down = stop = close = hold = repair = new Rect();
            if (!extendedInterior || paused) return;
            float width = Mathf.Clamp(ViewWidth * .24f, 138f, 190f);
            const float height = 44f, gap = 8f;
            float x = ViewWidth - width - 16f;
            if (phase == Phase.Moving)
            {
                float y = ViewHeight - 16f - height * 3 - gap * 2;
                up = new Rect(x, y, width, height);
                down = new Rect(x, y + height + gap, width, height);
                stop = new Rect(x, y + (height + gap) * 2, width, height);
            }
            else if (phase == Phase.Boarding)
            {
                bool showRepair = !FriendlyInterior && repairedFor <= 0 && !HasHandyman;
                int rows = showRepair ? 3 : 2;
                float y = ViewHeight - 16f - height * rows - gap * (rows - 1);
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
            Panel(rect, pressed ? Gold : new Color(Ink.r, Ink.g, Ink.b, .92f));
            Panel(new Rect(rect.x, rect.y, 4, rect.height), accent);
            Color previous = buttonStyle.normal.textColor;
            buttonStyle.normal.textColor = Cream;
            Label(rect, text, buttonStyle);
            buttonStyle.normal.textColor = previous;
        }

        void DrawHudTile(Rect area, Color color, string caption, string value)
        {
            Panel(area, color);
            Panel(new Rect(area.x + 7, area.y + 7, 34, area.height - 14), new Color(Ink.r, Ink.g, Ink.b, .82f));
            Panel(new Rect(area.x + 17, area.y + 15, 14, 14), Cream);

            Color oldSmall = inkSmall.normal.textColor;
            Color oldLarge = inkLarge.normal.textColor;
            inkSmall.normal.textColor = Ink;
            inkLarge.normal.textColor = Ink;
            GUI.Label(new Rect(area.x + 48, area.y + 4, area.width - 54, 18), caption, inkSmall);
            GUI.Label(new Rect(area.x + 48, area.y + 19, area.width - 54, 27), value, inkLarge);
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
            Panel(new Rect(x, 44, 246, 66), new Color(Ink.r, Ink.g, Ink.b, .9f));
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
            Panel(new Rect(0, 104, 1440, 796), new Color(0, .025f, .05f, .82f));
            Panel(new Rect(330, 222, 780, 456), Ink); Panel(new Rect(330, 222, 780, 5), Teal);
            string heading = paused ? "TAKE A BREATHER" : phase == Phase.Welcome ? "YOUR SHIFT. THEIR CHAOS." : phase == Phase.Tutorial ? "HOW TO PLAY" : "SHIFT COMPLETE";
            Label(new Rect(372, 256, 700, 52), heading, title);
            string copy;
            if (paused) copy = "The clock is paused.\n\nPress Start / Escape or resume when you are ready.";
            else if (phase == Phase.Welcome) copy = "3 minutes. Drop riders at their floors for points.\n\nOffice (0–3) → Candy (4–7) → Underwater (8–11).\n\nDrag to board or kick. CLOSE & TRAVEL, then hold UP/DOWN and STOP near a floor.";
            else if (phase == Phase.Tutorial) copy = "Three things to know.\n\nRead the short badge above each rider: type first, destination second.";
            else copy = "FINAL SCORE: " + round.Score
                + "\nHappy riders: " + round.Happy + "  •  Drop-offs: " + round.Delivered
                + "\nMissed riders: " + round.Missed + "  •  Turned away: " + round.TurnedAway
                + "\n\nYour 3-minute shift is over. Try again for a better mix!";
            Label(new Rect(374, 322, 690, 240), copy, body);
            if (phase == Phase.Tutorial)
            {
                Panel(new Rect(374, 418, 210, 112), Teal); Panel(new Rect(602, 418, 210, 112), new Color32(255, 205, 82, 255)); Panel(new Rect(830, 418, 210, 112), Coral);
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
