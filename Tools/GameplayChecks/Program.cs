using System;
using System.Collections.Generic;
using System.Linq;
using CrazyElevator.Shared;

internal static class Program
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static Rider NewRider(int origin, int stop, BoardingDoor door, int space = 1)
        => new Rider { Name = "Test", Kind = "COURIER", Origin = origin, OriginStop = stop,
            Destination = origin == 7 ? 6 : origin + 1, DestinationStop = stop,
            PickupDoor = door, Space = space, Patience = 60f, Remaining = 60f };

    static void CheckQueueConsistency()
    {
        var primary = new ElevatorRound();
        primary.Riders.Clear();
        for (int floor = 0; floor < 24; floor++)
            for (int stop = 0; stop < ElevatorRound.Stops; stop++)
                for (int i = 0; i < 5; i++)
                    primary.Riders.Add(NewRider(floor, stop, (BoardingDoor)(i % 3)));
        var second = new ElevatorRound();
        second.SharePassengerPoolWith(primary, 1);
        foreach (float elapsed in new[] { 0f, 40f, 110f })
        {
            primary.TimeLeft = second.TimeLeft = ElevatorRound.Duration - elapsed;
            for (int floor = 0; floor < 24; floor++)
                for (int stop = 0; stop < ElevatorRound.Stops; stop++)
                {
                    primary.Arrive(floor, stop);
                    second.Arrive(floor, stop);
                    Rider[] outside = primary.Riders.Where(r => r.Origin == floor && r.OriginStop == stop
                        && primary.IsQueuedAtStop(r)).ToArray();
                    Rider[] inside = primary.Riders.Where(primary.IsOffered).ToArray();
                    Rider[] otherInside = second.Riders.Where(second.IsOffered).ToArray();
                    Require(outside.SequenceEqual(inside) && inside.SequenceEqual(otherInside),
                        "Interior/exterior or duel queues disagree at floor " + floor + ", stop " + stop);
                    Require(inside.Length == primary.PassengerOfferLimit, "Offer limit not applied at this stop.");
                    foreach (BoardingDoor door in Enum.GetValues<BoardingDoor>())
                        Require(outside.Count(r => r.PickupDoor == door) == inside.Count(r => r.PickupDoor == door),
                            "A door's visible waiting count differs.");
                }
        }
        Rider hidden = primary.Riders.First(r => primary.IsOffered(r));
        hidden.SpawnDelay = 1f;
        Require(!primary.IsQueuedAtStop(hidden) && !second.IsOffered(hidden), "Delayed rider is visible early.");
        hidden.SpawnDelay = 0f;
        hidden.Resolved = true;
        Require(!primary.IsQueuedAtStop(hidden) && !second.IsOffered(hidden), "Resolved rider stays in the queue.");
    }

    static void CheckTwoSeatBoarding()
    {
        var primary = new ElevatorRound();
        primary.Riders.Clear();
        var first = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Left, 2);
        var next = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Right);
        primary.Riders.Add(first);
        primary.Riders.Add(next);
        var second = new ElevatorRound();
        second.SharePassengerPoolWith(primary, 1);
        Require(ReferenceEquals(primary.Riders, second.Riders), "Duel did not share passenger identities.");
        Require(primary.Board(first) && !second.Board(first), "Both players boarded one rider.");
        Require(second.Board(next), "Second player cannot board independently.");
        Require(primary.Load == 2 && second.Load == 1, "Seat loads mixed.");
        Require(!primary.Owns(next) && !second.Owns(first), "Seat ownership mixed.");
        Require(primary.Riders.Count(r => !r.Boarded && !r.Resolved && primary.IsQueuedAtStop(r)) == 2,
            "Shared pickups depleted the waiting offer for the other player.");
        float firstPatience = first.Remaining;
        float nextPatience = next.Remaining;
        primary.Tick(1f, true);
        Require(first.Remaining == firstPatience - 1f && next.Remaining == nextPatience,
            "Primary clock drained the other elevator's cabin.");
        second.Tick(1f, true, false);
        Require(next.Remaining == nextPatience - 1f && first.Remaining == firstPatience - 1f,
            "A cabin clock ticked twice.");
        primary.Arrive(first.Destination, first.DestinationStop);
        second.Arrive(next.Destination, next.DestinationStop);
        Require(primary.Offboard(first) == OffboardResult.Happy && second.Offboard(next) == OffboardResult.Happy,
            "Independent deliveries failed.");
        Require(primary.Delivered == 1 && second.Delivered == 1 && primary.Load == 0 && second.Load == 0,
            "Delivery totals mixed between players.");
        int primaryScore = primary.Score, secondScore = second.Score;
        Require(primary.Offboard(first) == OffboardResult.None && second.Offboard(first) == OffboardResult.None
            && primary.Score == primaryScore && second.Score == secondScore, "A rider scored twice.");
    }

    static void CheckDeadlineAndCapacity()
    {
        var round = new ElevatorRound();
        round.Riders.Clear();
        for (int i = 0; i < 12; i++) round.Riders.Add(NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Front));
        Rider extra = null;
        for (int i = 0; i < 9; i++) Require(round.Board(round.Riders[i]), "A free capacity space could not board.");
        extra = round.Riders[9];
        Require(round.Load == 9 && !round.Board(extra), "Capacity exceeded nine spaces.");
        Rider passenger = round.Riders[0];
        round.Tick(10000f, false);
        Require(round.TimeLeft == 0f && round.Finished, "Large frame went past the shift deadline.");
        round.Arrive(passenger.Destination, passenger.DestinationStop);
        Require(round.Offboard(passenger) == OffboardResult.None && !round.Remove(passenger)
            && !round.Board(extra) && round.Score == 0, "Passenger action changed score after the deadline.");
        Require(ElevatorRound.Duration == 180f && ElevatorRound.MaxUnlockedFloorAt(0) == 7
            && ElevatorRound.MaxUnlockedFloorAt(40f) == 15 && ElevatorRound.MaxUnlockedFloorAt(110f) == 23,
            "Three-minute duration or world progression changed.");
        Require(PassengerData.ThemeAtFloor(7) == PassengerTheme.Office
            && PassengerData.ThemeAtFloor(8) == PassengerTheme.Candy
            && PassengerData.ThemeAtFloor(15) == PassengerTheme.Candy
            && PassengerData.ThemeAtFloor(16) == PassengerTheme.Underwater, "World order changed.");
    }

    static int Main()
    {
        var checks = new Dictionary<string, Action>
        {
            { "Matching inside/outside and both-seat door queues over all 24 floors and 5 stops", CheckQueueConsistency },
            { "Two seats board exclusively, retain separate cabin clocks and score once", CheckTwoSeatBoarding },
            { "Capacity, deadline, and office/candy/underwater progression", CheckDeadlineAndCapacity }
        };
        int failures = 0;
        foreach (var check in checks)
            try { check.Value(); Console.WriteLine("PASS: " + check.Key); }
            catch (Exception e) { failures++; Console.Error.WriteLine("FAIL: " + check.Key + ": " + e.Message); }
        return failures == 0 ? 0 : 1;
    }
}
