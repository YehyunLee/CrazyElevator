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
