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
        foreach (float elapsed in new[] { 0f, 40f, 110f })
        {
            var primary = new ElevatorRound();
            primary.Riders.Clear();
            for (int floor = 0; floor < 24; floor++)
                for (int stop = 0; stop < ElevatorRound.Stops; stop++)
                    for (int i = 0; i < 5; i++)
                        primary.Riders.Add(NewRider(floor, stop, (BoardingDoor)(i % 3)));
            var second = new ElevatorRound();
            second.SharePassengerPoolWith(primary, 1);
            primary.TimeLeft = second.TimeLeft = ElevatorRound.Duration - elapsed;
            for (int floor = 0; floor < 24; floor++)
                for (int stop = 0; stop < ElevatorRound.Stops; stop++)
                {
                    primary.Arrive(floor, stop);
                    second.Arrive(floor, stop);
                    primary.RevealQueue(floor, stop);
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
            Rider hidden = primary.Riders.First(r => primary.IsOffered(r));
            hidden.SpawnDelay = 1f;
            Require(!primary.IsQueuedAtStop(hidden) && !second.IsOffered(hidden), "Delayed rider is visible early.");
            hidden.SpawnDelay = 0f;
            hidden.Resolved = true;
            Require(!primary.IsQueuedAtStop(hidden) && !second.IsOffered(hidden), "Resolved rider stays in the queue.");
        }
    }

    static void CheckFiniteLandingRoster()
    {
        foreach (bool shared in new[] { false, true })
        {
            var primary = new ElevatorRound { Floor = 3, TimeLeft = 140f };
            primary.Riders.Clear();
            var grandmaOne = NewRider(3, ElevatorRound.CenterStop, BoardingDoor.Left);
            var grandmaTwo = NewRider(3, ElevatorRound.CenterStop, BoardingDoor.Right);
            grandmaOne.Kind = grandmaTwo.Kind = "ELDERLY";
            grandmaOne.Arrival = grandmaTwo.Arrival = ElevatorRound.ElderlyArrivalSeconds;
            var boxMan = NewRider(3, ElevatorRound.CenterStop, BoardingDoor.Front, 2);
            var hiddenGrandma = NewRider(3, ElevatorRound.CenterStop, BoardingDoor.Left);
            hiddenGrandma.Kind = "ELDERLY";
            var hiddenBoxMan = NewRider(3, ElevatorRound.CenterStop, BoardingDoor.Front, 2);
            primary.Riders.AddRange(new[] { grandmaOne, grandmaTwo, boxMan, hiddenGrandma, hiddenBoxMan });
            foreach (Rider rider in primary.Riders) rider.Remaining = rider.Patience = 600f;
            Rider[] preview = primary.Riders.Where(primary.IsOffered).ToArray();
            Require(preview.SequenceEqual(new[] { grandmaOne, grandmaTwo, boxMan }), "Unexpected floor preview.");
            primary.RevealQueue(3, ElevatorRound.CenterStop);
            var second = new ElevatorRound { Floor = 3, TimeLeft = primary.TimeLeft };
            if (shared) second.SharePassengerPoolWith(primary, 1);
            var boxCar = shared ? second : primary;
            Require(!primary.Board(grandmaOne), "A slow grandma boarded before reaching the door.");
            primary.Tick(2f, true);
            Require(primary.Board(grandmaOne) && boxCar.Board(boxMan) && primary.Board(grandmaTwo),
                "The exact visible two grandmas and box man could not board.");
            Require(primary.Riders.Count == 5 && !primary.IsOffered(hiddenGrandma)
                && !primary.IsOffered(hiddenBoxMan), "Boarding spawned a replacement or revealed a reserve.");
            Require(!primary.Board(hiddenGrandma) && !boxCar.Board(hiddenBoxMan)
                && !boxCar.Board(grandmaOne), "A hidden or already claimed passenger boarded.");
            primary.Tick(80f, true);
            if (shared) second.TimeLeft = primary.TimeLeft;
            Require(!primary.Riders.Any(primary.IsOffered) && (!shared || !second.Riders.Any(second.IsOffered)),
                "Progression introduced more passengers into the shown landing.");
            Require(primary.Riders.Count(r => r.Boarded && r.Kind == "ELDERLY") == 2
                && primary.Riders.Count(r => r.Boarded && r.Kind == "COURIER") == 1,
                "The boarding total exceeded the preview's passenger types or amounts.");
            primary.Arrive(4);
            primary.Arrive(3);
            Require(!primary.Riders.Any(primary.IsOffered), "Revisiting a cleared landing refilled it.");
            Require(primary.Load + (shared ? second.Load : 0) == 4, "Physical cabin capacity mixed with passenger count.");
        }
    }

    static void CheckVisibleDoorDistribution()
    {
        var firstSides = new List<BoardingDoor>();
        for (int floor = 0; floor < 2; floor++)
        {
            var round = new ElevatorRound { Floor = floor };
            round.Riders.Clear();
            var locked = NewRider(floor, ElevatorRound.CenterStop, BoardingDoor.Left);
            locked.Kind = "ELDERLY";
            var delayed = NewRider(floor, ElevatorRound.CenterStop, BoardingDoor.Right);
            delayed.SpawnDelay = 1f;
            var front = NewRider(floor, ElevatorRound.CenterStop, BoardingDoor.Left);
            var side = NewRider(floor, ElevatorRound.CenterStop, BoardingDoor.Front);
            var reserve = NewRider(floor, ElevatorRound.CenterStop, BoardingDoor.Front);
            round.Riders.AddRange(new[] { locked, delayed, front, side, reserve });
            round.AssignBoardingDoors();
            BoardingDoor assignedSide = side.PickupDoor;
            Require(front.PickupDoor == BoardingDoor.Front && assignedSide != BoardingDoor.Front,
                "Small groups do not have both a front and a side passenger.");
            round.RevealQueue(floor, ElevatorRound.CenterStop);
            Require(round.Riders.Where(round.IsOffered).SequenceEqual(new[] { front, side })
                && front.PickupDoor == BoardingDoor.Front && side.PickupDoor == assignedSide
                && front.PickupQueueSlot == 0 && side.PickupQueueSlot == 1,
                "Locked or delayed passengers consumed a visible entrance slot.");
            firstSides.Add(side.PickupDoor);

            var other = new ElevatorRound { Floor = floor };
            other.SharePassengerPoolWith(round, 1);
            other.RevealQueue(floor, ElevatorRound.CenterStop);
            Require(other.IsOffered(front) && other.IsOffered(side),
                "The second seat lost the same front and side passengers.");
            Require(round.Board(front) && round.Reject(side), "Visible group could not leave.");
            delayed.SpawnDelay = 0f;
            round.TimeLeft = other.TimeLeft = 120f;
            round.AssignBoardingDoors();
            other.RevealQueue(floor, ElevatorRound.CenterStop);
            Require(front.PickupDoor == BoardingDoor.Front && side.PickupDoor == assignedSide
                && front.PickupQueueSlot == 0 && side.PickupQueueSlot == 1
                && !round.Riders.Any(round.IsOffered) && !other.Riders.Any(other.IsOffered),
                "Departures or a later unlock moved doors or refilled a captured landing.");
        }
        Require(firstSides.Contains(BoardingDoor.Left) && firstSides.Contains(BoardingDoor.Right),
            "Early side passengers do not use both sides across neighboring floors.");

        var larger = new ElevatorRound { Floor = 3, TimeLeft = 140f };
        larger.Riders.Clear();
        for (int i = 0; i < 5; i++)
            larger.Riders.Add(NewRider(3, ElevatorRound.CenterStop, BoardingDoor.Front));
        larger.AssignBoardingDoors();
        larger.RevealQueue(3, ElevatorRound.CenterStop);
        Rider[] visible = larger.Riders.Where(larger.IsOffered).ToArray();
        Require(visible.Length == 3 && visible.Select(r => r.PickupDoor).Distinct().Count() == 3,
            "A three-passenger landing does not use front, left, and right entrances.");
    }

    static void CheckQueueDepartures()
    {
        var round = new ElevatorRound();
        round.Riders.Clear();
        var rejected = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Left);
        var expired = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Right);
        var reserve = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Front);
        round.Riders.AddRange(new[] { rejected, expired, reserve });
        round.RevealQueue(0, ElevatorRound.CenterStop);
        Require(round.Reject(rejected), "Visible passenger could not be turned away.");
        expired.Remaining = .1f;
        Require(round.Tick(.2f, true) == 1 && expired.Resolved, "Waiting passenger did not time out.");
        Require(!round.IsOffered(reserve) && !round.Board(reserve), "A departure exposed an unseen reserve.");
        // A query used to plan navigation must not freeze an unseen landing early.
        var unseen = new ElevatorRound { Floor = 3 };
        unseen.Riders.Clear();
        var futureGrandma = NewRider(3, ElevatorRound.CenterStop, BoardingDoor.Front);
        futureGrandma.Kind = "ELDERLY";
        unseen.Riders.Add(futureGrandma);
        Require(!unseen.IsQueuedAtStop(futureGrandma), "Grandma mechanic unlocked too early.");
        unseen.Tick(31f, false);
        unseen.RevealQueue(3, ElevatorRound.CenterStop);
        Require(unseen.IsOffered(futureGrandma), "An unseen landing lost a newly unlocked passenger.");
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
        Require(primary.Riders.Count == 2 && !primary.Riders.Any(primary.IsOffered)
            && !second.Riders.Any(second.IsOffered), "Shared pickups created passengers beyond the preview.");
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
        var round = new ElevatorRound { TimeLeft = 60f };
        round.Riders.Clear();
        for (int i = 0; i < 5; i++)
            round.Riders.Add(NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Front, i < 4 ? 2 : 1));
        foreach (Rider rider in round.Riders) Require(round.Board(rider), "A free capacity space could not board.");
        var extra = NewRider(1, ElevatorRound.CenterStop, BoardingDoor.Front);
        round.Riders.Add(extra);
        round.Arrive(1);
        Require(round.IsOffered(extra) && round.Load == 9 && !round.Board(extra), "Capacity exceeded nine spaces.");
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
            { "Exact two-grandma and one-box-man preview limits in solo and two-player", CheckFiniteLandingRoster },
            { "Visible groups use front and alternating sides without moving or refilling captured queues", CheckVisibleDoorDistribution },
            { "Rejecting, timing out, revisiting, and progression preserve visible groups", CheckQueueDepartures },
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
