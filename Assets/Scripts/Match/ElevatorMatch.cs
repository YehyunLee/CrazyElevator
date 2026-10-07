using UnityEngine;
using UnityEngine.Serialization;
using CrazyElevator.Managers;
using CrazyElevator.Shared;
using Game = CrazyElevator.Managers.ElevatorManager;

namespace CrazyElevator.Match
{
    // One shared match; each elevator keeps its own controller and score.
    [DefaultExecutionOrder(-100)]
    public sealed partial class ElevatorMatch : MonoBehaviour
    {
        // The authored solo shaft sits at the centre of the exterior building.
        public const float ShaftCenterX = 9f;
        public Game player;
        [Min(3f)] public float shaftSpacing = 4f;
        [FormerlySerializedAs("cabinHorizontalFieldOfView")]
        [Range(45f, 90f)] public float cabinVerticalFieldOfView = 90f;
        [Header("Two-track collisions")]
        [Range(.15f, .8f)] public float trackSwitchSeconds = .32f;
        [FormerlySerializedAs("verticalBlockDistance")]
        [Tooltip("Extra world-space breathing room around the visible elevator cars.")]
        [Range(0f, .75f)] public float collisionPadding = .15f;
        [FormerlySerializedAs("collisionSpeedRetained")]
        [Tooltip("How much of the closing speed becomes a short rebound after a vertical hit.")]
        [Range(.15f, .75f)] public float collisionBounceStrength = .28f;
        [Range(.4f, 2.5f)] public float collisionSlowSeconds = 1.15f;
        public Game Rival { get; private set; }
        public bool Running { get; private set; }
        public bool Paused { get; private set; }
        public bool Finished => Running && player != null && Rival != null
            && player.ShiftFinished && Rival.ShiftFinished;
        ElevatorScene rivalView;
        Transform rivalShaft;
        readonly Transform[] soloRails = new Transform[2];
        readonly int[] targetTrack = { 0, 1 };
        readonly float[] trackPosition = { 0f, 1f };
        float collisionCooldown;
        float collisionFlash;
        AudioClip collisionClip;
        ParticleSystem collisionParticles;
        Material collisionParticleMaterial;

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
            collisionClip = CreateCollisionClip();
            collisionParticles = CreateCollisionParticles(player.sceneView.exteriorCar.parent);
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

            // Keep the solo shaft centred, with two equally spaced tracks in 1v1.
            rivalShaft = new GameObject("Duel guide rails").transform;
            rivalShaft.SetParent(source.exteriorCar.parent, false);
            string[] railNames = { "Left guide rail", "Right guide rail" };
            for (int i = 0; i < railNames.Length; i++)
            {
                var rail = source.exteriorCar.parent.Find(railNames[i]);
                if (rail == null) continue;
                soloRails[i] = rail;
                for (int track = 0; track < 2; track++)
                {
                    var copy = Instantiate(rail, rivalShaft, false);
                    copy.name = "Track " + (track + 1) + " " + railNames[i];
                    copy.localPosition += Vector3.right * ((track - .5f) * shaftSpacing);
                }
                rail.gameObject.SetActive(false);
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
            if (!enabled || player == null) return;
            float dt = Time.unscaledDeltaTime;
            collisionCooldown = Mathf.Max(0f, collisionCooldown - dt);
            collisionFlash = Mathf.Max(0f, collisionFlash - dt);
            float switchSpeed = 1f / Mathf.Max(.05f, trackSwitchSeconds);
            float playerTrackBefore = trackPosition[0];
            float rivalTrackBefore = trackPosition[1];
            trackPosition[0] = Mathf.MoveTowards(trackPosition[0], targetTrack[0], switchSpeed * dt);
            trackPosition[1] = Mathf.MoveTowards(trackPosition[1], targetTrack[1], switchSpeed * dt);
            if (Running && !Paused && CarsOverlap(player, Rival))
            {
                bool playerWasSwitching = !Mathf.Approximately(playerTrackBefore, targetTrack[0]);
                bool rivalWasSwitching = !Mathf.Approximately(rivalTrackBefore, targetTrack[1]);
                trackPosition[0] = playerTrackBefore;
                trackPosition[1] = rivalTrackBefore;
                if (playerWasSwitching) targetTrack[0] = playerTrackBefore < .5f ? 0 : 1;
                if (rivalWasSwitching) targetTrack[1] = rivalTrackBefore < .5f ? 0 : 1;
                TriggerCollision(player, Rival);
            }

            if (player.IntroPlaying || MenuManager.IsOpen) return;
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
            targetTrack[0] = 0; targetTrack[1] = 1;
            trackPosition[0] = 0; trackPosition[1] = 1;
            collisionCooldown = collisionFlash = 0;
            player.StartMatchShift();
            Rival.StartMatchShift(player);
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
                Mathf.Clamp(game.Match.cabinVerticalFieldOfView, 45f, 90f);
        }

        // Both duel tracks straddle the authored solo shaft at equal distances.
        public float ShaftX(int seat) => ShaftCenterX + (seat - .5f) * shaftSpacing;

        public float ShaftX(Game actor)
        {
            int seat = SeatOf(actor);
            return ShaftX(0) + Mathf.SmoothStep(0f, 1f, trackPosition[seat]) * shaftSpacing;
        }

        public int TrackNumber(Game actor) => targetTrack[SeatOf(actor)] + 1;

        public void RequestTrack(Game actor, int direction)
        {
            if (!Running || Paused || actor == null || !actor.MovingInShaft || direction == 0) return;
            int seat = SeatOf(actor);
            int requested = Mathf.Clamp(targetTrack[seat] + (direction > 0 ? 1 : -1), 0, 1);
            if (requested == targetTrack[seat]) return;

            targetTrack[seat] = requested;
        }

        // Prevent cars in the same track from passing through each other. A
        // player can escape the block with A/D; the NPC automatically tries the
        // free track after it reaches a blocker.
        public float ConstrainTravel(Game actor, float before, float candidate, ref float velocity)
        {
            if (!Running || actor == null) return candidate;
            Game other = Other(actor);
            if (other == null || !HorizontallyOverlaps(actor, other)) return candidate;

            float direction = Mathf.Sign(candidate - before);
            if (direction == 0) return candidate;
            float otherFloor = other.CurrentFloor;
            bool approaching = direction > 0 ? before <= otherFloor : before >= otherFloor;
            if (!approaching) return candidate;

            float contactDistance = VerticalContactDistance(first: actor, second: other);
            float boundary = otherFloor - direction * contactDistance;
            bool blocked = direction > 0 ? candidate >= boundary : candidate <= boundary;
            if (!blocked) return candidate;

            float closingSpeed = Mathf.Abs(velocity - other.TravelVelocity);
            candidate = Mathf.Clamp(boundary, 0f, ElevatorRound.Floors - 1f);
            velocity = 0f;
            TriggerVerticalCollision(actor, other, direction, closingSpeed);
            if (actor.IsNpc)
                RequestTrack(actor, targetTrack[SeatOf(actor)] == 0 ? 1 : -1);
            return candidate;
        }

        bool HorizontallyOverlaps(Game first, Game second)
        {
            float halfWidths = first.ExteriorCollisionHalfSize.x + second.ExteriorCollisionHalfSize.x;
            return Mathf.Abs(ShaftX(first) - ShaftX(second)) <= halfWidths + collisionPadding;
        }

        float VerticalContactDistance(Game first, Game second)
        {
            float worldDistance = first.ExteriorCollisionHalfSize.y
                + second.ExteriorCollisionHalfSize.y + collisionPadding;
            return worldDistance / Game.ShaftFloorHeight;
        }

        bool CarsOverlap(Game first, Game second)
        {
            if (first == null || second == null || !HorizontallyOverlaps(first, second)) return false;
            float verticalDistance = Mathf.Abs(first.CurrentFloor - second.CurrentFloor) * Game.ShaftFloorHeight;
            float combinedHalfHeights = first.ExteriorCollisionHalfSize.y
                + second.ExteriorCollisionHalfSize.y + collisionPadding;
            return verticalDistance <= combinedHalfHeights;
        }

        // Keep the two-argument entry point used by our match Play Mode check.
        void TriggerCollision(Game first, Game second) => PlayCollision(first, second, 0f, 0f);

        void TriggerVerticalCollision(Game first, Game second, float direction, float closingSpeed)
            => PlayCollision(first, second, direction, closingSpeed);

        void PlayCollision(Game first, Game second, float verticalDirection, float closingSpeed)
        {
            if (collisionCooldown > 0f) return;
            collisionCooldown = .55f;
            collisionFlash = .62f;
            if (verticalDirection != 0f)
            {
                float rebound = Mathf.Clamp(closingSpeed * collisionBounceStrength, .65f, 1.6f);
                first?.ApplyMatchBounce(collisionSlowSeconds, -verticalDirection * rebound);
                second?.ApplyMatchBounce(collisionSlowSeconds, verticalDirection * rebound);
            }
            else
            {
                // A lane-change bump still jolts both cars, but does not reverse their vertical travel.
                first?.ApplyMatchCollision(collisionSlowSeconds, collisionBounceStrength);
                second?.ApplyMatchCollision(collisionSlowSeconds, collisionBounceStrength);
            }
            if (first != null && second != null)
                EmitCollisionSparks((first.ShaftWorldPosition + second.ShaftWorldPosition) * .5f);
            SfxManager.Instance?.PlaySfx(collisionClip, 1f);
        }

        ParticleSystem CreateCollisionParticles(Transform parent)
        {
            var visual = new GameObject("Collision sparks");
            visual.SetActive(false);
            visual.layer = 31;
            visual.transform.SetParent(parent, false);
            var particles = visual.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = .45f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.22f, .48f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(.08f, .18f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color32(255, 226, 72, 255), new Color32(255, 104, 28, 255));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = false;
            var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
            particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            particleRenderer.velocityScale = .2f;
            particleRenderer.lengthScale = 2.8f;
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                collisionParticleMaterial = new Material(shader) { name = "Runtime collision sparks" };
                particleRenderer.sharedMaterial = collisionParticleMaterial;
            }
            visual.SetActive(true);
            return particles;
        }

        void EmitCollisionSparks(Vector3 position)
        {
            if (collisionParticles == null) return;
            for (int i = 0; i < 28; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float speed = Random.Range(3.5f, 8.5f);
                var spark = new ParticleSystem.EmitParams
                {
                    position = position,
                    velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed,
                    startLifetime = Random.Range(.22f, .48f),
                    startSize = Random.Range(.07f, .17f),
                    startColor = Color.Lerp(
                        new Color32(255, 226, 72, 255),
                        new Color32(255, 104, 28, 255), Random.value)
                };
                collisionParticles.Emit(spark, 1);
            }
        }

        int SeatOf(Game actor) => actor == Rival ? 1 : 0;
        Game Other(Game actor) => actor == Rival ? player : Rival;

        static AudioClip CreateCollisionClip()
        {
            const int rate = 22050;
            const float seconds = .34f;
            var data = new float[Mathf.CeilToInt(rate * seconds)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 11f);
                float thud = Mathf.Sin(2f * Mathf.PI * (115f - 55f * t) * t) * .62f;
                float metal = Mathf.Sin(2f * Mathf.PI * 760f * t) * Mathf.Exp(-t * 19f) * .19f;
                float noise = (Mathf.Repeat(Mathf.Sin(i * 78.233f) * 43758.5453f, 1f) * 2f - 1f) * .16f;
                data[i] = Mathf.Clamp((thud + metal + noise) * envelope, -.9f, .9f);
            }
            var clip = AudioClip.Create("Elevator track collision", data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void OnDestroy()
        {
            foreach (var rail in soloRails)
                if (rail != null) rail.gameObject.SetActive(true);
            if (collisionClip != null) Destroy(collisionClip);
            if (collisionParticles != null) Destroy(collisionParticles.gameObject);
            if (collisionParticleMaterial != null) Destroy(collisionParticleMaterial);
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
        [System.NonSerialized] GUIStyle heading, text, score, centred, tileCaption, tileValue, collisionWord;

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
            DrawCollisionFeedback();
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
            collisionWord = new GUIStyle(centred) { fontSize = 24, clipping = TextClipping.Overflow };
            GameTypography.Apply(collisionWord, true);
            collisionWord.normal.textColor = Color.white;
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
                seat == 0 ? "YOU" : "NPC", "F" + Mathf.RoundToInt(actor.CurrentFloor) + "  T" + TrackNumber(actor));
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

        void DrawCollisionFeedback()
        {
            if (collisionFlash <= 0f) return;
            float alpha = Mathf.Clamp01(collisionFlash / .62f);
            Color flash = new Color(1f, .35f, .18f, alpha * .9f);
            const float edge = 7f;
            Fill(new Rect(0, 0, Screen.width, edge), flash);
            Fill(new Rect(0, Screen.height - edge, Screen.width, edge), flash);
            Fill(new Rect(0, 0, edge, Screen.height), flash);
            Fill(new Rect(Screen.width - edge, 0, edge, Screen.height), flash);
            float width = Mathf.Min(430f, Screen.width - 24f);
            Rect banner = new Rect((Screen.width - width) * .5f, 78f, width, 44f);
            Fill(banner, new Color(.04f, .05f, .08f, alpha * .96f));
            Fill(new Rect(banner.x, banner.yMax - 5f, banner.width, 5f), flash);
            GUI.Label(banner, "BOUNCE!  SWITCH TRACKS", collisionWord);
        }

        void DrawMatchOverlay()
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, .78f));
            float width = Mathf.Min(620, Screen.width - 32), height = Mathf.Min(310, Screen.height - 32);
            var box = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
            Fill(box, Ink);
            string title = !Running ? "TWO ELEVATORS. ONE SHIFT." : Paused ? "BOTH ELEVATORS PAUSED" : "SHIFT COMPLETE";
            GUI.Label(new Rect(box.x + 20, box.y + 20, width - 40, 40), title, centred);
            string copy = "YOU vs NPC — one 3-minute shift.\n\nLeft: your elevator. Right: your rival.\nDrag passengers in or out. During shaft travel, use A / D to switch between the two tracks. Elevators bounce when they meet; switch tracks to pass.\n\nHighest delivery score wins.";
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
