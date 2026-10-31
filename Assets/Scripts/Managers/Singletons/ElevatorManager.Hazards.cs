using System.Collections.Generic;
using UnityEngine;
using CrazyElevator.Shared;
using ElevatorMatchType = CrazyElevator.Match.ElevatorMatch;

namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;

    // Shaft hazards. Art is the ElevatorScene tornado / monster prefabs; this only moves them.
    //   Monster: floats between two floors, patrols across the shafts and bounces the car back.
    //   Tornado: sweeps along a floor rail, spins the car and flings it away.
    // Side tracks and floor rails are the way around them.
    public sealed partial class ElevatorManager
    {
        enum HazardKind { Tornado, Monster }

        struct HazardSpec
        {
            public HazardKind Kind;
            public float Floor, Left, Right, Speed;
            public HazardSpec(HazardKind kind, float floor, float left, float right, float speed)
            { Kind = kind; Floor = floor; Left = left; Right = right; Speed = speed; }
        }

        sealed class ShaftHazard
        {
            public HazardSpec Spec;
            public Transform View, Spinner;
            public float X, Clock;
            public float Stunned;
            public int Direction = 1;
            public bool Live;
        }

        // Left / Right are offsets from the shaft centre (side tracks sit at ±14 and ±25).
        static readonly HazardSpec[] HazardLayout =
        {
            new HazardSpec(HazardKind.Monster, 3.5f, -7f, 7f, 2.4f),
            new HazardSpec(HazardKind.Tornado, 6f, -27f, 27f, 4.5f),
            new HazardSpec(HazardKind.Monster, 9.5f, -7f, 7f, 2.8f),
            new HazardSpec(HazardKind.Tornado, 11f, -27f, 27f, 5f),
            new HazardSpec(HazardKind.Monster, 13.5f, -18f, 2f, 3f),
            new HazardSpec(HazardKind.Tornado, 14f, -27f, 27f, 5.5f),
            new HazardSpec(HazardKind.Monster, 17.5f, -7f, 7f, 3f),
            new HazardSpec(HazardKind.Tornado, 19f, -27f, 27f, 5.5f),
            new HazardSpec(HazardKind.Monster, 20.5f, -2f, 18f, 3.2f),
            new HazardSpec(HazardKind.Tornado, 22f, -27f, 27f, 6f),
        };
        // The office stays calm for the first few seconds of a shift.
        const float OfficeHazardDelay = 12f;
        const float HazardHitCooldown = 1.4f;
        const float HazardPatienceCost = 3f;
        const float TornadoSpinSeconds = .9f;
        const float HazardDepth = -.8f;
        static readonly Vector2 MonsterHalfSize = new Vector2(1.6f, 1.5f);
        static readonly Vector2 TornadoHalfSize = new Vector2(1.5f, 2.6f);

        readonly List<ShaftHazard> hazards = new List<ShaftHazard>();
        Transform hazardRoot;
        float hazardCooldown, carSpin;
        float abilityFeedbackTime;
        string abilityFeedback;
        Vector3 abilityFeedbackPosition;
        Vector3 abilityFeedbackOrigin;

        public bool HasBouncer => round != null && round.HasOnboardFeature(PassengerFeature.StrongCollision);

        public void ShowPassengerAbility(string label, Vector3 worldPosition)
        {
            abilityFeedback = label;
            abilityFeedbackPosition = worldPosition;
            abilityFeedbackOrigin = exteriorCar != null ? exteriorCar.position : worldPosition;
            abilityFeedbackTime = 1.4f;
        }

        float CarSpinDegrees => carSpin > 0f ? Mathf.SmoothStep(0f, 720f, 1f - carSpin) : 0f;

        void TickHazardTimers(float dt)
        {
            hazardCooldown = Mathf.Max(0f, hazardCooldown - dt);
            carSpin = Mathf.Max(0f, carSpin - dt / TornadoSpinSeconds);
            abilityFeedbackTime = Mathf.Max(0f, abilityFeedbackTime - dt);
        }

        bool HazardOpen(HazardSpec spec) => !MenuManager.TutorialActive && round != null
            && spec.Floor >= MinTravelFloor && spec.Floor <= MaxTravelFloor + .5f
            && (spec.Floor >= ElevatorRound.FloorsPerDimension || round.Elapsed >= OfficeHazardDelay);

        void UpdateShaftHazards(float dt)
        {
            if (WorldOwner != this || exteriorCar == null || sceneView == null) return;
            if (hazardRoot == null) SpawnHazards();
            foreach (ShaftHazard hazard in hazards)
            {
                hazard.Live = HazardOpen(hazard.Spec);
                if (hazard.View.gameObject.activeSelf != hazard.Live) hazard.View.gameObject.SetActive(hazard.Live);
                if (!hazard.Live) continue;
                if (hazard.Stunned > 0f)
                {
                    hazard.Stunned = Mathf.Max(0f, hazard.Stunned - dt);
                    hazard.View.localScale = Vector3.one * (1f + .12f * Mathf.Sin(hazard.Stunned * 15f));
                    continue;
                }
                hazard.Clock += dt;
                float left = ElevatorMatchType.ShaftCenterX + hazard.Spec.Left;
                float right = ElevatorMatchType.ShaftCenterX + hazard.Spec.Right;
                hazard.X += hazard.Direction * hazard.Spec.Speed * dt;
                if (hazard.X >= right) { hazard.X = right; hazard.Direction = -1; }
                else if (hazard.X <= left) { hazard.X = left; hazard.Direction = 1; }
                PoseHazard(hazard);
            }
        }

        void SpawnHazards()
        {
            hazardRoot = new GameObject("Shaft hazards").transform;
            hazardRoot.SetParent(exteriorCar.parent, false);
            for (int i = 0; i < HazardLayout.Length; i++)
            {
                HazardSpec spec = HazardLayout[i];
                GameObject prefab = spec.Kind == HazardKind.Tornado ? sceneView.tornado : sceneView.monster;
                if (prefab == null) continue;
                GameObject view = Instantiate(prefab, hazardRoot, false);
                view.name = spec.Kind + " F" + spec.Floor;
                foreach (Transform part in view.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = ExteriorLayer;
                foreach (Collider part in view.GetComponentsInChildren<Collider>(true)) part.enabled = false;
                Transform funnel = view.transform.Find("Funnel");
                hazards.Add(new ShaftHazard
                {
                    Spec = spec,
                    View = view.transform,
                    Spinner = funnel != null ? funnel : view.transform,
                    X = ElevatorMatchType.ShaftCenterX + Mathf.Lerp(spec.Left, spec.Right, Hash01(i * 17 + 3)),
                    Direction = i % 2 == 0 ? 1 : -1,
                    Clock = i
                });
            }
        }

        static Vector2 HazardCentre(ShaftHazard hazard)
        {
            float floorY = hazard.Spec.Floor * ShaftFloorHeight;
            return hazard.Spec.Kind == HazardKind.Monster
                ? new Vector2(hazard.X, floorY + 1.4f + Mathf.Sin(hazard.Clock * 2.4f) * .35f)
                : new Vector2(hazard.X, floorY + TornadoHalfSize.y);
        }

        static Vector2 HazardHalfSize(HazardKind kind) => kind == HazardKind.Monster ? MonsterHalfSize : TornadoHalfSize;

        void PoseHazard(ShaftHazard hazard)
        {
            Vector2 centre = HazardCentre(hazard);
            hazard.View.localPosition = new Vector3(centre.x, centre.y, HazardDepth);
            if (hazard.Spec.Kind == HazardKind.Tornado)
            {
                hazard.Spinner.localRotation = Quaternion.Euler(0f, hazard.Clock * 540f, 0f);
                hazard.View.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(hazard.Clock * 3f) * 6f);
                return;
            }
            float squash = Mathf.Sin(hazard.Clock * 4.8f) * .05f;
            hazard.View.localRotation = Quaternion.Euler(0f, hazard.Direction * 14f, -hazard.Direction * 8f);
            hazard.View.localScale = new Vector3(1f + squash, 1f - squash, 1f);
        }

        // Every car checks the shared hazards owned by seat 0's world.
        void CheckHazardHits()
        {
            if (phase != Phase.Moving || round == null || MenuManager.TutorialActive) return;
            Vector3 carPosition = CrazyTravelLocalPosition(RouteLateralX, travelFloor)
                + CrazyTravelLocalRotation(travelFloor, 0f) * exteriorCollisionCenterOffset;
            Vector2 car = new Vector2(carPosition.x, carPosition.y);
            Vector2 carHalf = exteriorCollisionHalfSize * .85f;
            foreach (ShaftHazard hazard in WorldOwner.hazards)
            {
                if (!hazard.Live) continue;
                Vector2 centre = HazardCentre(hazard);
                if (hazard.Spec.Kind == HazardKind.Monster && hazard.Stunned <= 0f
                    && round.HasOnboardFeature(PassengerFeature.StunsMonster)
                    && Vector2.Distance(car, centre) <= 7f)
                {
                    hazard.Stunned = 3f;
                    ShowPassengerAbility("STUN!", exteriorCar.parent.TransformPoint(new Vector3(centre.x, centre.y + 2f, HazardDepth)));
                    Play(chime);
                }
                if (hazard.Stunned > 0f || hazardCooldown > 0f) continue;
                Vector2 half = HazardHalfSize(hazard.Spec.Kind);
                if (Mathf.Abs(car.x - centre.x) > half.x + carHalf.x
                    || Mathf.Abs(car.y - centre.y) > half.y + carHalf.y) continue;
                HitHazard(hazard, car, centre);
                return;
            }
        }

        void HitHazard(ShaftHazard hazard, Vector2 car, Vector2 centre)
        {
            hazardCooldown = HazardHitCooldown;
            if (hazard.Spec.Kind == HazardKind.Tornado
                && round.HasOnboardFeature(PassengerFeature.BlocksTornado))
            {
                ShowPassengerAbility("BLOCK!", exteriorCar.position + Vector3.up * 3f);
                Play(chime);
                return;
            }
            collisionImpact = 1f;
            boostAxis = 0f;
            boostHeld = false;
            foreach (Rider rider in round.Riders)
                if (round.Owns(rider)) rider.Remaining = Mathf.Max(0f, rider.Remaining - HazardPatienceCost);
            if (hazard.Spec.Kind == HazardKind.Monster) BounceOffMonster(car, centre);
            else SpinInTornado(hazard);
        }

        void BounceOffMonster(Vector2 car, Vector2 centre)
        {
            collisionSlowTimer = Mathf.Max(collisionSlowTimer, .9f);
            if (railRiding)
            {
                int away = car.x < centre.x ? -1 : 1;
                railDirection = away;
                railVelocity = away * Mathf.Max(CruiseSpeed, Mathf.Abs(railVelocity) * .7f);
            }
            else
            {
                int away = car.y < centre.y ? -1 : 1;
                travelDirection = away;
                travelVelocity = away * Mathf.Clamp(Mathf.Abs(travelVelocity) * .8f, 1.05f, 2.4f);
                collisionRecoilTimer = .5f;
            }
            notice = "MONSTER! Bounced back. Go around it.";
            Play(buzz);
        }

        void SpinInTornado(ShaftHazard hazard)
        {
            carSpin = 1f;
            if (railRiding)
            {
                railDirection = hazard.Direction;
                railVelocity = hazard.Direction * SpeedLimit;
            }
            else
            {
                int up = travelFloor < MaxTravelFloor - .5f ? 1 : -1;
                travelDirection = up;
                travelVelocity = up * SpeedLimit;
                collisionRecoilTimer = .6f;
            }
            notice = "TORNADO! Spun around and flung away.";
            Play(kickWhoosh);
        }

        void DrawPassengerAbilityFeedback()
        {
            if (abilityFeedbackTime <= 0f || exteriorCamera == null) return;
            Vector3 screen = exteriorCamera.WorldToScreenPoint(abilityFeedbackPosition);
            if (screen.z <= 0f) return;
            Rect view = Match != null ? Match.ViewRect(Seat) : new Rect(0, 0, Screen.width, Screen.height);
            if (abilityFeedback == "STUN!" && abilityFeedbackTime > 1f)
            {
                Vector3 source = exteriorCamera.WorldToScreenPoint(abilityFeedbackOrigin);
                if (source.z > 0f)
                {
                    Vector2 from = new Vector2(source.x, Screen.height - source.y);
                    Vector2 to = new Vector2(screen.x, Screen.height - screen.y);
                    Vector2 direction = to - from;
                    Matrix4x4 previousMatrix = GUI.matrix;
                    Color previousColour = GUI.color;
                    GUIUtility.RotateAroundPivot(Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg, from);
                    GUI.color = Gold;
                    GUI.DrawTexture(new Rect(from.x, from.y - 3f, direction.magnitude, 6f), Texture2D.whiteTexture);
                    GUI.color = previousColour;
                    GUI.matrix = previousMatrix;
                }
            }
            float x = Mathf.Clamp(screen.x, view.x + 90f, view.xMax - 90f);
            float y = Mathf.Clamp(Screen.height - screen.y, view.y + 80f, view.yMax - 45f);
            GUIStyle style = new GUIStyle(large) { alignment = TextAnchor.MiddleCenter, fontSize = 30 };
            OutlinedLabel(new Rect(x - 90f, y - 30f, 180f, 45f), abilityFeedback, style, Gold);
        }
    }
}
