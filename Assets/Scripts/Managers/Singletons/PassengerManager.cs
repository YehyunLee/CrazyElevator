using System.Collections.Generic;
using UnityEngine;
using CrazyElevator.Shared;

namespace CrazyElevator.Managers
{
    // Liam PassengerManager: boarding, kickout, waiting queues, prefabs, doors.
    // Lives on the same GameObject as ElevatorManager (one pair per elevator for 1v1).
    [DisallowMultipleComponent]
    public sealed class PassengerManager : MonoBehaviour
    {
        public ElevatorManager Elevator { get; private set; }

        public float DoorsOpenPercent => Elevator != null ? Elevator.DoorsOpenPercent : 0f;
        public IReadOnlyList<CrazyElevator.Shared.Rider> PassengersWaiting =>
            Elevator != null ? Elevator.WaitingPassengers : System.Array.Empty<CrazyElevator.Shared.Rider>();
        public IReadOnlyList<CrazyElevator.Shared.Rider> CurrentPassengers =>
            Elevator != null ? Elevator.CurrentPassengers : System.Array.Empty<CrazyElevator.Shared.Rider>();
        public PassengerView[] AllPassengerPrefabs =>
            Elevator != null ? Elevator.PassengerPrefabs : System.Array.Empty<PassengerView>();
        public Camera CinemachineCamera => Elevator != null ? Elevator.CabinCamera : null;

        public void Bind(ElevatorManager elevator) => Elevator = elevator;

        public IEnumerable<CrazyElevator.Shared.Rider> PassengersForDimension(DimensionRange dimension)
        {
            if (Elevator == null) yield break;
            foreach (var rider in Elevator.AllRiders)
            {
                if (rider == null || rider.Resolved) continue;
                if (dimension.Contains(rider.Origin) || dimension.Contains(rider.Destination))
                    yield return rider;
            }
        }

        public void OnboardPassenger() => Elevator?.OnboardSelectedPublic();
        public void KickoutPassenger() => Elevator?.KickoutSelectedPublic();
        public void CloseDoor() => Elevator?.CloseAndTravelPublic();
        public void SpawnPassengersAvailable() => Elevator?.RefreshWaitingPublic();
    }
}
