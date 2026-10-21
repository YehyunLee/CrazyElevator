using System.Collections.Generic;
using UnityEngine;
using CrazyElevator.Shared;
using ElevatorMatchType = CrazyElevator.Match.ElevatorMatch;

namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;

    // Every floor has five stops: far left, left, the centre shafts, right, far right.
    // Riders wait at one stop and want another, so the shaft map is what you navigate.
    public sealed partial class ElevatorManager
    {
        // Offsets from the shaft centre. Side stops sit on the side-track columns.
        public static readonly float[] StopOffsets = { -25f, -14f, 0f, 14f, 25f };
        const float StopReach = 1.5f;
        const int CenterStop = ElevatorRound.CenterStop;
        int dockingStop = CenterStop;
        float dockingStartX, dockingTargetX;

        public static float StopWorldX(int stop) => ElevatorMatchType.ShaftCenterX + StopOffsets[stop];

        // The centre stop is whichever shaft (or duel lane) the car is on.
        float StopX(int stop) => stop != CenterStop ? StopWorldX(stop)
            : Match != null ? Match.LaneX(this) : NearestShaftX(RouteLateralX);

        int NearestStop(float x)
        {
            if (OnMainShaft(x)) return CenterStop;
            int best = CenterStop;
            for (int stop = 0; stop < ElevatorRound.Stops; stop++)
                if (Mathf.Abs(x - StopWorldX(stop)) < Mathf.Abs(x - StopWorldX(best))) best = stop;
            return best;
        }

        // The stop the car is passing right now, or -1 between stops.
        int StopOnTrack(float x)
        {
            if (OnMainShaft(x)) return CenterStop;
            for (int stop = 0; stop < ElevatorRound.Stops; stop++)
                if (Mathf.Abs(x - StopWorldX(stop)) <= StopReach) return stop;
            return -1;
        }

        // A side stop is only used when its floor rail reaches it without crossing a teleport gate.
        static bool StopReachable(int floor, int stop)
        {
            if (stop == CenterStop) return true;
            float low = Mathf.Min(ElevatorMatchType.ShaftCenterX, StopWorldX(stop)) - 1f;
            float high = Mathf.Max(ElevatorMatchType.ShaftCenterX, StopWorldX(stop)) + 1f;
            foreach (TeleportGate gate in TeleportGates)
                if (gate.Floor == floor && gate.X >= low && gate.X <= high) return false;
            return true;
        }

        public static string StopTag(int floor, int stop)
        {
            switch (stop)
            {
                case 0: return "←←" + floor;
                case 1: return "←" + floor;
                case 3: return floor + "→";
                case 4: return floor + "→→";
                default: return floor.ToString();
            }
        }

        public static string DestinationTag(Rider rider) => StopTag(rider.Destination, rider.DestinationStop);

        static string StopSide(int stop)
        {
            switch (stop)
            {
                case 0: return " FAR LEFT";
                case 1: return " LEFT";
                case 3: return " RIGHT";
                case 4: return " FAR RIGHT";
                default: return "";
            }
        }

        // Stopping also slides the car sideways onto the nearest stop ("lining up").
        void BeginStopLineUp()
        {
            dockingStop = NearestStop(RouteLateralX);
            dockingStartX = RouteLateralX;
            dockingTargetX = StopX(dockingStop);
            dockingSeconds = Mathf.Clamp(Mathf.Max(dockingSeconds, Mathf.Abs(dockingTargetX - dockingStartX) / 12f), .35f, 1f);
            railRiding = false;
            railVelocity = 0f;
        }

        // Reaching an onboard rider's floor and stop docks there by itself; the doors then open as usual.
        // The tutorial keeps the manual stop so its STOP card still teaches the button.
        [SerializeField, Tooltip("Dock automatically on reaching an onboard rider's floor and stop.")]
        bool autoStopAtDestinations = true;

        bool AutoStopAtDestination()
        {
            if (!autoStopAtDestinations || phase != Phase.Moving || MenuManager.TutorialActive) return false;
            int floor = NearbyFloor;
            if (Mathf.Abs(travelFloor - floor) > stopWindow) return false;
            int stop = StopOnTrack(RouteLateralX);
            if (stop < 0 || floor == round.Floor && stop == round.Stop) return false;
            foreach (Rider rider in round.Riders)
                if (round.Owns(rider) && !exiting.ContainsKey(rider)
                    && rider.Destination == floor && rider.DestinationStop == stop)
                {
                    RequestFloorStop();
                    return phase == Phase.Docking;
                }
            return false;
        }

        void LineUpWithStop(float progress)
        {
            routeLateralX = Mathf.Lerp(dockingStartX, dockingTargetX, progress);
            if (progress >= 1f) Match?.SyncTrackFromLateral(this, routeLateralX);
        }

        // The tutorial keeps everyone at the shafts; afterwards riders spread out again.
        public void SetTutorialStops(bool centreOnly)
        {
            if (round == null) return;
            round.CenterStopsOnly = centreOnly;
            ElevatorManager other = Match == null ? null : Match.player == this ? Match.Rival : Match.player;
            if (other != null && other.round != null) other.round.CenterStopsOnly = centreOnly;
            round.AssignStops(centreOnly ? -1 : round.Floor);
        }

        // ---------- Destination beacons (shaft view) ----------
        readonly Dictionary<int, List<Rider>> beaconGroups = new Dictionary<int, List<Rider>>();
        readonly List<Rect> beaconTags = new List<Rect>();
        readonly List<int> beaconOrder = new List<int>();

        void DrawDestinationBeacons()
        {
            if (round == null || exteriorCamera == null || exteriorCar == null) return;
            foreach (List<Rider> group in beaconGroups.Values) group.Clear();
            foreach (Rider rider in round.Riders)
            {
                if (!round.Owns(rider) || exiting.ContainsKey(rider)) continue;
                int key = rider.Destination * ElevatorRound.Stops + rider.DestinationStop;
                if (!beaconGroups.TryGetValue(key, out List<Rider> group))
                    beaconGroups[key] = group = new List<Rider>();
                group.Add(rider);
            }
            Rect view = Match != null ? Match.ViewRect(Seat) : new Rect(0f, 0f, Screen.width, Screen.height);
            float size = Mathf.Max(1f, view.height / 900f);
            beaconTags.Clear();
            beaconOrder.Clear();
            foreach (KeyValuePair<int, List<Rider>> pair in beaconGroups)
                if (pair.Value.Count > 0) beaconOrder.Add(pair.Key);
            // Nearest destinations claim their spot first.
            beaconOrder.Sort((a, b) => Mathf.Abs(a / ElevatorRound.Stops - travelFloor)
                .CompareTo(Mathf.Abs(b / ElevatorRound.Stops - travelFloor)));
            foreach (int key in beaconOrder)
                DrawDestinationBeacon(view, size, key / ElevatorRound.Stops, key % ElevatorRound.Stops, beaconGroups[key]);
        }

        // On screen the landing glows under a bouncing arrow. Off screen an arrow on the view edge points at it.
        void DrawDestinationBeacon(Rect view, float size, int floor, int stop, List<Rider> riders)
        {
            float x = StopWorldX(stop), y = floor * ShaftFloorHeight;
            Vector2 spot = GuiPoint(new Vector3(x, y + 1.6f, BeaconDepth), out bool inFront);
            string label = "F" + floor + StopSide(stop);
            if (inFront && Inset(view, 12f * size, 12f * size, 12f * size, 12f * size).Contains(spot))
                DrawLandingHighlight(view, size, x, y, stop == CenterStop ? 4.6f : 2.6f, label, riders);
            else
                DrawEdgePointer(view, size, spot, inFront, label, riders);
        }

        const float BeaconDepth = CrowdDepth - .8f;

        void DrawLandingHighlight(Rect view, float size, float x, float y, float halfWidth, string label, List<Rider> riders)
        {
            Vector2 left = GuiPoint(new Vector3(x - halfWidth, y + .1f, BeaconDepth), out _);
            Vector2 right = GuiPoint(new Vector3(x + halfWidth, y + .1f, BeaconDepth), out _);
            Vector2 top = GuiPoint(new Vector3(x, y + 4.2f, BeaconDepth), out _);
            float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 6f);
            Rect bar = Rect.MinMaxRect(Mathf.Min(left.x, right.x), left.y - 6f * size,
                Mathf.Max(left.x, right.x), left.y + 2f * size);
            ClippedPanel(Rect.MinMaxRect(bar.xMin, top.y, bar.xMax, bar.yMin), view,
                new Color(Gold.r, Gold.g, Gold.b, .1f + .1f * pulse));
            ClippedPanel(bar, view, new Color(Gold.r, Gold.g, Gold.b, .6f + .4f * pulse));

            float arrow = 34f * size;
            float bounce = Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f)) * 8f * size;
            Vector2 arrowCentre = new Vector2(top.x, top.y - arrow * .5f - bounce);
            DrawBeaconArrow(arrowCentre, 90f, arrow);
            Vector2 tagSize = BeaconTagSize(size, label, riders.Count);
            Rect tag = ClampInto(view, new Rect(top.x - tagSize.x * .5f,
                arrowCentre.y - arrow * .5f - 4f * size - tagSize.y, tagSize.x, tagSize.y));
            tag.position += ClearOfBeaconTags(tag, new Vector2(0f, -(tagSize.y + 4f * size)));
            DrawBeaconTag(ClampInto(view, tag), size, label, riders);
        }

        void DrawEdgePointer(Rect view, float size, Vector2 target, bool inFront, string label, List<Rider> riders)
        {
            // Keep clear of the status HUD and route banner above and the controls below.
            Rect safe = Inset(view, 40f * size, view.height * .16f, 40f * size, view.height * .13f);
            Vector2 outward = inFront ? target - safe.center : safe.center - target;
            if (outward.sqrMagnitude < 1f) outward = Vector2.down;
            float reachX = Mathf.Abs(outward.x) > .01f ? safe.width * .5f / Mathf.Abs(outward.x) : float.MaxValue;
            float reachY = Mathf.Abs(outward.y) > .01f ? safe.height * .5f / Mathf.Abs(outward.y) : float.MaxValue;
            Vector2 pointing = outward.normalized;
            Vector2 tip = safe.center + outward * Mathf.Min(1f, Mathf.Min(reachX, reachY));

            float arrow = 40f * size;
            Vector2 tagSize = BeaconTagSize(size, label, riders.Count);
            float tagReach = Mathf.Abs(pointing.x) * tagSize.x * .5f + Mathf.Abs(pointing.y) * tagSize.y * .5f;
            Vector2 tagCentre = tip - pointing * (arrow + tagReach + 4f * size);
            Rect tag = new Rect(tagCentre - tagSize * .5f, tagSize);
            Rect arrowBox = new Rect(tip - pointing * arrow * .5f - Vector2.one * arrow * .5f, Vector2.one * arrow);

            // Move arrow and tag together: out of the cabin preview, then off earlier tags along the edge.
            Vector2 shift = CabinPreviewClearance(Union(tag, arrowBox), 12f * size);
            tag.position += shift;
            Vector2 step = Mathf.Abs(pointing.x) > Mathf.Abs(pointing.y)
                ? new Vector2(0f, (tag.center.y > view.center.y ? -1f : 1f) * (tagSize.y + 6f * size))
                : new Vector2((tag.center.x > view.center.x ? -1f : 1f) * (tagSize.x + 6f * size), 0f);
            Vector2 slide = ClearOfBeaconTags(tag, step);
            tag.position += slide;
            shift += slide;

            Vector2 arrowCentre = arrowBox.center + shift;
            if (inFront && (target - arrowCentre).sqrMagnitude > 1f) pointing = (target - arrowCentre).normalized;
            float nudge = (.5f + .5f * Mathf.Sin(Time.unscaledTime * 7f)) * 6f * size;
            DrawBeaconArrow(arrowCentre + pointing * nudge, Mathf.Atan2(pointing.y, pointing.x) * Mathf.Rad2Deg, arrow);
            DrawBeaconTag(ClampInto(view, tag), size, label, riders);
        }

        Vector2 BeaconTagSize(float size, string text, int riderCount)
        {
            int previousSize = calloutNumber.fontSize;
            calloutNumber.fontSize = Mathf.RoundToInt(20f * size);
            float textWidth = calloutNumber.CalcSize(new GUIContent(text)).x + 12f * size;
            calloutNumber.fontSize = previousSize;
            int icons = Mathf.Min(3, riderCount);
            return new Vector2(10f * size + icons * 28f * size + textWidth, 36f * size);
        }

        void DrawBeaconTag(Rect tag, float size, string text, List<Rider> riders)
        {
            beaconTags.Add(tag);
            FramedPanel(tag, Gold, 3f);
            float icon = 26f * size, cursor = tag.x + 6f * size;
            for (int i = 0; i < Mathf.Min(3, riders.Count); i++)
            {
                Rider rider = riders[i];
                DrawPassengerBadgeIcon(new Rect(cursor, tag.y + (tag.height - icon) * .5f, icon, icon),
                    rider.Kind, Palette[rider.Color % Palette.Length]);
                cursor += icon + 2f * size;
            }
            int previousSize = calloutNumber.fontSize;
            calloutNumber.fontSize = Mathf.RoundToInt(20f * size);
            OutlinedLabel(new Rect(cursor, tag.y, tag.xMax - cursor - 2f, tag.height), text, calloutNumber, Gold);
            calloutNumber.fontSize = previousSize;
        }

        // Slide a tag by `step` until it clears the tags already drawn this frame.
        Vector2 ClearOfBeaconTags(Rect tag, Vector2 step)
        {
            Vector2 shift = Vector2.zero;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Rect moved = new Rect(tag.position + shift, tag.size);
                bool overlaps = false;
                foreach (Rect used in beaconTags)
                    if (moved.Overlaps(used)) { overlaps = true; break; }
                if (!overlaps) break;
                shift += step;
            }
            return shift;
        }

        // The smallest move (left or up) that takes `area` off the cabin preview.
        Vector2 CabinPreviewClearance(Rect area, float pad)
        {
            if (!showCabinPreview || eye == null || !eye.enabled) return Vector2.zero;
            Rect pixels = eye.pixelRect;
            Rect cabin = new Rect(pixels.x - pad, Screen.height - pixels.yMax - pad,
                pixels.width + pad * 2f, pixels.height + pad * 2f);
            if (!cabin.Overlaps(area)) return Vector2.zero;
            float left = area.xMax - cabin.xMin, up = area.yMax - cabin.yMin;
            return left < up ? new Vector2(-left, 0f) : new Vector2(0f, -up);
        }

        void DrawBeaconArrow(Vector2 centre, float degrees, float length)
        {
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColour = GUI.color;
            GUIUtility.RotateAroundPivot(degrees, centre);
            GUI.color = Gold;
            GUI.DrawTexture(new Rect(centre.x - length * .5f, centre.y - length * .5f, length, length), BeaconArrowTexture());
            GUI.color = previousColour;
            GUI.matrix = previousMatrix;
        }

        // A flat right-pointing HUD triangle with a dark rim; tinted gold when drawn.
        static Texture2D beaconArrowTexture;
        static Texture2D BeaconArrowTexture()
        {
            if (beaconArrowTexture != null) return beaconArrowTexture;
            const int size = 64;
            const float baseU = .1f, tipU = .95f, halfBase = .42f, rim = .08f;
            float slant = (tipU - baseU) / Mathf.Sqrt((tipU - baseU) * (tipU - baseU) + halfBase * halfBase);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size, v = Mathf.Abs((y + .5f) / size - .5f);
                    float halfHeight = halfBase * (tipU - u) / (tipU - baseU);
                    float inside = Mathf.Min(u - baseU, (halfHeight - v) * slant);
                    byte alpha = (byte)(Mathf.Clamp01(inside * size + .5f) * 255f);
                    pixels[y * size + x] = inside > rim
                        ? new Color32(255, 255, 255, alpha)
                        : new Color32(20, 18, 28, alpha);
                }
            beaconArrowTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            beaconArrowTexture.SetPixels32(pixels);
            beaconArrowTexture.Apply(false, true);
            return beaconArrowTexture;
        }

        static Rect Inset(Rect r, float left, float top, float right, float bottom)
            => Rect.MinMaxRect(r.xMin + left, r.yMin + top, r.xMax - right, r.yMax - bottom);

        static Rect Union(Rect a, Rect b)
            => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        static Rect ClampInto(Rect view, Rect r)
        {
            r.x = Mathf.Clamp(r.x, view.x + 6f, view.xMax - r.width - 6f);
            r.y = Mathf.Clamp(r.y, view.y + 6f, view.yMax - r.height - 6f);
            return r;
        }

        Vector2 GuiPoint(Vector3 local, out bool inFront)
        {
            Vector3 screen = exteriorCamera.WorldToScreenPoint(exteriorCar.parent.TransformPoint(local));
            inFront = screen.z > 0f;
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        // ---------- Waiting crowd (shaft view) ----------
        // The shared exterior world belongs to seat 0, so the crowd and hazards are spawned once.
        ElevatorManager WorldOwner => Match != null && Match.player != null ? Match.player : this;

        sealed class CrowdFigure
        {
            public Transform Root;
            public float X, Home, Wander, WanderTimer, Clock, Yaw = 180f, Leave = -1f;
            public bool Boarding;
        }

        const float CrowdScale = 1.2f;
        const float CrowdDepth = 1.5f;
        const float CrowdFloorLift = .08f;
        const float CrowdWalkSpeed = 2.6f;
        const float CrowdLeaveSeconds = .7f;
        const float CrowdFloorRange = 3f;
        static readonly float[] CrowdSlotOffsets = { 0f, -1.35f, 1.35f, -2.7f, 2.7f };
        readonly Dictionary<Rider, CrowdFigure> crowd = new Dictionary<Rider, CrowdFigure>();
        readonly Dictionary<int, int> crowdSlots = new Dictionary<int, int>();
        Transform crowdRoot;

        void UpdateShaftCrowd(float dt)
        {
            if (WorldOwner != this || round == null || exteriorCar == null || sceneView.passengers == null) return;
            if (crowdRoot == null)
            {
                crowdRoot = new GameObject("Waiting crowd").transform;
                crowdRoot.SetParent(exteriorCar.parent, false);
            }
            float low = CurrentFloor, high = CurrentFloor;
            ElevatorManager rival = Match != null ? Match.Rival : null;
            if (rival != null)
            {
                low = Mathf.Min(low, rival.CurrentFloor);
                high = Mathf.Max(high, rival.CurrentFloor);
            }
            crowdSlots.Clear();
            foreach (Rider rider in round.Riders)
            {
                crowd.TryGetValue(rider, out CrowdFigure figure);
                bool waiting = rider.Origin <= MaxTravelFloor && rider.Origin >= low - CrowdFloorRange
                    && rider.Origin <= high + CrowdFloorRange && round.IsQueuedAtStop(rider);
                if (waiting)
                {
                    int key = rider.Origin * ElevatorRound.Stops + rider.OriginStop;
                    crowdSlots.TryGetValue(key, out int slot);
                    crowdSlots[key] = slot + 1;
                    float home = StopWorldX(rider.OriginStop) + CrowdSlotOffsets[slot % CrowdSlotOffsets.Length];
                    if (figure == null) figure = SpawnCrowdFigure(rider, home);
                    if (figure == null) continue;
                    figure.Home = home;
                    AnimateCrowdFigure(rider, figure, dt);
                    continue;
                }
                if (figure == null) continue;
                if (figure.Leave < 0f && (rider.Boarded || rider.Resolved))
                {
                    figure.Leave = 0f;
                    figure.Boarding = rider.Boarded;
                }
                if (figure.Leave >= 0f && AnimateCrowdExit(rider, figure, dt)) continue;
                if (figure.Root != null) Destroy(figure.Root.gameObject);
                crowd.Remove(rider);
            }
        }

        CrowdFigure SpawnCrowdFigure(Rider rider, float home)
        {
            PassengerView template = null;
            foreach (PassengerView art in sceneView.passengers)
                if (art != null && art.ResolvedKind == rider.Kind) template = art;
            if (template == null) return null;
            PassengerView view = Instantiate(template, crowdRoot, false);
            view.name = "Outside • " + rider.Name;
            Color color = Palette[rider.Color % Palette.Length];
            foreach (Renderer jacket in view.jackets) if (jacket != null) jacket.sharedMaterial = Mat(color);
            if (view.speech != null) view.speech.gameObject.SetActive(false);
            if (view.destination != null) view.destination.gameObject.SetActive(false);
            foreach (Collider part in view.GetComponentsInChildren<Collider>(true)) part.enabled = false;
            foreach (Transform part in view.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = ExteriorLayer;
            // New riders stroll in from the outer side of their stop.
            float side = home >= ElevatorMatchType.ShaftCenterX ? 1f : -1f;
            var figure = new CrowdFigure
            {
                Root = view.transform, X = home + side * 5f, Home = home,
                Clock = Random.value * 10f, WanderTimer = Random.Range(.5f, 2.5f)
            };
            crowd[rider] = figure;
            return figure;
        }

        // Short strolls around the slot, then a Wii-style idle bounce facing the camera.
        void AnimateCrowdFigure(Rider rider, CrowdFigure figure, float dt)
        {
            figure.Clock += dt;
            figure.WanderTimer -= dt;
            if (figure.WanderTimer <= 0f)
            {
                figure.Wander = Random.Range(-.7f, .7f);
                figure.WanderTimer = Random.Range(2f, 4.5f);
            }
            float goal = figure.Home + figure.Wander;
            float before = figure.X;
            float speed = Mathf.Abs(goal - figure.X) > 1.5f ? CrowdWalkSpeed : 1.1f;
            figure.X = Mathf.MoveTowards(figure.X, goal, speed * dt);
            PlaceCrowdFigure(rider, figure, figure.X - before, 1f, dt);
        }

        // Boarders walk into the car; riders who give up walk away. Returns false when done.
        bool AnimateCrowdExit(Rider rider, CrowdFigure figure, float dt)
        {
            figure.Leave += dt;
            figure.Clock += dt;
            float progress = Mathf.Clamp01(figure.Leave / CrowdLeaveSeconds);
            float away = figure.Home >= ElevatorMatchType.ShaftCenterX ? 4f : -4f;
            float goal = figure.Boarding ? StopWorldX(rider.OriginStop) : figure.Home + away;
            float before = figure.X;
            figure.X = Mathf.MoveTowards(figure.X, goal, CrowdWalkSpeed * 1.4f * dt);
            float shrink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.4f, 1f, progress));
            PlaceCrowdFigure(rider, figure, figure.X - before, 1f - shrink, dt);
            return progress < 1f;
        }

        void PlaceCrowdFigure(Rider rider, CrowdFigure figure, float step, float scale, float dt)
        {
            bool walking = Mathf.Abs(step) > .2f * dt;
            float wave = Mathf.Sin(figure.Clock * (walking ? 10f : IdleBobSpeed) + IdleBobPhase(rider));
            float bob = walking ? Mathf.Abs(wave) * .14f : IdleBobHeight * 2f * wave * wave;
            float facing = walking ? (step > 0f ? 90f : -90f) : 180f;
            figure.Yaw = Mathf.MoveTowardsAngle(figure.Yaw, facing, 540f * dt);
            figure.Root.localPosition = new Vector3(figure.X,
                rider.Origin * ShaftFloorHeight + CrowdFloorLift + bob, CrowdDepth);
            figure.Root.localRotation = Quaternion.Euler(0f, figure.Yaw, wave * (walking ? 6f : IdleWaddleDegrees * 1.5f));
            figure.Root.localScale = Vector3.one * (CrowdScale * Mathf.Max(.01f, scale));
        }

        void ClearShaftCrowd()
        {
            foreach (CrowdFigure figure in crowd.Values)
                if (figure.Root != null) Destroy(figure.Root.gameObject);
            crowd.Clear();
        }

        // ---------- CPU navigation ----------
        int npcTargetStop = CenterStop;

        // Pick the nearest useful stop: an onboard rider's destination or a waiting queue.
        public void NpcChooseFloor()
        {
            if (!IsBoarding || round == null) return;
            if (!FindNpcTarget(out int target, out int stop))
            {
                target = round.Floor < MaxTravelFloor ? round.Floor + 1 : Mathf.Max(0, round.Floor - 1);
                stop = CenterStop;
            }
            npcTargetFloor = Mathf.Clamp(target, 0, MaxTravelFloor);
            npcTargetStop = stop;
            if (npcTargetFloor != round.Floor) travelDirection = npcTargetFloor > round.Floor ? 1 : -1;
            CloseAndTravel();
        }

        public void NpcDriveToStop()
        {
            if (phase != Phase.Moving || round == null || npcTargetFloor < 0) return;
            float targetX = StopX(npcTargetStop);
            if (railRiding)
            {
                NpcDriveRail(targetX);
                return;
            }
            if (npcTargetFloor == round.Floor && npcTargetStop == round.Stop
                && Mathf.Abs(travelFloor - round.Floor) > 1f)
                RetargetNpcAfterCollision();
            if (Mathf.Abs(npcTargetFloor - travelFloor) > .05f)
            {
                int targetDirection = npcTargetFloor > travelFloor ? 1 : -1;
                if (targetDirection != travelDirection)
                {
                    travelDirection = targetDirection;
                    travelVelocity = targetDirection * Mathf.Abs(travelVelocity);
                }
            }
            boostAxis = travelDirection;
            boostHeld = true;
            bool onTargetTrack = npcTargetStop == CenterStop
                ? OnMainShaft(RouteLateralX) : Mathf.Abs(RouteLateralX - targetX) <= StopReach;
            if (onTargetTrack)
            {
                if (!CanStopAtFloor || NearbyFloor != npcTargetFloor) return;
                boostAxis = 0;
                boostHeld = false;
                RequestFloorStop();
            }
            else if (Mathf.Abs(npcTargetFloor - travelFloor) < 1f)
                pendingRailDirection = targetX > RouteLateralX ? 1 : -1;
        }

        // On the target floor's rail: ride to the stop. Otherwise head for the shafts and climb.
        void NpcDriveRail(float targetX)
        {
            boostAxis = 0f;
            boostHeld = false;
            if (railFloor == npcTargetFloor)
            {
                float distance = targetX - routeLateralX;
                routeSteer = Mathf.Sign(distance);
                railBoost = Mathf.Abs(distance) > 6f;
                if (Mathf.Abs(distance) <= 2.5f) RequestFloorStop();
                return;
            }
            float toCentre = ElevatorMatchType.ShaftCenterX - routeLateralX;
            routeSteer = Mathf.Abs(toCentre) > .5f ? Mathf.Sign(toCentre) : 0f;
            pendingTrackDirection = npcTargetFloor > railFloor ? 1 : -1;
        }

        bool FindNpcTarget(out int floor, out int stop)
        {
            floor = -1;
            stop = CenterStop;
            float best = float.MaxValue;
            foreach (Rider rider in round.Riders)
            {
                int riderFloor, riderStop;
                if (round.Owns(rider)) { riderFloor = rider.Destination; riderStop = rider.DestinationStop; }
                else if (round.IsQueuedAtStop(rider) && round.HasCapacityFor(rider))
                { riderFloor = rider.Origin; riderStop = rider.OriginStop; }
                else continue;
                if (riderFloor > MaxTravelFloor || riderFloor == round.Floor && riderStop == round.Stop) continue;
                float cost = Mathf.Abs(riderFloor - round.Floor)
                    + Mathf.Abs(StopOffsets[riderStop] - StopOffsets[round.Stop]) / 11f;
                if (cost >= best) continue;
                best = cost;
                floor = riderFloor;
                stop = riderStop;
            }
            return floor >= 0;
        }
    }
}
