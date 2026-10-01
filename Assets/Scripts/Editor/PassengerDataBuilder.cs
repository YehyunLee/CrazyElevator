using UnityEditor;
using UnityEngine;
using CrazyElevator.Shared;

namespace CrazyElevator.EditorTools
{
    // Creates default PassengerData assets under Assets/Data/Passengers.
    public static class PassengerDataBuilder
    {
        [MenuItem("Crazy Elevator/Create Default Passenger Data")]
        public static void CreateDefaults()
        {
            const string folder = "Assets/Data/Passengers";
            EnsureFolder(folder);

            var catalog = LoadOrCreateCatalog(folder + "/PassengerCatalog.asset");
            catalog.types = new[]
            {
                Make(folder, "Courier", "Remy", "COURIER", "BOX", "Two spaces. Quick stop!", 0, 2, 34, 0, 30, 0,
                    PassengerFeature.MultiSpace),
                Make(folder, "Pregnant", "Mina", "PREGNANT", "2X", "Two spaces, please.", 1, 2, 42, 0, 70, 0,
                    PassengerFeature.MultiSpace),
                Make(folder, "Interview", "Jules", "INTERVIEW", "!", "My interview starts soon!", 2, 1, 13, 0, 120, 0,
                    PassengerFeature.UrgencySpeech),
                Make(folder, "Boss", "Morgan", "BOSS", "B", "Hold OPEN for my bonus.", 3, 1, 30, 0, 140, 1.25f,
                    PassengerFeature.HoldDoorBonus),
                Make(folder, "Elderly", "Eli", "ELDERLY", "SLOW", "Please wait for me...", 4, 1, 58, 6.2f, 175, 0,
                    PassengerFeature.SlowArrival),
                Make(folder, "Group", "The Trio", "GROUP", "3X", "All three or none!", 5, 3, 32, 0, 130, 0,
                    PassengerFeature.MultiSpace | PassengerFeature.GroupParty),
                Make(folder, "Handyman", "Casey", "HANDYMAN", "FIX", "Rust-free while I'm aboard!", 4, 1, 80, 0, 110, 0,
                    PassengerFeature.ClearsRust)
            };
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = catalog;
            Debug.Log("Created default passenger data + catalog in " + folder);
        }

        static PassengerData Make(string folder, string file, string name, string kind, string badge, string request,
            int color, int space, float patience, float arrival, int bonus, float hold, PassengerFeature features)
        {
            string path = folder + "/PassengerData_" + file + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<PassengerData>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<PassengerData>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.displayName = name;
            asset.kind = kind;
            asset.badge = badge;
            asset.request = request;
            asset.colorIndex = color;
            asset.space = space;
            asset.patience = patience;
            asset.arrivalDelay = arrival;
            asset.bonus = bonus;
            asset.holdRequired = hold;
            asset.features = features;
            EditorUtility.SetDirty(asset);
            return asset;
        }

        static PassengerCatalog LoadOrCreateCatalog(string path)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PassengerCatalog>(path);
            if (catalog != null) return catalog;
            catalog = ScriptableObject.CreateInstance<PassengerCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
            return catalog;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
