using System;
using System.Collections.Generic;

namespace CrazyElevator.Shared
{
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
            Data != null ? Data.Has(feature) : FeatureFallback(feature);

        bool FeatureFallback(PassengerFeature feature)
        {
            switch (feature)
            {
                case PassengerFeature.SlowArrival: return Arrival > 0 || Kind == "ELDERLY";
                case PassengerFeature.HoldDoorBonus: return HoldRequired > 0 || Kind == "BOSS";
                case PassengerFeature.MultiSpace: return Space > 1;
                case PassengerFeature.UrgencySpeech: return Kind == "INTERVIEW";
                case PassengerFeature.ClearsRust: return Kind == "HANDYMAN";
                case PassengerFeature.GroupParty: return Kind == "GROUP" || Space >= 3;
                case PassengerFeature.SpeedBoost: return Kind == "GROUP";
                case PassengerFeature.SpeedSlow: return Kind == "ELDERLY";
                default: return false;
            }
        }

        // Route, occupied space, and existing delivery bonus.
        public int Origin;
        public int Destination;
        public int Space;
        public int Bonus;

        // Patience and the boss's existing hold-door request.
        // 2 = happy, 1 = reduced mood, 0 = unhappy.
        public int Mood = 2;
        public float Patience;
        // Patience seconds remaining.
        public float Remaining;
        // Seconds until a slow waiting rider reaches the door.
        public float Arrival;
        public float HoldRequired;
        public float HoldProgress;

        public bool Boarded;
        // A shared passenger pool still needs to know which car holds a rider.
        public int BoardedBySeat = -1;
        // Brief entry delay before a replacement rider joins the shared queue.
        public float SpawnDelay;
        // Resolved riders cannot board or score again.
        public bool Resolved;
        public bool HoldSatisfied => HoldRequired <= 0 || HoldProgress >= HoldRequired;
    }

    public enum OffboardResult { None, Happy, Late, WrongFloor }

}


namespace CrazyElevator.Shared
{
    // Pure game rules; independent of scene objects and frame rate.
    public sealed class ElevatorRound
    {
        public const int Floors = 12;
        public const int Capacity = 9;
        public const float Duration = 180f;
        public const float ElderlyArrivalSeconds = 1.5f;
        static readonly Random DestinationRandom = new Random();
        public List<Rider> Riders = new List<Rider>();
        public int Seat { get; private set; }
        public int Floor, PeakFloor, Score, Delivered, Happy, Missed, TurnedAway;
        public float PeakTime;
        public float TimeLeft = Duration;
        bool RefillPassengerQueue;
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
        // Every round lasts three minutes. Reaching the top or clearing all
        // waiting passengers does not end it; the final Score is the result.
        public bool Finished => TimeLeft <= 0;

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

        void SeedDefaults()
        {
            for (int f = 0; f < Floors; f++)
            {
                PassengerTheme world = PassengerData.ThemeAtFloor(f);
                if (world == PassengerTheme.Office)
                    Add("Remy", "COURIER", "Two spaces. Quick stop!", "BOX", f, (f + 1) % Floors, 2, 34, 0, 0, 30);
                if (world == PassengerTheme.Candy)
                    Add("Mina", "PREGNANT", "Two spaces, please.", "2X", f, (f + 2) % Floors, 2, 42, 0, 1, 70);
                if (world == PassengerTheme.Office)
                    Add("Jules", "INTERVIEW", "My interview starts soon!", "!", f, (f + 3) % Floors, 1, 13, 0, 2, 120);
                if (world == PassengerTheme.Candy)
                    Add("Morgan", "BOSS", "Hold OPEN for my bonus.", "B", f, RandomDestination(f), 1, 30, 0, 3, 140, 1.25f);
                Add("Eli", "ELDERLY", "Please wait for me...", "SLOW", f, (f + 2) % Floors, 1, 58, ElderlyArrivalSeconds, 4, 175);
                if (world == PassengerTheme.Candy)
                    Add("The Trio", "GROUP", "All three or none!", "3X", f, (f + 1) % Floors, 3, 32, 0, 5, 130);
            }
        }

        // Store a passenger with fresh patience.
        void Add(string name, string kind, string request, string badge, int from, int to, int space,
            float patience, float arrival, int color, int bonus, float holdRequired = 0)
        {
            Riders.Add(new Rider { Name = name, Kind = kind, Request = request, Origin = from,
                Destination = to, Space = space, Patience = patience, Remaining = patience, Arrival = arrival,
                Color = color, Badge = badge, Bonus = bonus, HoldRequired = holdRequired });
        }

        // Special passengers may request any other floor, including a floor in
        // their own world. Skipping the origin prevents a zero-distance trip.
        public static int RandomDestination(int origin)
        {
            if (origin < 0 || origin >= Floors) throw new ArgumentOutOfRangeException(nameof(origin));
            int choice = DestinationRandom.Next(Floors - 1);
            return choice >= origin ? choice + 1 : choice;
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
                source.RefillPassengerQueue = true;
                RefillPassengerQueue = true;
                OwnsPassengerPoolClock = false;
            }
            Seat = seat;
        }

        public bool Owns(Rider rider) => rider != null && rider.Boarded
            && !rider.Resolved && rider.BoardedBySeat == Seat;

        public bool HasCapacityFor(Rider p)
        {
            return p != null && p.Space > 0 && Load + p.Space <= Capacity;
        }

        // Validate boarding; input code also checks physical fit.
        public bool Board(Rider p)
        {
            if (p == null || Finished || p.Resolved || p.Boarded || p.Origin != Floor
                || !IsOffered(p) || p.Arrival > 0 || !HasCapacityFor(p)) return false;
            p.Boarded = true;
            p.BoardedBySeat = Seat;
            if (RefillPassengerQueue) Riders.Add(CreateReplacement(p));
            return true;
        }

        static Rider CreateReplacement(Rider previous)
        {
            int destination = DestinationFor(previous.Kind, previous.Origin);
            Rider replacement = previous.Data != null
                ? previous.Data.CreateRider(previous.Origin, destination)
                : new Rider
                {
                    Name = previous.Name,
                    Request = previous.Request,
                    Kind = previous.Kind,
                    Badge = previous.Badge,
                    Color = previous.Color,
                    Origin = previous.Origin,
                    Destination = destination,
                    Space = previous.Space,
                    Patience = previous.Patience,
                    Remaining = previous.Patience,
                    Arrival = previous.Kind == "ELDERLY" ? ElderlyArrivalSeconds : 0,
                    Bonus = previous.Bonus,
                    HoldRequired = previous.HoldRequired
                };
            replacement.SpawnDelay = 1f;
            return replacement;
        }

        static int DestinationFor(string kind, int origin)
        {
            if (kind == "BOSS" || kind == "HANDYMAN") return RandomDestination(origin);
            if (kind == "ELDERLY" || kind == "PREGNANT") return (origin + 2) % Floors;
            if (kind == "INTERVIEW") return (origin + 3) % Floors;
            return (origin + 1) % Floors;
        }

        // Skip a waiting passenger with the existing penalty.
        public bool Reject(Rider p)
        {
            if (Finished || p.Resolved || p.Boarded || p.Origin != Floor || !IsOffered(p)) return false;
            p.Resolved = true; TurnedAway++; Score -= 15; return true;
        }

        // Offer only the first three eligible waiting parties.
        public bool IsOffered(Rider candidate)
        {
            if (candidate == null || candidate.Resolved || candidate.Boarded || candidate.SpawnDelay > 0
                || candidate.Origin != Floor) return false;
            int offered = 0;
            foreach (var p in Riders)
            {
                if (p.Resolved || p.Boarded || p.SpawnDelay > 0 || p.Origin != Floor) continue;
                if (p == candidate) return offered < 3;
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

        // Accumulate the boss bonus while OPEN is held.
        public void HoldDoor(float dt)
        {
            if (dt <= 0) return;
            foreach (var p in Riders)
            {
                if (!Owns(p) || p.HoldRequired <= 0 || p.HoldSatisfied) continue;
                p.HoldProgress = Math.Min(p.HoldRequired, p.HoldProgress + dt);
                if (p.HoldSatisfied) p.Status = "Boss bonus ready!";
            }
        }

        // Called only when the car truly leaves, so reopening a closing door
        // still lets the player rescue somebody at their requested floor.
        // Penalize riders whose requested stop is being passed.
        public int LeaveFloor()
        {
            int missedHere = 0;
            foreach (var p in Riders)
            {
                if (!Owns(p) || p.Destination != Floor) continue;
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
            // A large frame must not simulate past the deadline.
            dt = Math.Min(dt, TimeLeft);
            TimeLeft = Math.Max(0, TimeLeft - dt);
            if (RefillPassengerQueue && OwnsPassengerPoolClock)
                foreach (var rider in Riders)
                    if (!rider.Resolved && rider.SpawnDelay > 0)
                        rider.SpawnDelay = Math.Max(0, rider.SpawnDelay - dt);
            int waitingMisses = 0;
            foreach (var p in Riders)
            {
                if (p.Resolved) continue;
                if (Owns(p)) p.Remaining = Math.Max(0, p.Remaining - dt);
                else if (!p.Boarded && tickWaiting && stopped && p.Origin == Floor && IsOffered(p))
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
        public int Arrive(int floor)
        {
            if (floor < 0 || floor >= Floors) throw new ArgumentOutOfRangeException(nameof(floor));
            Floor = floor;
            if (floor > PeakFloor)
            {
                PeakFloor = floor;
                PeakTime = Duration - TimeLeft;
            }
            int waitingToExit = 0;
            foreach (var p in Riders)
            {
                if (Owns(p) && p.Destination == floor) waitingToExit++;
            }
            return waitingToExit;
        }

        // Resolve a delivery and calculate its reward or penalty.
        public OffboardResult Offboard(Rider p)
        {
            if (Finished || !Owns(p)) return OffboardResult.None;
            p.Boarded = false; p.BoardedBySeat = -1; p.Resolved = true; Delivered++;
            if (p.Destination != Floor)
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
                if (p.HoldSatisfied) reward += p.Bonus;
            }
            Score += reward;
            p.Status = onTime && p.Mood > 0 ? "Made it!" : "Finally...";
            return onTime && p.Mood > 0 ? OffboardResult.Happy : OffboardResult.Late;
        }
    }
}
