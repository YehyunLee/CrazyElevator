using System.Collections.Generic;
using UnityEngine;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;

    // Liam-facing public surface for ElevatorManager + PassengerManager hooks.
    public sealed partial class ElevatorManager
    {
        GameManager session;
        PassengerManager passengerManager;

        public bool IsPaused => paused;
        public float DoorsOpenPercent => doors;
        public float SessionFadeAlpha => sessionFadeAlpha;
        public float TravelVelocity => travelVelocity;
        public bool TravelInputActive => Mathf.Abs(boostAxis) > .25f;
        public bool BoostActive => boostHeld && TravelInputActive;
        public int TravelStartCount => travelStartCount;
        public int FloorStopCount => floorStopCount;
        public int DiscreteFloor => round != null ? round.Floor : 0;
        public int DiscreteStop => round != null ? round.Stop : ElevatorRound.CenterStop;
        public int MaxCapacity => ElevatorRound.Capacity;
        public bool HasSelectedPassenger => selectedRider != null;
        public int PassengerExitCount => passengerExitCount;
        public int KickoutCount => kickoutCount;
        public bool HasSelectedBoardedPassenger => selectedRider != null && selectedRider.Boarded;
        public int SelectedPassengerDestination => selectedRider != null ? selectedRider.Destination : -1;
        // Only show a steal when the rival can still claim this landing.
        public bool ApproachingPickup(Rider rider)
        {
            if (rider == null || round == null || round.Finished || !round.HasCapacityFor(rider))
                return false;
            // Both cars can already be at the same shared queue. This is the
            // most common steal, and it used to miss the arrival-only check.
            if (phase == Phase.Boarding || phase == Phase.Opening)
                return round.Floor == rider.Origin && round.Stop == rider.OriginStop;
            if (phase == Phase.Docking)
                return destination == rider.Origin && dockingStop == rider.OriginStop;

            // The CPU picks a specific landing before travel. Catch its final
            // approach too: a car already boarding can collide with it before docking.
            if (phase != Phase.Moving || !IsNpc || strongCollisionRecoil
                || npcTargetFloor != rider.Origin || npcTargetStop != rider.OriginStop)
                return false;
            float remaining = rider.Origin - travelFloor;
            return remaining * travelVelocity > 0f
                && Mathf.Abs(remaining) <= Mathf.Abs(travelVelocity) * 1.5f;
        }
        public AudioClip DingClip => ding;
        public AudioClip ElevatorDing => ding;
        public Camera CabinCamera => eye;
        public Camera CinemachineCamera => eye;
        public PassengerView[] PassengerPrefabs => sceneView != null ? sceneView.passengers : null;
        public PassengerManager Passengers => passengerManager;

        public IReadOnlyList<Rider> CurrentPassengers
        {
            get
            {
                if (round == null) return System.Array.Empty<Rider>();
                var boarded = new List<Rider>();
                foreach (var rider in round.Riders)
                    if (round.Owns(rider)) boarded.Add(rider);
                return boarded;
            }
        }

        public IReadOnlyList<Rider> WaitingPassengers
        {
            get
            {
                if (round == null) return System.Array.Empty<Rider>();
                var waiting = new List<Rider>();
                foreach (var rider in round.Riders)
                    if (round.IsOffered(rider))
                        waiting.Add(rider);
                return waiting;
            }
        }

        public IReadOnlyList<Rider> AllRiders =>
            round != null ? round.Riders : (IReadOnlyList<Rider>)System.Array.Empty<Rider>();

        public bool TryGetWaitingPassengerWorldPosition(Rider rider, out Vector3 position)
        {
            ElevatorManager owner = WorldOwner;
            if (rider == null || owner == null || owner.exteriorCar == null
                || owner.exteriorCar.parent == null)
            {
                position = Vector3.zero;
                return false;
            }
            if (owner.crowd.TryGetValue(rider, out CrowdFigure figure) && figure.Root != null)
            {
                position = figure.Root.position;
                return true;
            }
            position = owner.exteriorCar.parent.TransformPoint(owner.WaitingWorldPosition(rider));
            return true;
        }

        // Select from the real, shared landing queues; do not spawn a duplicate passenger.
        public Rider ChooseMatchVip()
        {
            if (round == null) return null;
            Rider best = null;
            float bestCost = float.MaxValue;
            foreach (var rider in round.Riders)
            {
                if (!round.IsQueuedAtStop(rider) || rider.Space > 2) continue;
                float cost = Mathf.Abs(rider.Origin - (round.MinFloor + 3))
                    + Mathf.Abs(rider.OriginStop - ElevatorRound.CenterStop) * 1.5f;
                if (cost >= bestCost) continue;
                best = rider; bestCost = cost;
            }
            if (best != null) { best.IsVip = true; best.Bonus += 250; }
            return best;
        }

        public bool HasCandyTrail => round != null && round.HasOnboardFeature(PassengerFeature.GroupParty);

        public void ApplyCandyTrail()
        {
            collisionSlowTimer = Mathf.Max(collisionSlowTimer, 2f);
            travelVelocity *= .45f;
            railVelocity *= .45f;
            boostAxis = 0f; boostHeld = false;
            collisionImpact = .7f;
            notice = "STICKY CANDY!";
            Play(buzz);
        }

        public void BindSession(GameManager manager) => session = manager;

        void EnsureManagers()
        {
            // Player elevator should already have PassengerManager in Main.
            // NPC elevators created for 1v1 may still need one added here.
            passengerManager = GetComponent<PassengerManager>();
            if (passengerManager == null)
            {
                if (!IsNpc)
                    Debug.LogWarning("PassengerManager missing on player elevator — add it in Main.");
                passengerManager = gameObject.AddComponent<PassengerManager>();
            }
            passengerManager.Bind(this);
            if (GameManager.Instance != null && Seat == 0)
                GameManager.Instance.BindPlayer(this);
        }

        public void StopAtFloor() => RequestFloorStop();
        public void RequestFloorStopPublic() => RequestFloorStop();
        public void CloseAndTravelPublic() => CloseAndTravel();
        public void ResetRoundPublic()
        {
            if (Match != null) Match.StartMatch();
            else StartSoloRound();
        }
        public void SetPausedPublic(bool value) => paused = value;

        // Place the real car on the main shaft for the hands-on collision lesson.
        // The match holds it still until the learner actually pushes the stick.
        public bool StageTutorialCollision(float floor)
        {
            if (!MenuManager.TutorialActive || round == null || Match == null) return false;
            CancelPassengerDrag();
            SelectRider(null);
            ClearHandymanRepair();
            ClearInteriorTransfers();
            CloseRequestedBoardingDoors();
            round.Arrive(Mathf.Clamp(Mathf.RoundToInt(floor), MinTravelFloor, MaxTravelFloor),
                ElevatorRound.CenterStop);
            travelFloor = Mathf.Clamp(floor, MinTravelFloor, MaxTravelFloor);
            travelDirection = 1;
            destination = selectedFloor = -1;
            phase = Phase.Moving;
            phaseTime = 0f;
            tutorialPassengerView = false;
            ResetRouteLateral();
            StopTutorialCollisionMotion();
            passedFloors.Clear();
            passedFloors.Add(round.Floor);
            doors = 0f;
            SetDoors(0f);
            notice = "BUMP YOUR RIVAL";
            return true;
        }

        public void StopTutorialCollisionMotion()
        {
            travelVelocity = railVelocity = 0f;
            boostAxis = 0f;
            boostHeld = false;
            collisionSlowTimer = collisionRecoilTimer = collisionImpact = bouncerImpact = 0f;
            strongCollisionRecoil = false;
            ClearRouteTurns();
        }

        public void SetTravelInput(float axis, bool boost)
        {
            if (phase != Phase.Moving) return;
            if (strongCollisionRecoil && collisionRecoilTimer > 0f) return;
            boostAxis = Mathf.Abs(axis) > .25f ? Mathf.Clamp(axis, -1f, 1f) : 0f;
            if (boostAxis != 0)
            {
                int requestedDirection = boostAxis > 0 ? 1 : -1;
                if (requestedDirection != travelDirection
                    && travelVelocity * requestedDirection < 0f
                    && collisionRecoilTimer <= 0f)
                {
                    travelVelocity = requestedDirection * CruiseSpeed;
                }
                travelDirection = requestedDirection;
            }
            boostHeld = boost && boostAxis != 0;
        }

        public void OnboardSelectedPublic()
        {
            OpenBoardingDoor(selectedBoardingDoor);
        }

        public void KickoutSelectedPublic()
        {
            if (!CanSelect(selectedRider) || !selectedRider.Boarded) return;
            kickoutCount++;
            ConfirmPassenger();
        }

        public void RefreshWaitingPublic() => SyncFigures(0);
    }
}
