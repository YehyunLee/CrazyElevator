using System;
using System.Collections.Generic;
using System.Linq;
using CrazyElevator.Map;

internal static class Program
{
    private static int Main()
    {
        var checks = new Dictionary<string, Action>
        {
            { "Waiting passengers group by current floor", FloorGrouping },
            { "Boarding and terminal states leave the waiting group", BoardingAndDelivery },
            { "Passenger updates replace IDs without duplicates", PassengerReplacement },
            { "Elevator snapshots track fractional floor movement", ElevatorReplacement },
            { "A hundred-floor map accepts every floor and rejects 101", HundredFloorBounds },
            { "A passenger can board on floor 1 and arrive on floor 100", HundredFloorJourney },
            { "Elevators retain fractional positions near floor 100", HundredFloorMovement },
            { "Floor sections divide at floors 50 and 51", SectionBoundaries },
            { "Section passenger counts use the current floor across 50 and 51", SectionPassengerCounts },
            { "Boarding keeps section counts and ended trips leave them", SectionBoardingAndTerminalStates },
            { "Replacing passenger snapshots does not inflate section counts", SectionReplacementCounts },
            { "Section count helpers reject invalid arguments", SectionCountValidation },
            { "Gameplay owns deadlines and satisfaction", AuthoritativeData },
            { "Invalid updates cannot partially mutate the map", InvalidUpdates },
            { "Constructors reject malformed snapshots", SnapshotValidation },
            { "Mutations notify observers and clear resets the map", NotificationsAndClear },
            { "Public data cannot mutate stored snapshots or collections", ReadOnlyData }
        };

        int failed = 0;
        foreach (KeyValuePair<string, Action> check in checks)
        {
            try
            {
                check.Value();
                Console.WriteLine("PASS " + check.Key);
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + check.Key + ": " + exception.Message);
            }
        }
        Console.WriteLine((checks.Count - failed) + "/" + checks.Count + " map checks passed.");
        return failed == 0 ? 0 : 1;
    }

    private static PassengerSnapshot Passenger(string id, int currentFloor = 1,
        PassengerStatus status = PassengerStatus.Waiting, int destinationFloor = 5,
        float deadline = 30, int points = 100)
    {
        return new PassengerSnapshot(id, "Passenger " + id, 1, destinationFloor,
            currentFloor, status == PassengerStatus.Riding ? "A" : null, deadline, points, status);
    }

    private static void FloorGrouping()
    {
        var map = new MapState(5);
        map.UpsertPassenger(Passenger("one", 1));
        map.UpsertPassenger(Passenger("two", 3));
        map.UpsertPassenger(Passenger("three", 3));
        Equal(1, map.CountWaiting(1));
        Equal(2, map.CountWaiting(3));
        Equal(0, map.CountWaiting(5));
        map.UpsertPassenger(Passenger("two", 2));
        Equal(1, map.CountWaiting(3));
        Equal(1, map.CountWaiting(2));
    }

    private static void BoardingAndDelivery()
    {
        var map = new MapState(5);
        map.UpsertPassenger(Passenger("rider", 2));
        Equal(1, map.CountWaiting(2));
        map.UpsertPassenger(Passenger("rider", 0, PassengerStatus.Riding));
        Equal(0, map.CountWaiting(2));
        True(map.TryGetPassenger("rider", out PassengerSnapshot rider));
        Equal("A", rider.ElevatorId);
        map.UpsertPassenger(Passenger("rider", 5, PassengerStatus.Delivered));
        map.UpsertPassenger(Passenger("late", 2, PassengerStatus.Expired));
        Equal(0, map.CountWaiting(5));
        Equal(0, map.CountWaiting(2));
        Equal(2, map.Passengers.Count());
    }

    private static void PassengerReplacement()
    {
        var map = new MapState(5);
        PassengerSnapshot old = Passenger("same");
        PassengerSnapshot latest = Passenger("same", 3, deadline: 55, points: 70);
        map.UpsertPassenger(old);
        map.UpsertPassenger(latest);
        Equal(1, map.Passengers.Count());
        True(map.TryGetPassenger("same", out PassengerSnapshot found));
        True(ReferenceEquals(latest, found));
        Equal(1, old.CurrentFloor);
        True(map.RemovePassenger("same"));
        True(!map.RemovePassenger("same"));
        True(!map.TryGetPassenger("same", out _));
    }

    private static void ElevatorReplacement()
    {
        var map = new MapState(5);
        map.UpsertElevator(new ElevatorSnapshot("A", 0, 1, false, 0, 4));
        map.UpsertElevator(new ElevatorSnapshot("A", 0, 2.5f, true, 3, 4));
        map.UpsertElevator(new ElevatorSnapshot("B", 2, 5, false, 0, 2));
        Equal(2, map.Elevators.Count());
        True(map.TryGetElevator("A", out ElevatorSnapshot elevator));
        Equal(2.5f, elevator.FloorPosition);
        True(elevator.IsMoving);
        Equal(3, elevator.Occupancy);
        True(!map.TryGetElevator("missing", out _));
    }

    private static void AuthoritativeData()
    {
        var map = new MapState(5);
        map.SetTotalSatisfaction(75);
        map.UpsertPassenger(Passenger("done", 5, PassengerStatus.Delivered, deadline: 42, points: 300));
        Equal(75, map.TotalSatisfaction);
        True(map.TryGetPassenger("done", out PassengerSnapshot passenger));
        Equal(42f, passenger.DeadlineSeconds);
        Equal(300, passenger.AvailablePoints);
        map.SetTotalSatisfaction(-25);
        Equal(-25, map.TotalSatisfaction);
    }

    private static void SectionBoundaries()
    {
        Equal(50, MapSections.FirstSectionLastFloor);
        Equal("Cotton Candy", MapSections.GetName(1));
        Equal("Cotton Candy", MapSections.GetName(50));
        Equal("Water", MapSections.GetName(51));
        Equal("Water", MapSections.GetName(100));
        for (int floor = 1; floor <= 50; floor++)
            Equal(0, MapSections.GetSectionIndex(floor));
        for (int floor = 51; floor <= 100; floor++)
            Equal(1, MapSections.GetSectionIndex(floor));
        Throws<ArgumentOutOfRangeException>(() => MapSections.GetName(0));
        Throws<ArgumentOutOfRangeException>(() => MapSections.GetName(101));
        Throws<ArgumentOutOfRangeException>(() => MapSections.GetSectionIndex(0));
        Throws<ArgumentOutOfRangeException>(() => MapSections.GetSectionIndex(101));
    }

    private static void HundredFloorBounds()
    {
        var map = new MapState(100);
        for (int floor = 1; floor <= 100; floor++)
        {
            map.UpsertPassenger(new PassengerSnapshot("floor-" + floor, "Floor " + floor,
                floor, floor == 100 ? 1 : 100, floor, null, 120, 100, PassengerStatus.Waiting));
            Equal(1, map.CountWaiting(floor));
        }
        Equal(100, map.Passengers.Count());
        Throws<ArgumentOutOfRangeException>(() => map.CountWaiting(0));
        Throws<ArgumentOutOfRangeException>(() => map.CountWaiting(101));
        Throws<ArgumentOutOfRangeException>(() => map.UpsertPassenger(Passenger("too-high", 101, destinationFloor: 100)));
        Throws<ArgumentOutOfRangeException>(() => map.UpsertPassenger(Passenger("bad-destination", destinationFloor: 101)));
        Throws<ArgumentOutOfRangeException>(() => map.UpsertPassenger(
            new PassengerSnapshot("bad-source", "Invalid", 101, 1, 100, null, 120, 100, PassengerStatus.Waiting)));
        Equal(100, map.Passengers.Count());
        Equal(1, map.CountWaiting(100));
    }

    private static void SectionPassengerCounts()
    {
        var map = new MapState(100);
        Equal(0, MapSections.CountPassengers(map, 0));
        Equal(0, MapSections.CountPassengers(map, 1));
        foreach (int floor in new[] { 1, 50, 51, 100 })
            map.UpsertPassenger(Passenger("floor-" + floor, floor));
        Equal(2, MapSections.CountPassengers(map, 0));
        Equal(2, MapSections.CountPassengers(map, 1));
        // SourceFloor is 1 here: waiting passengers must group by CurrentFloor instead.
        True(map.TryGetPassenger("floor-51", out PassengerSnapshot waterPassenger));
        Equal(1, MapSections.GetPassengerSectionIndex(waterPassenger));
        True(map.TryGetPassenger("floor-50", out PassengerSnapshot candyPassenger));
        Equal(0, MapSections.GetPassengerSectionIndex(candyPassenger));
    }

    private static void SectionBoardingAndTerminalStates()
    {
        var map = new MapState(100);
        map.UpsertPassenger(new PassengerSnapshot("candy", "Candy rider", 50, 100, 50,
            null, 120, 100, PassengerStatus.Waiting));
        map.UpsertPassenger(new PassengerSnapshot("water", "Water rider", 51, 1, 51,
            null, 120, 100, PassengerStatus.Waiting));
        Equal(1, MapSections.CountPassengers(map, 0));
        Equal(1, MapSections.CountPassengers(map, 1));
        map.UpsertPassenger(new PassengerSnapshot("candy", "Candy rider", 50, 100, 0,
            "A", 120, 100, PassengerStatus.Riding));
        map.UpsertPassenger(new PassengerSnapshot("water", "Water rider", 51, 1, 0,
            "B", 120, 100, PassengerStatus.Riding));
        Equal(1, MapSections.CountPassengers(map, 0));
        Equal(1, MapSections.CountPassengers(map, 1));
        True(map.TryGetPassenger("candy", out PassengerSnapshot candyRider));
        True(map.TryGetPassenger("water", out PassengerSnapshot waterRider));
        Equal(0, MapSections.GetPassengerSectionIndex(candyRider));
        Equal(1, MapSections.GetPassengerSectionIndex(waterRider));

        map.UpsertPassenger(new PassengerSnapshot("candy", "Candy rider", 50, 100, 100,
            null, 120, 100, PassengerStatus.Delivered));
        map.UpsertPassenger(new PassengerSnapshot("water", "Water rider", 51, 1, 51,
            null, 120, 0, PassengerStatus.Expired));
        Equal(0, MapSections.CountPassengers(map, 0));
        Equal(0, MapSections.CountPassengers(map, 1));
        Equal(2, map.Passengers.Count());
    }

    private static void SectionReplacementCounts()
    {
        var map = new MapState(100);
        map.UpsertPassenger(Passenger("same", 50));
        map.UpsertPassenger(Passenger("same", 50, points: 90));
        Equal(1, MapSections.CountPassengers(map, 0));
        Equal(0, MapSections.CountPassengers(map, 1));
        map.UpsertPassenger(Passenger("same", 51));
        Equal(0, MapSections.CountPassengers(map, 0));
        Equal(1, MapSections.CountPassengers(map, 1));
        Equal(1, map.Passengers.Count());
        True(map.RemovePassenger("same"));
        Equal(0, MapSections.CountPassengers(map, 1));
    }

    private static void SectionCountValidation()
    {
        var map = new MapState(100);
        Throws<ArgumentNullException>(() => MapSections.CountPassengers(null, 0));
        Throws<ArgumentNullException>(() => MapSections.GetPassengerSectionIndex(null));
        Throws<ArgumentOutOfRangeException>(() => MapSections.CountPassengers(map, -1));
        Throws<ArgumentOutOfRangeException>(() => MapSections.CountPassengers(map, 2));
    }

    private static void HundredFloorJourney()
    {
        var map = new MapState(100);
        map.UpsertPassenger(Passenger("long-trip", 1, destinationFloor: 100));
        Equal(1, map.CountWaiting(1));
        Equal(0, map.CountWaiting(100));
        map.UpsertPassenger(Passenger("long-trip", 0, PassengerStatus.Riding, destinationFloor: 100));
        Equal(0, map.CountWaiting(1));
        Equal(0, map.CountWaiting(100));
        True(map.TryGetPassenger("long-trip", out PassengerSnapshot riding));
        Equal(1, riding.SourceFloor);
        Equal(100, riding.DestinationFloor);
        Equal(0, riding.CurrentFloor);
        Equal("A", riding.ElevatorId);
        map.UpsertPassenger(Passenger("long-trip", 100, PassengerStatus.Delivered, destinationFloor: 100));
        True(map.TryGetPassenger("long-trip", out PassengerSnapshot delivered));
        Equal(100, delivered.CurrentFloor);
        Equal(PassengerStatus.Delivered, delivered.Status);
        Equal(1, map.Passengers.Count());
        Equal(0, map.CountWaiting(100));
        Equal(0, map.TotalSatisfaction);
    }

    private static void HundredFloorMovement()
    {
        var map = new MapState(100);
        map.UpsertElevator(new ElevatorSnapshot("A", 0, 1, false, 0, 4));
        map.UpsertElevator(new ElevatorSnapshot("A", 0, 99.5f, true, 1, 4));
        True(map.TryGetElevator("A", out ElevatorSnapshot approaching));
        Equal(99.5f, approaching.FloorPosition);
        True(approaching.IsMoving);
        map.UpsertElevator(new ElevatorSnapshot("A", 0, 100, false, 0, 4));
        True(map.TryGetElevator("A", out ElevatorSnapshot arrived));
        Equal(100f, arrived.FloorPosition);
        True(!arrived.IsMoving);
        Throws<ArgumentOutOfRangeException>(() => map.UpsertElevator(new ElevatorSnapshot("A", 0, 101, true, 0, 4)));
        True(map.TryGetElevator("A", out ElevatorSnapshot afterInvalid));
        True(ReferenceEquals(arrived, afterInvalid));
        Equal(1, map.Elevators.Count());
    }

    private static void InvalidUpdates()
    {
        var map = new MapState(5, 2);
        PassengerSnapshot original = Passenger("original");
        map.UpsertPassenger(original);
        int notifications = 0;
        map.Changed += () => notifications++;
        Throws<ArgumentOutOfRangeException>(() => map.UpsertPassenger(Passenger("original", 6)));
        Throws<ArgumentOutOfRangeException>(() => map.UpsertPassenger(Passenger("new", destinationFloor: 6)));
        Throws<ArgumentOutOfRangeException>(() => map.UpsertPassenger(
            new PassengerSnapshot("source", "Source", 6, 2, 2, null, 10, 20, PassengerStatus.Waiting)));
        Throws<ArgumentOutOfRangeException>(() => map.UpsertElevator(new ElevatorSnapshot("A", 2, 3, false, 0, 4)));
        Throws<ArgumentOutOfRangeException>(() => map.UpsertElevator(new ElevatorSnapshot("A", 0, 5.1f, true, 0, 4)));
        Throws<ArgumentOutOfRangeException>(() => map.CountWaiting(0));
        Throws<ArgumentOutOfRangeException>(() => map.CountWaiting(6));
        Throws<ArgumentNullException>(() => map.UpsertPassenger(null));
        Throws<ArgumentNullException>(() => map.UpsertElevator(null));
        Equal(0, notifications);
        True(map.TryGetPassenger("original", out PassengerSnapshot found));
        True(ReferenceEquals(original, found));
        Equal(1, map.Passengers.Count());
        Equal(0, map.Elevators.Count());
    }

    private static void SnapshotValidation()
    {
        Throws<ArgumentOutOfRangeException>(() => new MapState(0));
        Throws<ArgumentOutOfRangeException>(() => new MapState(5, 4));
        Throws<ArgumentException>(() => Passenger(" "));
        Throws<ArgumentOutOfRangeException>(() => Passenger("x", deadline: float.NaN));
        Throws<ArgumentOutOfRangeException>(() => Passenger("x", deadline: float.PositiveInfinity));
        Throws<ArgumentOutOfRangeException>(() => Passenger("x", deadline: -1));
        Throws<ArgumentOutOfRangeException>(() => Passenger("x", points: -1));
        Throws<ArgumentException>(() => Passenger("x", 1, PassengerStatus.Riding));
        Throws<ArgumentOutOfRangeException>(() => Passenger("x", 0));
        Throws<ArgumentOutOfRangeException>(() => Passenger("x", status: (PassengerStatus)100));
        Throws<ArgumentException>(() => new PassengerSnapshot("x", "X", 1, 5, 0, null, 30, 20, PassengerStatus.Riding));
        Throws<ArgumentOutOfRangeException>(() => new ElevatorSnapshot("A", -1, 1, false, 0, 4));
        Throws<ArgumentOutOfRangeException>(() => new ElevatorSnapshot("A", 3, 1, false, 0, 4));
        Throws<ArgumentOutOfRangeException>(() => new ElevatorSnapshot("A", 0, float.NaN, false, 0, 4));
        Throws<ArgumentOutOfRangeException>(() => new ElevatorSnapshot("A", 0, float.NegativeInfinity, false, 0, 4));
        Throws<ArgumentOutOfRangeException>(() => new ElevatorSnapshot("A", 0, 1, false, 5, 4));
        Throws<ArgumentOutOfRangeException>(() => new ElevatorSnapshot("A", 0, 1, false, 0, 0));
    }

    private static void NotificationsAndClear()
    {
        var map = new MapState(5);
        int notifications = 0;
        map.Changed += () => notifications++;
        map.Clear();
        map.SetTotalSatisfaction(0);
        Equal(0, notifications);
        PassengerSnapshot passenger = Passenger("one");
        map.UpsertPassenger(passenger);
        map.UpsertPassenger(passenger);
        map.UpsertElevator(new ElevatorSnapshot("A", 0, 1, false, 0, 4));
        map.SetTotalSatisfaction(50);
        map.SetTotalSatisfaction(50);
        Equal(3, notifications);
        True(!map.RemovePassenger("missing"));
        Equal(3, notifications);
        map.Clear();
        Equal(4, notifications);
        Equal(0, map.Passengers.Count());
        Equal(0, map.Elevators.Count());
        Equal(0, map.TotalSatisfaction);
    }

    private static void ReadOnlyData()
    {
        var map = new MapState(5);
        True(!(map.Passengers is ICollection<PassengerSnapshot>));
        True(!(map.Elevators is ICollection<ElevatorSnapshot>));
        foreach (Type type in new[] { typeof(PassengerSnapshot), typeof(ElevatorSnapshot) })
        {
            True(type.IsSealed);
            foreach (var property in type.GetProperties())
                True(property.SetMethod == null);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("Expected " + expected + ", got " + actual + ".");
    }

    private static void True(bool condition)
    {
        if (!condition)
            throw new Exception("Assertion failed.");
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name + ".");
    }
}
