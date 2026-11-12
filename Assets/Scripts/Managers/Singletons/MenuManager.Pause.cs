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
            Panel(new Rect(panel.x + 8, panel.y + 10, panel.width, panel.height), Color.black);
            Panel(panel, MenuPresentation.Wall);
            Panel(new Rect(panel.x, panel.y, panel.width, 12), Teal);
            Panel(new Rect(panel.x, panel.y + 12, 10, panel.height - 12), Coral);
            GUI.Label(new Rect(panel.x + 60, panel.y + 44, panel.width - 120, 80), "TAKE A BREATHER", title);
            GUI.Label(new Rect(panel.x + 72, panel.y + 132, panel.width - 144, 60),
                "The clock is paused. Pick up where you left off when you're ready.", caption);

            string[] actions = { "RESUME SHIFT", "RETURN TO MODE LOBBY", "MAIN MENU" };
            Color[] accents = { Teal, Coral, MenuPresentation.Brass };
            for (int i = 0; i < actions.Length; i++)
            {
                Rect button = new Rect(panel.x + 104, panel.y + 224 + i * 102, panel.width - 208, 76);
                if (pauseSelection == i) Panel(new Rect(button.x - 7, button.y - 7,
                    button.width + 14, button.height + 14), Cream);
                Panel(button, accents[i]);
                GUI.Label(button, actions[i], cardTitle);
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
