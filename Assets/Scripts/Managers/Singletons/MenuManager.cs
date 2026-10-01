using UnityEngine;
using UnityEngine.SceneManagement;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    // Mode select, pause overlay, and passenger destination overlay.
    [DefaultExecutionOrder(32000)]
    public sealed class MenuManager : MonoBehaviour
    {
        const string SinglePlayerScene = "Assets/Scenes/Main.unity";
        const string DuelScene = SinglePlayerScene;
        static readonly Color Ink = new Color32(25, 31, 46, 255);
        static readonly Color Teal = new Color32(75, 226, 202, 255);
        static readonly Color Coral = new Color32(255, 124, 104, 255);
        static readonly Color Cream = new Color32(255, 250, 234, 255);
        GUIStyle title, cardTitle, caption, cardCaption, body;
        static bool startDuelOnLoad;
        static bool pauseOpen;
        static bool passengerOverlayOpen;
        static ElevatorManager overlayGame;
        public static bool IsOpen { get; private set; }
        public static bool PauseOpen => pauseOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ShowBeforeTheGame()
        {
            IsOpen = true;
            pauseOpen = false;
            passengerOverlayOpen = false;
            startDuelOnLoad = false;
            var menu = new GameObject("Game Mode Menu");
            DontDestroyOnLoad(menu);
            menu.AddComponent<MenuManager>();
            InputManager.Instance?.SetView(GameView.Menu);
        }

        void OnEnable() { title = null; }

        public static bool ConsumeDuelRequest()
        {
            bool requested = startDuelOnLoad;
            startDuelOnLoad = false;
            return requested;
        }

        public static void Open() => IsOpen = true;
        public static void Close() => IsOpen = false;

        public static void OpenPause()
        {
            pauseOpen = true;
            InputManager.Instance?.SetView(GameView.Pause);
        }

        public static void ClosePause()
        {
            pauseOpen = false;
        }

        public static void TogglePassengerOverlay(bool visible, ElevatorManager game)
        {
            passengerOverlayOpen = visible;
            overlayGame = game;
        }

        void OnGUI()
        {
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            Styles();
            if (IsOpen)
            {
                DrawModeSelect();
                return;
            }
            if (passengerOverlayOpen) DrawPassengerOverlay();
        }

        void DrawModeSelect()
        {
            Panel(new Rect(0, 0, Screen.width, Screen.height), Ink);

            float scale = Mathf.Min(Screen.width / 1200f, Screen.height / 720f);
            float cardWidth = Mathf.Min(430f, Screen.width * .39f);
            float cardHeight = Mathf.Min(250f, Screen.height * .38f);
            float gap = Mathf.Min(34f, Screen.width * .04f);
            float totalWidth = cardWidth * 2 + gap;
            float left = (Screen.width - totalWidth) * .5f;
            float top = (Screen.height - cardHeight) * .5f + 30f * scale;

            GUI.Label(new Rect(20, top - 128f * scale, Screen.width - 40, 55f * scale),
                "CRAZY ELEVATORS", title);
            GUI.Label(new Rect(20, top - 70f * scale, Screen.width - 40, 32f * scale),
                "CHOOSE YOUR MODE", caption);

            DrawMode(new Rect(left, top, cardWidth, cardHeight), Teal,
                "SINGLE PLAYER", "Patience, passenger perks & mouse controls", SinglePlayerScene, false);
            DrawMode(new Rect(left + cardWidth + gap, top, cardWidth, cardHeight), Coral,
                "1V1", "Local duel • NPC rival for now", DuelScene, true);
            GUI.Label(new Rect(12, Screen.height - 38f * scale, Screen.width - 24, 24f * scale),
                "CHOOSE A MODE TO BEGIN", caption);
        }

        void DrawPassengerOverlay()
        {
            if (overlayGame == null) return;
            float width = Mathf.Min(360f, Screen.width * .34f);
            float height = Mathf.Min(420f, Screen.height * .7f);
            var card = new Rect(Screen.width - width - 16f, 80f, width, height);
            Panel(card, new Color(Ink.r, Ink.g, Ink.b, .92f));
            GUI.Label(new Rect(card.x + 12, card.y + 10, card.width - 24, 28), "PASSENGERS", caption);
            float y = card.y + 44;
            foreach (var rider in overlayGame.AllRiders)
            {
                if (rider == null || rider.Resolved) continue;
                if (!rider.Boarded && rider.Origin != overlayGame.DiscreteFloor) continue;
                string line = rider.Name + "  →  F" + rider.Destination + (rider.Boarded ? "  [IN]" : "  [WAIT]");
                GUI.Label(new Rect(card.x + 14, y, card.width - 28, 22), line, body);
                y += 22;
                if (y > card.yMax - 28) break;
            }
        }

        void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label)
            { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.Clamp(Screen.height / 16, 30, 56), fontStyle = FontStyle.Bold };
            title.normal.textColor = Cream;
            cardTitle = new GUIStyle(title) { fontSize = Mathf.Clamp(Screen.height / 25, 23, 38) };
            cardTitle.normal.textColor = Ink;
            caption = new GUIStyle(title) { fontSize = Mathf.Clamp(Screen.height / 48, 14, 19), fontStyle = FontStyle.Normal };
            caption.normal.textColor = Cream;
            cardCaption = new GUIStyle(caption);
            cardCaption.normal.textColor = Ink;
            body = new GUIStyle(caption) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.Clamp(Screen.height / 55, 12, 16) };
        }

        void DrawMode(Rect rect, Color accent, string heading, string detail, string scene, bool duel)
        {
            Panel(rect, Cream);
            Panel(new Rect(rect.x, rect.y, rect.width, 10), accent);
            GUI.Label(new Rect(rect.x + 16, rect.y + 47, rect.width - 32, 60), heading, cardTitle);
            GUI.Label(new Rect(rect.x + 24, rect.y + 116, rect.width - 48, 46), detail, cardCaption);
            var button = new Rect(rect.x + 24, rect.yMax - 66, rect.width - 48, 46);
            Panel(button, accent);
            if (GUI.Button(button, "PLAY", cardTitle))
            {
                Close();
                startDuelOnLoad = duel;
                SceneManager.LoadScene(scene);
            }
        }

        static void Panel(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
