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
        ShaftHazard eventHazard;
        float eventHazardTime;
        float abilityFeedbackTime;
        string abilityFeedback;
        Vector3 abilityFeedbackPosition;
        Vector3 abilityFeedbackOrigin;

        // One shared event per world, with independent hit tracking per car.
        const float WorldEventFirst = 18f, WorldEventInterval = 22f;
        const float WorldEventWarning = 2f, WorldEventDuration = 9f;
        const float WorldEventApproachDistance = 1.5f, WorldEventSweepDistance = 10f;
        GameObject worldEventView;
        int worldEventId, worldEventSeen, worldEventAnnounced, worldEventNearby, worldEventResult, worldEventDimension = -1;
        float worldEventStart = -1f, worldEventFloor, worldEventX, worldEventStartX;
        int worldEventDirection;
        GameObject worldLeakEffect;
        float worldLeakTime;
        GameObject worldStatusEffect;
        float worldScanTime, worldSyrupTime;
        float WorldHazardSpeedMultiplier => worldScanTime > 0f ? .18f : worldSyrupTime > 0f ? .42f : 1f;

        public bool HasBouncer => round != null && round.HasOnboardFeature(PassengerFeature.StrongCollision);

        public void ShowPassengerAbility(string label, Vector3 worldPosition)
        {
            abilityFeedback = label;
            abilityFeedbackPosition = worldPosition;
            abilityFeedbackOrigin = exteriorCar != null ? exteriorCar.position : worldPosition;
            abilityFeedbackTime = 1.4f;
            string kind = label == "STUN!" ? "HUNTER" : label == "BLOCK!" ? "DIVER"
                : label == "BUMP!" ? "BOUNCER" : null;
            if (kind != null) GameManager.Instance?.RequestAbilityIntro(this, kind);
        }

        float CarSpinDegrees => carSpin > 0f ? Mathf.SmoothStep(0f, 720f, 1f - carSpin) : 0f;

        void TickHazardTimers(float dt)
        {
            hazardCooldown = Mathf.Max(0f, hazardCooldown - dt);
            carSpin = Mathf.Max(0f, carSpin - dt / TornadoSpinSeconds);
            abilityFeedbackTime = Mathf.Max(0f, abilityFeedbackTime - dt);
            worldLeakTime = Mathf.Max(0f, worldLeakTime - dt);
            worldScanTime = Mathf.Max(0f, worldScanTime - dt);
            worldSyrupTime = Mathf.Max(0f, worldSyrupTime - dt);
            if (worldStatusEffect != null)
                worldStatusEffect.SetActive(worldScanTime > 0f || worldSyrupTime > 0f);
            if (worldLeakEffect != null)
                worldLeakEffect.SetActive(worldLeakTime > 0f && !HasHandyman && repairedFor <= 0f);
            TickWorldEventFeedback();
        }

        bool HazardOpen(HazardSpec spec) => !MenuManager.TutorialActive && round != null
            && spec.Floor >= MinTravelFloor && spec.Floor <= MaxTravelFloor + .5f
            && (spec.Floor >= ElevatorRound.FloorsPerDimension || round.Elapsed >= OfficeHazardDelay);

        void UpdateShaftHazards(float dt)
        {
            if (WorldOwner != this || exteriorCar == null || sceneView == null) return;
            if (hazardRoot == null) SpawnHazards();
            UpdateWorldEvent();
            eventHazardTime = Mathf.Max(0f, eventHazardTime - dt);
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
                hazard.X += hazard.Direction * hazard.Spec.Speed * (hazard == eventHazard && eventHazardTime > 0f ? 2f : 1f) * dt;
                if (hazard.X >= right) { hazard.X = right; hazard.Direction = -1; }
                else if (hazard.X <= left) { hazard.X = left; hazard.Direction = 1; }
                PoseHazard(hazard);
            }
        }

        // One visible shared hazard speeds up briefly for both elevators.
        public string StartSharedHazardEvent()
        {
            if (WorldOwner != this) return WorldOwner.StartSharedHazardEvent();
            if (hazardRoot == null) SpawnHazards();
            eventHazard = null;
            foreach (var hazard in hazards)
            {
                if (!HazardOpen(hazard.Spec)) continue;
                if (eventHazard == null || hazard.Spec.Kind == HazardKind.Tornado) eventHazard = hazard;
                if (hazard.Spec.Kind == HazardKind.Tornado) break;
            }
            if (eventHazard == null) return null;
            eventHazardTime = 8f;
            int floor = Mathf.Clamp(Mathf.FloorToInt(eventHazard.Spec.Floor), MinTravelFloor, MaxTravelFloor);
            return (eventHazard.Spec.Kind == HazardKind.Tornado ? "TORNADO" : "MONSTER")
                + " SURGE · F" + ElevatorRound.FloorNumber(floor);
        }

        public void ResetSharedHazardEvent() { eventHazard = null; eventHazardTime = 0f; }

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

        void ResetWorldEvent()
        {
            worldEventSeen = worldEventAnnounced = worldEventNearby = worldEventResult = 0;
            worldLeakTime = 0f;
            worldScanTime = worldSyrupTime = 0f;
            if (worldStatusEffect != null) Destroy(worldStatusEffect);
            worldStatusEffect = null;
            if (worldLeakEffect != null) worldLeakEffect.SetActive(false);
            if (WorldOwner != this) return;
            worldEventId = 0;
            worldEventStart = -1f;
            worldEventDimension = -1;
            if (worldEventView != null) Destroy(worldEventView);
            worldEventView = null;
        }

        void UpdateWorldEvent()
        {
            if (round == null || round.ActiveDimension < 0 || round.ActiveDimension > 2 || MenuManager.TutorialActive)
            {
                if (worldEventView != null) worldEventView.SetActive(false);
                return;
            }
            int dimension = round.ActiveDimension;
            if (worldEventDimension != dimension)
            {
                if (worldEventView != null) Destroy(worldEventView);
                worldEventView = null;
                worldEventDimension = dimension;
                worldEventStart = -1f;
                worldEventId = 0;
            }
            float elapsed = round.Elapsed;
            ElevatorManager other = Match != null ? Match.Rival : null;
            ElevatorManager travellingCar = phase == Phase.Moving ? this
                : other != null && other.phase == Phase.Moving ? other : null;
            float nextStart = worldEventId == 0 ? WorldEventFirst : worldEventStart + WorldEventInterval;
            if (elapsed >= nextStart && travellingCar != null)
            {
                worldEventId++;
                worldEventStart = elapsed;
                float introWarning = worldEventId == 1 ? WorldEventWarning + 1f : WorldEventWarning;
                // The sweep starts just ahead of the travelling car and reaches its lane
                // about one second after the warning ends. Predict to that moment so the
                // visible pass and the collision row stay together at every world speed.
                float forecast = travellingCar.travelVelocity * (introWarning + 1f);
                worldEventFloor = travellingCar.railRiding ? travellingCar.railFloor
                    : Mathf.Clamp(Mathf.RoundToInt(travellingCar.travelFloor + forecast),
                        travellingCar.MinTravelFloor, travellingCar.MaxTravelFloor);
                worldEventDirection = travellingCar.RouteLateralX <= ElevatorMatchType.ShaftCenterX ? 1 : -1;
                worldEventStartX = travellingCar.RouteLateralX - worldEventDirection * WorldEventApproachDistance;
                GameObject prefab = dimension == 0 ? sceneView.securitySweep
                    : dimension == 1 ? sceneView.syrupSurge : sceneView.oceanCurrent;
                if (worldEventView != null) Destroy(worldEventView);
                worldEventView = prefab != null ? Instantiate(prefab, hazardRoot, false) : null;
            }
            if (worldEventView == null) return;
            float age = elapsed - worldEventStart;
            bool active = age >= 0f && age < WorldEventDuration;
            if (worldEventView.activeSelf != active) worldEventView.SetActive(active);
            if (!active) return;
            float warning = worldEventId == 1 ? WorldEventWarning + 1f : WorldEventWarning;
            float progress = Mathf.Clamp01((age - warning) / (WorldEventDuration - warning));
            worldEventX = worldEventStartX + worldEventDirection * WorldEventSweepDistance * progress;
            worldEventView.transform.localPosition = new Vector3(worldEventX, worldEventFloor * ShaftFloorHeight + 2f, HazardDepth);
            float pulse = age < warning ? .7f + .3f * Mathf.Abs(Mathf.Sin(age * 9f))
                : 1f + .06f * Mathf.Sin(age * 5f);
            worldEventView.transform.localScale = new Vector3(pulse * worldEventDirection, pulse, pulse);
            if (dimension == 2)
            {
                AnimateCurrentBubble("Bubble one", .2f, age);
                AnimateCurrentBubble("Bubble two", .45f, age + 1f);
                AnimateCurrentBubble("Bubble three", -.2f, age + 2f);
            }
        }

        void AnimateCurrentBubble(string name, float height, float time)
        {
            Transform bubble = worldEventView.transform.Find(name);
            if (bubble == null) return;
            Vector3 position = bubble.localPosition;
            position.y = height + .3f * Mathf.Sin(time * 3f);
            bubble.localPosition = position;
        }

        void CheckWorldEventHit()
        {
            ElevatorManager owner = WorldOwner;
            if (phase != Phase.Moving || round == null || MenuManager.TutorialActive
                || owner.worldEventView == null || !owner.worldEventView.activeSelf
                || owner.worldEventId == worldEventSeen || round.ActiveDimension != owner.worldEventDimension) return;
            float age = owner.round.Elapsed - owner.worldEventStart;
            float warning = owner.worldEventId == 1 ? WorldEventWarning + 1f : WorldEventWarning;
            if (age < warning || age >= WorldEventDuration) return;
            if (Mathf.Abs(travelFloor - owner.worldEventFloor) < 1.35f
                && Mathf.Abs(RouteLateralX - owner.worldEventX) < 10f) worldEventNearby = owner.worldEventId;
            if (Mathf.Abs(travelFloor - owner.worldEventFloor) > 1f
                || Mathf.Abs(RouteLateralX - owner.worldEventX) > 5.5f) return;
            worldEventSeen = owner.worldEventId;
            if (owner.worldEventDimension == 2 && round.HasOnboardFeature(PassengerFeature.BlocksTornado))
            {
                ShowActionFeedback("CURRENT BLOCKED!", true);
                Play(chime);
                return;
            }
            collisionImpact = 1f;
            if (owner.worldEventDimension == 2)
            {
                StartRailRide(Mathf.Clamp(Mathf.RoundToInt(travelFloor), MinTravelFloor, MaxTravelFloor),
                    owner.worldEventDirection, Mathf.Max(CruiseSpeed, .6f));
                if (!HasHandyman && repairedFor <= 0f)
                {
                    rustExposure = Mathf.Min(secondsPerRustLevel * 2f, rustExposure + secondsPerRustLevel * .45f);
                    ShowCurrentLeak();
                }
                ShowActionFeedback("PUSHED BY CURRENT", false);
                Play(kickWhoosh);
            }
            else
            {
                bool scanner = owner.worldEventDimension == 0;
                if (scanner) worldScanTime = 2.5f;
                else worldSyrupTime = 4.5f;
                travelVelocity *= scanner ? .2f : .42f;
                railVelocity *= scanner ? .2f : .42f;
                ShowWorldStatus(scanner ? sceneView.securitySweep : sceneView.syrupSurge, scanner);
                ShowActionFeedback(scanner ? "SCANNER HIT · SLOWED" : "SYRUP HIT · SLOWED", false);
                Play(buzz);
            }
        }

        void ShowWorldStatus(GameObject prefab, bool scanner)
        {
            if (worldStatusEffect != null) Destroy(worldStatusEffect);
            if (prefab == null || exteriorCar == null) return;
            worldStatusEffect = Instantiate(prefab, exteriorCar, false);
            worldStatusEffect.name = scanner ? "Security scan impact" : "Syrup stuck to elevator";
            worldStatusEffect.transform.localPosition = new Vector3(0f, scanner ? 2.7f : .65f, -1.5f);
            worldStatusEffect.transform.localScale = Vector3.one * (scanner ? .35f : .55f);
        }

        void ShowCurrentLeak()
        {
            if (exteriorCar == null || sceneView == null || sceneView.oceanCurrent == null) return;
            if (worldLeakEffect == null)
            {
                worldLeakEffect = Instantiate(sceneView.oceanCurrent, exteriorCar, false);
                worldLeakEffect.name = "Current splash";
                worldLeakEffect.transform.localPosition = new Vector3(2f, 2.1f, -.8f);
                worldLeakEffect.transform.localRotation = Quaternion.Euler(0f, 0f, -65f);
                worldLeakEffect.transform.localScale = Vector3.one * .3f;
            }
            worldLeakTime = 4f;
            worldLeakEffect.SetActive(true);
        }

        void TickWorldEventFeedback()
        {
            ElevatorManager owner = WorldOwner;
            if (owner.round == null || owner.worldEventId == 0 || owner.worldEventView == null || round == null
                || round.ActiveDimension != owner.worldEventDimension || MenuManager.TutorialActive) return;
            float age = owner.round.Elapsed - owner.worldEventStart;
            float warning = owner.worldEventId == 1 ? WorldEventWarning + 1f : WorldEventWarning;
            if (age >= 0f && age < warning && worldEventAnnounced != owner.worldEventId
                && DisplayBuildingView && Mathf.Abs(CurrentFloor - owner.worldEventFloor) < 3f)
            {
                worldEventAnnounced = owner.worldEventId;
                string label = owner.worldEventDimension == 0 ? "SCAN" : owner.worldEventDimension == 1 ? "SYRUP" : "CURRENT";
                ShowActionFeedback(label + (owner.worldEventDirection > 0 ? "  →" : "  ←"), true);
                Play(chime);
            }
            if (age >= WorldEventDuration && worldEventNearby == owner.worldEventId
                && worldEventSeen != owner.worldEventId && worldEventResult != owner.worldEventId)
            {
                worldEventResult = owner.worldEventId;
                ShowActionFeedback("DODGED!", true);
                Play(chime);
            }
        }

        void DrawWorldEventCue()
        {
            ElevatorManager owner = WorldOwner;
            if (owner.worldEventView == null || !owner.worldEventView.activeSelf || exteriorCamera == null) return;
            Vector3 screen = exteriorCamera.WorldToScreenPoint(owner.worldEventView.transform.position);
            Rect view = Match != null ? Match.ViewRect(Seat) : new Rect(0, 0, Screen.width, Screen.height);
            float y = Screen.height - screen.y;
            if (screen.z <= 0f || screen.x < view.x || screen.x > view.xMax || y < view.y || y > view.yMax) return;
            string label = owner.worldEventDimension == 0 ? "SCAN" : owner.worldEventDimension == 1 ? "SYRUP" : "CURRENT";
            label += owner.worldEventDirection > 0 ? "  →" : "  ←";
            GUIStyle style = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Mathf.Clamp(view.height / 35f, 16f, 26f)), fontStyle = FontStyle.Bold };
            float width = Mathf.Min(180f, Mathf.Max(1f, view.width - 8f));
            float x = Mathf.Clamp(screen.x - width * .5f, view.x + 4f, view.xMax - width - 4f);
            float safeTop = Mathf.Min(view.yMax - 40f, view.y + Mathf.Max(90f, view.height * .11f));
            float top = Mathf.Clamp(y - 65f, safeTop, view.yMax - 40f);
            if (showPassengerList && BuildingView && cameraViewBlend >= .99f)
            {
                Rect passengerList = PassengerListRect();
                if (passengerList.Overlaps(new Rect(x, top, width, 36f)))
                {
                    if (passengerList.xMin - view.x >= width + 8f) x = passengerList.xMin - width - 8f;
                    else top = Mathf.Min(view.yMax - 40f, passengerList.yMax + 8f);
                }
            }
            OutlinedLabel(new Rect(x, top, width, 36f), label, style, Gold);
        }

        // Briefly draw attention to the first warning without stopping either car.
        void ApplyWorldEventCameraAccent()
        {
            ElevatorManager owner = WorldOwner;
            if (!DisplayBuildingView || owner.worldEventId != 1 || owner.worldEventView == null
                || !owner.worldEventView.activeSelf || owner.round == null || exteriorCamera == null
                || Mathf.Abs(CurrentFloor - owner.worldEventFloor) > 3f) return;
            float age = owner.round.Elapsed - owner.worldEventStart;
            if (age < 0f || age >= 1.6f) return;
            float emphasis = Mathf.Sin(Mathf.PI * age / 1.6f);
            exteriorCamera.fieldOfView *= 1f - .08f * emphasis;
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
