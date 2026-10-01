using UnityEngine;

namespace CrazyElevator.Shared
{
    // Optional unique passenger traits. Custom logic lives where it fits cleanly
    // (boarding, scoring, impairment, speech) and checks these flags.
    [System.Flags]
    public enum PassengerFeature
    {
        None = 0,
        SlowArrival = 1 << 0,
        HoldDoorBonus = 1 << 1,
        MultiSpace = 1 << 2,
        UrgencySpeech = 1 << 3,
        ClearsRust = 1 << 4,
        GroupParty = 1 << 5
    }
}

namespace CrazyElevator.Shared
{
    // Shared designer-facing passenger template. Prefabs keep visuals;
    // this asset owns the rules data Liam's diagram called PassengerData.
    [CreateAssetMenu(menuName = "Crazy Elevator/Passenger Data", fileName = "PassengerData")]
    public sealed class PassengerData : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Rider";
        public string kind = "GENERIC";
        public string badge = "?";
        [TextArea] public string request = "Take me somewhere.";
        [Range(0, 7)] public int colorIndex;

        [Header("Rules")]
        [Range(1, 3)] public int space = 1;
        [Min(5f)] public float patience = 60f;
        public float arrivalDelay;
        public int bonus;
        public float holdRequired;
        public PassengerFeature features = PassengerFeature.None;

        [Header("Prefab")]
        public PassengerView prefab;

        public bool Has(PassengerFeature feature) => (features & feature) != 0;

        public Rider CreateRider(int origin, int destination)
        {
            return new Rider
            {
                Name = displayName,
                Kind = kind,
                Request = request,
                Badge = badge,
                Color = colorIndex,
                Origin = origin,
                Destination = destination,
                Space = Mathf.Clamp(space, 1, 3),
                Patience = patience,
                Remaining = patience,
                Arrival = arrivalDelay,
                Bonus = bonus,
                HoldRequired = holdRequired,
                Data = this
            };
        }
    }
}

namespace CrazyElevator.Shared
{
    // Optional catalog of passenger types for designer-driven rounds.
    [CreateAssetMenu(menuName = "Crazy Elevator/Passenger Catalog", fileName = "PassengerCatalog")]
    public sealed class PassengerCatalog : ScriptableObject
    {
        public PassengerData[] types = System.Array.Empty<PassengerData>();

        public PassengerData FindKind(string kind)
        {
            if (string.IsNullOrEmpty(kind) || types == null) return null;
            foreach (var type in types)
                if (type != null && type.kind == kind) return type;
            return null;
        }
    }
}
