using UnityEngine;
using UnityEngine.Serialization;
using CrazyElevator.Managers;
using CrazyElevator.Shared;
using Game = CrazyElevator.Managers.ElevatorManager;

namespace CrazyElevator.Match
{
    // One shared match; each elevator keeps its own controller and rules.
    [DefaultExecutionOrder(-100)]
    public sealed partial class ElevatorMatch : MonoBehaviour
    {
        public Game player;
        [Min(3f)] public float shaftSpacing = 4f;
        [FormerlySerializedAs("cabinHorizontalFieldOfView")]
        [Range(45f, 70f)] public float cabinVerticalFieldOfView = 52f;
        public Game Rival { get; private set; }
        public bool Running { get; private set; }
        public bool Paused { get; private set; }
        public bool Finished => Running && player != null && Rival != null
            && player.ShiftFinished && Rival.ShiftFinished;
        ElevatorScene rivalView;
        Transform rivalShaft;

        void Awake()
        {
            // The same scene and gameplay controller serve both menu choices.
            // This manager only activates for the 1v1 selection.
            if (!MenuManager.ConsumeDuelRequest())
            {
                enabled = false;
                return;
            }

            if (player == null || player.sceneView == null)
            {
                Debug.LogError("Assign the player elevator to Elevator Match.", this);
                enabled = false;
                return;
            }
            player.ConfigureMatch(this, 0);
            CreateRival();
        }

        // Reuse authored prefabs, not a second copy of the whole environment.
        void CreateRival()
        {
            var source = player.sceneView;
            rivalView = Instantiate(source);
            rivalView.name = "NPC elevator scene";
            Transform duplicateWorld = rivalView.exteriorCar.parent;
            rivalView.exteriorCar.SetParent(source.exteriorCar.parent, false);
            rivalView.exteriorCar.name = "NPC exterior car";
            Destroy(duplicateWorld.gameObject);

            // Cabins are independent sets, outside each other's camera/raycast range.
            rivalView.cabin.position += Vector3.right * 1000f;
            rivalView.cabinCamera.transform.position += Vector3.right * 1000f;
            foreach (var light in rivalView.lights)
                if (light != null) light.transform.position += Vector3.right * 1000f;
            foreach (var listener in rivalView.GetComponentsInChildren<AudioListener>(true))
            { listener.enabled = false; Destroy(listener); }
            if (rivalView.clearCamera != null) rivalView.clearCamera.gameObject.SetActive(false);

            // Clone the existing rails as editable scenery for the adjacent shaft.
            rivalShaft = new GameObject("NPC guide rails").transform;
            rivalShaft.SetParent(source.exteriorCar.parent, false);
            foreach (string railName in new[] { "Left guide rail", "Right guide rail" })
            {
                var rail = source.exteriorCar.parent.Find(railName);
                if (rail == null) continue;
                var copy = Instantiate(rail, rivalShaft, false);
                copy.name = "NPC " + railName;
                copy.localPosition += Vector3.right * shaftSpacing;
            }

            // Assign references before Awake runs on the rival controller.
            var actor = new GameObject("NPC elevator controller");
            actor.SetActive(false);
            Rival = actor.AddComponent<Game>();
            Rival.sceneView = rivalView;
            player.CopyMatchSettingsTo(Rival);
            Rival.ConfigureMatch(this, 1, true);
            actor.AddComponent<NpcElevator>().game = Rival;
            actor.SetActive(true);
            ConfigureCameras();
        }

        void Update()
        {
            if (!enabled || player == null || player.IntroPlaying || MenuManager.IsOpen) return;
            var input = InputManager.Instance;
            if (input == null) return;

            if (!Running || Finished)
            {
                if (input.StartPressed || input.ConfirmPressed) StartMatch();
            }
            // Pause is owned by GameManager so InputManager only fires once.
        }

        // Both shifts start together; a restart clears both scores and NPC state.
        public void StartMatch()
        {
            Paused = false;
            player.StartMatchShift();
            Rival.StartMatchShift();
            Rival.GetComponent<NpcElevator>().ResetDecisions();
            Running = true;
            Paused = false;
        }

        public void TogglePause()
        {
            Paused = !Paused;
            player.SetMatchPaused(Paused);
            Rival.SetMatchPaused(Paused);
        }

        void ConfigureCameras()
        {
            SetCameraRect(player, 0);
            SetCameraRect(Rival, 1);
            if (player.sceneView.clearCamera != null)
                player.sceneView.clearCamera.rect = CameraRect(0);
            if (Rival.sceneView.clearCamera != null)
                Rival.sceneView.clearCamera.gameObject.SetActive(false);
        }

        static void SetCameraRect(Game game, int seat)
        {
            Rect rect = CameraRect(seat);
            game.sceneView.cabinCamera.rect = rect;
            game.sceneView.exteriorCamera.rect = rect;
            game.sceneView.cabinCamera.targetTexture = null;
            game.sceneView.exteriorCamera.targetTexture = null;
            float aspect = Mathf.Max(.1f, Screen.width * .5f / Screen.height);
            game.sceneView.cabinCamera.aspect = aspect;
            game.sceneView.cabinCamera.fieldOfView =
                Mathf.Clamp(game.Match.cabinVerticalFieldOfView, 45f, 70f);
        }

        // Future blocking/collision logic can compare these shared shaft positions.
        public float ShaftX(int seat) => 9f + seat * shaftSpacing;

        void OnDestroy()
        {
            if (Rival != null) Destroy(Rival.gameObject);
            if (rivalView != null && rivalView.exteriorCar != null) Destroy(rivalView.exteriorCar.gameObject);
            if (rivalShaft != null) Destroy(rivalShaft.gameObject);
            if (rivalView != null) Destroy(rivalView.gameObject);
        }
    }
}

// --- MatchScreens ---
namespace CrazyElevator.Match
{
    public sealed partial class ElevatorMatch
    {
        // Two full-height screen regions; cameras render directly into each half.
        public Rect ViewRect(int seat)
        {
            float paneWidth = Screen.width * .5f;
            return new Rect(seat * paneWidth + (seat == 1 ? 2 : 0), 0,
                Mathf.Max(1, paneWidth - 2), Mathf.Max(1, Screen.height));
        }

        public static Rect CameraRect(int seat)
        {
            return new Rect(seat * .5f, 0, .5f, 1);
        }
    }
}

// --- MatchHUD ---
namespace CrazyElevator.Match
{
    public sealed partial class ElevatorMatch
    {
        static readonly Color Teal = new Color32(75, 226, 202, 255);
        static readonly Color Coral = new Color32(255, 124, 104, 255);
        static readonly Color Ink = new Color32(25, 31, 46, 255);
        [System.NonSerialized] GUIStyle heading, text, score, centred, tileCaption, tileValue;

        void OnGUI()
        {
            if (player == null || Rival == null) return;
            var previousMatrix = GUI.matrix;
            var previousColour = GUI.color;
            var previousContent = GUI.contentColor;
            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
            GUI.contentColor = Color.white;
            GameTypography.ApplyToSkin(GUI.skin);
            MatchStyles();
            if (player.IntroPlaying)
            {
                player.DrawMatchIntroGUI();
                GUI.matrix = previousMatrix;
                GUI.color = previousColour;
                GUI.contentColor = previousContent;
                return;
            }
            DrawSeat(player, 0, Teal);
            DrawSeat(Rival, 1, Coral);
            if (!Running || Paused || Finished) DrawMatchOverlay();
            GUI.matrix = previousMatrix;
            GUI.color = previousColour;
            GUI.contentColor = previousContent;
        }

        void MatchStyles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            GameTypography.Apply(text);
            text.normal.textColor = Color.white;
            heading = new GUIStyle(text) { fontSize = 18, fontStyle = FontStyle.Bold };
            GameTypography.Apply(heading, true);
            score = new GUIStyle(heading) { fontSize = 25 };
            GameTypography.Apply(score, true);
            centred = new GUIStyle(heading) { alignment = TextAnchor.MiddleCenter };
            GameTypography.Apply(centred, true);
            tileCaption = new GUIStyle(text) { fontSize = 11, fontStyle = FontStyle.Bold };
            GameTypography.Apply(tileCaption, true);
            tileCaption.normal.textColor = new Color(1f, 1f, 1f, .72f);
            tileValue = new GUIStyle(score) { fontSize = 22 };
            GameTypography.Apply(tileValue, true);
            tileValue.normal.textColor = Color.white;
        }

        static void Fill(Rect rect, Color colour)
        {
            Color previous = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        void DrawSeat(Game actor, int seat, Color colour)
        {
            Rect view = ViewRect(seat);
            actor.DrawMatchViewGUI();
            int seconds = Mathf.CeilToInt(player.SecondsLeft);
            string clock = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            float barWidth = Mathf.Max(1f, view.width - 16f);
            var bar = new Rect(view.x + 8f, view.y + 8f, barWidth, 58f);
            Fill(new Rect(bar.x - 2f, bar.y - 2f, bar.width + 4f, bar.height + 4f), Color.white);
            Fill(bar, new Color(9f / 255f, 11f / 255f, 16f / 255f, .96f));
            float tileWidth = bar.width / 5f;
            DrawSeatTile(new Rect(bar.x, bar.y, tileWidth, bar.height), colour,
                seat == 0 ? "YOU" : "NPC", "F" + Mathf.RoundToInt(actor.CurrentFloor));
            DrawSeatTile(new Rect(bar.x + tileWidth, bar.y, tileWidth, bar.height),
                new Color32(255, 205, 82, 255), "LOAD", actor.PassengerLoad + "/10");
            DrawSeatTile(new Rect(bar.x + tileWidth * 2f, bar.y, tileWidth, bar.height),
                Teal, "SCORE", actor.Points.ToString());
            DrawSeatTile(new Rect(bar.x + tileWidth * 3f, bar.y, tileWidth, bar.height),
                Coral, "HAPPY", actor.HappyDeliveries.ToString());
            DrawSeatTile(new Rect(bar.x + tileWidth * 4f, bar.y, tileWidth, bar.height),
                new Color32(112, 183, 255, 255), "TIME", clock);
        }

        void DrawSeatTile(Rect area, Color accent, string caption, string value)
        {
            Fill(new Rect(area.x, area.yMax - 5f, area.width, 5f), accent);
            Fill(new Rect(area.x + 5f, area.y + 7f, 5f, area.height - 19f), accent);
            GUI.Label(new Rect(area.x + 16f, area.y + 3f, area.width - 18f, 18f), caption, tileCaption);
            GUI.Label(new Rect(area.x + 16f, area.y + 19f, area.width - 18f, 30f), value, tileValue);
        }

        void DrawMatchOverlay()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, .78f));
            float width = Mathf.Min(620, Screen.width - 32), height = Mathf.Min(310, Screen.height - 32);
            var box = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            Fill(box, Ink);
            string title = !Running ? "TWO ELEVATORS. ONE SHIFT." : Paused ? "BOTH ELEVATORS PAUSED" : "SHIFT COMPLETE";
            GUI.Label(new Rect(box.x + 20, box.y + 20, width - 40, 40), title, centred);
            string copy = "YOU vs NPC — one 3-minute shift.\n\nLeft: your elevator. Right: your rival.\nDrag passengers in or out. Close the doors, steer up or down, then stop at a floor to deliver riders and score.\n\nHighest delivery score wins.";
            if (Paused) copy = "Both elevators are paused.\n\nResume when you're ready.";
            if (Finished)
            {
                string winner = player.Points == Rival.Points ? "DRAW!" : player.Points > Rival.Points ? "YOU WIN!" : "NPC WINS — REMATCH?";
                copy = winner + "\n\nYOU: " + player.Points + " points\nNPC: " + Rival.Points + " points\n\nTry a different passenger mix and route. Highest score wins, not highest floor.";
            }
            GUI.Label(new Rect(box.x + 24, box.y + 72, width - 48, height - 130), copy, text);
            var button = new Rect(box.x + 24, box.yMax - 52, width - 48, 34);
            Fill(button, Teal);
            if (GUI.Button(button, Paused ? "RESUME BOTH" : Finished ? "REMATCH / ENTER" : "START BOTH / ENTER"))
            { if (Paused) TogglePause(); else StartMatch(); }
        }
    }
}
