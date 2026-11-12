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
            Destination = origin % ElevatorRound.FloorsPerDimension == ElevatorRound.FloorsPerDimension - 1
                ? origin - 1 : origin + 1, DestinationStop = stop,
            PickupDoor = door, Space = space, Patience = 60f, Remaining = 60f };

    static void CheckQueueConsistency()
    {
        foreach (float elapsed in new[] { 0f, 30f, 50f })
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
            var primary = new ElevatorRound { Floor = 0, TimeLeft = ElevatorRound.Duration - 40f };
            primary.Riders.Clear();
            var grandmaOne = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Left);
            var grandmaTwo = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Right);
            grandmaOne.Kind = grandmaTwo.Kind = "ELDERLY";
            grandmaOne.Arrival = grandmaTwo.Arrival = ElevatorRound.ElderlyArrivalSeconds;
            var boxMan = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Front, 2);
            var hiddenGrandma = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Left);
            hiddenGrandma.Kind = "ELDERLY";
            var hiddenBoxMan = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Front, 2);
            primary.Riders.AddRange(new[] { grandmaOne, grandmaTwo, boxMan, hiddenGrandma, hiddenBoxMan });
            foreach (Rider rider in primary.Riders) rider.Remaining = rider.Patience = 600f;
            Rider[] preview = primary.Riders.Where(primary.IsOffered).ToArray();
            Require(preview.SequenceEqual(new[] { grandmaOne, grandmaTwo, boxMan }), "Unexpected floor preview.");
            primary.RevealQueue(0, ElevatorRound.CenterStop);
            var second = new ElevatorRound { Floor = 0, TimeLeft = primary.TimeLeft };
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
            primary.Arrive(0);
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
            round.RevealQueue(floor, ElevatorRound.CenterStop);
            Rider[] offered = round.Riders.Where(round.IsOffered).OrderBy(r => r.PickupQueueSlot).ToArray();
            Require(offered.Length == 2 && !offered.Contains(locked) && !offered.Contains(delayed),
                "Locked or delayed passengers consumed a visible entrance slot.");
            front = offered[0];
            side = offered[1];
            BoardingDoor assignedSide = side.PickupDoor;
            Require(front.PickupDoor == BoardingDoor.Front && assignedSide != BoardingDoor.Front
                && front.PickupQueueSlot == 0 && side.PickupQueueSlot == 1,
                "Small groups do not have stable front and side passengers.");
            firstSides.Add(side.PickupDoor);

            var other = new ElevatorRound { Floor = floor };
            other.SharePassengerPoolWith(round, 1);
            other.RevealQueue(floor, ElevatorRound.CenterStop);
            Require(other.IsOffered(front) && other.IsOffered(side),
                "The second seat lost the same front and side passengers.");
            Require(round.Board(front) && round.Reject(side), "Visible group could not leave.");
            delayed.SpawnDelay = 0f;
            round.TimeLeft = other.TimeLeft = ElevatorRound.Duration - 40f;
            round.AssignBoardingDoors();
            other.RevealQueue(floor, ElevatorRound.CenterStop);
            Require(front.PickupDoor == BoardingDoor.Front && side.PickupDoor == assignedSide
                && front.PickupQueueSlot == 0 && side.PickupQueueSlot == 1
                && !round.Riders.Any(round.IsOffered) && !other.Riders.Any(other.IsOffered),
                "Departures or a later unlock moved doors or refilled a captured landing.");
        }
        Require(firstSides.Contains(BoardingDoor.Left) && firstSides.Contains(BoardingDoor.Right),
            "Early side passengers do not use both sides across neighboring floors.");

        var larger = new ElevatorRound { Floor = 3, TimeLeft = ElevatorRound.Duration - 40f };
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
        var round = new ElevatorRound();
        round.Riders.Clear();
        for (int i = 0; i < 5; i++)
            round.Riders.Add(NewRider(i, ElevatorRound.CenterStop, BoardingDoor.Front, i < 4 ? 2 : 1));
        foreach (Rider rider in round.Riders)
        {
            round.Arrive(rider.Origin);
            Require(round.Board(rider), "A free capacity space could not board.");
        }
        var extra = NewRider(5, ElevatorRound.CenterStop, BoardingDoor.Front);
        round.Riders.Add(extra);
        round.Arrive(extra.Origin);
        Require(round.IsOffered(extra) && round.Load == 9 && !round.Board(extra), "Capacity exceeded nine spaces.");
        Rider passenger = round.Riders[0];
        round.Tick(10000f, false);
        Require(round.TimeLeft == 0f && round.Finished, "Large frame went past the shift deadline.");
        round.Arrive(passenger.Destination, passenger.DestinationStop);
        Require(round.Offboard(passenger) == OffboardResult.None && !round.Remove(passenger)
            && !round.Board(extra) && round.Score == 0, "Passenger action changed score after the deadline.");
        Require(ElevatorRound.Duration == 90f, "Preserve the branch's existing 90-second round duration.");
        Require(PassengerData.ThemeAtFloor(7) == PassengerTheme.Office
            && PassengerData.ThemeAtFloor(8) == PassengerTheme.Candy
            && PassengerData.ThemeAtFloor(15) == PassengerTheme.Candy
            && PassengerData.ThemeAtFloor(16) == PassengerTheme.Underwater, "World order changed.");
    }

    static void CheckSingleDimensionRounds()
    {
        var catalog = new PassengerCatalog
        {
            types = new[]
            {
                new PassengerData { kind = "COURIER", theme = PassengerTheme.Office },
                new PassengerData { kind = "GROUP", theme = PassengerTheme.Candy },
                new PassengerData { kind = "BOSS", theme = PassengerTheme.Candy },
                new PassengerData { kind = "ELDERLY", theme = PassengerTheme.Any }
            }
        };
        foreach (bool useCatalog in new[] { false, true })
            for (int dimension = 0; dimension < ElevatorRound.Dimensions; dimension++)
            {
                var round = useCatalog ? ElevatorRound.FromCatalog(catalog) : new ElevatorRound();
                round.ConfigureDimension(dimension);
                int firstFloor = dimension * ElevatorRound.FloorsPerDimension;
                int lastFloor = firstFloor + ElevatorRound.FloorsPerDimension - 1;
                Require(round.ActiveDimension == dimension && round.MinFloor == firstFloor
                    && round.MaxFloor == lastFloor && round.Floor == firstFloor && round.PeakFloor == firstFloor,
                    "Round did not start at its dimension's first floor.");
                Require(round.TimeLeft == ElevatorRound.Duration && round.Elapsed == 0f && !round.Finished,
                    "A new dimension round did not get its full configured duration.");
                Require(round.Riders.Count > 0 && round.Riders.All(r => r.Origin >= firstFloor
                    && r.Origin <= lastFloor && r.Destination >= firstFloor && r.Destination <= lastFloor
                    && r.Destination != r.Origin), "A rider requests or waits in an inactive dimension.");
                Require(round.Riders.All(round.IsPassengerTypeUnlocked),
                    "An active world's passenger mechanic remains behind an old three-minute unlock.");
                foreach (string kind in new[] { "BOSS", "GROUP", "HANDYMAN" })
                    Require(round.IsPassengerTypeUnlocked(new Rider { Kind = kind, Origin = firstFloor }),
                        "An active round never unlocks " + kind + ".");
                int inactiveFloor = dimension == 0 ? lastFloor + 1 : firstFloor - 1;
                bool refused = false;
                try { round.Arrive(inactiveFloor); }
                catch (ArgumentOutOfRangeException) { refused = true; }
                Require(refused && round.Floor == firstFloor, "Arrive escaped the active dimension.");
                round.Arrive(lastFloor);
                round.Tick(ElevatorRound.Duration - 1f, false);
                Require(round.MaxUnlockedFloor == lastFloor && round.MinFloor == firstFloor
                    && !round.Finished && round.TimeLeft == 1f, "World bounds or timer changed before the deadline.");
                round.Tick(2f, false);
                Require(round.TimeLeft == 0f && round.Finished && round.MaxUnlockedFloor == lastFloor,
                    "The round escaped its deadline or dimension.");
            }
    }

    static void CheckLocalFloorNumbers()
    {
        for (int dimension = 0; dimension < ElevatorRound.Dimensions; dimension++)
            for (int number = 1; number <= ElevatorRound.FloorsPerDimension; number++)
            {
                int floor = dimension * ElevatorRound.FloorsPerDimension + number - 1;
                Require(ElevatorRound.FloorNumber(floor) == number,
                    "A world does not number its floors from 1 through 8.");
            }
        foreach (int invalid in new[] { -1, ElevatorRound.Floors })
        {
            bool rejected = false;
            try { ElevatorRound.FloorNumber(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Floor labels accepted an invalid authored floor.");
        }
    }

    static void CheckWorldDestinations()
    {
        string[] kinds = { "COURIER", "INTERVIEW", "ELDERLY", "PREGNANT", "GROUP", "BOSS", "HANDYMAN", "GENERIC" };
        foreach (string kind in kinds)
            for (int origin = 0; origin < ElevatorRound.Floors; origin++)
                for (int sample = 0; sample < 256; sample++)
                {
                    int destination = ElevatorRound.DestinationFor(kind, origin);
                    Require(destination >= 0 && destination < ElevatorRound.Floors
                        && destination / ElevatorRound.FloorsPerDimension == origin / ElevatorRound.FloorsPerDimension
                        && destination != origin, kind + " requests another world or its origin floor.");
                    int distance = Math.Abs(destination - origin);
                    Require(kind == "BOSS" || kind == "HANDYMAN" || distance <= (kind == "INTERVIEW" ? 3 : 6),
                        kind + " lost its existing trip-length limit.");
                    Require(ElevatorRound.FloorNumber(destination) >= 1
                        && ElevatorRound.FloorNumber(destination) <= ElevatorRound.FloorsPerDimension,
                        "A destination label falls outside the world's visible floor numbers.");
                    int specialDestination = ElevatorRound.RandomDestination(origin);
                    Require(specialDestination >= 0 && specialDestination < ElevatorRound.Floors
                        && specialDestination / ElevatorRound.FloorsPerDimension == origin / ElevatorRound.FloorsPerDimension
                        && specialDestination != origin, "A special passenger requests another world or its origin floor.");
                }

        var catalog = new PassengerCatalog
        {
            types = kinds.Select(kind => new PassengerData { kind = kind, theme = PassengerTheme.Any }).ToArray()
        };
        foreach (ElevatorRound round in new[] { new ElevatorRound(), ElevatorRound.FromCatalog(catalog) })
            Require(round.Riders.Count > 0 && round.Riders.All(r => r.Destination != r.Origin
                && r.Destination / ElevatorRound.FloorsPerDimension == r.Origin / ElevatorRound.FloorsPerDimension),
                "An initial default or catalog roster contains a cross-world request before round configuration.");
    }

    static void CheckSharedDimensionResets()
    {
        var previousRiders = new HashSet<Rider>();
        for (int roundNumber = 1; roundNumber <= 6; roundNumber++)
        {
            int dimension = (roundNumber - 1) % ElevatorRound.Dimensions;
            var primary = new ElevatorRound();
            var second = new ElevatorRound();
            primary.ConfigureDimension(dimension);
            second.ConfigureDimension(dimension);
            second.SharePassengerPoolWith(primary, 1);
            Require(ReferenceEquals(primary.Riders, second.Riders)
                && primary.Floor == second.Floor && primary.MaxFloor == second.MaxFloor,
                "Both seats must start with the same world and passenger identities.");
            Require(primary.Score == 0 && second.Score == 0 && primary.TimeLeft == ElevatorRound.Duration
                && second.TimeLeft == ElevatorRound.Duration && primary.Riders.All(r => !r.Boarded && !r.Resolved
                    && !previousRiders.Contains(r)), "A new round retained scores, clock, or passengers from the previous round.");
            Rider shared = primary.Riders.First();
            primary.Arrive(shared.Origin, shared.OriginStop);
            second.Arrive(shared.Origin, shared.OriginStop);
            primary.RevealQueue(shared.Origin, shared.OriginStop);
            shared = primary.Riders.First(primary.IsOffered);
            shared.Arrival = 0f;
            Require(primary.Board(shared) && !second.Board(shared),
                "A dimension reset allowed both elevators to claim the same passenger.");
            previousRiders.UnionWith(primary.Riders);
            primary.Tick(ElevatorRound.Duration, false);
            second.Tick(ElevatorRound.Duration, false);
        }
    }

    static void CheckBestOfThreeMatch()
    {
        var match = new MatchProgress();
        match.SetDimensionOrder(new[] { 4, 2, 0 });
        Require(MatchProgress.RoundsToWin == 2 && match.RoundNumber == 1 && match.ActiveDimension == 4
            && !match.Finished, "A match did not start with the configured world order.");
        match.RecordResult(200, 100);
        match.RecordResult(0, 999);
        Require(match.PlayerOneWins == 1 && match.PlayerTwoWins == 0,
            "Recording one round twice awarded another win.");
        match.AdvanceRound();
        Require(match.ActiveDimension == 2 && !match.RoundRecorded, "Round two did not advance.");
        match.RecordResult(100, 200);
        match.AdvanceRound();
        Require(match.ActiveDimension == 0 && !match.Finished, "Round three did not advance.");
        match.RecordResult(150, 100);
        Require(match.Finished && match.MatchWinner == 0 && match.PlayerOneWins == 2,
            "The player with two round wins did not win the match.");
        match.AdvanceRound();
        Require(match.RoundNumber == 3, "A finished match advanced again.");
        match.Reset();
        Require(match.RoundNumber == 1 && !match.Finished && match.PlayerOneWins == 0,
            "A rematch did not reset results.");
        match.RecordResult(100, 100);
        match.AdvanceRound();
        match.RecordResult(200, 100);
        match.AdvanceRound();
        match.RecordResult(100, 200);
        Require(match.Finished && match.MatchWinner == -1,
            "A tied three-round series with equal total points was not a draw.");
    }

    static void CheckAbilityOwnership()
    {
        var first = new ElevatorRound();
        first.Riders.Clear();
        var hunter = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Front);
        hunter.Kind = "HUNTER";
        var diver = NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Left);
        diver.Kind = "DIVER";
        first.Riders.AddRange(new[] { hunter, diver });
        var second = new ElevatorRound();
        second.SharePassengerPoolWith(first, 1);
        Require(first.Board(hunter) && second.Board(diver), "Ability riders could not board independently.");
        Require(first.HasOnboardFeature(PassengerFeature.StunsMonster)
            && !second.HasOnboardFeature(PassengerFeature.StunsMonster)
            && second.HasOnboardFeature(PassengerFeature.BlocksTornado)
            && !first.HasOnboardFeature(PassengerFeature.BlocksTornado), "Abilities leaked between elevators.");
        Require(first.Remove(hunter) && !first.HasOnboardFeature(PassengerFeature.StunsMonster),
            "The ability stayed active after its passenger left.");
        var bouncer = new Rider { Kind = "BOUNCER", Space = 2 };
        Require(bouncer.HasFeature(PassengerFeature.StrongCollision), "Bouncer is missing its collision ability.");
    }

    static void CheckFiniteTutorialBoarding()
    {
        var left = new ElevatorRound();
        left.Riders.Clear();
        for (int i = 0; i < 5; i++)
            left.Riders.Add(NewRider(0, ElevatorRound.CenterStop, BoardingDoor.Front));
        left.Riders[2].Arrival = ElevatorRound.ElderlyArrivalSeconds;

        left.RevealPracticeQueue(2);
        var right = new ElevatorRound();
        right.SharePassengerPoolWith(left, 1);
        Rider[] waiting = left.Riders.Where(left.IsOffered).ToArray();
        Require(waiting.Length == 4 && waiting.All(rider => rider.Arrival == 0f),
            "The opening practice group must have four riders already at their doors.");
        Require(waiting.Select(rider => rider.PickupDoor).Distinct().Count() == 3,
            "The practice riders should use all three doors.");

        Require(left.Board(waiting[0]) && left.Board(waiting[1]),
            "The first player could not board two waiting riders.");
        Require(right.Riders.Where(right.IsOffered).SequenceEqual(waiting.Skip(2)),
            "The second player did not get the remaining original riders.");
        Require(right.Board(waiting[2]) && right.Board(waiting[3]),
            "The second player could not board the remaining pair.");

        right.RevealPracticeQueue(2);
        left.RevealQueue(0, ElevatorRound.CenterStop);
        Require(left.Riders.Count == 5 && !left.Riders.Any(left.IsOffered) && !right.Riders.Any(right.IsOffered),
            "Boarding or revisiting a practice queue should not reveal replacements.");
    }

    static void CheckBaseDeliveryPoints()
    {
        foreach (float remaining in new[] { 60f, 30f, 0f })
        foreach (int mood in new[] { 2, 1, 0 })
        {
            var round = new ElevatorRound();
            round.Riders.Clear();
            Rider rider = NewRider(0, 0, BoardingDoor.Front);
            rider.Remaining = remaining; rider.Mood = mood; rider.Bonus = 250;
            rider.Boarded = true; rider.BoardedBySeat = round.Seat;
            round.Riders.Add(rider);
            int expected = remaining > 0 ? 100 + (int)(50 * remaining / 60f)
                : round.LateDeliveriesScore ? 25 : 0;
            if (mood == 1) expected /= 2;
            Require(round.BaseDeliveryPoints(rider) == expected, "Manifest base points include bonuses or ignore patience/mood.");
            round.Arrive(rider.Destination, rider.DestinationStop);
            round.Offboard(rider);
            Require(round.Score == expected + (remaining > 0 && mood > 0 ? 250 : 0),
                "Displayed base points disagree with the delivery scoring calculation.");
        }
    }

    static void CheckUfoRelocation()
    {
        foreach (bool revealDestination in new[] { false, true })
        {
            var first = new ElevatorRound();
            first.ConfigureDimension(4);
            first.Riders.Clear();
            int floor = first.MinFloor;
            int targetFloor = floor + 2;
            Rider moved = NewRider(floor, ElevatorRound.CenterStop, BoardingDoor.Front);
            moved.Kind = "ALIEN";
            Rider existing = NewRider(targetFloor, ElevatorRound.CenterStop, BoardingDoor.Front);
            first.Riders.AddRange(new[] { moved, existing });
            var second = new ElevatorRound();
            second.ConfigureDimension(4);
            second.SharePassengerPoolWith(first, 1);
            first.RevealQueue(floor, ElevatorRound.CenterStop);
            if (revealDestination) first.RevealQueue(targetFloor, ElevatorRound.CenterStop);
            Require(first.CanRelocateWaitingPassenger(moved, targetFloor), "A landing with space was rejected.");
            Require(first.TryRelocateWaitingPassenger(moved, targetFloor), "UFO relocation failed.");
            Require(first.IsQueuedAtStop(moved) && second.IsQueuedAtStop(moved)
                && first.IsQueuedAtStop(existing) && second.IsQueuedAtStop(existing),
                "Relocation hid a visible passenger from one or both players.");
            Require(moved.PickupQueueSlot != existing.PickupQueueSlot,
                "The relocated passenger overlaps another waiting passenger.");
            first.Arrive(targetFloor, moved.OriginStop);
            second.Arrive(targetFloor, moved.OriginStop);
            moved.Arrival = 0f;
            Require(first.Board(moved) && !second.Board(moved),
                "The relocated passenger could not board exclusively.");
            Require(!first.TryRelocateWaitingPassenger(moved, floor),
                "A boarded passenger was moved by the UFO.");
        }

        var full = new ElevatorRound();
        full.ConfigureDimension(4);
        full.Riders.Clear();
        full.StopOpen = (floor, stop) => stop == ElevatorRound.CenterStop;
        Rider passenger = NewRider(full.MinFloor, ElevatorRound.CenterStop, BoardingDoor.Front);
        full.Riders.Add(passenger);
        for (int i = 0; i < 3; i++)
            full.Riders.Add(NewRider(full.MinFloor + 2, ElevatorRound.CenterStop, BoardingDoor.Front));
        full.RevealQueue(full.MinFloor, ElevatorRound.CenterStop);
        full.RevealQueue(full.MinFloor + 2, ElevatorRound.CenterStop);
        Require(!full.CanRelocateWaitingPassenger(passenger, full.MinFloor + 2),
            "The UFO accepted a full landing.");
        Require(!full.TryRelocateWaitingPassenger(passenger, full.MinFloor + 2)
            && full.IsQueuedAtStop(passenger), "A failed relocation changed the original queue.");
        Rider departed = full.Riders[1];
        departed.Resolved = true;
        Require(full.TryRelocateWaitingPassenger(passenger, full.MinFloor + 2)
            && full.IsQueuedAtStop(passenger), "The UFO could not use a vacated landing slot.");
    }

    static int Main()
    {
        var checks = new Dictionary<string, Action>
        {
            { "UFO relocation preserves queues, capacity and exclusive boarding", CheckUfoRelocation },
            { "Manifest base points match delivery, excluding bonuses", CheckBaseDeliveryPoints },
            { "Matching inside/outside and both-seat door queues over all 24 floors and 5 stops", CheckQueueConsistency },
            { "Exact two-grandma and one-box-man preview limits in solo and two-player", CheckFiniteLandingRoster },
            { "Visible groups use front and alternating sides without moving or refilling captured queues", CheckVisibleDoorDistribution },
            { "Rejecting, timing out, revisiting, and progression preserve visible groups", CheckQueueDepartures },
            { "Two seats board exclusively, retain separate cabin clocks and score once", CheckTwoSeatBoarding },
            { "Capacity, configured deadline, and office/candy/underwater order", CheckDeadlineAndCapacity },
            { "Default and catalog rounds stay inside one dimension with reachable destinations", CheckSingleDimensionRounds },
            { "Every world's displayed floors run from 1 to 8", CheckLocalFloorNumbers },
            { "Every passenger kind and random request stays within its origin world", CheckWorldDestinations },
            { "Six dimension rounds reset clocks and rosters while sharing identities between seats", CheckSharedDimensionResets },
            { "Best-of-three results, ties, world order, championship lock, and reset", CheckBestOfThreeMatch },
            { "Passenger abilities follow their owning elevator", CheckAbilityOwnership },
            { "Both tutorial seats board from one finite waiting group", CheckFiniteTutorialBoarding }
        };
        int failures = 0;
        foreach (var check in checks)
            try { check.Value(); Console.WriteLine("PASS: " + check.Key); }
            catch (Exception e) { failures++; Console.Error.WriteLine("FAIL: " + check.Key + ": " + e.Message); }
        return failures == 0 ? 0 : 1;
    }
}
