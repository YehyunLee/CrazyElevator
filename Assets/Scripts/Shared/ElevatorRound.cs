using System;
using System.Collections.Generic;

namespace CrazyElevator.Shared
{
    public enum BoardingDoor { Left, Front, Right }

    public sealed class Rider
    {
        // Optional ScriptableObject template; Kind string remains the runtime key.
        public PassengerData Data;

        // Passenger identity and presentation.
        public string Name;
        public string Request;
        public string Kind;
        public string Badge;
        public string Status;
        public int Color;

        public bool HasFeature(PassengerFeature feature) =>
            (Data != null && Data.Has(feature)) || FeatureFallback(feature);

        bool FeatureFallback(PassengerFeature feature)
        {
            switch (feature)
            {
                case PassengerFeature.SlowArrival: return Arrival > 0 || Kind == "ELDERLY";
                case PassengerFeature.MultiSpace: return Space > 1;
                case PassengerFeature.UrgencySpeech: return Kind == "INTERVIEW";
                case PassengerFeature.ClearsRust: return Kind == "HANDYMAN";
                case PassengerFeature.GroupParty: return Kind == "GROUP" || Space >= 3;
                case PassengerFeature.SpeedBoost: return Kind == "GROUP";
                case PassengerFeature.SpeedSlow: return Kind == "ELDERLY";
                case PassengerFeature.CryingBaby: return Kind == "PREGNANT";
                case PassengerFeature.CalmsBaby: return Kind == "ELDERLY";
                default: return false;
            }
        }

        // Route, occupied space, and existing delivery bonus.
        // A stop is where on the floor the rider waits / wants to go (ElevatorRound.CenterStop = the shafts).
        public int Origin;
        public int Destination;
        public int OriginStop = ElevatorRound.CenterStop;
        public int DestinationStop = ElevatorRound.CenterStop;
        public BoardingDoor PickupDoor = BoardingDoor.Front;
        // Captured landing position, retained when earlier passengers leave.
        public int PickupQueueSlot = -1;
        public int Space;
        public int Bonus;

        // Patience and mood.
        // 2 = happy, 1 = reduced mood, 0 = unhappy.
        public int Mood = 2;
        public float Patience;
        // Patience seconds remaining.
        public float Remaining;
        // Seconds until a slow waiting rider reaches the door.
        public float Arrival;

        public bool Boarded;
        // A shared passenger pool still needs to know which car holds a rider.
        public int BoardedBySeat = -1;
        // Optional entry delay before a waiting rider can be shown.
        public float SpawnDelay;
        // Resolved riders cannot board or score again.
        public bool Resolved;
    }

    public enum OffboardResult { None, Happy, Late, WrongFloor }

}


namespace CrazyElevator.Shared
{
    // Pure game rules; independent of scene objects and frame rate.
    public sealed class ElevatorRound
    {
        public const int Floors = 24;
        public const int Dimensions = 3;
        public const int FloorsPerDimension = Floors / Dimensions;
        // Authored floor indices stay global; every world's signs start at 1.
        public static int FloorNumber(int floor)
        {
            if (floor < 0 || floor >= Floors) throw new ArgumentOutOfRangeException(nameof(floor));
            return floor % FloorsPerDimension + 1;
        }
        public const int Capacity = 9;
        public const float Duration = 60f;
        // Seconds into the shift when candy (1) and underwater (2) open.
        static readonly float[] DimensionUnlockSeconds = { 0f, 40f, 110f };
        public static float DimensionUnlockAt(int dimension)
            => DimensionUnlockSeconds[Math.Max(0, Math.Min(Dimensions - 1, dimension))];
        public const float ElderlyArrivalSeconds = 1.5f;
        public const float CryingPatienceMultiplier = 1.6f;
        public const float CalmPatienceMultiplier = .5f;
        static readonly Random DestinationRandom = new Random();
        public List<Rider> Riders = new List<Rider>();
        public int Seat { get; private set; }
        // -1 retains the full-building rules for previews and standalone rule checks.
        public int ActiveDimension { get; private set; } = -1;
        public int MinFloor => ActiveDimension < 0 ? 0 : ActiveDimension * FloorsPerDimension;
        public int MaxFloor => ActiveDimension < 0 ? Floors - 1 : MinFloor + FloorsPerDimension - 1;
        public bool ContainsFloor(int floor) => floor >= MinFloor && floor <= MaxFloor;
        public int Floor, PeakFloor, Score, Delivered, Happy, Missed, TurnedAway;
        // Each floor has five stops, left to right. The car is parked at Stop on Floor.
        public const int Stops = 5;
        public const int CenterStop = 2;
        public int Stop = CenterStop;
        // The scene decides which stops a car can reach on each floor (floor, stop).
        public Func<int, int, bool> StopOpen;
        // The tutorial keeps every rider at the shafts.
        public bool CenterStopsOnly;
        // Office stays near the shafts; later worlds spread riders across the whole floor.
        static readonly int[][] StopWeights =
        {
            new[] { 0, 2, 3, 2, 0 },
            new[] { 1, 2, 2, 2, 1 },
            new[] { 2, 2, 1, 2, 2 }
        };
        public float PeakTime;
        public float TimeLeft = Duration;
        public float Elapsed => Duration - TimeLeft;
        public int MaxUnlockedFloor => ActiveDimension >= 0 ? MaxFloor : MaxUnlockedFloorAt(Elapsed);
        int TimeBasedPassengerOfferLimit => Elapsed < 30f ? 2
            : Elapsed < 70f ? 3
            : Elapsed < 110f ? 4 : 5;
        public int PassengerOfferLimit => OfferLimitAt(Floor);
        int OfferLimitAt(int floor) => ActiveDimension >= 0 ? 3
            : PassengerData.ThemeAtFloor(floor) == PassengerTheme.Office
                ? TimeBasedPassengerOfferLimit : Math.Min(3, TimeBasedPassengerOfferLimit);
        // The first visible group at a landing is its finite roster for this shift.
        // Both elevators share these identities, just as they share Riders.
        Dictionary<int, HashSet<Rider>> revealedQueues = new Dictionary<int, HashSet<Rider>>();
        bool OwnsPassengerPoolClock = true;
        // Scene variants can opt out of the prototype's consolation points for
        // passengers whose patience has completely expired.
        public bool LateDeliveriesScore = true;
        public int Load
        {
            get
            {
                int occupiedSpaces = 0;
                foreach (var rider in Riders)
                {
                    if (Owns(rider))
                        occupiedSpaces += rider.Space;
                }
                return occupiedSpaces;
            }
        }
        // Every round lasts one minute. Reaching the top or clearing all
        // waiting passengers does not end it; the final Score is the result.
        public bool Finished => TimeLeft <= 0;

        // A short office-only warm-up, then candy and underwater open as
        // match-wide interludes at 0:40 and 1:50.
        public static int MaxUnlockedFloorAt(float elapsed)
        {
            int unlockedDimensions = 1;
            if (elapsed >= DimensionUnlockAt(1)) unlockedDimensions++;
            if (elapsed >= DimensionUnlockAt(2)) unlockedDimensions++;
            return Math.Min(Floors - 1, unlockedDimensions * FloorsPerDimension - 1);
        }

        // Seed the built-in roster only in the worlds that match each character.
        public ElevatorRound()
        {
            SeedDefaults();
        }

        // Prefer ScriptableObject templates when a catalog is assigned.
        public static ElevatorRound FromCatalog(PassengerCatalog catalog)
        {
            if (catalog == null || catalog.types == null || catalog.types.Length == 0)
                return new ElevatorRound();

            var round = new ElevatorRound(seedDefaults: false);
            for (int f = 0; f < Floors; f++)
            {
                foreach (var type in catalog.types)
                {
                    if (type == null || type.kind == "HANDYMAN" || !type.CanWaitAt(f)) continue;
                    round.Riders.Add(type.CreateRider(f, DestinationFor(type.kind, f)));
                }
            }
            return round;
        }

        ElevatorRound(bool seedDefaults)
        {
            if (seedDefaults) SeedDefaults();
        }

        // Keep the authored world floors, but play and deliver only inside this
        // round's dimension. Configure a fresh roster before assigning its stops.
        public void ConfigureDimension(int dimension)
        {
            if (dimension < 0 || dimension >= Dimensions)
                throw new ArgumentOutOfRangeException(nameof(dimension));
            ActiveDimension = dimension;
            Floor = PeakFloor = MinFloor;
            Stop = CenterStop;
            PeakTime = 0f;
            TimeLeft = Duration;
            revealedQueues.Clear();
            Riders.RemoveAll(rider => rider == null || !ContainsFloor(rider.Origin));
            foreach (Rider rider in Riders)
                rider.Destination = MinFloor + DestinationFor(rider.Kind,
                    rider.Origin - MinFloor, FloorsPerDimension - 1);
        }

        void SeedDefaults()
        {
            for (int f = 0; f < Floors; f++)
            {
                PassengerTheme world = PassengerData.ThemeAtFloor(f);
                if (world == PassengerTheme.Office)
                    Add("Remy", "COURIER", "Two spaces. Quick stop!", "BOX", f, 2, 34, 0, 0, 30);
                if (world == PassengerTheme.Candy)
                    Add("Mina & Baby", "PREGNANT", "WAAAH! Keep the baby calm.", "BABY", f, 2, 42, 0, 1, 70);
                if (world == PassengerTheme.Office)
                    Add("Jules", "INTERVIEW", "My interview starts soon!", "!", f, 1, 13, 0, 2, 120);
                if (world == PassengerTheme.Candy)
                    Add("Morgan", "BOSS", "Deliver me on time for my bonus.", "B", f, 1, 30, 0, 3, 140);
                Add("Eli", "ELDERLY", "Please wait for me...", "SLOW", f, 1, 58, ElderlyArrivalSeconds, 4, 175);
                if (world == PassengerTheme.Candy)
                    Add("The Trio", "GROUP", "All three or none!", "3X", f, 3, 32, 0, 5, 130);
            }
        }

        // Store a passenger with fresh patience.
        void Add(string name, string kind, string request, string badge, int from, int space,
            float patience, float arrival, int color, int bonus)
        {
            Riders.Add(new Rider { Name = name, Kind = kind, Request = request, Origin = from,
                Destination = DestinationFor(kind, from), Space = space, Patience = patience, Remaining = patience, Arrival = arrival,
                Color = color, Badge = badge, Bonus = bonus });
        }

        // Special passengers may request any other floor in their origin world.
        // Skipping the origin prevents a zero-distance trip.
        public static int RandomDestination(int origin)
        {
            if (origin < 0 || origin >= Floors) throw new ArgumentOutOfRangeException(nameof(origin));
            int firstFloor = origin / FloorsPerDimension * FloorsPerDimension;
            return firstFloor + RandomDestination(origin - firstFloor, FloorsPerDimension - 1);
        }

        static int RandomDestination(int origin, int highestFloor)
        {
            ValidateDestinationRange(origin, highestFloor);
            int choice = DestinationRandom.Next(highestFloor);
            return choice >= origin ? choice + 1 : choice;
        }

        static void ValidateDestinationRange(int origin, int highestFloor)
        {
            if (highestFloor < 1 || highestFloor >= Floors)
                throw new ArgumentOutOfRangeException(nameof(highestFloor));
            if (origin < 0 || origin > highestFloor)
                throw new ArgumentOutOfRangeException(nameof(origin));
        }

        // The match creates one set of riders, then gives its rival round those
        // same Rider objects. Each round still owns its own floor, clock and score.
        public void SharePassengerPoolWith(ElevatorRound source, int seat)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (seat < 0) throw new ArgumentOutOfRangeException(nameof(seat));
            if (source != this)
            {
                Riders = source.Riders;
                revealedQueues = source.revealedQueues;
                OwnsPassengerPoolClock = false;
            }
            Seat = seat;
        }

        public bool Owns(Rider rider) => rider != null && rider.Boarded
            && !rider.Resolved && rider.BoardedBySeat == Seat;

        public bool HasOnboardFeature(PassengerFeature feature)
        {
            foreach (var rider in Riders)
                if (Owns(rider) && rider.HasFeature(feature)) return true;
            return false;
        }

        public bool CryingBabyAboard => HasOnboardFeature(PassengerFeature.CryingBaby);
        public bool BabyCalmedByGrandma => CryingBabyAboard && HasOnboardFeature(PassengerFeature.CalmsBaby);
        public float CabinPatienceMultiplier => !CryingBabyAboard ? 1f
            : BabyCalmedByGrandma ? CalmPatienceMultiplier : CryingPatienceMultiplier;

        public bool HasCapacityFor(Rider p)
        {
            return p != null && p.Space > 0 && Load + p.Space <= Capacity;
        }

        // Validate boarding; input code also checks physical fit.
        public bool Board(Rider p)
        {
            if (p == null || Finished || p.Resolved || p.Boarded || !ContainsFloor(p.Origin)
                || !ContainsFloor(p.Destination) || p.Origin != Floor
                || p.OriginStop != Stop) return false;
            RevealQueue(Floor, Stop);
            if (!IsOffered(p) || p.Arrival > 0 || !HasCapacityFor(p)) return false;
            p.Boarded = true;
            p.BoardedBySeat = Seat;
            return true;
        }

        // ---------- Stops ----------
        // Keep queues stable while doors open/close and between the two duel views.
        public void AssignBoardingDoors()
        {
            var queues = new Dictionary<int, int>();
            foreach (Rider rider in Riders)
            {
                int key = rider.Origin * Stops + rider.OriginStop;
                if (revealedQueues.ContainsKey(key) || rider.Resolved || rider.Boarded
                    || rider.SpawnDelay > 0 || !IsPassengerTypeUnlocked(rider)) continue;
                queues.TryGetValue(key, out int slot);
                rider.PickupDoor = QueueDoor(rider.Origin, rider.OriginStop, slot);
                rider.PickupQueueSlot = slot;
                queues[key] = slot + 1;
            }
        }

        // Even the first small group has someone at the front. Alternate the
        // next side between landings; larger groups use all three entrances.
        static BoardingDoor QueueDoor(int floor, int stop, int slot)
        {
            int entrance = slot % 3;
            if (entrance == 0) return BoardingDoor.Front;
            bool leftFirst = (floor + stop) % 2 == 0;
            return (entrance == 1) == leftFirst ? BoardingDoor.Left : BoardingDoor.Right;
        }

        public bool WaitsHere(Rider p) => p != null && !p.Boarded && !p.Resolved
            && ContainsFloor(p.Origin) && p.Origin == Floor && p.OriginStop == Stop;
        public bool AtDestination(Rider p) => p != null && p.Destination == Floor && p.DestinationStop == Stop;

        public int PickStop(int floor)
        {
            if (CenterStopsOnly) return CenterStop;
            int[] weights = StopWeights[Math.Max(0, Math.Min(Dimensions - 1, floor / FloorsPerDimension))];
            int total = 0;
            for (int stop = 0; stop < Stops; stop++)
                if (StopIsOpen(floor, stop)) total += weights[stop];
            if (total <= 0) return CenterStop;
            int roll = DestinationRandom.Next(total);
            for (int stop = 0; stop < Stops; stop++)
            {
                if (!StopIsOpen(floor, stop)) continue;
                roll -= weights[stop];
                if (roll < 0) return stop;
            }
            return CenterStop;
        }

        bool StopIsOpen(int floor, int stop) => stop == CenterStop || StopOpen == null || StopOpen(floor, stop);

        // Riders already aboard keep their stops; everyone else is (re)placed.
        // keepFloor leaves the queue the car is looking at in place.
        public void AssignStops(int keepFloor = -1)
        {
            // Tutorial transitions can move the other landings; keep the group
            // currently in view and reveal the moved queues in their new places.
            foreach (int key in new List<int>(revealedQueues.Keys))
                if (key / Stops != keepFloor) revealedQueues.Remove(key);
            foreach (var rider in Riders)
            {
                if (rider.Resolved || rider.Boarded || rider.Origin == keepFloor) continue;
                rider.OriginStop = PickStop(rider.Origin);
                rider.DestinationStop = PickStop(rider.Destination);
            }
        }

        public static int DestinationFor(string kind, int origin)
        {
            if (origin < 0 || origin >= Floors) throw new ArgumentOutOfRangeException(nameof(origin));
            // Generate locally so a downward request cannot enter an earlier world.
            int firstFloor = origin / FloorsPerDimension * FloorsPerDimension;
            return firstFloor + DestinationFor(kind, origin - firstFloor, FloorsPerDimension - 1);
        }

        public static int DestinationFor(string kind, int origin, int highestFloor)
        {
            ValidateDestinationRange(origin, highestFloor);
            if (kind == "BOSS" || kind == "HANDYMAN") return RandomDestination(origin, highestFloor);

            // Ignore the elevator's direction: either side is equally likely.
            // At the building edges, choose the side that has floors available.
            bool goingUp = origin == 0 || origin < highestFloor && DestinationRandom.Next(2) == 0;
            int available = goingUp ? highestFloor - origin : origin;
            // Most rides are 1–3 floors, with occasional 4–6-floor detours.
            // Interview passengers keep short trips to suit their tight patience.
            int reach = Math.Min(available, kind == "INTERVIEW" ? 3 : 6);
            bool longerTrip = reach > 3 && DestinationRandom.Next(4) == 0;
            int distance = longerTrip ? DestinationRandom.Next(4, reach + 1)
                : DestinationRandom.Next(1, Math.Min(3, reach) + 1);
            return origin + (goingUp ? distance : -distance);
        }

        // Begin with the two straightforward office riders. New mechanics are
        // introduced alongside larger queues so the difficulty rises in clear steps.
        public bool IsPassengerTypeUnlocked(Rider rider)
        {
            if (rider == null) return false;
            // Each one-minute round starts with its world's complete mechanics.
            if (ActiveDimension >= 0) return ContainsFloor(rider.Origin);
            switch (rider.Kind)
            {
                case "COURIER":
                case "INTERVIEW":
                    return true;
                case "ELDERLY":
                    return Elapsed >= 30f || rider.Origin >= FloorsPerDimension * 2;
                case "PREGNANT":
                    return Elapsed >= 30f || rider.Origin >= FloorsPerDimension;
                case "BOSS":
                    return Elapsed >= 70f;
                case "HANDYMAN":
                    return Elapsed >= 90f || rider.Origin >= FloorsPerDimension * 2;
                case "GROUP":
                    return Elapsed >= 110f;
                default:
                    return true;
            }
        }

        // Skip a waiting passenger with the existing penalty.
        public bool Reject(Rider p)
        {
            if (Finished || !WaitsHere(p)) return false;
            RevealQueue(Floor, Stop);
            if (!IsOffered(p)) return false;
            p.Resolved = true; TurnedAway++; Score -= 15; return true;
        }

        // New landings introduce larger groups as the shift progresses.
        // Once shown, a landing keeps those exact passengers until they leave.
        public bool IsOffered(Rider candidate) => WaitsHere(candidate) && IsQueuedAtStop(candidate);

        // Capture before showing a landing or changing its passengers. Merely
        // querying an unseen stop (for example for CPU navigation) stays read-only.
        public void RevealQueue(int floor, int stop)
        {
            if (!ContainsFloor(floor) || stop < 0 || stop >= Stops) return;
            int key = floor * Stops + stop;
            if (revealedQueues.ContainsKey(key)) return;
            var queue = new HashSet<Rider>();
            int limit = OfferLimitAt(floor);
            foreach (Rider rider in Riders)
            {
                if (rider.Origin != floor || rider.OriginStop != stop || rider.Resolved
                    || rider.Boarded || rider.SpawnDelay > 0 || !IsPassengerTypeUnlocked(rider)) continue;
                // Capture doors together with the visible roster. Hidden or
                // locked riders must not consume the front entrance's place.
                rider.PickupDoor = QueueDoor(floor, stop, queue.Count);
                rider.PickupQueueSlot = queue.Count;
                queue.Add(rider);
                if (queue.Count >= limit) break;
            }
            revealedQueues.Add(key, queue);
        }

        // Both camera views and boarding use the same remaining landing roster.
        public bool IsQueuedAtStop(Rider candidate)
        {
            if (candidate == null || candidate.Resolved || candidate.Boarded || candidate.SpawnDelay > 0
                || !ContainsFloor(candidate.Origin) || !ContainsFloor(candidate.Destination))
                return false;
            int key = candidate.Origin * Stops + candidate.OriginStop;
            if (revealedQueues.TryGetValue(key, out HashSet<Rider> queue)) return queue.Contains(candidate);
            if (!IsPassengerTypeUnlocked(candidate)) return false;
            int offered = 0;
            foreach (var p in Riders)
            {
                if (p.Resolved || p.Boarded || p.SpawnDelay > 0 || p.Origin != candidate.Origin
                    || p.OriginStop != candidate.OriginStop || !IsPassengerTypeUnlocked(p)) continue;
                if (p == candidate) return offered < OfferLimitAt(candidate.Origin);
                offered++;
            }
            return false;
        }

        // Resolve an onboard passenger with the eject penalty.
        public bool Remove(Rider p)
        {
            if (Finished || !Owns(p)) return false;
            p.Boarded = false; p.BoardedBySeat = -1; p.Resolved = true;
            TurnedAway++; Score -= 40; return true;
        }

        // Called only when the car truly leaves, so reopening a closing door
        // still lets the player rescue somebody at their requested floor.
        // Penalize riders whose requested stop is being passed.
        public int LeaveFloor()
        {
            int missedHere = 0;
            foreach (var p in Riders)
            {
                if (!Owns(p) || !AtDestination(p)) continue;
                p.Mood = Math.Max(0, p.Mood - 1);
                p.Status = "You passed my floor!";
                Score -= 35; missedHere++;
            }
            return missedHere;
        }

        // Advance the shift and passenger patience clocks.
        public int Tick(float dt, bool stopped, bool tickWaiting = true)
        {
            if (Finished || dt <= 0) return 0;
            if (stopped) RevealQueue(Floor, Stop);
            // A large frame must not simulate past the deadline.
            dt = Math.Min(dt, TimeLeft);
            TimeLeft = Math.Max(0, TimeLeft - dt);
            if (OwnsPassengerPoolClock)
                foreach (var rider in Riders)
                    if (!rider.Resolved && rider.SpawnDelay > 0)
                        rider.SpawnDelay = Math.Max(0, rider.SpawnDelay - dt);
            float cabinPatienceMultiplier = CabinPatienceMultiplier;
            int waitingMisses = 0;
            foreach (var p in Riders)
            {
                if (p.Resolved) continue;
                if (Owns(p)) p.Remaining = Math.Max(0, p.Remaining - dt * cabinPatienceMultiplier);
                else if (!p.Boarded && tickWaiting && stopped && IsOffered(p))
                {
                    float waiting = dt;
                    if (p.Arrival > 0) { waiting = Math.Max(0, dt - p.Arrival); p.Arrival = Math.Max(0, p.Arrival - dt); }
                    p.Remaining = Math.Max(0, p.Remaining - waiting);
                    if (p.Remaining <= 0) { p.Resolved = true; waitingMisses++; }
                }
            }
            AddWaitingMisses(waitingMisses);
            return waitingMisses;
        }

        // When two cars wait at the same floor, each shares responsibility for
        // a timed-out passenger even though the queue's patience ticks once.
        public void AddWaitingMisses(int count)
        {
            if (count <= 0) return;
            Missed += count;
            Score -= 20 * count;
        }

        // Arrival only opens the doors. Delivery is deliberately manual: the
        // player must drag each onboard party out to deliver them.
        // Change floors; delivery remains manual.
        public int Arrive(int floor, int stop = CenterStop)
        {
            if (!ContainsFloor(floor)) throw new ArgumentOutOfRangeException(nameof(floor));
            Floor = floor;
            Stop = Math.Max(0, Math.Min(Stops - 1, stop));
            if (floor > PeakFloor)
            {
                PeakFloor = floor;
                PeakTime = Duration - TimeLeft;
            }
            int waitingToExit = 0;
            foreach (var p in Riders)
            {
                if (Owns(p) && AtDestination(p)) waitingToExit++;
            }
            return waitingToExit;
        }

        // Resolve a delivery and calculate its reward or penalty.
        public OffboardResult Offboard(Rider p)
        {
            if (Finished || !Owns(p)) return OffboardResult.None;
            p.Boarded = false; p.BoardedBySeat = -1; p.Resolved = true; Delivered++;
            if (!AtDestination(p))
            {
                p.Mood = 0; p.Status = "Wrong floor!"; Score -= 60;
                return OffboardResult.WrongFloor;
            }

            // Base points depend on remaining patience; type bonuses come next.
            bool onTime = p.Remaining > 0;
            int reward = onTime ? 100 + (int)(50 * p.Remaining / p.Patience) : LateDeliveriesScore ? 25 : 0;
            // One missed stop halves the base reward.
            if (p.Mood == 1) reward /= 2;
            if (onTime && p.Mood > 0)
            {
                Happy++;
                reward += p.Bonus;
            }
            Score += reward;
            p.Status = onTime && p.Mood > 0 ? "Made it!" : "Finally...";
            return onTime && p.Mood > 0 ? OffboardResult.Happy : OffboardResult.Late;
        }
    }
}
