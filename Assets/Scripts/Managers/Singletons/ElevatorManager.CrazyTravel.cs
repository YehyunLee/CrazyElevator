using UnityEngine;
using CrazyElevator.Shared;
using ElevatorMatchType = CrazyElevator.Match.ElevatorMatch;

namespace CrazyElevator.Managers
{
    // The logical floor remains simple and predictable. This layer only bends
    // the visible route between landings and adds readable express junctions.
    public sealed partial class ElevatorManager
    {
        const string CrazyRouteMapName = "Crazy 3D route map";
        static readonly int[] CrazyJunctionSegments = { 2, 5, 8 };
        Transform crazyRouteMap;
        int crazyRouteSignature = int.MinValue;
        float routeBoostPulse;

        static bool IsCrazyJunctionSegment(int segment)
        {
            for (int i = 0; i < CrazyJunctionSegments.Length; i++)
                if (CrazyJunctionSegments[i] == segment) return true;
            return false;
        }

        static float CrazyVerticalProgress(float t)
        {
            t = Mathf.Clamp01(t);
            if (t < .22f)
                return Mathf.Lerp(0f, .34f, Mathf.SmoothStep(0f, 1f, t / .22f));
            if (t < .78f)
                return Mathf.Lerp(.34f, .66f, Mathf.SmoothStep(0f, 1f, (t - .22f) / .56f));
            return Mathf.Lerp(.66f, 1f, Mathf.SmoothStep(0f, 1f, (t - .78f) / .22f));
        }

        Vector3 CrazyTravelLocalPosition(float baseX, float logicalFloor)
        {
            logicalFloor = Mathf.Clamp(logicalFloor, 0f, ElevatorRound.Floors - 1f);
            int segment = Mathf.Min(Mathf.FloorToInt(logicalFloor), ElevatorRound.Floors - 2);
            float t = Mathf.Clamp01(logicalFloor - segment);
            float yProgress = t;
            float lateral = 0f;
            float depth = 0f;
            if (IsCrazyJunctionSegment(segment))
            {
                float arc = Mathf.Sin(t * Mathf.PI);
                float direction = segment % 2 == 0 ? 1f : -1f;
                lateral = direction * 4.15f * arc;
                depth = 3.2f * arc;
                yProgress = CrazyVerticalProgress(t);
            }
            float y = (segment + yProgress) * ShaftFloorHeight + 1.4f;
            return new Vector3(baseX + lateral, y, -.8f + depth);
        }

        Quaternion CrazyTravelLocalRotation(float logicalFloor, float collisionKick)
        {
            int segment = Mathf.Min(Mathf.FloorToInt(Mathf.Clamp(logicalFloor, 0f,
                ElevatorRound.Floors - 1f)), ElevatorRound.Floors - 2);
            float t = Mathf.Clamp01(logicalFloor - segment);
            float routeTurn = 0f;
            if (IsCrazyJunctionSegment(segment))
            {
                float direction = segment % 2 == 0 ? 1f : -1f;
                routeTurn = direction * Mathf.Sin(t * Mathf.PI * 2f);
            }
            return exteriorCarHomeRotation * Quaternion.Euler(
                0f, routeTurn * 14f, routeTurn * -5.5f + collisionKick * (Seat == 0 ? -4.5f : 4.5f));
        }

        void UpdateCrazyTravelCamera(Vector3 carLocal)
        {
            Transform world = exteriorCar.parent;
            float baseX = Match != null ? Match.ShaftX(this) : ElevatorMatchType.ShaftCenterX;
            float side = Match == null ? -8.5f : Seat == 0 ? -7.2f : 7.2f;
            float bend = carLocal.x - baseX;
            Vector3 cameraLocal = new Vector3(
                baseX + side + bend * .24f,
                carLocal.y + 5.2f,
                -34f + Mathf.Max(0f, carLocal.z + .8f) * .18f);

            routeBoostPulse = Mathf.MoveTowards(routeBoostPulse, 0f, Time.unscaledDeltaTime * 1.55f);
            if (phase == Phase.Moving)
            {
                float clock = Time.unscaledTime;
                float strength = shaftTravelShake * travelShakeBlend;
                cameraLocal += new Vector3(Mathf.Sin(clock * 15f), Mathf.Sin(clock * 21f) * .65f, 0f)
                    * strength;
            }

            Vector3 targetLocal = carLocal + new Vector3(0f, .9f, 0f);
            exteriorCamera.transform.position = world.TransformPoint(cameraLocal);
            Vector3 targetWorld = world.TransformPoint(targetLocal);
            exteriorCamera.transform.rotation = Quaternion.LookRotation(
                targetWorld - exteriorCamera.transform.position, world.up);
            exteriorCamera.orthographic = false;
            exteriorCamera.fieldOfView = Mathf.Lerp(61f, 55f, routeBoostPulse);
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
                    CreateCrazyJunctionFrames();
                }
            }

            int signature = Match != null ? Match.JunctionSignature : 0;
            if (signature == crazyRouteSignature) return;
            crazyRouteSignature = signature;
            RefreshCrazyJunctionColours();
        }

        void CreateCrazyRouteLines()
        {
            int tracks = Match != null ? 2 : 1;
            const int samplesPerFloor = 12;
            int sampleCount = (ElevatorRound.Floors - 1) * samplesPerFloor + 1;
            for (int track = 0; track < tracks; track++)
            {
                var route = new GameObject("Route ribbon track " + (track + 1));
                route.layer = ExteriorLayer;
                route.transform.SetParent(crazyRouteMap, false);
                var line = route.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = sampleCount;
                line.startWidth = line.endWidth = .11f;
                line.numCornerVertices = 3;
                line.numCapVertices = 2;
                line.sharedMaterial = Mat(track == 0
                    ? new Color32(75, 226, 202, 255)
                    : new Color32(255, 124, 104, 255));
                float trackX = Match != null ? Match.ShaftX(track) : ElevatorMatchType.ShaftCenterX;
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    float floor = sample / (float)(sampleCount - 1) * (ElevatorRound.Floors - 1);
                    line.SetPosition(sample, CrazyTravelLocalPosition(trackX, floor));
                }
            }
        }

        void CreateCrazyJunctionFrames()
        {
            int tracks = Match != null ? 2 : 1;
            for (int junction = 0; junction < CrazyJunctionSegments.Length; junction++)
            {
                float floor = CrazyJunctionSegments[junction] + .5f;
                for (int track = 0; track < tracks; track++)
                {
                    float trackX = Match != null ? Match.ShaftX(track) : ElevatorMatchType.ShaftCenterX;
                    Vector3 centre = CrazyTravelLocalPosition(trackX, floor);
                    var frame = new GameObject("Junction " + junction + " track " + track);
                    frame.layer = ExteriorLayer;
                    frame.transform.SetParent(crazyRouteMap, false);
                    AddJunctionPiece(frame.transform, "Left", centre + new Vector3(-1.72f, 1.25f, 0f),
                        new Vector3(.16f, 3.4f, .2f));
                    AddJunctionPiece(frame.transform, "Right", centre + new Vector3(1.72f, 1.25f, 0f),
                        new Vector3(.16f, 3.4f, .2f));
                    AddJunctionPiece(frame.transform, "Top", centre + new Vector3(0f, 2.92f, 0f),
                        new Vector3(3.6f, .16f, .2f));
                }
            }
        }

        void AddJunctionPiece(Transform frame, string name, Vector3 position, Vector3 scale)
        {
            Transform piece = Shape(name, PrimitiveType.Cube, position, scale,
                new Color32(72, 82, 108, 255), frame);
            piece.gameObject.layer = ExteriorLayer;
            DisableCollider(piece);
        }

        void RefreshCrazyJunctionColours()
        {
            if (crazyRouteMap == null) return;
            for (int junction = 0; junction < CrazyJunctionSegments.Length; junction++)
            for (int track = 0; track < (Match != null ? 2 : 1); track++)
            {
                Transform frame = crazyRouteMap.Find("Junction " + junction + " track " + track);
                if (frame == null) continue;
                bool express = Match == null || Match.ExpressTrack(junction) == track;
                Material material = Mat(express
                    ? new Color32(255, 205, 82, 255)
                    : new Color32(62, 70, 94, 255));
                foreach (Renderer renderer in frame.GetComponentsInChildren<Renderer>(true))
                    renderer.sharedMaterial = material;
            }
        }

        void DrawCrazyJunctionGuide()
        {
            if (Match == null || !phase.Equals(Phase.Moving)) return;
            Rect view = Match.ViewRect(Seat);
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
                message = "3D JUNCTION F" + exitFloor + "  /  EXPRESS " + direction
                    + "  /  " + distance.ToString("0.0") + "F";
                accent = Teal;
            }

            float width = Mathf.Min(350f, view.width - 30f);
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
