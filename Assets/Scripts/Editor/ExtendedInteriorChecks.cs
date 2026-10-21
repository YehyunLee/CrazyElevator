using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Game = CrazyElevator.Managers.ElevatorManager;
using Round = CrazyElevator.Shared.ElevatorRound;
using SharedRider = CrazyElevator.Shared.Rider;

// Explicitly requested checks only. Never opens, saves, or rebuilds another scene.
[InitializeOnLoad]
public static class ExtendedInteriorChecks
{
    const string Work = "Temp/ExtendedInterior";
    const string Scene = "Assets/Scenes/Main.unity";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static ExtendedInteriorChecks() { EditorApplication.update += Poll; }
    static object Get(Game game, string name)
    {
        object value = typeof(Game).GetField(name, Flags).GetValue(game);
        // These checks cover the shaft-stop rules only: riders wait at the centre stop,
        // shaft hazards stay out of the way, and stops are pressed manually.
        if (value is Round round && !round.CenterStopsOnly)
        {
            round.CenterStopsOnly = true;
            round.AssignStops();
            Set(game, "hazardCooldown", 9999f);
            Set(game, "autoStopAtDestinations", false);
        }
        return value;
    }
    static void Set(Game game, string name, object value) => typeof(Game).GetField(name, Flags).SetValue(game, value);
    static void Call(Game game, string name, params object[] args) => typeof(Game).GetMethod(name, Flags).Invoke(game, args);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string request = Work + "/request.txt";
        if (!File.Exists(request)) return;
        string action;
        try { action = File.ReadAllText(request).Trim(); }
        catch (IOException) { return; }
        if (action.Length == 0) return;
        if (!EditorApplication.isPlaying)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != Scene) return;
            EditorApplication.isPlaying = true;
            return;
        }
        var game = UnityEngine.Object.FindAnyObjectByType<Game>();
        if (!game || game.gameObject.scene.path != Scene || Get(game, "round") == null) return;
        if (action == "match-prepare")
        {
            File.WriteAllText(request, "match-run");
            typeof(CrazyElevator.Managers.MenuManager)
                .GetField("startDuelOnLoad", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, true);
            CrazyElevator.Managers.MenuManager.Close();
            UnityEngine.SceneManagement.SceneManager.LoadScene(Scene);
            return;
        }
        try { File.Delete(request); }
        catch (IOException) { return; }
        int exitCode = 0;
        try
        {
            if (action == "checks") { Run(game); RunBoost(game); }
            else if (action == "boost-checks") RunBoost(game);
            else if (action == "repair-checks") RunRepair(game);
            else if (action == "match-run") RunMatch();
            else if (action == "solo-basic") RunSoloBasic(game);
            else if (action == "grid-checks") RunGridChecks(game);
            else if (action == "reward-preview") PreviewDeliveryReward(game);
            else if (action == "rust-inside" || action == "rust-outside")
            {
                game.enabled = true; Call(game, "Restart");
                int water = game.underwaterStartsAtFloor;
                ((Round)Get(game, "round")).Floor = water;
                Set(game, "rustExposure", game.secondsPerRustLevel * 2);
                game.sceneView.floorDisplay.text = water.ToString();
                Call(game, "SyncFigures", 0f); Call(game, "UpdateInteriorPersona", 0f);
                if (action == "rust-outside")
                {
                    Call(game, "CloseAndTravel"); Call(game, "SetDoors", 0f); Call(game, "BeginBuildingTravel");
                    Set(game, "travelFloor", water + 1.1f); Set(game, "travelVelocity", 0f);
                }
                Call(game, "LateUpdate");
                EditorApplication.isPaused = true;
            }
            else if (action == "building" || action == "hover")
            {
                game.enabled = true; Call(game, "Restart");
                if (action == "building")
                {
                    Call(game, "CloseAndTravel"); Call(game, "SetDoors", 0f); Call(game, "BeginBuildingTravel");
                    Call(game, "AdvanceBuildingTravel", 4.1f / game.floorsPerSecond); Call(game, "LateUpdate");
                }
                else
                {
                    Set(game, "controllerSelection", true);
                    Set(game, "lastMousePosition", Mouse.current == null ? Vector2.zero : Mouse.current.position.ReadValue());
                    Call(game, "SelectRider", ((Round)Get(game, "round")).Riders[1]);
                }
            }
            else if (action == "water" || action == "candy" || action == "water-transfer" || action == "candy-transfer")
            {
                game.enabled = true;
                Call(game, "Restart");
                bool water = action.StartsWith("water");
                int previewFloor = water ? 8 : action.StartsWith("candy") ? 4 : 0;
                string previewKind = water ? "HANDYMAN" : action.StartsWith("candy") ? "PREGNANT" : "COURIER";
                ((Round)Get(game, "round")).Floor = previewFloor;
                game.sceneView.floorDisplay.text = previewFloor.ToString();
                Call(game, "SyncFigures", 0f); Call(game, "UpdateInteriorPersona", 0f);
                if (action.EndsWith("-transfer"))
                {
                    Call(game, "SelectRider", ((Round)Get(game, "round")).Riders.Find(r => r.Origin == previewFloor && r.Kind == previewKind));
                    Call(game, "ConfirmPassenger");
                    if (water) Call(game, "SyncFigures", .3f);
                    else
                    {
                        // Finish the candy boarding pose, then apply its final
                        // facing and crying state for a stable visual check.
                        Call(game, "SyncFigures", 2f);
                        Call(game, "SyncFigures", 0f);
                        Call(game, "UpdatePassengerEffects", .5f);
                    }
                    game.enabled = false;
                }
            }
            else throw new Exception("Unknown interior check request: " + action);
        }
        catch (Exception e) { exitCode = 1; File.WriteAllText(Work + "/result.txt", e.ToString()); Debug.LogException(e); }
        finally
        {
            if (action == "repair-checks")
            {
                EditorApplication.isPaused = false;
                EditorApplication.isPlaying = false;
            }
            if ((action == "match-run" || action == "solo-basic" || action == "checks" || action == "boost-checks") && Application.isBatchMode)
                EditorApplication.Exit(exitCode);
        }
    }

    [MenuItem("Tools/Crazy Elevator/Check Extended Interior (Play Mode)")]
    public static void RequestChecks()
    {
        Directory.CreateDirectory(Work); File.WriteAllText(Work + "/request.txt", "checks");
    }

    [MenuItem("Tools/Crazy Elevator/Check Travel Speeds and Boost (Play Mode)")]
    public static void RequestBoostChecks()
    {
        Directory.CreateDirectory(Work); File.WriteAllText(Work + "/request.txt", "boost-checks");
    }

    [MenuItem("Tools/Crazy Elevator/Check Handyman Repair (Returns to Edit Mode)")]
    static void RequestRepairChecks()
    {
        Directory.CreateDirectory(Work); File.WriteAllText(Work + "/request.txt", "repair-checks");
    }

    [MenuItem("Tools/Crazy Elevator/Check Destination Rules")]
    public static void CheckDestinationRules()
    {
        string[] kinds = { "COURIER", "INTERVIEW", "ELDERLY", "PREGNANT", "GROUP", "BOSS", "HANDYMAN" };
        foreach (string kind in kinds)
            for (int origin = 0; origin < Round.Floors; origin++)
            {
                int ceiling = (origin / Round.FloorsPerDimension + 1) * Round.FloorsPerDimension - 1;
                for (int sample = 0; sample < 256; sample++)
                {
                    int destination = Round.DestinationFor(kind, origin);
                    int distance = Math.Abs(destination - origin);
                    Require(destination >= 0 && destination <= ceiling && distance > 0,
                        kind + " requested its origin or a floor in a locked world.");
                    if (kind == "INTERVIEW")
                        Require(distance <= 3, "An interview passenger received an excessively long trip.");
                    else if (kind != "BOSS" && kind != "HANDYMAN")
                        Require(distance <= 6, "A regular passenger received an excessively long trip.");
                }
            }

        // Large samples and broad bounds check the play balance without relying
        // on a particular random seed or an exact sequence of destinations.
        foreach (string kind in new[] { "COURIER", "INTERVIEW", "ELDERLY", "PREGNANT", "GROUP" })
            foreach (int origin in new[] { 3, 11, 19 })
            {
                int up = 0, down = 0, shortTrips = 0;
                var lengths = new HashSet<int>();
                const int samples = 2048;
                for (int sample = 0; sample < samples; sample++)
                {
                    int destination = Round.DestinationFor(kind, origin);
                    int distance = Math.Abs(destination - origin);
                    if (destination > origin) up++; else down++;
                    if (distance <= 3) shortTrips++;
                    lengths.Add(distance);
                }
                Require(up > samples * .35f && down > samples * .35f && lengths.Count >= 3,
                    kind + " did not offer varied trips in both directions at floor " + origin + ".");
                Require(shortTrips > samples * .65f,
                    "Most " + kind + " requests should be short enough to combine into a route.");
                if (kind != "INTERVIEW")
                    Require(shortTrips < samples * .97f,
                        kind + " never offered the occasional longer trip.");
            }

        foreach (Action invalid in new Action[]
        {
            () => Round.DestinationFor("COURIER", -1),
            () => Round.DestinationFor("COURIER", Round.Floors),
            () => Round.DestinationFor("COURIER", 0, 0),
            () => Round.DestinationFor("COURIER", 0, Round.Floors),
            () => Round.DestinationFor("COURIER", -1, 7),
            () => Round.DestinationFor("COURIER", 8, 7)
        })
        {
            bool rejected = false;
            try { invalid(); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Require(rejected, "Destination generation accepted an invalid floor range.");
        }

        bool reachedNewWorld = false;
        for (int sample = 0; sample < 512; sample++)
        {
            int destination = Round.DestinationFor("COURIER", 7, 15);
            Require(destination >= 0 && destination <= 15 && destination != 7,
                "A live request exceeded its unlocked ceiling.");
            reachedNewWorld |= destination > 7;
            int special = Round.RandomDestination(12);
            Require(special >= 0 && special < Round.Floors && special != 12,
                "Unrestricted special requests must remain valid.");
        }
        Require(reachedNewWorld, "Live requests never reached the newly opened world.");

        bool defaultUp = false, defaultDown = false;
        for (int sample = 0; sample < 32; sample++)
            foreach (SharedRider rider in new Round().Riders)
                if (rider.Origin == 3 && rider.Kind != "BOSS" && rider.Kind != "HANDYMAN")
                {
                    defaultUp |= rider.Destination > rider.Origin;
                    defaultDown |= rider.Destination < rider.Origin;
                }
        Require(defaultUp && defaultDown, "The default roster still uses fixed upward routes.");

        var catalog = ScriptableObject.CreateInstance<CrazyElevator.Shared.PassengerCatalog>();
        var type = ScriptableObject.CreateInstance<CrazyElevator.Shared.PassengerData>();
        try
        {
            type.kind = "COURIER";
            catalog.types = new[] { type };
            foreach (SharedRider rider in Round.FromCatalog(catalog).Riders)
            {
                int ceiling = (rider.Origin / Round.FloorsPerDimension + 1) * Round.FloorsPerDimension - 1;
                Require(rider.Destination >= 0 && rider.Destination <= ceiling && rider.Destination != rider.Origin,
                    "A catalog passenger received an invalid or locked destination.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(type);
            UnityEngine.Object.DestroyImmediate(catalog);
        }

        var player = new Round { Floor = 3 };
        player.Riders.Clear();
        var upward = new SharedRider { Kind = "COURIER", Origin = 3, Destination = 5,
            Space = 1, Patience = 40f, Remaining = 40f };
        var downward = new SharedRider { Kind = "INTERVIEW", Origin = 3, Destination = 1,
            Space = 1, Patience = 40f, Remaining = 40f };
        player.Riders.Add(upward);
        player.Riders.Add(downward);
        var rival = new Round { Floor = 3 };
        rival.SharePassengerPoolWith(player, 1);
        Require(player.Board(upward) && !rival.Board(upward) && rival.Board(downward),
            "Mixed-direction requests did not preserve exclusive shared boarding.");
        Require(player.Riders.Count == 4 && ReferenceEquals(player.Riders, rival.Riders),
            "Shared boarding did not replenish the same passenger pool.");
        foreach (SharedRider replacement in player.Riders)
            if (!replacement.Boarded)
                Require(replacement.Destination >= 0 && replacement.Destination <= 7
                    && replacement.Destination != replacement.Origin,
                    "A shared replacement requested a locked or zero-distance trip.");
        player.Arrive(upward.Destination);
        rival.Arrive(downward.Destination);
        Require(player.Offboard(upward) == CrazyElevator.Shared.OffboardResult.Happy
            && rival.Offboard(downward) == CrazyElevator.Shared.OffboardResult.Happy
            && player.Delivered == 1 && rival.Delivered == 1 && player.Load == 0 && rival.Load == 0,
            "Upward and downward deliveries did not score independently.");
        Require(Round.Duration == 180f && Round.MaxUnlockedFloorAt(0f) == 7
            && Round.MaxUnlockedFloorAt(40f) == 15 && Round.MaxUnlockedFloorAt(110f) == 23,
            "Random routes changed the three-minute shift or world progression.");
        Debug.Log("PASS: valid randomized destinations, balanced up/down requests, mostly short trips, catalog/default/shared generation, deliveries in both directions, and unchanged world progression.");
    }

    [MenuItem("Tools/Crazy Elevator/Check Shared Passenger Rules")]
    public static void CheckSharedPassengerRules()
    {
        var player = new Round();
        var rival = new Round();
        rival.SharePassengerPoolWith(player, 1);
        Require(player.Riders.Count == rival.Riders.Count
            && ReferenceEquals(player.Riders[0], rival.Riders[0]),
            "Both elevators must use the same rider objects.");

        // A duel must replenish the shared queue immediately. Otherwise the
        // first car can consume the only two floor-zero offers before the
        // second car gets a chance to board.
        var fairnessPlayer = new Round();
        var fairnessRival = new Round();
        fairnessRival.SharePassengerPoolWith(fairnessPlayer, 1);
        SharedRider first = fairnessPlayer.Riders.Find(r => r.Origin == 0 && r.Kind == "COURIER");
        SharedRider second = fairnessPlayer.Riders.Find(r => r.Origin == 0 && r.Kind == "INTERVIEW");
        Require(fairnessPlayer.Board(first) && fairnessPlayer.Board(second),
            "The first elevator could not board the initial floor-zero offers.");
        SharedRider third = fairnessPlayer.Riders.Find(r => r.Origin == 0
            && !r.Boarded && !r.Resolved && r.Kind == "COURIER");
        Require(third != null && fairnessPlayer.Board(third),
            "The shared queue did not replenish after the first elevator boarded three.");
        for (int i = 0; i < 2; i++)
        {
            SharedRider available = fairnessRival.Riders.Find(r => r.Origin == 0
                && !r.Boarded && !r.Resolved && r.Kind != "ELDERLY");
            Require(available != null && fairnessRival.Board(available),
                "The second elevator could not claim its own shared-queue passenger.");
        }
        int fairnessPlayerLoad = fairnessPlayer.Riders.FindAll(r => r.Boarded && r.BoardedBySeat == 0).Count;
        int fairnessRivalLoad = fairnessRival.Riders.FindAll(r => r.Boarded && r.BoardedBySeat == 1).Count;
        Require(fairnessPlayerLoad == 3 && fairnessRivalLoad == 2,
            "Shared passenger replenishment did not preserve both players' loads.");

        Require(player.Riders.FindAll(r => r.Kind == "BOSS").Count == 4
            && player.Riders.TrueForAll(r => r.Kind != "BOSS" || (r.Origin >= 4 && r.Origin <= 7)),
            "Candy-world passengers must wait only on candy floors.");
        foreach (var rider in player.Riders)
            Require(rider.Destination >= 0 && rider.Destination < Round.Floors
                && rider.Destination != rider.Origin,
                "Every passenger needs a valid destination on another floor.");

        var courier = player.Riders.Find(r => r.Kind == "COURIER" && r.Origin == 0);
        var pregnant = player.Riders.Find(r => r.Kind == "PREGNANT" && r.Origin == 4);
        Require(player.Board(courier) && !rival.Board(courier)
            && player.Owns(courier) && !rival.Owns(courier)
            && player.Load == courier.Space && rival.Load == 0,
            "The same rider boarded two cars or counted toward the rival's load.");
        player.Floor = 4;
        rival.Floor = 4;
        float patience = pregnant.Remaining;
        player.Tick(1f, true);
        rival.Tick(1f, true, false);
        Require(Math.Abs(pregnant.Remaining - (patience - 1f)) < .001f,
            "A jointly stopped floor drained waiting patience twice.");
        var timeoutPlayer = new Round();
        var timeoutRival = new Round();
        timeoutRival.SharePassengerPoolWith(timeoutPlayer, 1);
        var impatient = timeoutPlayer.Riders.Find(r => r.Kind == "COURIER" && r.Origin == 0);
        impatient.Remaining = .25f;
        int misses = timeoutPlayer.Tick(.5f, true);
        timeoutRival.Tick(.5f, true, false);
        timeoutRival.AddWaitingMisses(misses);
        Require(misses == 1 && impatient.Resolved && timeoutPlayer.Score == -20 && timeoutRival.Score == -20,
            "A shared waiting timeout must penalize both cars stopped at that floor once.");
        Require(rival.Board(pregnant) && !player.Board(pregnant)
            && rival.Load == pregnant.Space && player.Load == courier.Space,
            "The rival could not claim a distinct rider from the shared queue.");
        Require(rival.Offboard(courier) == CrazyElevator.Shared.OffboardResult.None
            && !rival.Remove(courier)
            && player.Offboard(pregnant) == CrazyElevator.Shared.OffboardResult.None,
            "One car changed the other car's passenger state.");
        player.Arrive(courier.Destination);
        Require(player.Offboard(courier) == CrazyElevator.Shared.OffboardResult.Happy
            && player.Load == 0, "The player could not deliver its rider.");
        int playerScore = player.Score;
        rival.Arrive(pregnant.Destination);
        Require(rival.Offboard(pregnant) == CrazyElevator.Shared.OffboardResult.Happy
            && rival.Load == 0 && player.Score == playerScore,
            "The rival delivery changed the player's score or load.");

        var babyRound = new Round { Floor = 4 };
        var baby = babyRound.Riders.Find(r => r.Kind == "PREGNANT" && r.Origin == 4);
        var companion = babyRound.Riders.Find(r => r.Kind == "BOSS" && r.Origin == 4);
        var grandma = babyRound.Riders.Find(r => r.Kind == "ELDERLY" && r.Origin == 4);
        grandma.Arrival = 0;
        Require(babyRound.Board(baby) && babyRound.Board(companion)
            && Math.Abs(babyRound.CabinPatienceMultiplier - Round.CryingPatienceMultiplier) < .001f,
            "A boarded baby did not activate the cabin patience penalty.");
        float cryingPatience = companion.Remaining;
        babyRound.Tick(1f, true);
        Require(Math.Abs(companion.Remaining - (cryingPatience - Round.CryingPatienceMultiplier)) < .001f,
            "The crying baby did not accelerate onboard patience drain.");
        Require(babyRound.Board(grandma)
            && babyRound.BabyCalmedByGrandma
            && Math.Abs(babyRound.CabinPatienceMultiplier - Round.CalmPatienceMultiplier) < .001f,
            "A boarded Grandma did not calm the baby.");
        float calmPatience = companion.Remaining;
        babyRound.Tick(1f, true);
        Require(Math.Abs(companion.Remaining - (calmPatience - Round.CalmPatienceMultiplier)) < .001f,
            "Grandma did not slow onboard patience drain while calming the baby.");

        var catalog = ScriptableObject.CreateInstance<CrazyElevator.Shared.PassengerCatalog>();
        var boss = ScriptableObject.CreateInstance<CrazyElevator.Shared.PassengerData>();
        var regular = ScriptableObject.CreateInstance<CrazyElevator.Shared.PassengerData>();
        try
        {
            boss.kind = "BOSS";
            boss.theme = CrazyElevator.Shared.PassengerTheme.Candy;
            regular.kind = "COURIER";
            regular.theme = CrazyElevator.Shared.PassengerTheme.Office;
            catalog.types = new[] { boss, regular };
            var fromCatalog = Round.FromCatalog(catalog);
            Require(fromCatalog.Riders.FindAll(r => r.Kind == "BOSS").Count == 4
                && fromCatalog.Riders.TrueForAll(r => r.Kind != "BOSS" || (r.Origin >= 4 && r.Origin <= 7))
                && fromCatalog.Riders.FindAll(r => r.Kind == "COURIER").Count == 4
                && fromCatalog.Riders.TrueForAll(r => r.Kind != "COURIER" || r.Origin <= 3),
                "The passenger catalog placed themed passengers outside their worlds.");
            for (int origin = 0; origin < Round.Floors; origin++)
                for (int sample = 0; sample < 20; sample++)
                {
                    int destination = Round.RandomDestination(origin);
                    Require(destination >= 0 && destination < Round.Floors && destination != origin,
                        "A special passenger received an invalid destination.");
                }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalog);
            UnityEngine.Object.DestroyImmediate(boss);
            UnityEngine.Object.DestroyImmediate(regular);
        }
        Debug.Log("PASS: shared passenger identities, exclusive boarding, separate loads and scores, single waiting tick, baby and Grandma patience combo, world-specific passenger origins, and valid special destinations.");
    }

    [MenuItem("Tools/Crazy Elevator/Check 1v1 Shared Passengers")]
    public static void RequestMatchChecks()
    {
        EditorSceneManager.OpenScene(Scene);
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/request.txt", "match-prepare");
    }

    [MenuItem("Tools/Crazy Elevator/Check Solo Layout and Passengers")]
    public static void RequestSoloBasic()
    {
        EditorSceneManager.OpenScene(Scene);
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/request.txt", "solo-basic");
    }

    [MenuItem("Tools/Crazy Elevator/Check 3x3 Cabin Occupancy")]
    public static void RequestGridChecks()
    {
        EditorSceneManager.OpenScene(Scene);
        Directory.CreateDirectory(Work);
        File.WriteAllText(Work + "/request.txt", "grid-checks");
    }

    static void RunGridChecks(Game game)
    {
        CrazyElevator.Managers.MenuManager.Close();
        var titleCanvas = GameObject.Find("Title Menu Canvas");
        if (titleCanvas) titleCanvas.SetActive(false);
        Call(game, "Restart");
        var round = (Round)Get(game, "round");
        var courier = round.Riders.Find(r => r.Origin == 0 && r.Space == 2);
        var single = round.Riders.Find(r => r.Origin == 0 && r.Space == 1);
        Require(courier != null && single != null, "The first floor needs two-space and one-space riders.");

        var find = typeof(Game).GetMethod("TryFindGridPlacement", Flags);
        var place = typeof(Game).GetMethod("TryGridPlacement", Flags);
        object[] findArgs = { courier, Vector3.zero };
        Require((bool)find.Invoke(game, findArgs), "A two-cell rider cannot find an empty pair.");
        Vector3 pair = (Vector3)findArgs[1];
        Require(round.Board(courier), "The two-cell rider did not board.");
        ((IDictionary)Get(game, "cabinPositions"))[courier] = pair;

        object[] overlap = { single, pair, Vector3.zero };
        Require(!(bool)place.Invoke(game, overlap), "A second rider entered an occupied grid cell.");
        object[] neighbor = { single, new Vector3(1.27f, .12f, pair.z), Vector3.zero };
        Require((bool)place.Invoke(game, neighbor), "The free neighboring cell is not usable.");
        Require(round.Board(single), "The single-cell rider did not board.");
        ((IDictionary)Get(game, "cabinPositions"))[single] = neighbor[2];

        Call(game, "UpdateGridDestinations");
        var labels = game.sceneView.gridDestinations;
        Require(labels.Length == 9 && labels[0].text == courier.Destination.ToString()
            && labels[1].text == "+" && labels[2].text == single.Destination.ToString(),
            "The visible cell labels do not match the occupied footprints.");
        File.WriteAllText(Work + "/result.txt", "3x3 occupancy, adjacent placement, and floor labels passed.");

        // Leave a crowded cabin running for a visual check of floor-label
        // size, passenger occlusion, and the waiting-only overhead badges.
        var extraPair = round.Riders.Find(r => r != courier && r.Space == 2);
        var extraSingles = round.Riders.FindAll(r => r != single && r.Space == 1);
        Require(extraPair != null && extraSingles.Count >= 2, "Not enough riders for a crowded preview.");
        void ShowOnGrid(SharedRider rider, Vector3 spot)
        {
            rider.Boarded = true;
            rider.BoardedBySeat = round.Seat;
            ((IDictionary)Get(game, "cabinPositions"))[rider] = spot;
        }
        ShowOnGrid(extraSingles[0], new Vector3(-1.266667f, .12f, 2.3f));
        ShowOnGrid(extraPair, new Vector3(.633333f, .12f, 2.3f));
        ShowOnGrid(extraSingles[1], new Vector3(0f, .12f, 3.733333f));
        Call(game, "SyncFigures", 2f);
    }

    static void RunSoloBasic(Game game)
    {
        Require(game.Match == null, "Solo mode unexpectedly created a rival elevator.");
        CrazyElevator.Managers.MenuManager.Close();
        Call(game, "Restart");
        Call(game, "LateUpdate");
        var round = (Round)Get(game, "round");
        float centre = CrazyElevator.Match.ElevatorMatch.ShaftCenterX;
        Require(Mathf.Abs(game.sceneView.exteriorCar.localPosition.x - centre) < .001f
            && Mathf.Abs(game.sceneView.exteriorCamera.transform.position.x - centre) < .001f
            && !game.sceneView.exteriorCamera.orthographic,
            "Solo travel must keep the car and perspective camera aligned straight on.");
        var shaft = game.sceneView.exteriorCar.parent;
        foreach (int floor in new[] { 1, 11 })
        {
            float landingHeight = floor * 13.2f;
            var shaftLanding = shaft.Find("Shaft landing " + floor);
            var worldLanding = shaft.Find("World landing " + floor);
            var rightLanding = shaft.Find("World landing " + floor + " right");
            Require(shaftLanding && worldLanding && rightLanding
                && Mathf.Abs(shaftLanding.localPosition.y - landingHeight) < .02f
                && Mathf.Abs(worldLanding.localPosition.y - landingHeight) < .02f
                && Mathf.Abs(rightLanding.localPosition.y - landingHeight) < .02f,
                "The taller shaft landings do not line up with the floors.");
        }
        Require(round.Riders.FindAll(r => r.Kind == "HANDYMAN").Count == 4
            && round.Riders.TrueForAll(r => r.Kind != "HANDYMAN" || r.Origin >= 8)
            && round.Riders.FindAll(r => r.Kind == "BOSS").Count == 4
            && round.Riders.TrueForAll(r => r.Kind != "BOSS" || (r.Origin >= 4 && r.Origin <= 7)),
            "Special passengers are waiting outside their assigned worlds.");
        var band = typeof(Game).GetMethod("BandForFloor", Flags);
        Require(band.Invoke(game, new object[] { 0f }).ToString() == "Office"
            && band.Invoke(game, new object[] { 4f }).ToString() == "Candy"
            && band.Invoke(game, new object[] { 8f }).ToString() == "Water",
            "Office, candy, and underwater floors are out of order.");

        var rider = round.Riders.Find(r => r.Kind == "COURIER" && r.Origin == 0);
        Require(round.Board(rider) && game.PassengerLoad == rider.Space
            && game.CurrentPassengers.Count == 1, "Solo boarding changed.");
        float before = round.TimeLeft;
        round.Tick(1f, true);
        Require(Math.Abs(round.TimeLeft - (before - 1f)) < .001f,
            "Solo shift timer did not advance.");
        Call(game, "CloseAndTravel");
        Call(game, "BeginBuildingTravel");
        Set(game, "travelFloor", 1f);
        Call(game, "LateUpdate");
        Require(Mathf.Abs(game.sceneView.exteriorCar.localPosition.y
            - (Game.ShaftFloorHeight + 1.4f)) < .02f,
            "The traveling elevator does not line up with the taller first floor.");
        Call(game, "RequestFloorStop");
        Call(game, "AdvanceBuildingTravel", .5f);
        Require(round.Floor == 1 && Get(game, "phase").ToString() == "Opening"
            && round.Offboard(rider) == CrazyElevator.Shared.OffboardResult.Happy
            && game.PassengerLoad == 0 && round.Delivered == 1,
            "Solo travel or delivery changed.");
        File.WriteAllText(Work + "/result.txt",
            "PASS: solo car centred with a diagonal perspective camera; office/candy/water order; special origins; boarding, travel, timer, and delivery.");
        Debug.Log(File.ReadAllText(Work + "/result.txt"));
    }

    static void RunMatch()
    {
        var match = UnityEngine.Object.FindAnyObjectByType<CrazyElevator.Match.ElevatorMatch>();
        Require(match != null && match.player != null && match.Rival != null,
            "1v1 did not create the NPC elevator.");
        var player = match.player;
        match.StartMatch();
        var rival = match.Rival;
        var playerRound = (Round)Get(player, "round");
        var rivalRound = (Round)Get(rival, "round");
        Require(ReferenceEquals(playerRound.Riders[0], rivalRound.Riders[0]),
            "1v1 elevators did not start with one shared passenger pool.");
        Require(Mathf.Abs(match.ShaftX(0) + match.ShaftX(1) - 2f * CrazyElevator.Match.ElevatorMatch.ShaftCenterX) < .001f,
            "1v1 tracks do not straddle the building centre.");

        var matchType = typeof(CrazyElevator.Match.ElevatorMatch);
        var trackPositions = (float[])matchType.GetField("trackPosition", Flags).GetValue(match);
        var carsOverlap = matchType.GetMethod("CarsOverlap", Flags);
        Require(!(bool)carsOverlap.Invoke(match, new object[] { player, rival }),
            "Elevators on separate tracks collide before their visible cars touch.");
        trackPositions[1] = trackPositions[0];
        Require((bool)carsOverlap.Invoke(match, new object[] { player, rival }),
            "Elevators at the same position do not register a visible-car collision.");
        matchType.GetField("collisionCooldown", Flags).SetValue(match, 0f);
        matchType.GetMethod("TriggerCollision", Flags).Invoke(match, new object[] { player, rival });
        var sparks = (ParticleSystem)matchType.GetField("collisionParticles", Flags).GetValue(match);
        Require(sparks != null && sparks.gameObject.layer == 31 && sparks.particleCount > 0,
            "A 1v1 collision did not emit sparks in the exterior camera layer.");
        trackPositions[1] = 1f;

        var rider = playerRound.Riders.Find(r => r.Origin == 0 && r.Kind == "COURIER");
        Require(playerRound.Board(rider) && !rivalRound.Board(rider),
            "Both elevators boarded the same floor-zero passenger.");
        Require(player.PassengerLoad == rider.Space && rival.PassengerLoad == 0
            && player.CurrentPassengers.Count == 1 && rival.CurrentPassengers.Count == 0,
            "A boarded passenger appeared inside both elevators.");
        var rivalFigures = (IDictionary)Get(rival, "figures");
        Require(rivalFigures.Contains(rider), "The NPC was not showing the shared floor passenger.");
        var departingFigure = (Transform)rivalFigures[rider];
        Vector3 departureStart = departingFigure.localPosition;
        rival.RefreshWaitingPublic();
        Require(departingFigure.gameObject.activeInHierarchy,
            "A passenger claimed by the player vanished from the NPC view without departing.");
        Call(rival, "SyncFigures", .3f);
        Require(departingFigure.gameObject.activeInHierarchy
            && Vector3.Distance(departingFigure.localPosition, departureStart) > .05f,
            "A shared passenger did not visibly walk away from the NPC view.");
        Call(rival, "SyncFigures", 1f);
        Require(!departingFigure.gameObject.activeInHierarchy,
            "The NPC kept showing a passenger after their departure animation finished.");
        foreach (var waiting in rival.WaitingPassengers)
            Require(!ReferenceEquals(waiting, rider), "A boarded passenger remained in the rival's queue.");
        Require(rival.NpcBoard() && rival.PassengerLoad > 0 && player.PassengerLoad == rider.Space
            && rival.CurrentPassengers.Count == 1 && !ReferenceEquals(rival.CurrentPassengers[0], rider),
            "The NPC could not board a different passenger from the shared queue.");
        var npcClaim = rival.CurrentPassengers[0];
        var playerFigures = (IDictionary)Get(player, "figures");
        Require(playerFigures.Contains(npcClaim), "The player was not showing the NPC's floor passenger.");
        var playerDeparture = (Transform)playerFigures[npcClaim];
        Vector3 playerDepartureStart = playerDeparture.localPosition;
        player.RefreshWaitingPublic();
        Require(playerDeparture.gameObject.activeInHierarchy,
            "A passenger claimed by the NPC vanished from the player view without departing.");
        Call(player, "SyncFigures", .3f);
        Require(playerDeparture.gameObject.activeInHierarchy
            && Vector3.Distance(playerDeparture.localPosition, playerDepartureStart) > .05f,
            "The NPC's passenger did not visibly leave the player view.");
        Call(player, "SyncFigures", 1f);
        Require(!playerDeparture.gameObject.activeInHierarchy,
            "The player kept showing the NPC's passenger after departure finished.");

        player.ResetRoundPublic();
        playerRound = (Round)Get(player, "round");
        rivalRound = (Round)Get(rival, "round");
        Require(ReferenceEquals(playerRound.Riders[0], rivalRound.Riders[0])
            && !ReferenceEquals(playerRound.Riders[0], rider)
            && player.PassengerLoad == 0 && rival.PassengerLoad == 0,
            "Resetting the match must restore a fresh shared pool for both elevators.");
        // A real vertical contact should reverse both moving cars, not just stop them.
        trackPositions[1] = trackPositions[0];
        var phaseField = typeof(Game).GetField("phase", Flags);
        object moving = Enum.Parse(phaseField.FieldType, "Moving");
        phaseField.SetValue(player, moving); phaseField.SetValue(rival, moving);
        Set(player, "travelFloor", 4f); Set(rival, "travelFloor", 4.5f);
        Set(player, "travelVelocity", 1f); Set(rival, "travelVelocity", -1f);
        float incoming = 1f;
        float contact = match.ConstrainTravel(player, 4f, 4.4f, ref incoming);
        Require(contact < 4.4f && player.TravelVelocity < 0f && rival.TravelVelocity > 0f
            && (float)Get(player, "collisionRecoilTimer") > 0f,
            "Vertical contact must separate both moving cars with a short rebound.");

        // Stopping to handle passengers cannot make a car collision-proof.
        // A moving rival should knock it back into shaft travel and visibly
        // displace it away from the floor.
        match.StartMatch();
        trackPositions[1] = trackPositions[0];
        phaseField.SetValue(player, moving);
        var stoppedRound = (Round)Get(rival, "round");
        stoppedRound.Floor = 4;
        Set(player, "travelFloor", 3.4f); Set(player, "travelVelocity", 1.8f);
        Set(player, "travelDirection", 1);
        float ramVelocity = 1.8f;
        match.ConstrainTravel(player, 3.4f, 3.9f, ref ramVelocity);
        float shoveStart = rival.CurrentFloor;
        Require(rival.MovingInShaft && rival.TravelVelocity > 0f
            && (float)Get(rival, "collisionRecoilTimer") >= .6f,
            "A boarding elevator remained protected from an incoming collision.");
        Call(rival, "AdvanceBuildingTravel", .45f);
        Require(rival.CurrentFloor > shoveStart + .4f,
            "The stopped elevator was not knocked visibly away from its floor.");

        // The route remains a straight vertical shaft. Choosing the highlighted
        // express gate still grants one burst without changing the track path.
        match.StartMatch();
        phaseField.SetValue(player, moving);
        float junctionFloor = match.JunctionFloor(0);
        Set(player, "travelFloor", junctionFloor);
        Set(player, "travelVelocity", 1f);
        var targetTracks = (int[])matchType.GetField("targetTrack", Flags).GetValue(match);
        targetTracks[0] = match.ExpressTrack(0);
        Call(player, "LateUpdate");
        Require(!player.sceneView.exteriorCamera.orthographic
            && Mathf.Abs(player.sceneView.exteriorCar.localPosition.x - match.ShaftX(player)) < .001f
            && Mathf.Abs(player.sceneView.exteriorCar.localPosition.z + .8f) < .001f,
            "The generated elevator track is not straight and vertical.");
        float speedBeforeExpress = player.TravelVelocity;
        match.EvaluateJunction(player, junctionFloor - .01f, junctionFloor + .01f);
        Require(player.TravelVelocity > speedBeforeExpress && match.ExpressFlash(player) > 0f,
            "The highlighted express track did not grant its speed burst.");
        match.StartMatch();
        File.WriteAllText(Work + "/result.txt",
            "PASS: Main creates two centred vertical 1v1 tracks; travel cameras, visible-car collision, sparks and stronger vertical bounce work; stopped boarding cars can be knocked away; express boost works; both cars share rider identities; boarding is exclusive; loads and queues remain car-specific; match reset restores a fresh shared pool.");
        Debug.Log(File.ReadAllText(Work + "/result.txt"));
    }

    [MenuItem("Tools/Crazy Elevator/Preview Delivery Reward Popup")]
    static void RequestRewardPreview()
    {
        Directory.CreateDirectory(Work); File.WriteAllText(Work + "/request.txt", "reward-preview");
    }

    static void PreviewDeliveryReward(Game game)
    {
        CrazyElevator.Managers.MenuManager.Close();
        Call(game, "Restart");
        int scoreBefore = game.Points;
        Call(game, "RegisterDeliveryPopup", new Vector3(-.75f, .2f, 2.1f),
            CrazyElevator.Shared.OffboardResult.Happy, 145);
        Call(game, "RegisterDeliveryPopup", new Vector3(.7f, .05f, 2.2f),
            CrazyElevator.Shared.OffboardResult.Happy, 110);
        Require(((ICollection)Get(game, "deliveryPopups")).Count == 2
            && (int)Get(game, "deliveryCombo") == 2,
            "The delivery preview did not create a two-drop combo.");
        Require(game.Points == scoreBefore, "The presentation-only combo changed the score.");
        Call(game, "RegisterDeliveryPopup", Vector3.zero,
            CrazyElevator.Shared.OffboardResult.WrongFloor, -60);
        Require((int)Get(game, "deliveryCombo") == 0
            && ((ICollection)Get(game, "deliveryPopups")).Count == 2,
            "A wrong-floor drop must reset the combo without creating a positive popup.");
        File.WriteAllText(Work + "/result.txt",
            "PASS: points popups created; quick combo reached x2; wrong-floor reset; score unchanged. Editor paused for visual review.");
        EditorApplication.isPaused = true;
    }

    static void Input(Game game, Gamepad pad, GamepadState state, float dt = .02f)
    {
        InputSystem.QueueStateEvent(pad, state);
        InputSystem.Update();
        RefreshInputManager();
        Call(game, "UpdatePassengerSelection");
        Call(game, "HandleInteriorController", dt);
    }

    static void RefreshInputManager()
    {
        var input = CrazyElevator.Managers.InputManager.Instance;
        if (input != null)
            typeof(CrazyElevator.Managers.InputManager).GetMethod("Update", Flags).Invoke(input, null);
    }

    static void TravelTo(Game game, float target)
    {
        for (int i = 0; i < 12000 && Mathf.Abs((float)Get(game, "travelFloor") - target) > .045f; i++)
            Call(game, "AdvanceBuildingTravel", .02f);
        Require(Mathf.Abs((float)Get(game, "travelFloor") - target) <= .045f, "Travel did not reach the test floor.");
    }
    static object Property(Game game, string name) => typeof(Game).GetProperty(name, Flags).GetValue(game);
    static void Phase(Game game, string name) => Set(game, "phase", Enum.Parse(Get(game, "phase").GetType(), name));

    static void FinishDock(Game game)
    {
        Call(game, "AdvanceBuildingTravel", .5f);
        Call(game, "LateUpdate");
        Require(Get(game, "phase").ToString() == "Opening" && game.sceneView.exteriorCamera.enabled,
            "Building view must stay visible while the stopped elevator opens its doors.");
        Set(game, "phaseTime", 1f); Call(game, "Update"); Call(game, "LateUpdate");
        Require(Get(game, "phase").ToString() == "Boarding" && game.sceneView.cabinCamera.enabled && !game.sceneView.exteriorCamera.enabled,
            "The fully open elevator must return to the interior.");
    }
    static void Close(Game game)
    {
        Call(game, "CloseAndTravel"); Call(game, "LateUpdate");
        Require(Get(game, "phase").ToString() == "Closing" && game.sceneView.cabinCamera.enabled, "The inside view should show the doors closing.");
        Set(game, "phaseTime", 1f); Call(game, "Update"); Call(game, "LateUpdate");
        Require(Get(game, "phase").ToString() == "Moving" && game.sceneView.exteriorCamera.enabled
            && game.sceneView.cabinCamera.enabled && game.sceneView.cabinCamera.rect.width < game.sceneView.exteriorCamera.rect.width,
            "Closed doors must switch to shaft travel with a smaller cabin preview.");
    }

    static void Run(Game game)
    {
        CrazyElevator.Managers.MenuManager.Close();
        var pad = InputSystem.AddDevice<Gamepad>();
        var mouse = InputSystem.AddDevice<Mouse>();
        game.enabled = false;
        try
        {
            Call(game, "Restart"); Call(game, "LateUpdate"); Physics.SyncTransforms();
            var round = (Round)Get(game, "round");
            var rider = round.Riders[1]; // Deliberately select the second passenger, never the automatic first one.
            var rig = (ElevatorPersonaRig)Get(game, "persona");
            var figures = (IDictionary)Get(game, "figures");
            var badges = (IDictionary)Get(game, "destinationTags");
            var badge = (TextMesh)badges[rider];
            Vector2 cursor = game.sceneView.cabinCamera.WorldToScreenPoint(badge.transform.position);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }); InputSystem.Update();
            Call(game, "UpdatePassengerSelection");
            Require(ReferenceEquals(Get(game, "selectedRider"), rider), "Hover failed to select the exact passenger.");
            Require(((IDictionary)Get(game, "selectionMaterials")).Count > 0 && !rider.Boarded, "Hover must highlight without boarding.");
            Vector3 before = ((Transform)figures[rider]).localPosition;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }.WithButton(MouseButton.Left)); InputSystem.Update();
            Call(game, "UpdatePassengerSelection"); Call(game, "HandleInteriorController", .02f);
            Require(ReferenceEquals(Get(game, "draggedRider"), rider) && !rider.Boarded,
                "Mouse press should begin dragging the highlighted passenger without boarding immediately.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }); InputSystem.Update();
            Call(game, "UpdatePassengerSelection");
            Require(Get(game, "draggedRider") == null && !rider.Boarded && ((Transform)figures[rider]).localPosition == before,
                "A click without a drag should leave the passenger in place.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = Vector2.zero }); InputSystem.Update();
            Call(game, "UpdatePassengerSelection");
            Require(Get(game, "selectedRider") == null && ((Transform)figures[rider]).localPosition == before,
                "Moving off a passenger must clear its highlight.");
            Input(game, pad, new GamepadState { leftStick = Vector2.right });
            Require(Get(game, "selectedRider") != null && Get(game, "phase").ToString() == "Boarding", "Stick navigation must select, not depart.");
            Input(game, pad, new GamepadState());
            // Hover takes over again after controller selection.
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }); InputSystem.Update(); Call(game, "UpdatePassengerSelection");
            Input(game, pad, new GamepadState().WithButton(GamepadButton.South));
            Require(rider.Boarded && !round.Riders[0].Boarded && ((IDictionary)Get(game, "boardingTransfers")).Count == 1,
                "Confirm must move only the selected passenger.");
            Call(game, "CloseAndTravel");
            Require(Get(game, "phase").ToString() == "Boarding", "Doors closed during a passenger transfer.");
            Call(game, "SyncFigures", 1.5f);
            Input(game, pad, new GamepadState());
            Close(game);
            Call(game, "AdvanceBuildingTravel", .55f / game.floorsPerSecond); Call(game, "RequestFloorStop");
            Require(Get(game, "phase").ToString() == "Moving", "A stop between floors was accepted.");
            TravelTo(game, 2.1f);
            Require(round.Floor == 0 && Get(game, "phase").ToString() == "Moving" && (float)Get(game, "doors") == 0,
                "Passing a floor must not automatically stop or open doors.");
            Set(game, "paused", true); float frozen = (float)Get(game, "travelFloor"); Call(game, "Update"); Call(game, "LateUpdate");
            Require((float)Get(game, "travelFloor") == frozen && game.sceneView.exteriorCamera.enabled, "Pause changed position or camera.");
            Set(game, "paused", false);
            Input(game, pad, new GamepadState().WithButton(GamepadButton.South));
            Require(Get(game, "phase").ToString() == "Docking" && (int)Get(game, "destination") == 2, "Confirm did not select the nearby floor.");
            FinishDock(game); Require(round.Floor == 2, "Docked at the wrong floor.");
            Call(game, "SelectRider", rider); Call(game, "ConfirmPassenger");
            int score = round.Score;
            Require(round.Delivered == 1 && ((IDictionary)Get(game, "exiting")).Count == 1
                && ((ICollection)Get(game, "deliveryPopups")).Count == 1
                && (int)Get(game, "deliveryCombo") == 1,
                "A correct unload must create one points popup and start its combo.");
            Call(game, "RegisterDeliveryPopup", before, CrazyElevator.Shared.OffboardResult.Happy, 100);
            Require(((ICollection)Get(game, "deliveryPopups")).Count == 2
                && (int)Get(game, "deliveryCombo") == 2,
                "A quick second delivery must advance the visible combo.");
            Call(game, "SyncFigures", 1.5f);
            Require(round.Score == score && ((IDictionary)Get(game, "exiting")).Count == 0, "Unload scored twice or failed to finish.");
            Input(game, pad, new GamepadState()); Close(game);
            Call(game, "AdvanceBuildingTravel", 100f);
            Require((float)Get(game, "travelFloor") == Round.Floors - 1 && Get(game, "phase").ToString() == "Moving", "Top boundary opened automatically or exceeded the shaft.");
            Input(game, pad, new GamepadState { leftStick = Vector2.down });
            float waterFloor = game.underwaterStartsAtFloor + .1f;
            TravelTo(game, waterFloor); Call(game, "RequestFloorStop"); FinishDock(game);
            Require(round.Floor == game.underwaterStartsAtFloor && rig.waterBackdrop.activeSelf, "Upper floor did not become underwater.");
            for (int i = 0; i < 4; i++) Input(game, pad, new GamepadState().WithButton(GamepadButton.RightShoulder), .5f);
            Require((float)Get(game, "repairedFor") > 19, "Repair no longer works.");
            Input(game, pad, new GamepadState()); Close(game);
            float candyFloor = game.candyStartsAtFloor + .1f;
            TravelTo(game, candyFloor); Call(game, "RequestFloorStop"); FinishDock(game);
            Require(round.Floor == game.candyStartsAtFloor && rig.candyBackdrop.activeSelf, "Lower floor did not restore candy.");
            Close(game); Call(game, "AdvanceBuildingTravel", 100f);
            Require((float)Get(game, "travelFloor") == 0 && Get(game, "phase").ToString() == "Moving", "Bottom boundary opened automatically or exceeded the shaft.");
            // A real mouse drag boards the rider at the released cabin position.
            Call(game, "Restart"); Call(game, "LateUpdate"); Physics.SyncTransforms();
            round = (Round)Get(game, "round"); rider = round.Riders[1];
            badge = (TextMesh)badges[rider]; cursor = game.sceneView.cabinCamera.WorldToScreenPoint(badge.transform.position);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }); InputSystem.Update();
            Call(game, "UpdatePassengerSelection");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }.WithButton(MouseButton.Left)); InputSystem.Update();
            Call(game, "UpdatePassengerSelection");
            float dragY = (float)Get(game, "dragPlaneLocalY");
            var stage = (Transform)Get(game, "stage");
            Vector3 cabinTarget = new Vector3(0, dragY, 2f);
            Vector2 cabinCursor = game.sceneView.cabinCamera.WorldToScreenPoint(stage.TransformPoint(cabinTarget));
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cabinCursor }.WithButton(MouseButton.Left)); InputSystem.Update();
            Call(game, "UpdatePassengerSelection");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cabinCursor }); InputSystem.Update();
            Call(game, "UpdatePassengerSelection");
            Require(rider.Boarded && ((IDictionary)Get(game, "cabinPositions")).Contains(rider),
                "Dragging a waiting passenger into the cabin should board and place them.");
            File.WriteAllText(Work + "/result.txt", "PASS: exact hover selection/highlight; click versus drag handling; drag-to-board; moving away clears highlight; joystick selection and confirm remain available; safe doors; interior while closing; shaft travel with cabin PIP; no automatic floor stops; stop rejected between floors; confirm docks at nearby floor; doors open before interior return; pause preserves travel view; top/bottom bounds remain closed; up/down reversal; candy below/water above; repair; single scoring; points popup; quick-delivery combo.");
        }
        finally
        {
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(pad);
            Call(game, "Restart"); game.enabled = true;
        }
    }
    static SharedRider PrepareRepair(Game game, bool damaged = true)
    {
        Call(game, "Restart");
        var round = (Round)Get(game, "round");
        round.Floor = damaged ? game.underwaterStartsAtFloor : 0;
        game.sceneView.floorDisplay.text = round.Floor.ToString();
        Set(game, "rustExposure", damaged ? game.secondsPerRustLevel * 2 : 0f);
        Call(game, "SyncFigures", 0f); Call(game, "UpdateInteriorPersona", 0f);
        Call(game, "LateUpdate"); Physics.SyncTransforms();
        return round.Riders.Find(r => r.Kind == "HANDYMAN" && r.Origin == round.Floor);
    }

    static void RequireRepairCleared(Game game, string context)
    {
        Require(!(bool)Property(game, "HandymanRepairActive")
            && Get(game, "repairToolPivot") == null && Get(game, "repairShovel") == null
            && ((ICollection)Get(game, "repairPartPoses")).Count == 0,
            context + " left active repair state or temporary tools.");
    }

    sealed class AuthoredRepairPose
    {
        public Transform part, parent;
        public Vector3 position, scale;
        public Quaternion rotation;
        public bool active;
    }

    static void RunRepair(Game game)
    {
        CrazyElevator.Managers.MenuManager.Close();
        game.enabled = false;
        try
        {
            var rider = PrepareRepair(game, false);
            Require(rider != null, "The scene is missing its handyman passenger.");
            Call(game, "SelectRider", rider); Call(game, "ConfirmPassenger");
            Call(game, "SyncFigures", 1.5f);
            Require(rider.Boarded && !(bool)Property(game, "HandymanRepairActive"),
                "A handyman must board normally when there are no damaged starfish.");

            rider = PrepareRepair(game);
            var inside = (StarfishImpairmentView)Get(game, "interiorImpairment");
            var outside = (StarfishImpairmentView)Get(game, "exteriorImpairment");
            Require(rider != null && inside && outside
                && inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3,
                "The underwater handyman and both damaged starfish gauges must be present.");
            Require(game.handymanShovelPrefab, "Assign the Handyman Shovel prefab in Main.");
            var figures = (IDictionary)Get(game, "figures");
            var figure = (Transform)figures[rider];
            var poses = new List<AuthoredRepairPose>();
            foreach (Transform part in figure)
                if (part.name.StartsWith("Wrench") || part.name == "Arm" && part.localPosition.x < 0)
                    poses.Add(new AuthoredRepairPose { part = part, parent = part.parent,
                        position = part.localPosition, rotation = part.localRotation,
                        scale = part.localScale, active = part.gameObject.activeSelf });
            Require(poses.Count >= 2, "Handyman art must include the authored wrench and left arm.");

            Call(game, "SelectRider", rider); Call(game, "SyncFigures", .1f);
            Require(!rider.Boarded && !(bool)Property(game, "HandymanRepairActive")
                && inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3,
                "Highlighting the handyman must not start repair.");
            Call(game, "ConfirmPassenger"); Call(game, "UpdateInteriorPersona", 0f);
            Require(rider.Boarded && (bool)Property(game, "HandymanRepairPending")
                && inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3,
                "Confirmed boarding must start repair without immediately clearing starfish.");
            Call(game, "SyncFigures", .3f);
            Require((float)Get(game, "handymanRepairTime") == 0f,
                "The repair animation must wait for boarding to finish.");
            Call(game, "SyncFigures", 1f);
            Vector3 home = figure.localPosition, homeScale = figure.localScale;
            Call(game, "CloseAndTravel");
            Require(Get(game, "phase").ToString() == "Boarding", "Doors closed during repair.");

            Call(game, "SyncFigures", .8f); Call(game, "UpdateInteriorPersona", 0f);
            var pivot = (Transform)Get(game, "repairToolPivot");
            var shovel = (Transform)Get(game, "repairShovel");
            Require(pivot && shovel && Vector3.Distance(figure.localPosition, home) > .1f,
                "The handyman did not move to the repair location or create the tools.");
            foreach (var pose in poses)
                Require(pose.part.parent == pivot, "The authored arm and wrench did not follow the repair tool pivot.");
            Require(!shovel.gameObject.activeSelf && inside.DisplayedSeverity == 3,
                "The wrench stage must precede the shovel sweep.");

            Vector3 starStart = inside.starfish[0].localPosition;
            Call(game, "SyncFigures", 1.2f); Call(game, "UpdateInteriorPersona", 0f);
            Require(shovel.gameObject.activeSelf && inside.starfish[0].localScale.x < .5f
                && inside.starfish[0].localPosition != starStart && inside.DisplayedSeverity == 3
                && outside.starfish[0].localScale.x < .5f && outside.DisplayedSeverity == 3,
                "The sweep must visibly remove starfish in both gauges before severity clears.");
            Call(game, "SyncFigures", 1.3f); Call(game, "UpdateInteriorPersona", 0f);
            Require(inside.DisplayedSeverity == 0 && outside.DisplayedSeverity == 0
                && inside.starfish[0].localScale == Vector3.zero && outside.starfish[0].localScale == Vector3.zero
                && !(bool)Property(game, "HandymanRepairPending")
                && (bool)Property(game, "HandymanRepairActive"),
                "Sweep completion must clear both gauges while the handyman returns.");
            Call(game, "SyncFigures", 1f);
            RequireRepairCleared(game, "Completing the animation");
            Require(Vector3.Distance(figure.localPosition, home) < .001f
                && Vector3.Distance(figure.localScale, homeScale) < .001f
                && Quaternion.Angle(figure.localRotation, Quaternion.identity) < .01f,
                "The handyman did not return to the original cabin pose.");
            foreach (var pose in poses)
                Require(pose.part.parent == pose.parent && pose.part.gameObject.activeSelf == pose.active
                    && Vector3.Distance(pose.part.localPosition, pose.position) < .001f
                    && Vector3.Distance(pose.part.localScale, pose.scale) < .001f
                    && Quaternion.Angle(pose.part.localRotation, pose.rotation) < .01f,
                    "Repair changed the authored arm or wrench pose.");
            Call(game, "CloseAndTravel");
            Require(Get(game, "phase").ToString() == "Closing", "Travel did not unlock after repair.");

            // Drag release must start the same repair, and an interrupted repair must restore damage.
            rider = PrepareRepair(game); figure = (Transform)figures[rider];
            var badges = (IDictionary)Get(game, "destinationTags");
            Vector2 cursor = game.sceneView.cabinCamera.WorldToScreenPoint(((TextMesh)badges[rider]).transform.position);
            Call(game, "BeginPassengerDrag", rider, cursor);
            Require(ReferenceEquals(Get(game, "draggedRider"), rider), "The handyman could not be grabbed.");
            var stage = (Transform)Get(game, "stage");
            Vector3 grabTarget = stage.TransformPoint(new Vector3(0, .12f, 2f)) - (Vector3)Get(game, "dragOffset");
            Vector2 dropCursor = game.sceneView.cabinCamera.WorldToScreenPoint(grabTarget);
            Call(game, "UpdatePassengerDrag", dropCursor); Call(game, "FinishPassengerDrag", dropCursor);
            Require(rider.Boarded && (bool)Property(game, "HandymanRepairActive")
                && ((IDictionary)Get(game, "boardingTransfers")).Count == 0,
                "Drag boarding must start repair without another boarding transfer.");
            Call(game, "SyncFigures", 2f);
            Require(((Transform)Get(game, "repairShovel")).gameObject.activeSelf,
                "A dragged handyman never reached the shovel sweep.");
            Call(game, "QueueRiderExit", rider, figure.localPosition);
            RequireRepairCleared(game, "Unloading during repair");
            Require(!rider.Boarded && inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3
                && inside.starfish[0].localScale == Vector3.one,
                "Interrupted repair must restore damage and starfish poses.");

            rider = PrepareRepair(game);
            Call(game, "SelectRider", rider); Call(game, "ConfirmPassenger");
            Require((bool)Property(game, "HandymanRepairActive"), "Confirmed boarding did not restart repair.");
            Call(game, "SyncFigures", 1f); Call(game, "SyncFigures", 2f);
            Call(game, "Restart");
            RequireRepairCleared(game, "Restarting during repair");
            Require((float)Get(game, "elevatorSpeechTime") == 0f
                && inside.DisplayedSeverity == 0 && outside.DisplayedSeverity == 0,
                "Restart left repair dialogue or damaged starfish in the new shift.");
            File.WriteAllText(Work + "/result.txt", "PASS: Main; undamaged boarding and hover do not repair; confirmed and drag boarding start repair; boarding finishes first; doors stay open; authored wrench and shovel move; both starfish gauges sweep before clearing; passenger and tool poses restore; travel unlocks; unloading and restart clean up repair state.");
        }
        finally
        {
            Call(game, "Restart");
            game.enabled = true;
        }
    }

    static void CheckGrandmaSpeedStack(Game game, Round round)
    {
        var grandmas = round.Riders.FindAll(r => r.Kind == "ELDERLY");
        Require(grandmas.Count >= 4, "The round needs four Grandmas to check speed stacking.");
        float previousSpeed = (float)Property(game, "PassengerSpeedMultiplier");
        float firstSpeed = 0f;
        float thirdSpeed = 0f;
        for (int i = 0; i < 4; i++)
        {
            grandmas[i].Boarded = true;
            grandmas[i].BoardedBySeat = round.Seat;
            float speed = (float)Property(game, "PassengerSpeedMultiplier");
            Require(speed <= previousSpeed && speed >= game.minimumGrandmaSpeedMultiplier - .001f,
                "Grandma stacking must never speed up the elevator or exceed the slowdown limit.");
            if (i == 0) firstSpeed = speed;
            if (i == 2) thirdSpeed = speed;
            previousSpeed = speed;
        }
        Require(previousSpeed < firstSpeed
            && Mathf.Abs(firstSpeed - Mathf.Max(game.minimumGrandmaSpeedMultiplier, game.grandmaSpeedMultiplier)) < .001f
            && ((string)Property(game, "PassengerSpeedStatus")).Contains("GRANDMA x4"),
            "The first Grandma or four-Grandma status changed unexpectedly.");

        grandmas[3].BoardedBySeat = round.Seat + 1;
        float ownedSpeed = (float)Property(game, "PassengerSpeedMultiplier");
        Require(Mathf.Abs(ownedSpeed - thirdSpeed) < .001f,
            "The rival's Grandma must not slow this elevator.");
        grandmas[3].BoardedBySeat = round.Seat;
        var clown = round.Riders.Find(r => r.Kind == "GROUP");
        Require(clown != null, "The round needs a clown group to check combined speed.");
        clown.Boarded = true;
        clown.BoardedBySeat = round.Seat;
        Require(Mathf.Abs((float)Property(game, "PassengerSpeedMultiplier")
            - previousSpeed * game.clownSpeedMultiplier) < .001f,
            "The clown boost must still combine with the Grandma stack.");
    }

    static void RunBoost(Game game)
    {
        var pad = InputSystem.AddDevice<Gamepad>();
        var keyboard = InputSystem.AddDevice<Keyboard>();
        game.enabled = false;
        try
        {
            Call(game, "Restart");
            var round = (Round)Get(game, "round");
            var handyman = round.Riders.Find(r => r.Kind == "HANDYMAN" && r.Origin == 8);
            Require(handyman != null && round.Riders.FindAll(r => r.Kind == "HANDYMAN").Count == 4
                && round.Riders.TrueForAll(r => r.Kind != "HANDYMAN" || r.Origin >= 8),
                "Handymen must wait only on underwater floors.");
            float officeInitialSpeed = (float)Property(game, "CruiseSpeed");
            round.Floor = game.candyStartsAtFloor;
            float candyInitialSpeed = (float)Property(game, "CruiseSpeed");
            round.Floor = game.underwaterStartsAtFloor;
            float underwaterInitialSpeed = (float)Property(game, "CruiseSpeed");
            Require(underwaterInitialSpeed < officeInitialSpeed && officeInitialSpeed < candyInitialSpeed,
                "Initial speed must be slow underwater, medium in the office, and fast in candy.");
            round.Floor = 0;
            Call(game, "CloseAndTravel"); Call(game, "BeginBuildingTravel"); Set(game, "travelFloor", 2f);
            float cruise = (float)Property(game, "CruiseSpeed");
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - cruise) < .001f,
                "The elevator must depart immediately at normal cruise speed.");
            game.SetTravelInput(1f, true);
            Call(game, "AdvanceBuildingTravel", .15f); float first = (float)Get(game, "travelVelocity");
            Call(game, "AdvanceBuildingTravel", .15f); float second = (float)Get(game, "travelVelocity");
            Require(first > cruise && second > first, "Boost must accumulate velocity over time.");
            Call(game, "AdvanceBuildingTravel", 1.5f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - game.maximumTravelSpeed) < .002f, "Boost speed cap failed.");
            game.SetTravelInput(0f, false); float fast = (float)Get(game, "travelVelocity");
            Call(game, "AdvanceBuildingTravel", .15f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - fast) < .001f, "Boost momentum disappeared on release.");
            Set(game, "travelFloor", 3f); Set(game, "travelVelocity", 1.5f); Set(game, "travelDirection", 1);
            game.SetTravelInput(-1f, true);
            float reversed = (float)Get(game, "travelVelocity");
            Require(reversed <= -cruise + .001f, "Opposite input must immediately reverse at normal cruise speed.");
            Call(game, "AdvanceBuildingTravel", .2f);
            Require((float)Get(game, "travelVelocity") < reversed, "Boost must accelerate after the immediate reversal.");
            Set(game, "travelFloor", 2f); Set(game, "travelVelocity", cruise); Set(game, "travelDirection", 1);
            Input(game, pad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.LeftShoulder));
            Call(game, "AdvanceBuildingTravel", .2f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - cruise) < .001f, "Horizontal stick input must not add vertical boost.");
            Input(game, pad, new GamepadState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.W)); InputSystem.Update(); RefreshInputManager();
            Call(game, "HandleInteriorController", .02f); Call(game, "AdvanceBuildingTravel", .3f);
            Require((float)Get(game, "travelVelocity") > cruise + .25f, "Shift + keyboard direction did not accelerate.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); RefreshInputManager();

            // Same acceleration interval in one call and across 36 frames.
            Set(game, "travelFloor", 2f); Set(game, "travelVelocity", cruise); Set(game, "boostAxis", 1f); Set(game, "boostHeld", true);
            Call(game, "AdvanceBuildingTravel", .6f);
            float singlePosition = (float)Get(game, "travelFloor"), singleSpeed = (float)Get(game, "travelVelocity");
            Set(game, "travelFloor", 2f); Set(game, "travelVelocity", cruise);
            for (int i = 0; i < 36; i++) Call(game, "AdvanceBuildingTravel", 1f / 60);
            Require(Mathf.Abs((float)Get(game, "travelFloor") - singlePosition) < .002f && Mathf.Abs((float)Get(game, "travelVelocity") - singleSpeed) < .002f,
                "Boost behavior changed with frame rate.");
            float waterTravel = game.underwaterStartsAtFloor + .1f;
            Set(game, "travelFloor", waterTravel); Set(game, "rustExposure", 0f);
            float waterCruise = (float)Property(game, "CruiseSpeed"), waterAcceleration = (float)Property(game, "CurrentAcceleration");
            Require(waterCruise < cruise && waterAcceleration < game.boostAcceleration, "Underwater must reduce default speed and acceleration.");
            Set(game, "travelVelocity", waterCruise); Call(game, "AdvanceBuildingTravel", .5f);
            Require((float)Get(game, "travelVelocity") - waterCruise < game.boostAcceleration * .5f, "Underwater boost grew as fast as candy boost.");

            Phase(game, "Boarding"); round.Floor = game.underwaterStartsAtFloor; Set(game, "rustExposure", 0f); Call(game, "SyncFigures", 0f);
            Call(game, "UpdateInteriorPersona", 0f);
            var inside = (StarfishImpairmentView)Get(game, "interiorImpairment");
            var outside = (StarfishImpairmentView)Get(game, "exteriorImpairment");
            Require(inside && outside && inside.DisplayedSeverity == 1 && outside.DisplayedSeverity == 1, "Both views must show one starfish on entering water.");
            Call(game, "AccumulateRust", game.secondsPerRustLevel + .1f); Call(game, "UpdateInteriorPersona", 0f);
            Require(inside.DisplayedSeverity == 2 && outside.DisplayedSeverity == 2, "Both views must show medium impairment.");
            Call(game, "AccumulateRust", game.secondsPerRustLevel + .1f); Call(game, "UpdateInteriorPersona", 0f);
            Require(inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3 && (float)Property(game, "CruiseSpeed") < waterCruise
                && (float)Property(game, "CurrentAcceleration") < waterAcceleration, "Severe rust must show three starfish and impair performance further.");
            Require(!(bool)Property(game, "HasHandyman"), "Waiting handyman granted immunity before boarding.");
            Call(game, "SelectRider", handyman); Call(game, "ConfirmPassenger"); Call(game, "UpdateInteriorPersona", 0f);
            Require(handyman.Boarded && (bool)Property(game, "HasHandyman")
                && (bool)Property(game, "HandymanRepairPending")
                && inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3,
                "Boarding a handyman must preserve damage until the repair sweep.");
            float protectedWaterCruise = game.underwaterFloorsPerSecond;
            Require(Mathf.Abs((float)Property(game, "CruiseSpeed") - protectedWaterCruise) < .001f
                && Mathf.Abs((float)Property(game, "CurrentAcceleration") - game.boostAcceleration) < .001f
                && Mathf.Abs((float)Property(game, "SpeedLimit") - game.maximumTravelSpeed) < .001f,
                "Handyman must remove rust while keeping the underwater starting speed.");
            Call(game, "SyncFigures", 1.5f); Call(game, "SyncFigures", 4.3f);
            Require(inside.DisplayedSeverity == 0 && outside.DisplayedSeverity == 0
                && !(bool)Property(game, "HandymanRepairActive"),
                "Completed handyman repair did not clear both impairment meters.");
            Phase(game, "Moving");
            Set(game, "travelFloor", waterTravel); Set(game, "travelVelocity", protectedWaterCruise); Set(game, "boostHeld", true); Set(game, "boostAxis", 1f);
            float exposure = (float)Get(game, "rustExposure"); Call(game, "AdvanceBuildingTravel", .5f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - (protectedWaterCruise + game.boostAcceleration * .5f)) < .003f
                && (float)Get(game, "rustExposure") == exposure, "Handyman protection did not restore acceleration or prevent rust growth.");
            Set(game, "travelFloor", waterTravel); Call(game, "RequestFloorStop");
            Require((float)Get(game, "travelVelocity") == 0 && !(bool)Get(game, "boostHeld"), "Docking did not clear boost momentum.");
            Call(game, "AdvanceBuildingTravel", .5f); Phase(game, "Boarding");
            Call(game, "SelectRider", handyman); Call(game, "ConfirmPassenger"); Call(game, "UpdateInteriorPersona", 0f);
            Require(!handyman.Boarded && !(bool)Property(game, "HasHandyman") && inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3,
                "Handyman protection remained after unloading.");
            Call(game, "Restart");
            CheckGrandmaSpeedStack(game, (Round)Get(game, "round"));
            File.AppendAllText(Work + "/result.txt", "\nPASS: cumulative boost; speed cap; momentum retention; sharp opposite-direction reversal; horizontal/neutral boost ignored; Shift + keyboard boost; frame-rate independence; office/candy/underwater initial speeds; lower underwater speed and acceleration; matching 1/2/3 starfish meters; handyman spawned and visible; immunity only while aboard; underwater speed with full acceleration/cap restored; rust growth suppressed; unloading restores impairment; docking resets momentum; four-Grandma slowdown; seat isolation; clown combination.");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(pad);
            Call(game, "Restart"); game.enabled = true;
        }
    }

}
