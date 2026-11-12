using UnityEngine;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    public sealed partial class MenuManager
    {
        void DrawPauseScreen()
        {
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            Panel(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, .82f));
            float scale = Mathf.Min(Screen.width / ArtWidth, Screen.height / ArtHeight);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - ArtWidth * scale) * .5f,
                (Screen.height - ArtHeight * scale) * .5f, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            Rect panel = new Rect(430, 202, 840, 660);
            MenuPresentation.Plaque(panel, MenuPresentation.Wall, MenuPresentation.Ink, 10f);
            MenuPresentation.Fasteners(panel, MenuPresentation.Brass);
            Rect header = new Rect(panel.x + 104, panel.y + 48, panel.width - 208, 100);
            MenuPresentation.Plaque(header, MenuPresentation.Paper, MenuPresentation.Metal);
            DrawLabel(header, "SHIFT PAUSED", cardTitle, MenuPresentation.Ink);

            string[] actions = { "RESUME SHIFT", "RETURN TO LOBBY", "MAIN MENU" };
            Color[] accents = { MenuPresentation.Teal, MenuPresentation.Coral, MenuPresentation.Brass };
            for (int i = 0; i < actions.Length; i++)
            {
                Rect button = new Rect(panel.x + 104, panel.y + 200 + i * 104, panel.width - 208, 76);
                bool selected = pauseSelection == i;
                MenuPresentation.Plaque(button, selected ? accents[i] : MenuPresentation.Ink,
                    selected ? MenuPresentation.Metal : MenuPresentation.Ink, 4f);
                DrawLabel(button, actions[i], cardTitle,
                    selected ? MenuPresentation.Ink : MenuPresentation.Paper);
                if (GUI.Button(button, GUIContent.none, GUIStyle.none))
                {
                    if (i == 0) ResumeFromPause();
                    else if (i == 1) ReturnToLobby();
                    else ReturnToMainMenu();
                }
            }
            GUI.Label(new Rect(panel.x + 52, panel.yMax - 92, panel.width - 104, 40),
                KeyboardHintsEnabled ? "↑ / ↓ MOVE  ·  C SELECT  ·  ESC RESUME"
                    : "STICK / D-PAD MOVE  ·  A SELECT  ·  B RESUME", caption);
            GUI.matrix = oldMatrix;
        }

    }
}
