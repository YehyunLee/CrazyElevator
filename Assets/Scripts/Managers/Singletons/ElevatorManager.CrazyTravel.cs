using System.Collections.Generic;
using UnityEngine;
using CrazyElevator.Shared;
using ElevatorMatchType = CrazyElevator.Match.ElevatorMatch;

namespace CrazyElevator.Managers
{
    // Logical floor stays simple. The route network is:
    //   - two full-height centre shafts,
    //   - a horizontal rail on every floor (hold A/D at a floor to ride it),
    //   - short side tracks left and right that end after a few floors,
    //   - teleport gates on some floor rails (the big number is the destination).
    // WorldBandShaftBuilder bakes the visuals from the same tables below.
    public sealed partial class ElevatorManager
    {
        public struct SideTrack
        {
            public float X;
            public int Bottom, Top;
            public SideTrack(float x, int bottom, int top) { X = x; Bottom = bottom; Top = top; }
        }

        public struct TeleportGate
        {
            public float X;
            public int Floor, Target;
            public TeleportGate(float x, int floor, int target) { X = x; Floor = floor; Target = target; }
        }

        const string CrazyRouteMapName = "Crazy vertical route map";
        public const string TeleportGateRootName = "Teleport Gates";
        // A late turn press still catches a floor / track the car only just passed.
        const float RailCatchGrace = .15f;
        const float TrackCatchGrace = .6f;
        const float RailSettleSpeed = 4.5f;
        const float TrackGlideSpeed = 14f;
        const float ShaftAlignReach = 1.35f;
        // Floor rails span the whole building, both wings included.
        public const float GroundTrackSpan = 29f;
        const float SoloShaftOffset = 2.5f;
        const float TeleportCooldownSeconds = .6f;
        const float TeleportFlashSeconds = 1.4f;

        // Side track columns (offset from the shaft centre) and where their first
        // segment starts. Each segment is SideTrackLength floors, then a gap.
        const int SideTrackLength = 3;
        const int SideTrackPeriod = 5;
        static readonly float[] SideTrackOffsets = { -25f, -14f, 14f, 25f };
        static readonly int[] SideTrackStarts = { 0, 2, 1, 3 };
        public static readonly SideTrack[] SideTracks = BuildSideTracks();

        // Gates sit on floor rails between the side columns. Target floors never
        // have a gate at the same x, so a warp cannot chain into another warp.
        public static readonly TeleportGate[] TeleportGates =
        {
            Gate(-20f, 1, 6), Gate(8f, 3, 7), Gate(20f, 4, 0),
            Gate(-20f, 7, 11), Gate(20f, 9, 14), Gate(-8f, 12, 4),
            Gate(-20f, 14, 19), Gate(8f, 17, 22), Gate(20f, 20, 10),
            Gate(-20f, 23, 15)
        };

        Transform crazyRouteMap;
        Transform[] teleportGateViews;
        float routeBoostPulse;
        float routeLateralX;
        bool routeLateralReady;
        bool railRiding;
        int railFloor;
        // Rail speed uses the same floors/s, cruise and boost as vertical travel.
        float railVelocity;
        int railDirection = 1;
        // The next turn the player asked for: onto a rail (A/D) or a track (W/S).
        int pendingRailDirection, pendingTrackDirection;
        int lastRouteHorizontal;
        bool laneSwitchHeld;
        // The rail / track just left can't be re-caught, so a held diagonal climbs like stairs.
        int railCatchBlockFloor = -1;
        float trackCatchBlockX = float.NaN;
        float routeSteer;
        bool railBoost;
        float teleportCooldown;
        float teleportFlash;
        int teleportFlashTarget;
        float cameraFollowBlend;
        Vector2 cameraFraming;
        int cameraFramingFrame = -10;
        const float CameraFollowRate = 9f;
        const float CameraSnapDistance = 80f;

        static TeleportGate Gate(float offset, int floor, int target) =>
            new TeleportGate(ElevatorMatchType.ShaftCenterX + offset, floor, target);

        static SideTrack[] BuildSideTracks()
        {
            var tracks = new List<SideTrack>();
            int topFloor = ElevatorRound.Floors - 1;
            for (int column = 0; column < SideTrackOffsets.Length; column++)
            {
                float x = ElevatorMatchType.ShaftCenterX + SideTrackOffsets[column];
                for (int start = SideTrackStarts[column]; start < topFloor; start += SideTrackPeriod)
                {
                    int top = Mathf.Min(start + SideTrackLength, topFloor);
                    if (top - start >= 2) tracks.Add(new SideTrack(x, start, top));
                }
            }
            return tracks.ToArray();
        }

        public float RouteLateralX => routeLateralReady ? routeLateralX : DefaultRouteLateralX;
        float DefaultRouteLateralX => Match != null
            ? Match.LaneX(this)
            : ElevatorMatchType.ShaftCenterX - SoloShaftOffset;
        float LeftShaftX => Match != null
            ? Match.ShaftX(0)
            : ElevatorMatchType.ShaftCenterX - SoloShaftOffset;
        float RightShaftX => Match != null
            ? Match.ShaftX(1)
            : ElevatorMatchType.ShaftCenterX + SoloShaftOffset;
        float GroundLeftX => ElevatorMatchType.ShaftCenterX - GroundTrackSpan;
        float GroundRightX => ElevatorMatchType.ShaftCenterX + GroundTrackSpan;

        public bool OnFloorRail => phase == Phase.Moving && railRiding;

        // The two centre shafts plus the lane-change space between them.
        bool OnMainShaft(float x) => x >= LeftShaftX - ShaftAlignReach && x <= RightShaftX + ShaftAlignReach;

        Vector3 CrazyTravelLocalPosition(float baseX, float logicalFloor)
        {
            logicalFloor = Mathf.Clamp(logicalFloor, 0f, ElevatorRound.Floors - 1f);
            float y = logicalFloor * ShaftFloorHeight + 1.4f;
            return new Vector3(baseX, y, -.8f);
        }

        Quaternion CrazyTravelLocalRotation(float logicalFloor, float collisionKick)
        {
            return exteriorCarHomeRotation * Quaternion.Euler(
                0f, CarSpinDegrees, collisionKick * (Seat == 0 ? -4.5f : 4.5f));
        }

        void ResetRouteLateral()
        {
            routeLateralX = Match != null
                ? Match.LaneX(this)
                : ElevatorMatchType.ShaftCenterX + (Seat == 0 ? -SoloShaftOffset : SoloShaftOffset);
            routeLateralReady = true;
            railRiding = false;
            railVelocity = 0f;
            ClearRouteTurns();
            teleportCooldown = teleportFlash = 0f;
        }

        void ClearRouteTurns()
        {
            pendingRailDirection = pendingTrackDirection = 0;
            railCatchBlockFloor = -1;
            trackCatchBlockX = float.NaN;
        }

        // Vertical track under x at this floor: a centre shaft or a short side track.
        bool TryGetVerticalTrack(float x, float floor, out float trackX, out float bottom, out float top)
        {
            trackX = NearestShaftX(x);
            bottom = 0f;
            top = ElevatorRound.Floors - 1f;
            if (OnMainShaft(x)) return true;
            foreach (SideTrack track in SideTracks)
            {
                if (Mathf.Abs(x - track.X) > ShaftAlignReach) continue;
                if (floor < track.Bottom - .02f || floor > track.Top + .02f) continue;
                trackX = track.X;
                bottom = track.Bottom;
                top = track.Top;
                return true;
            }
            return false;
        }

        static int AxisSign(float axis) => axis >= .5f ? 1 : axis <= -.5f ? -1 : 0;
        int ClampRailFloor(float floor) => Mathf.Clamp(Mathf.RoundToInt(floor), 0, MaxTravelFloor);
        int TowardCentre => routeLateralX < ElevatorMatchType.ShaftCenterX ? 1 : -1;
        float RouteSpeed => railRiding ? Mathf.Abs(railVelocity) : Mathf.Abs(travelVelocity);

        // The cross axis wins, so the car never has to stop to turn. Holding or tapping
        // W/S on a rail takes the next track; A/D on a track takes the next floor rail.
        // A tap stays queued after release.
        void ReadRouteInput(float horizontal, float vertical, bool boost)
        {
            routeSteer = horizontal;
            railBoost = boost;
            int h = AxisSign(horizontal), v = AxisSign(vertical);
            bool horizontalPressed = h != 0 && h != lastRouteHorizontal;
            lastRouteHorizontal = h;
            if (h == 0) laneSwitchHeld = false;
            if (railRiding)
            {
                if (v != 0) pendingTrackDirection = v;
                return;
            }
            if (horizontalPressed) laneSwitchHeld = TrySwitchDuelLane(h);
            if (h != 0 && !laneSwitchHeld) pendingRailDirection = h;
        }

        // In 1v1, A/D between the centre shafts still changes lane first.
        bool TrySwitchDuelLane(int direction)
        {
            if (Match == null || !OnMainShaft(RouteLateralX)) return false;
            int lane = Match.TrackNumber(this);
            Match.RequestTrack(this, direction);
            return Match.TrackNumber(this) != lane;
        }

        // Called once per frame around the travel steps.
        void BeginRouteFrame(float dt)
        {
            if (!routeLateralReady) ResetRouteLateral();
            teleportCooldown = Mathf.Max(0f, teleportCooldown - dt);
            teleportFlash = Mathf.Max(0f, teleportFlash - dt);
        }

        void EndRouteFrame(float dt)
        {
            if (!railRiding) FollowVerticalTrack(dt);
            routeSteer = 0f;
            railBoost = false;
        }

        // Momentum carries through the corner: the new axis starts at the old speed.
        void StartRailRide(int floor, int direction, float carriedSpeed)
        {
            railRiding = true;
            railFloor = floor;
            railDirection = direction >= 0 ? 1 : -1;
            railVelocity = railDirection * Mathf.Min(SpeedLimit, Mathf.Max(CruiseSpeed, carriedSpeed));
            travelVelocity = 0f;
            boostAxis = 0f;
            boostHeld = false;
            ClearRouteTurns();
            trackCatchBlockX = routeLateralX;
        }

        void LeaveRailOntoTrack()
        {
            int direction = pendingTrackDirection;
            float speed = Mathf.Min(SpeedLimit, Mathf.Max(CruiseSpeed, Mathf.Abs(railVelocity)));
            railRiding = false;
            travelFloor = railFloor;
            travelDirection = direction;
            travelVelocity = direction * speed;
            railVelocity = 0f;
            ClearRouteTurns();
            railCatchBlockFloor = railFloor;
        }

        // Same cruise / boost / instant-reverse rules as vertical travel (AdvanceTravelStep).
        void SteerRail(float dt)
        {
            railVelocity = Mathf.Clamp(railVelocity, -SpeedLimit, SpeedLimit);
            int steer = AxisSign(routeSteer);
            if (steer != 0 && steer != railDirection)
            {
                if (railVelocity * steer <= 0f) railVelocity = steer * CruiseSpeed;
                railDirection = steer;
            }
            if (railBoost && steer != 0)
                railVelocity = Mathf.Clamp(railVelocity + steer * CurrentAcceleration * dt, -SpeedLimit, SpeedLimit);
            else if (railVelocity * railDirection < CruiseSpeed)
                railVelocity = Mathf.MoveTowards(railVelocity, railDirection * CruiseSpeed,
                    CurrentAcceleration * .7f * dt);
        }

        void AdvanceRailStep(float dt)
        {
            travelFloor = Mathf.MoveTowards(travelFloor, railFloor, RailSettleSpeed * dt);
            SteerRail(dt);
            float before = routeLateralX;
            routeLateralX += railVelocity * ShaftFloorHeight * dt;
            if (routeLateralX < GroundLeftX || routeLateralX > GroundRightX)
            {
                routeLateralX = Mathf.Clamp(routeLateralX, GroundLeftX, GroundRightX);
                railVelocity = 0f;
                notice = "End of the rail. Steer back, or press W/S to take a track.";
            }
            CheckTeleportGates(before, routeLateralX);
            if (pendingTrackDirection != 0) TryTurnOntoTrack(before, routeLateralX);
            Match?.SyncTrackFromLateral(this, routeLateralX);
        }

        void TryTurnOntoTrack(float fromX, float toX)
        {
            if (Mathf.Abs(toX - trackCatchBlockX) > ShaftAlignReach) trackCatchBlockX = float.NaN;
            int topFloor = ElevatorRound.Floors - 1;
            bool found = TrackCatches(fromX, toX, LeftShaftX, 0, topFloor)
                || TrackCatches(fromX, toX, RightShaftX, 0, topFloor);
            foreach (SideTrack track in SideTracks)
                found = found || TrackCatches(fromX, toX, track.X, track.Bottom, track.Top);
            if (found) LeaveRailOntoTrack();
        }

        // The car passed (or is right at) a track that continues the queued way.
        bool TrackCatches(float fromX, float toX, float trackX, int bottom, int top)
        {
            if (Mathf.Abs(trackX - trackCatchBlockX) <= ShaftAlignReach) return false;
            if (railFloor < bottom || railFloor > top) return false;
            bool continues = pendingTrackDirection > 0
                ? railFloor < Mathf.Min(top, MaxTravelFloor)
                : railFloor > bottom;
            if (!continues) return false;
            bool crossed = (fromX - trackX) * (toX - trackX) <= 0f;
            return crossed || Mathf.Abs(toX - trackX) <= TrackCatchGrace;
        }

        bool TryTurnOntoRail(float floorPosition)
        {
            if (railCatchBlockFloor >= 0 && Mathf.Abs(floorPosition - railCatchBlockFloor) > RailCatchGrace)
                railCatchBlockFloor = -1;
            if (pendingRailDirection == 0) return false;
            int floor = Mathf.RoundToInt(floorPosition);
            if (floor < 0 || floor > MaxTravelFloor || floor == railCatchBlockFloor
                || Mathf.Abs(floorPosition - floor) > RailCatchGrace)
                return false;
            StartRailRide(floor, pendingRailDirection, Mathf.Abs(travelVelocity));
            return true;
        }

        void FollowVerticalTrack(float dt)
        {
            float targetX = Match != null && OnMainShaft(routeLateralX) ? Match.LaneX(this)
                : TryGetVerticalTrack(routeLateralX, travelFloor, out float trackX, out _, out _) ? trackX
                : routeLateralX;
            routeLateralX = Mathf.MoveTowards(routeLateralX, targetX, TrackGlideSpeed * dt);
        }

        // Vertical travel stays on its track. A queued A/D turn takes the next floor
        // rail; the broken end of a side track rolls straight onto its floor rail.
        float ConstrainToRoute(float before, float candidate, ref float velocity)
        {
            if (!TryGetVerticalTrack(RouteLateralX, before, out _, out float bottom, out float top))
            {
                StartRailRide(ClampRailFloor(before), TowardCentre, Mathf.Abs(velocity));
                return before;
            }
            top = Mathf.Min(top, MaxTravelFloor);
            float clamped = Mathf.Clamp(candidate, bottom, top);
            if (TryTurnOntoRail(clamped)) return railFloor;
            if (!Mathf.Approximately(clamped, candidate))
            {
                if (!OnMainShaft(RouteLateralX))
                    StartRailRide(ClampRailFloor(clamped), TowardCentre, Mathf.Abs(velocity));
                velocity = 0f;
            }
            return clamped;
        }

        // Departing a floor off the centre shafts starts on that floor's rail.
        void PrepareRouteDeparture()
        {
            railRiding = false;
            railVelocity = 0f;
            ClearRouteTurns();
            teleportFlash = 0f;
            if (!TryGetVerticalTrack(RouteLateralX, travelFloor, out _, out _, out _))
                StartRailRide(ClampRailFloor(travelFloor), TowardCentre, 0f);
        }

        bool TeleportGateOpen(TeleportGate gate) =>
            gate.Floor <= MaxTravelFloor && gate.Target <= MaxTravelFloor;

        void CheckTeleportGates(float fromX, float toX)
        {
            if (teleportCooldown > 0f || Mathf.Approximately(fromX, toX)) return;
            foreach (TeleportGate gate in TeleportGates)
            {
                if (gate.Floor != railFloor || !TeleportGateOpen(gate)) continue;
                bool crossed = fromX < gate.X ? toX >= gate.X : toX <= gate.X;
                if (!crossed) continue;
                ApplyTeleportGate(gate);
                return;
            }
        }

        void ApplyTeleportGate(TeleportGate gate)
        {
            railFloor = gate.Target;
            travelFloor = gate.Target;
            teleportCooldown = TeleportCooldownSeconds;
            teleportFlash = TeleportFlashSeconds;
            teleportFlashTarget = gate.Target;
            routeBoostPulse = 1f;
            notice = "TELEPORT! Warped to floor " + gate.Target + ".";
            Play(chime, 1.05f);
        }

        float NearestShaftX(float x)
        {
            return Mathf.Abs(x - LeftShaftX) <= Mathf.Abs(x - RightShaftX)
                ? LeftShaftX : RightShaftX;
        }

        void UpdateCrazyTravelCamera(Vector3 carLocal)
        {
            Transform world = exteriorCar.parent;
            // Follow the car across the building on rails and side tracks;
            // stay framed on the centre shafts otherwise.
            float dt = Time.unscaledDeltaTime;
            bool followCar = railRiding || !OnMainShaft(carLocal.x);
            cameraFollowBlend = Mathf.MoveTowards(cameraFollowBlend, followCar ? 1f : 0f, dt * 1.6f);
            float cameraX = Mathf.Lerp(ElevatorMatchType.ShaftCenterX, carLocal.x,
                Mathf.Lerp(.18f, 1f, Mathf.SmoothStep(0f, 1f, cameraFollowBlend)));
            // A short lag rounds off rail/track corners and turns a warp into a quick swoop.
            Vector2 framing = new Vector2(cameraX, carLocal.y);
            bool snap = cameraFramingFrame < 0
                || (framing - cameraFraming).sqrMagnitude > CameraSnapDistance * CameraSnapDistance;
            cameraFraming = snap ? framing
                : Vector2.Lerp(cameraFraming, framing, 1f - Mathf.Exp(-CameraFollowRate * dt));
            cameraFramingFrame = Time.frameCount;
            cameraX = cameraFraming.x;
            // Frame the car and nearby queues closely enough to recognise riders.
            // Keep the same shallow downward view while bringing the camera in.
            Vector3 cameraLocal = new Vector3(
                cameraX,
                cameraFraming.y + 2.9f,
                -16f + Mathf.Max(0f, carLocal.z + .8f) * .18f);

            routeBoostPulse = Mathf.MoveTowards(routeBoostPulse, 0f, Time.unscaledDeltaTime * 1.55f);
            if (phase == Phase.Moving)
            {
                float clock = Time.unscaledTime;
                float strength = shaftTravelShake * travelShakeBlend;
                cameraLocal += new Vector3(Mathf.Sin(clock * 15f), Mathf.Sin(clock * 21f) * .65f, 0f)
                    * strength;
            }

            Vector3 targetLocal = new Vector3(cameraX, cameraFraming.y + .9f, carLocal.z);
            exteriorCamera.transform.position = world.TransformPoint(cameraLocal);
            Vector3 targetWorld = world.TransformPoint(targetLocal);
            exteriorCamera.transform.rotation = Quaternion.LookRotation(
                targetWorld - exteriorCamera.transform.position, world.up);
            exteriorCamera.orthographic = false;
            UpdateSpeedFx(dt);
            // The view widens as the car speeds up, so going fast reads at a glance.
            exteriorCamera.fieldOfView = Mathf.Lerp(61f + SpeedFovWiden * speedFx, 55f, routeBoostPulse);
        }

        // ---------- Speed feedback ----------
        // 0 at cruising speed, 1 at the speed limit. Drives the FOV, speed lines and car trail.
        const float SpeedFovWiden = 10f;
        float speedFx;
        Renderer[] exteriorCarRenderers;

        void UpdateSpeedFx(float dt)
        {
            float target = phase == Phase.Moving && !paused
                ? Mathf.InverseLerp(CruiseSpeed * 1.08f, SpeedLimit, RouteSpeed) : 0f;
            speedFx = Mathf.MoveTowards(speedFx, target, dt * (target > speedFx ? 3f : 1.6f));
        }

        static float Hash01(int n) => Mathf.Repeat(Mathf.Sin(n * 12.9898f) * 43758.5453f, 1f);

        void DrawSpeedLines()
        {
            if (phase != Phase.Moving || speedFx <= .02f) return;
            Rect view = Match != null ? Match.ViewRect(Seat) : new Rect(0f, 0f, Screen.width, Screen.height);
            float size = Mathf.Max(1f, view.height / 900f);
            bool sideways = railRiding;
            int direction = sideways ? (railVelocity >= 0f ? 1 : -1) : (travelVelocity >= 0f ? 1 : -1);
            DrawSpeedEdgeTint(view, sideways);
            DrawSpeedStreaks(view, size, sideways, direction);
            DrawCarTrail(view, size, sideways, direction);
        }

        // The world's colour creeps in from the edges the car is racing along.
        void DrawSpeedEdgeTint(Rect view, bool sideways)
        {
            Color tint = WorldAccent(NearbyFloor);
            tint.a = .08f * speedFx;
            for (int band = 1; band <= 3; band++)
            {
                float depth = band * .045f;
                if (sideways)
                {
                    Panel(new Rect(view.x, view.y, view.width, view.height * depth), tint);
                    Panel(new Rect(view.x, view.yMax - view.height * depth, view.width, view.height * depth), tint);
                }
                else
                {
                    Panel(new Rect(view.x, view.y, view.width * depth, view.height), tint);
                    Panel(new Rect(view.xMax - view.width * depth, view.y, view.width * depth, view.height), tint);
                }
            }
        }

        // Wind streaks in the outer bands stream past opposite to the motion.
        void DrawSpeedStreaks(Rect view, float size, bool sideways, int direction)
        {
            int count = Mathf.RoundToInt(Mathf.Lerp(6f, 24f, speedFx));
            float clock = Time.unscaledTime * Mathf.Lerp(1.1f, 3.4f, speedFx);
            for (int i = 0; i < count; i++)
            {
                float lane = Hash01(i * 7 + 1);
                float band = lane < .5f ? lane * .56f : .72f + (lane - .5f) * .56f;
                float length = Mathf.Lerp(50f, 200f, speedFx) * Mathf.Lerp(.6f, 1.2f, Hash01(i * 3 + 2)) * size;
                float t = Mathf.Repeat(Hash01(i * 13 + 5) + clock * Mathf.Lerp(.8f, 1.3f, lane), 1f);
                Color streak = new Color(1f, 1f, 1f,
                    speedFx * Mathf.Lerp(.2f, .55f, Hash01(i * 11 + 4)) * Mathf.Sin(t * Mathf.PI));
                Rect line;
                if (sideways)
                {
                    float travel = view.width + length;
                    float x = direction > 0 ? view.xMax - t * travel : view.x - length + t * travel;
                    line = new Rect(x, view.y + band * view.height, length, 3f * size);
                }
                else
                {
                    float travel = view.height + length;
                    float y = direction > 0 ? view.y - length + t * travel : view.yMax - t * travel;
                    line = new Rect(view.x + band * view.width, y, 3f * size, length);
                }
                ClippedPanel(line, view, streak);
            }
        }

        // A few streaks trail off the back of the car itself.
        void DrawCarTrail(Rect view, float size, bool sideways, int direction)
        {
            if (!TryGetCarScreenRect(out Rect car)) return;
            float length = Mathf.Lerp(30f, 150f, speedFx) * size;
            for (int i = 0; i < 4; i++)
            {
                float across = (i + .5f) / 4f;
                float flicker = Mathf.Lerp(.65f, 1f, Hash01(i * 5 + Mathf.FloorToInt(Time.unscaledTime * 18f)));
                float trail = length * flicker;
                Color streak = new Color(Gold.r, Gold.g, Gold.b, .55f * speedFx);
                Rect line = sideways
                    ? new Rect(direction > 0 ? car.x - trail : car.xMax, car.y + across * car.height, trail, 4f * size)
                    : new Rect(car.x + across * car.width, direction > 0 ? car.yMax : car.y - trail, 4f * size, trail);
                ClippedPanel(line, view, streak);
            }
        }

        bool TryGetCarScreenRect(out Rect rect)
        {
            rect = default;
            if (exteriorCar == null || exteriorCamera == null) return false;
            if (exteriorCarRenderers == null || exteriorCarRenderers.Length == 0)
                exteriorCarRenderers = exteriorCar.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds bounds = default;
            foreach (Renderer part in exteriorCarRenderers)
            {
                if (part == null || !part.enabled || !part.gameObject.activeInHierarchy) continue;
                if (!any) { bounds = part.bounds; any = true; }
                else bounds.Encapsulate(part.bounds);
            }
            if (!any) return false;
            Vector3 low = exteriorCamera.WorldToScreenPoint(bounds.min);
            Vector3 high = exteriorCamera.WorldToScreenPoint(bounds.max);
            if (low.z <= 0f || high.z <= 0f) return false;
            rect = Rect.MinMaxRect(Mathf.Min(low.x, high.x), Screen.height - Mathf.Max(low.y, high.y),
                Mathf.Max(low.x, high.x), Screen.height - Mathf.Min(low.y, high.y));
            return true;
        }

        // Split-screen streaks must not spill into the other player's half.
        void ClippedPanel(Rect rect, Rect view, Color colour)
        {
            Rect clipped = Rect.MinMaxRect(Mathf.Max(rect.xMin, view.xMin), Mathf.Max(rect.yMin, view.yMin),
                Mathf.Min(rect.xMax, view.xMax), Mathf.Min(rect.yMax, view.yMax));
            if (clipped.width > 0f && clipped.height > 0f) Panel(clipped, colour);
        }

        void EnsureCrazyRouteMap()
        {
            if (exteriorCar == null || exteriorCar.parent == null) return;
            Transform world = exteriorCar.parent;
            if (crazyRouteMap == null)
            {
                crazyRouteMap = world.Find(CrazyRouteMapName);
                if (crazyRouteMap == null)
                {
                    crazyRouteMap = new GameObject(CrazyRouteMapName).transform;
                    crazyRouteMap.SetParent(world, false);
                    CreateCrazyRouteLines();
                }
            }
            RefreshTeleportGateViews(world);
        }

        // Gates to floors that are still locked stay hidden until that dimension opens.
        void RefreshTeleportGateViews(Transform world)
        {
            if (teleportGateViews == null)
            {
                teleportGateViews = new Transform[TeleportGates.Length];
                Transform root = world.Find(TeleportGateRootName);
                if (root != null)
                    for (int i = 0; i < TeleportGates.Length; i++)
                        teleportGateViews[i] = root.Find(TeleportGateName(i));
            }
            for (int i = 0; i < teleportGateViews.Length; i++)
            {
                Transform view = teleportGateViews[i];
                bool open = TeleportGateOpen(TeleportGates[i]);
                if (view != null && view.gameObject.activeSelf != open) view.gameObject.SetActive(open);
            }
        }

        public static string TeleportGateName(int index) => "Teleport gate " + index;

        // Floor rails, side tracks and gates are baked into ExteriorWorld (WorldBandShaftBuilder).
        void CreateCrazyRouteLines()
        {
            CreateRouteLine("Playable route left", LeftShaftX, 0f,
                ElevatorRound.Floors - 1f, new Color32(75, 226, 202, 255));
            CreateRouteLine("Playable route right", RightShaftX, 0f,
                ElevatorRound.Floors - 1f, new Color32(255, 124, 104, 255));
        }

        void CreateRouteLine(string name, float x, float startFloor, float endFloor, Color colour)
        {
            var route = new GameObject(name);
            route.layer = 28; // Elevator tracks: independently hidden by the cabin camera.
            route.transform.SetParent(crazyRouteMap, false);
            var line = route.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = line.endWidth = .11f;
            line.numCornerVertices = 3;
            line.numCapVertices = 2;
            line.sharedMaterial = Mat(colour);
            line.SetPosition(0, CrazyTravelLocalPosition(x, startFloor));
            line.SetPosition(1, CrazyTravelLocalPosition(x, endFloor));
        }

        void DrawCrazyJunctionGuide()
        {
            if (!phase.Equals(Phase.Moving)) return;
            Rect view = Match != null ? Match.ViewRect(Seat) : new Rect(0f, 0f, Screen.width, Screen.height);
            if (teleportFlash > 0f)
            {
                DrawRouteGuide(view, "TELEPORT!  >>  FLOOR " + teleportFlashTarget, Gold);
                return;
            }
            if (railRiding)
            {
                DrawRouteGuide(view, pendingTrackDirection != 0
                    ? "TURNING " + (pendingTrackDirection > 0 ? "UP" : "DOWN") + " AT NEXT TRACK"
                    : "FLOOR " + railFloor + " RAIL  ·  W/S TO TURN AT NEXT TRACK", Gold);
                return;
            }
            if (pendingRailDirection != 0)
            {
                DrawRouteGuide(view, "TURNING " + (pendingRailDirection > 0 ? "RIGHT" : "LEFT")
                    + " AT NEXT FLOOR", Gold);
                return;
            }
            if (!OnMainShaft(routeLateralX)
                && TryGetVerticalTrack(routeLateralX, travelFloor, out _, out float bottom, out float top))
            {
                DrawRouteGuide(view, "SIDE TRACK  F" + bottom + "-F" + top + "  ·  ENDS AT F"
                    + (travelDirection > 0 ? top : bottom), Teal);
                return;
            }

            if (Match == null) return;
            float flash = Match.ExpressFlash(this);
            string message;
            Color accent;
            if (flash > 0f)
            {
                message = "EXPRESS!  SPEED BURST";
                accent = Gold;
            }
            else
            {
                if (!Match.TryGetUpcomingJunction(this, out int junction, out float distance)) return;
                string direction = Match.ExpressTrack(junction) == 0 ? "LEFT" : "RIGHT";
                int exitFloor = Mathf.CeilToInt(Match.JunctionFloor(junction));
                message = "EXPRESS GATE F" + exitFloor + "  /  " + direction
                    + "  /  " + distance.ToString("0.0") + "F";
                accent = Teal;
            }
            DrawRouteGuide(view, message, accent);
        }

        void DrawRouteGuide(Rect view, string message, Color accent)
        {
            float width = Mathf.Min(420f, view.width - 30f);
            Rect banner = new Rect(view.x + (view.width - width) * .5f, view.y + 78f, width, 38f);
            Panel(banner, new Color(Ink.r, Ink.g, Ink.b, .94f));
            Panel(new Rect(banner.x, banner.yMax - 4f, banner.width, 4f), accent);
            var guide = new GUIStyle(body)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.Max(12, Mathf.RoundToInt(14f * Mathf.Min(1f, view.width / 650f)))
            };
            guide.normal.textColor = Color.white;
            Label(banner, message, guide);
        }
    }
}
