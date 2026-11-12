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
        const float OfficeHazardDelay = 8f;
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
        const float WorldEventFirst = 8f, WorldEventInterval = 14f;
        const float WorldEventWarning = 2f, WorldEventDuration = 9f;
        const float WorldEventApproachDistance = 1.5f, WorldEventSweepDistance = 10f;
        GameObject worldEventView;
        int worldEventId, worldEventSeen, worldEventAnnounced, worldEventNearby, worldEventResult, worldEventDimension = -1;
        float worldEventStart = -1f, worldEventFloor, worldEventX, worldEventStartX;
        float worldEventTravelDistance = WorldEventSweepDistance;
        int worldEventDirection;
        Rider worldEventPassenger;
        int worldEventPassengerOldFloor = -1;
        int worldEventPassengerNewFloor = -1;
        bool worldEventPassengerMoved;
        float worldEventPassengerMoveStart = -1f;
        Vector3 worldEventPassengerFrom, worldEventPassengerTo;
        float forestScoutBoostTime;
        GameObject worldLeakEffect;
        float worldLeakTime;
        GameObject worldStatusEffect;
        float worldScanTime, worldSyrupTime, worldVineTime;
        float worldForcedRailTime, worldImpactVisualTime;
        float WorldHazardSpeedMultiplier => (worldScanTime > 0f ? .18f : worldSyrupTime > 0f ? .42f
            : worldVineTime > 0f ? .72f : 1f)
            * (forestScoutBoostTime > 0f ? 1.18f : 1f);

        public bool HasBouncer => round != null && round.HasOnboardFeature(PassengerFeature.StrongCollision);

        public void ShowPassengerAbility(string label, Vector3 worldPosition)
        {
            abilityFeedback = label;
            abilityFeedbackPosition = worldPosition;
            abilityFeedbackOrigin = exteriorCar != null ? exteriorCar.position : worldPosition;
            abilityFeedbackTime = 1.4f;
            string kind = label == "STUN!" ? "HUNTER" : label == "BLOCK!" ? "DIVER"
                : label == "BUMP!" ? "BOUNCER" : label == "CUT THROUGH!" ? "GOBLIN"
                : label == "FIREFLIES!" ? "FAIRY" : label == "SPORES!" ? "MUSHROOM" : label == "TETHER!" ? "ASTRONAUT"
                : label == "UFO LINK!" ? "ALIEN" : null;
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
            worldVineTime = Mathf.Max(0f, worldVineTime - dt);
            forestScoutBoostTime = Mathf.Max(0f, forestScoutBoostTime - dt);
            worldForcedRailTime = Mathf.Max(0f, worldForcedRailTime - dt);
            worldImpactVisualTime = Mathf.Max(0f, worldImpactVisualTime - dt);
            if (worldStatusEffect != null)
                worldStatusEffect.SetActive(worldScanTime > 0f || worldSyrupTime > 0f || worldImpactVisualTime > 0f);
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
            worldScanTime = worldSyrupTime = worldVineTime = 0f;
            worldForcedRailTime = worldImpactVisualTime = 0f;
            if (worldStatusEffect != null) Destroy(worldStatusEffect);
            worldStatusEffect = null;
            if (worldLeakEffect != null) worldLeakEffect.SetActive(false);
            if (WorldOwner != this) return;
            worldEventId = 0;
            worldEventStart = -1f;
            worldEventDimension = -1;
            worldEventPassenger = null;
            worldEventPassengerOldFloor = -1;
            worldEventPassengerNewFloor = -1;
            worldEventPassengerMoved = false;
            worldEventPassengerMoveStart = -1f;
            if (worldEventView != null) Destroy(worldEventView);
            worldEventView = null;
        }

        void UpdateWorldEvent()
        {
            if (round == null || round.ActiveDimension < 0 || round.ActiveDimension >= ElevatorRound.Dimensions
                || MenuManager.TutorialActive)
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
                worldEventPassenger = null;
                worldEventPassengerOldFloor = -1;
                worldEventPassengerNewFloor = -1;
                worldEventPassengerMoved = false;
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
                worldEventTravelDistance = WorldEventSweepDistance;
                if (dimension == 4) PrepareSpacePassengerTarget(travellingCar);
                GameObject prefab = WorldEventPrefab(dimension);
                if (worldEventView != null) Destroy(worldEventView);
                worldEventView = prefab != null ? Instantiate(prefab, hazardRoot, false) : null;
            }
            if (worldEventView == null) return;
            float age = elapsed - worldEventStart;
            bool active = age >= 0f && age < WorldEventDuration;
            if (worldEventView.activeSelf != active) worldEventView.SetActive(active);
            if (!active) return;
            float warning = worldEventId == 1 ? WorldEventWarning + 1f : WorldEventWarning;
            float sweepStart = dimension == 4 && worldEventPassenger != null
                ? warning + .55f + 1.15f : warning;
            float progress = Mathf.Clamp01((age - sweepStart) / (WorldEventDuration - sweepStart));
            worldEventX = worldEventStartX + worldEventDirection * worldEventTravelDistance * progress;
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
            else if (dimension == 3) AnimateForestVines(age, warning);
            else if (dimension == 4)
            {
                AnimateGravityWake(age, warning);
                UpdateSpacePassengerBeam(age, warning);
            }
        }

        GameObject WorldEventPrefab(int dimension)
        {
            if (dimension == 0) return sceneView.securitySweep;
            if (dimension == 1) return sceneView.syrupSurge;
            if (dimension == 2) return sceneView.oceanCurrent;
            if (dimension == 3) return sceneView.forestVineSweep;
            return sceneView.spaceGravitySweep;
        }

        void PrepareSpacePassengerTarget(ElevatorManager travellingCar)
        {
            worldEventPassenger = null;
            worldEventPassengerOldFloor = -1;
            worldEventPassengerNewFloor = -1;
            worldEventPassengerMoved = false;
            worldEventPassengerMoveStart = -1f;
            Rider best = null;
            int bestDistance = int.MaxValue;
            foreach (Rider rider in round.Riders)
            {
                if (rider == null || rider.Boarded || rider.Resolved || !round.IsQueuedAtStop(rider)) continue;
                int distance = Mathf.Abs(rider.Origin - Mathf.RoundToInt(worldEventFloor));
                if (distance > 1 || distance >= bestDistance
                    || FindSpacePassengerDestination(rider) < 0) continue;
                best = rider;
                bestDistance = distance;
            }
            if (best == null) return;

            worldEventPassenger = best;
            worldEventFloor = best.Origin;
            worldEventPassengerOldFloor = best.Origin;
            worldEventPassengerNewFloor = FindSpacePassengerDestination(best);
            PassengerView view = WaitingPassengerView(best);
            if (view != null)
            {
                worldEventPassengerFrom = view.transform.localPosition;
                float targetX = worldEventPassengerFrom.x;
                float towardCar = travellingCar.RouteLateralX - targetX;
                worldEventDirection = Mathf.Abs(towardCar) > .5f ? (towardCar > 0f ? 1 : -1)
                    : worldEventDirection;
                worldEventStartX = targetX;
                worldEventTravelDistance = Mathf.Clamp(Mathf.Abs(towardCar) + 3f,
                    WorldEventSweepDistance, 55f);
            }
        }

        int FindSpacePassengerDestination(Rider rider)
        {
            if (rider == null) return -1;
            // Prefer a visible two-floor lift, but use a nearby landing if full.
            int[] offsets = { 2, -2, 1, -1, 3, -3 };
            foreach (int offset in offsets)
            {
                int floor = rider.Origin + offset;
                if (round.CanRelocateWaitingPassenger(rider, floor)) return floor;
            }
            return -1;
        }

        void AnimateForestVines(float age, float warning)
        {
            Transform vines = worldEventView.transform.Find("Vines");
            if (vines == null) return;
            Transform safeRoute = worldEventView.transform.Find("Fairy safe route");
            ElevatorManager rival = Match != null ? Match.Rival : null;
            bool hasScout = round.HasOnboardFeature(PassengerFeature.ForestScout)
                || rival != null && rival.round != null && rival.round.HasOnboardFeature(PassengerFeature.ForestScout);
            if (safeRoute != null)
            {
                safeRoute.gameObject.SetActive(hasScout);
                safeRoute.localScale = Vector3.one * (1f + .08f * Mathf.Sin(age * 7f));
            }
            float warningPulse = age < warning ? .06f * Mathf.Sin(age * 12f) : 0f;
            vines.localRotation = Quaternion.Euler(0f, 0f, warningPulse);
            float sway = Mathf.Sin(age * 3.6f) * .055f;
            for (int i = 0; i < vines.childCount; i++)
            {
                Transform strand = vines.GetChild(i);
                if (strand.name.Contains("vine")) strand.localRotation = Quaternion.Euler(0f, 0f, sway * (i % 2 == 0 ? 1f : -1f));
            }
        }

        void AnimateGravityWake(float age, float warning)
        {
            Transform orbit = worldEventView.transform.Find("Orbit");
            if (orbit != null) orbit.localRotation = Quaternion.Euler(0f, 0f, age * 84f);
            Transform saucer = worldEventView.transform.Find("UFO");
            if (saucer != null)
            {
                saucer.localPosition = new Vector3(.18f * Mathf.Sin(age * 2.7f),
                    .4f + .2f * Mathf.Sin(age * 3.3f), -.2f);
                saucer.localRotation = Quaternion.Euler(0f, 0f, 7f * Mathf.Sin(age * 2.2f));
                Transform beam = saucer.Find("Beam");
                if (beam != null)
                {
                    float strength = age < warning ? .35f + .15f * Mathf.Sin(age * 8f)
                        : .8f + .2f * Mathf.Sin(age * 7f);
                    beam.localScale = new Vector3(.68f * strength, 1.52f, .58f * strength);
                }
            }
            Transform singularity = worldEventView.transform.Find("Singularity");
            if (singularity != null)
            {
                float pulse = 1f + .07f * Mathf.Sin(age * 4.4f);
                singularity.localScale = new Vector3(1.48f * pulse, 1.48f * pulse, .25f);
            }
        }

        void UpdateSpacePassengerBeam(float age, float warning)
        {
            if (worldEventPassenger == null || worldEventPassengerMoved
                || age < warning + .55f) return;
            // The rival can board this rider or fill the landing while the beam charges.
            if (!round.IsQueuedAtStop(worldEventPassenger))
            {
                worldEventPassenger = null;
                return;
            }
            if (!round.CanRelocateWaitingPassenger(worldEventPassenger, worldEventPassengerNewFloor))
                worldEventPassengerNewFloor = FindSpacePassengerDestination(worldEventPassenger);
            if (worldEventPassengerNewFloor < 0)
            {
                worldEventPassenger = null;
                return;
            }
            PassengerView view = WaitingPassengerView(worldEventPassenger);
            if (view == null || !round.TryRelocateWaitingPassenger(worldEventPassenger, worldEventPassengerNewFloor))
            {
                worldEventPassenger = null;
                return;
            }
            worldEventPassengerTo = WaitingWorldPosition(worldEventPassenger);
            worldEventPassengerMoveStart = round.Elapsed;
            worldEventPassengerMoved = true;
            ShowActionFeedback("UFO BEAM · F" + ElevatorRound.FloorNumber(worldEventPassengerNewFloor), true);
            Play(chime);
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
            int floor = Mathf.Clamp(Mathf.RoundToInt(travelFloor), MinTravelFloor, MaxTravelFloor);
            Vector3 eventPoint = exteriorCar.parent.TransformPoint(new Vector3(owner.worldEventX,
                owner.worldEventFloor * ShaftFloorHeight + 2f, HazardDepth));
            if (owner.worldEventDimension == 3)
            {
                if (round.HasOnboardFeature(PassengerFeature.ForestSporeGuard))
                {
                    collisionImpact = .35f;
                    ShowPassengerAbility("SPORES!", eventPoint);
                    ShowActionFeedback("SPORES BLOCK VINES!", true);
                    Play(chime);
                    return;
                }
                collisionImpact = .85f;
                if (round.HasOnboardFeature(PassengerFeature.ForestShortcut))
                {
                    StartRailRide(floor, owner.worldEventDirection,
                        Mathf.Max(CruiseSpeed * 1.5f, Mathf.Abs(travelVelocity)));
                    ShowPassengerAbility("CUT THROUGH!", eventPoint);
                    ShowActionFeedback("GOBLIN SHORTCUT!", true);
                    Play(kickWhoosh);
                    return;
                }
                StartRailRide(floor, owner.worldEventDirection, Mathf.Max(CruiseSpeed, .6f));
                worldVineTime = 1.5f;
                worldForcedRailTime = .35f;
                ShowRailHazardImpact(sceneView.forestVineSweep, "Vines", .48f);
                ShowActionFeedback("CAUGHT BY VINES!", false);
                Play(buzz);
                return;
            }

            if (owner.worldEventDimension == 4)
            {
                bool alien = round.HasOnboardFeature(PassengerFeature.UfoLink);
                if (!alien && round.HasOnboardFeature(PassengerFeature.GravityAnchor))
                {
                    ShowPassengerAbility("TETHER!", eventPoint);
                    ShowActionFeedback("ASTRONAUT ANCHORS THE CAR!", true);
                    Play(chime);
                    return;
                }
                float towardHole = owner.worldEventX - RouteLateralX;
                int direction = Mathf.Abs(towardHole) > .5f ? (towardHole > 0f ? 1 : -1)
                    : owner.worldEventDirection;
                if (alien)
                {
                    direction = owner.worldEventDirection;
                    float roomAhead = direction > 0 ? GroundRightX - RouteLateralX : RouteLateralX - GroundLeftX;
                    if (roomAhead < 5f) direction = -direction;
                }

                float speed = alien ? SpeedLimit : Mathf.Max(CruiseSpeed * 1.6f, Mathf.Abs(travelVelocity));
                StartRailRide(floor, direction, speed);
                worldForcedRailTime = .55f;
                collisionImpact = alien ? .35f : 1f;
                ShowRailHazardImpact(sceneView.spaceGravitySweep, alien ? "UFO" : "Orbit", 1f);
                if (alien)
                {
                    ShowPassengerAbility("UFO LINK!", eventPoint);
                    ShowActionFeedback("ALIEN SHORTCUT!", true);
                }
                else ShowActionFeedback("PULLED BY BLACK HOLE!", false);
                Play(alien ? kickWhoosh : buzz);
                return;
            }

            collisionImpact = 1f;
            if (owner.worldEventDimension == 2)
            {
                StartRailRide(floor, owner.worldEventDirection, Mathf.Max(CruiseSpeed, .6f));
                if (!HasHandyman && repairedFor <= 0f)
                {
                    rustExposure = Mathf.Min(secondsPerRustLevel * 2f, rustExposure + secondsPerRustLevel * .45f);
                    ShowCurrentLeak();
                }
                ShowActionFeedback("PUSHED BY CURRENT", false);
                Play(kickWhoosh);
                return;
            }

            bool scanner = owner.worldEventDimension == 0;
            if (scanner) worldScanTime = 2.5f;
            else worldSyrupTime = 4.5f;
            travelVelocity *= scanner ? .2f : .42f;
            railVelocity *= scanner ? .2f : .42f;
            ShowWorldStatus(scanner ? sceneView.securitySweep : sceneView.syrupSurge, scanner);
            ShowActionFeedback(scanner ? "SCANNER HIT · SLOWED" : "SYRUP HIT · SLOWED", false);
            Play(buzz);
        }

        void ShowRailHazardImpact(GameObject prefab, string visiblePart, float scale)
        {
            if (prefab == null || exteriorCar == null) return;
            if (worldStatusEffect != null) Destroy(worldStatusEffect);
            worldStatusEffect = Instantiate(prefab, exteriorCar, false);
            worldStatusEffect.name = visiblePart + " impact";
            foreach (Transform part in worldStatusEffect.transform)
                part.gameObject.SetActive(part.name == visiblePart);
            worldStatusEffect.transform.localPosition = new Vector3(0f, 1.1f, -1.6f);
            worldStatusEffect.transform.localScale = Vector3.one * scale;
            worldImpactVisualTime = .85f;
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
                string label = WorldEventLabel(owner);
                if (owner.worldEventDimension == 3 && round.HasOnboardFeature(PassengerFeature.ForestScout))
                    label = "FIREFLY ROUTE · " + (owner.worldEventDirection > 0 ? "←" : "→");
                ShowActionFeedback(label, true);
                Play(chime);
            }
            if (age >= WorldEventDuration && worldEventNearby == owner.worldEventId
                && worldEventSeen != owner.worldEventId && worldEventResult != owner.worldEventId)
            {
                worldEventResult = owner.worldEventId;
                if (owner.worldEventDimension == 3 && round.HasOnboardFeature(PassengerFeature.ForestScout))
                {
                    forestScoutBoostTime = 2.5f;
                    ShowPassengerAbility("FIREFLIES!", exteriorCar.position + Vector3.up * 3f);
                    ShowActionFeedback("FIREFLIES · SPEED UP!", true);
                }
                else ShowActionFeedback("DODGED!", true);
                Play(chime);
            }
        }

        static string WorldEventLabel(ElevatorManager owner)
        {
            if (owner.worldEventDimension == 0) return "SCAN";
            if (owner.worldEventDimension == 1) return "SYRUP";
            if (owner.worldEventDimension == 2) return "CURRENT";
            if (owner.worldEventDimension == 3) return "VINES";
            if (owner.worldEventPassenger != null && owner.worldEventPassengerNewFloor >= 0)
                return "UFO F" + ElevatorRound.FloorNumber(owner.worldEventPassengerOldFloor)
                    + " → F" + ElevatorRound.FloorNumber(owner.worldEventPassengerNewFloor);
            return "BLACK HOLE";
        }

        void DrawWorldEventCue()
        {
            ElevatorManager owner = WorldOwner;
            if (owner.worldEventView == null || !owner.worldEventView.activeSelf || exteriorCamera == null) return;
            Vector3 screen = exteriorCamera.WorldToScreenPoint(owner.worldEventView.transform.position);
            Rect view = Match != null ? Match.ViewRect(Seat) : new Rect(0, 0, Screen.width, Screen.height);
            float y = Screen.height - screen.y;
            if (screen.z <= 0f || screen.x < view.x || screen.x > view.xMax || y < view.y || y > view.yMax) return;
            string label = WorldEventLabel(owner);
            if (owner.worldEventDimension == 3 && round.HasOnboardFeature(PassengerFeature.ForestScout))
                label = "FIREFLY ROUTE · " + (owner.worldEventDirection > 0 ? "←" : "→");
            if (owner.worldEventDimension < 3) label += owner.worldEventDirection > 0 ? "  →" : "  ←";
            GUIStyle style = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Mathf.Clamp(view.height / 35f, 16f, 26f)), fontStyle = FontStyle.Bold };
            if (view.width <= 24f || view.height < 44f) return;
            style.wordWrap = false;
            float width = FitFeedbackText(style, new GUIContent(label), view.width - 24f) + 16f;
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
            if (view.width <= 24f || view.height < 53f) return;
            GUIStyle style = new GUIStyle(large)
                { alignment = TextAnchor.MiddleCenter, fontSize = 30, wordWrap = false };
            float width = FitFeedbackText(style, new GUIContent(abilityFeedback), view.width - 24f) + 16f;
            float x = Mathf.Clamp(screen.x - width * .5f, view.x + 4f, view.xMax - width - 4f);
            float y = Mathf.Clamp(Screen.height - screen.y - 30f,
                Mathf.Min(view.y + 50f, view.yMax - 49f), view.yMax - 49f);
            OutlinedLabel(new Rect(x, y, width, 45f), abilityFeedback, style, Gold);
        }
    }
}
