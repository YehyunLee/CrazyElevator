using UnityEngine;
using CrazyElevator.Shared;
using ElevatorMatchType = CrazyElevator.Match.ElevatorMatch;

namespace CrazyElevator.Managers
{
    // The logical floor remains simple and predictable. The visible route is
    // kept as a straight vertical shaft; express gates are gameplay/UI only.
    public sealed partial class ElevatorManager
    {
        const string CrazyRouteMapName = "Crazy vertical route map";
        Transform crazyRouteMap;
        float routeBoostPulse;

        Vector3 CrazyTravelLocalPosition(float baseX, float logicalFloor)
        {
            logicalFloor = Mathf.Clamp(logicalFloor, 0f, ElevatorRound.Floors - 1f);
            float y = logicalFloor * ShaftFloorHeight + 1.4f;
            return new Vector3(baseX, y, -.8f);
        }

        Quaternion CrazyTravelLocalRotation(float logicalFloor, float collisionKick)
        {
            return exteriorCarHomeRotation * Quaternion.Euler(
                0f, 0f, collisionKick * (Seat == 0 ? -4.5f : 4.5f));
        }

        void UpdateCrazyTravelCamera(Vector3 carLocal)
        {
            Transform world = exteriorCar.parent;
            Vector3 cameraLocal = new Vector3(
                carLocal.x,
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
                }
            }
        }

        void CreateCrazyRouteLines()
        {
            int tracks = Match != null ? 2 : 1;
            for (int track = 0; track < tracks; track++)
            {
                var route = new GameObject("Route ribbon track " + (track + 1));
                route.layer = ExteriorLayer;
                route.transform.SetParent(crazyRouteMap, false);
                var line = route.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = 2;
                line.startWidth = line.endWidth = .11f;
                line.numCornerVertices = 3;
                line.numCapVertices = 2;
                line.sharedMaterial = Mat(track == 0
                    ? new Color32(75, 226, 202, 255)
                    : new Color32(255, 124, 104, 255));
                float trackX = Match != null ? Match.ShaftX(track) : ElevatorMatchType.ShaftCenterX;
                line.SetPosition(0, CrazyTravelLocalPosition(trackX, 0f));
                line.SetPosition(1, CrazyTravelLocalPosition(trackX, ElevatorRound.Floors - 1f));
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
                message = "EXPRESS GATE F" + exitFloor + "  /  " + direction
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
