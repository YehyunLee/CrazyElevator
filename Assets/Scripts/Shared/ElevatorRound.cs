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
        public const int Capacity = 10;
        public const float Duration = 180f;
        public readonly List<Rider> Riders = new List<Rider>();
        public int Floor, PeakFloor, Score, Delivered, Happy, Missed, TurnedAway;
        public float PeakTime;
        public float TimeLeft = Duration;
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
                    if (rider.Boarded && !rider.Resolved)
                        occupiedSpaces += rider.Space;
                }
                return occupiedSpaces;
            }
        }
        // Every round lasts three minutes. Reaching the top or clearing all
        // waiting passengers does not end it; the final Score is the result.
        public bool Finished => TimeLeft <= 0;

        // Create six passenger types on every floor.
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
                    if (type == null || type.kind == "HANDYMAN") continue;
                    int destination = (f + 1) % Floors;
                    if (type.kind == "BOSS") destination = (f + 4) % Floors;
                    else if (type.kind == "ELDERLY" || type.kind == "PREGNANT") destination = (f + 2) % Floors;
                    else if (type.kind == "INTERVIEW") destination = (f + 3) % Floors;
                    round.Riders.Add(type.CreateRider(f, destination));
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
                Add("Remy", "COURIER", "Two spaces. Quick stop!", "BOX", f, (f + 1) % Floors, 2, 34, 0, 0, 30);
                Add("Mina", "PREGNANT", "Two spaces, please.", "2X", f, (f + 2) % Floors, 2, 42, 0, 1, 70);
                Add("Jules", "INTERVIEW", "My interview starts soon!", "!", f, (f + 3) % Floors, 1, 13, 0, 2, 120);
                Add("Morgan", "BOSS", "Hold OPEN for my bonus.", "B", f, (f + 4) % Floors, 1, 30, 0, 3, 140, 1.25f);
                Add("Eli", "ELDERLY", "Please wait for me...", "SLOW", f, (f + 2) % Floors, 1, 58, 6.2f, 4, 175);
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
            return true;
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
            if (candidate == null || candidate.Resolved || candidate.Boarded || candidate.Origin != Floor) return false;
            int offered = 0;
            foreach (var p in Riders)
            {
                if (p.Resolved || p.Boarded || p.Origin != Floor) continue;
                if (p == candidate) return offered < 3;
                offered++;
            }
            return false;
        }

        // Resolve an onboard passenger with the eject penalty.
        public bool Remove(Rider p)
        {
            if (Finished || !p.Boarded || p.Resolved) return false;
            p.Boarded = false; p.Resolved = true; TurnedAway++; Score -= 40; return true;
        }

        // Accumulate the boss bonus while OPEN is held.
        public void HoldDoor(float dt)
        {
            if (dt <= 0) return;
            foreach (var p in Riders)
            {
                if (!p.Boarded || p.Resolved || p.HoldRequired <= 0 || p.HoldSatisfied) continue;
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
                if (!p.Boarded || p.Resolved || p.Destination != Floor) continue;
                p.Mood = Math.Max(0, p.Mood - 1);
                p.Status = "You passed my floor!";
                Score -= 35; missedHere++;
            }
            return missedHere;
        }

        // Advance the shift and passenger patience clocks.
        public void Tick(float dt, bool stopped)
        {
            if (Finished || dt <= 0) return;
            // A large frame must not simulate past the deadline.
            dt = Math.Min(dt, TimeLeft);
            TimeLeft = Math.Max(0, TimeLeft - dt);
            foreach (var p in Riders)
            {
                if (p.Resolved) continue;
                if (p.Boarded) p.Remaining = Math.Max(0, p.Remaining - dt);
                else if (stopped && p.Origin == Floor && IsOffered(p))
                {
                    float waiting = dt;
                    if (p.Arrival > 0) { waiting = Math.Max(0, dt - p.Arrival); p.Arrival = Math.Max(0, p.Arrival - dt); }
                    p.Remaining = Math.Max(0, p.Remaining - waiting);
                    if (p.Remaining <= 0) { p.Resolved = true; Missed++; Score -= 20; }
                }
            }
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
                if (p.Boarded && !p.Resolved && p.Destination == floor) waitingToExit++;
            }
            return waitingToExit;
        }

        // Resolve a delivery and calculate its reward or penalty.
        public OffboardResult Offboard(Rider p)
        {
            if (Finished || p == null || !p.Boarded || p.Resolved) return OffboardResult.None;
            p.Boarded = false; p.Resolved = true; Delivered++;
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
