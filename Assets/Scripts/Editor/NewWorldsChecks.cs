using System;
using System.IO;
using System.Reflection;
using CrazyElevator.Managers;
using CrazyElevator.Match;
using CrazyElevator.Shared;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using SharedRider = CrazyElevator.Shared.Rider;

// Exercises the actual Main scene and both elevator controllers in Play Mode.
[InitializeOnLoad]
public static class NewWorldsChecks
{
    const string StageKey = "CrazyElevator.NewWorldChecks.Stage";
    const string MaskKey = "CrazyElevator.DimensionPool.Mask";
    const string VersionKey = "CrazyElevator.DimensionPool.Version";
    const string Output = "Temp/NewWorldChecks/result.txt";
    const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static NewWorldsChecks() { EditorApplication.update += Poll; }

    [MenuItem("Tools/Crazy Elevator/Check Forest and Space Worlds")]
    public static void Request()
    {
        Directory.CreateDirectory("Temp/NewWorldChecks");
        SessionState.SetBool("NewWorlds.HadMask", PlayerPrefs.HasKey(MaskKey));
        SessionState.SetInt("NewWorlds.Mask", PlayerPrefs.GetInt(MaskKey, 0));
        SessionState.SetBool("NewWorlds.HadVersion", PlayerPrefs.HasKey(VersionKey));
        SessionState.SetInt("NewWorlds.Version", PlayerPrefs.GetInt(VersionKey, 0));
        SessionState.SetString("NewWorlds.Started", DateTime.UtcNow.Ticks.ToString());
        PlayerPrefs.SetInt(VersionKey, 1);
        PlayerPrefs.SetInt(MaskKey, 1 << 3);
        PlayerPrefs.Save();
        SessionState.SetInt(StageKey, 1);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.isPlaying = true;
    }

    static void Poll()
    {
        int stage = SessionState.GetInt(StageKey, 0);
        if (stage == 0) return;
        if (long.TryParse(SessionState.GetString("NewWorlds.Started", "0"), out long ticks)
            && ticks > 0 && DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) > TimeSpan.FromSeconds(75))
        {
            Finish(false, "Timed out waiting for Play Mode.");
            return;
        }
        if (!EditorApplication.isPlaying || Time.frameCount < 5) return;
        try
        {
            if (stage == 1)
            {
                ElevatorManager solo = FindSeat(0);
                if (solo == null || solo.sceneView == null) return;
                CheckExistingWorlds(solo);
                PlayerPrefs.SetInt(MaskKey, 1 << 3);
                GameManager.Instance.ResetMatchProgress();
                solo.StartMatchShift();
                CheckWorld(solo, 3);
                CheckWorldEvent(solo, 3);
                CaptureIfRequested(solo, "forest");
                PlayerPrefs.SetInt(MaskKey, 1 << 4);
                GameManager.Instance.ResetMatchProgress();
                solo.StartMatchShift();
                CheckWorld(solo, 4);
                CheckWorldEvent(solo, 4);
                CaptureIfRequested(solo, "space");

                PlayerPrefs.SetInt(MaskKey, 1 << 3);
                typeof(MenuManager).GetField("startDuelOnLoad", PrivateStatic).SetValue(null, true);
                typeof(MenuManager).GetField("playerTwoHumanOnLoad", PrivateStatic).SetValue(null, true);
                typeof(MenuManager).GetField("playerOneCpuOnLoad", PrivateStatic).SetValue(null, false);
                typeof(MenuManager).GetField("tutorialOnLoad", PrivateStatic).SetValue(null, false);
                MenuManager.Close();
                SessionState.SetInt(StageKey, 2);
                SessionState.SetInt("NewWorlds.LoadFrame", Time.frameCount);
                SceneManager.LoadScene("Assets/Scenes/Main.unity");
                return;
            }

            if (Time.frameCount < SessionState.GetInt("NewWorlds.LoadFrame", 0) + 5) return;
            ElevatorMatch match = Object.FindFirstObjectByType<ElevatorMatch>();
            if (match == null || match.Rival == null) return;
            match.StartMatch();
            CheckWorld(match.player, 3);
            CheckWorld(match.Rival, 3);
            CheckSharedPool(match);
            CheckDuelWorldEvent(match, 3);
            PlayerPrefs.SetInt(MaskKey, 1 << 4);
            match.StartMatch();
            CheckWorld(match.player, 4);
            CheckWorld(match.Rival, 4);
            CheckSharedPool(match);
            CheckDuelWorldEvent(match, 4);
            Finish(true, "Solo and two-player Main: both worlds, event prefabs, all five world events and passenger protections, UFO beam relocation, passenger models, shared queues, boarding and scoring.");
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    static ElevatorManager FindSeat(int seat)
    {
        foreach (ElevatorManager game in Object.FindObjectsByType<ElevatorManager>(FindObjectsSortMode.None))
            if (game.Seat == seat) return game;
        return null;
    }

    static ElevatorRound Round(ElevatorManager game)
        => (ElevatorRound)typeof(ElevatorManager).GetField("round", PrivateInstance).GetValue(game);

    static void CheckExistingWorlds(ElevatorManager game)
    {
        string[] kinds = { "COURIER", "PREGNANT", "HANDYMAN" };
        for (int dimension = 0; dimension < 3; dimension++)
        {
            PlayerPrefs.SetInt(MaskKey, 1 << dimension);
            GameManager.Instance.ResetMatchProgress();
            game.StartMatchShift();
            ElevatorRound round = Round(game);
            Require(round.ActiveDimension == dimension && round.Floor == dimension * 8
                && round.Riders.Exists(rider => rider.Kind == kinds[dimension]),
                "An existing world lost its expected starting floor or passengers.");
            CheckWorldEvent(game, dimension);
            if (dimension == 0 || dimension == 2) CheckShaftHazard(game, dimension);
            bool inWater = (bool)typeof(ElevatorManager).GetProperty("InWater", PrivateInstance).GetValue(game);
            Require(inWater == (dimension == 2), "Rust applies in the wrong world.");
        }
    }

    static void CheckWorld(ElevatorManager game, int dimension)
    {
        ElevatorRound round = Round(game);
        Require(round != null && round.ActiveDimension == dimension, "Wrong active world for seat " + game.Seat);
        int min = dimension * ElevatorRound.FloorsPerDimension;
        Require(round.MinFloor == min && round.MaxFloor == min + 7 && round.Floor == min,
            "Wrong floor range in world " + dimension);
        string[] kinds = dimension == 3 ? new[] { "FAIRY", "GOBLIN", "MUSHROOM" }
            : new[] { "ASTRONAUT", "ALIEN" };
        Require(game.sceneView.forestVineSweep != null && game.sceneView.spaceGravitySweep != null,
            "Forest or Space event prefab is not assigned in ElevatorScene.");
        foreach (string kind in kinds)
        {
            for (int floor = min; floor <= min + 7; floor++)
            {
                SharedRider waiting = round.Riders.Find(r => r.Kind == kind && r.Origin == floor);
                Require(waiting != null && waiting.Destination >= min && waiting.Destination <= min + 7
                    && waiting.Destination != floor, "Missing or invalid " + kind + " on floor " + floor);
            }
            SharedRider rider = round.Riders.Find(r => r.Kind == kind && r.Origin == min);
            PassengerFeature expectedFeature = kind == "FAIRY" ? PassengerFeature.ForestScout
                : kind == "GOBLIN" ? PassengerFeature.ForestShortcut
                : kind == "MUSHROOM" ? PassengerFeature.ForestSporeGuard
                : kind == "ASTRONAUT" ? PassengerFeature.GravityAnchor : PassengerFeature.UfoLink;
            Require(rider.HasFeature(expectedFeature), kind + " is missing its world ability.");
            foreach (PassengerFeature feature in Enum.GetValues(typeof(PassengerFeature)))
                if (feature != PassengerFeature.None && feature != expectedFeature)
                    Require(!rider.HasFeature(feature), kind + " has an unexpected ability.");
            var spawn = typeof(ElevatorManager).GetMethod("SpawnPassengerArt", PrivateInstance);
            PassengerView view = (PassengerView)spawn.Invoke(game, new object[] { rider, game.sceneView.exteriorCar.parent });
            Require(view != null && view.ResolvedKind == kind && view.destination != null
                && view.GetComponentsInChildren<Renderer>(true).Length > 0, "Could not spawn " + kind + " art.");
            Object.Destroy(view.gameObject);
        }
        Require(!round.Riders.Exists(r => r.Kind == "HANDYMAN"), "Underwater handyman leaked into the new world.");
        Require(!(bool)typeof(ElevatorManager).GetProperty("InWater", PrivateInstance).GetValue(game),
            "The new world incorrectly uses underwater rust.");
        Transform world = game.sceneView.exteriorCar.parent;
        Transform backgrounds = world.Find("World Backdrops");
        Require(backgrounds != null && backgrounds.Find(dimension == 3 ? "Forest Backdrop" : "Space Backdrop") != null,
            "World backdrop missing from Main.");
        Require(backgrounds.Find(dimension == 3 ? "Forest Backdrop" : "Space Backdrop").gameObject.activeSelf,
            "Active world backdrop is hidden.");
        foreach (int floor in new[] { min, min + 7 })
        {
            Require(world.Find("World landing " + floor) != null && world.Find("Shaft landing " + floor) != null
                && world.Find("Sign • " + floor) != null && world.Find("Floor Rails/Floor rail " + floor) != null,
                "Missing a playable landing, sign or rail on floor " + floor);
        }
        if (dimension == 4) CheckUfoPassengerRelocation();
    }

    static object Get(ElevatorManager game, string name)
        => typeof(ElevatorManager).GetField(name, PrivateInstance).GetValue(game);

    static void Set(ElevatorManager game, string name, object value)
        => typeof(ElevatorManager).GetField(name, PrivateInstance).SetValue(game, value);

    static void Call(ElevatorManager game, string name, params object[] args)
        => typeof(ElevatorManager).GetMethod(name, PrivateInstance).Invoke(game, args);

    static void CheckWorldEvent(ElevatorManager game, int dimension)
    {
        ElevatorRound round = Round(game);
        var phase = typeof(ElevatorManager).GetField("phase", PrivateInstance);
        phase.SetValue(game, Enum.Parse(phase.FieldType, "Moving"));
        Set(game, "travelFloor", (float)(round.MinFloor + 1));
        Set(game, "travelVelocity", 0f);
        round.TimeLeft = ElevatorRound.Duration - 20f;
        Call(game, "UpdateShaftHazards", .1f);
        var eventView = (GameObject)Get(game, "worldEventView");
        Require(eventView != null && eventView.activeSelf, "World event failed to spawn in dimension " + dimension);
        int eventId = (int)Get(game, "worldEventId");
        Require(eventId > 0, "World event did not start.");

        // Advance through the warning and target the event's actual collision position.
        float start = (float)Get(game, "worldEventStart");
        round.TimeLeft = ElevatorRound.Duration - start - 5f;
        Call(game, "UpdateShaftHazards", .1f);
        if (dimension == 4)
        {
            var moved = (SharedRider)Get(game, "worldEventPassenger");
            Require(moved != null && (bool)Get(game, "worldEventPassengerMoved")
                && round.IsQueuedAtStop(moved), "UFO beam did not move a waiting passenger.");
        }

        Set(game, "travelFloor", (float)Get(game, "worldEventFloor"));
        Set(game, "routeLateralReady", true);
        Set(game, "routeLateralX", (float)Get(game, "worldEventX"));
        Set(game, "worldEventSeen", 0);
        Set(game, "railRiding", false);
        Call(game, "CheckWorldEventHit");
        Require((int)Get(game, "worldEventSeen") == eventId,
            "The world event did not detect contact in dimension " + dimension);
        if (dimension == 0) Require((float)Get(game, "worldScanTime") > 0f, "Scanner did not slow the car.");
        if (dimension == 1) Require((float)Get(game, "worldSyrupTime") > 0f, "Syrup did not slow the car.");
        if (dimension >= 2) Require((bool)Get(game, "railRiding"), "World event did not push the car.");

        string[] protections = dimension == 2 ? new[] { "DIVER" }
            : dimension == 3 ? new[] { "MUSHROOM", "GOBLIN" }
            : dimension == 4 ? new[] { "ASTRONAUT", "ALIEN" } : Array.Empty<string>();
        foreach (string kind in protections)
        {
            SharedRider rider = round.Riders.Find(r => r.Kind == kind && !r.Resolved);
            Require(rider != null, "Missing " + kind + " for world event check.");
            rider.Boarded = true;
            rider.BoardedBySeat = game.Seat;
            Set(game, "worldEventSeen", 0);
            Set(game, "railRiding", false);
            Call(game, "CheckWorldEventHit");
            Require((int)Get(game, "worldEventSeen") == eventId, kind + " failed to detect the world event.");
            bool shouldRide = kind == "GOBLIN" || kind == "ALIEN";
            Require((bool)Get(game, "railRiding") == shouldRide,
                kind + " did not apply its protection or shortcut.");
            rider.Boarded = false;
            rider.BoardedBySeat = -1;
        }
    }

    static void CheckShaftHazard(ElevatorManager game, int dimension)
    {
        var hazards = (System.Collections.IEnumerable)Get(game, "hazards");
        object chosen = null;
        string kind = dimension == 0 ? "Monster" : "Tornado";
        foreach (object hazard in hazards)
        {
            Type type = hazard.GetType();
            type.GetField("Live").SetValue(hazard, false);
            object spec = type.GetField("Spec").GetValue(hazard);
            float hazardFloor = (float)spec.GetType().GetField("Floor").GetValue(spec);
            if (chosen == null && spec.GetType().GetField("Kind").GetValue(spec).ToString() == kind
                && hazardFloor >= Round(game).MinFloor && hazardFloor <= Round(game).MaxFloor)
                chosen = hazard;
        }
        Require(chosen != null, kind + " prefab did not spawn.");
        Type hazardType = chosen.GetType();
        object selectedSpec = hazardType.GetField("Spec").GetValue(chosen);
        float floor = (float)selectedSpec.GetType().GetField("Floor").GetValue(selectedSpec);
        float previousFloor = (float)Get(game, "travelFloor");
        hazardType.GetField("Live").SetValue(chosen, true);
        hazardType.GetField("Stunned").SetValue(chosen, 0f);
        hazardType.GetField("X").SetValue(chosen, 0f);
        Set(game, "travelFloor", floor);
        Set(game, "routeLateralReady", true);
        Set(game, "routeLateralX", 0f);
        Vector3 collisionOffset = (Vector3)Get(game, "exteriorCollisionCenterOffset");
        Set(game, "exteriorCollisionCenterOffset", Vector3.zero);
        Set(game, "hazardCooldown", 0f);
        Set(game, "carSpin", 0f);
        Call(game, "CheckHazardHits");
        Require((float)Get(game, "hazardCooldown") > 0f, kind + " did not detect the elevator.");
        if (dimension == 2) Require((float)Get(game, "carSpin") > 0f, "Tornado did not spin the elevator.");

        SharedRider protector = Round(game).Riders.Find(r => r.Kind == (dimension == 0 ? "HUNTER" : "DIVER"));
        Require(protector != null, "Missing passenger protection for " + kind);
        protector.Boarded = true;
        protector.BoardedBySeat = game.Seat;
        Set(game, "hazardCooldown", 0f);
        Set(game, "carSpin", 0f);
        Call(game, "CheckHazardHits");
        if (dimension == 0)
            Require((float)hazardType.GetField("Stunned").GetValue(chosen) > 0f, "Hunter did not stun the monster.");
        else
            Require((float)Get(game, "hazardCooldown") > 0f && (float)Get(game, "carSpin") == 0f,
                "Diver did not block the tornado.");
        protector.Boarded = false;
        protector.BoardedBySeat = -1;
        Set(game, "travelFloor", previousFloor);
        Set(game, "exteriorCollisionCenterOffset", collisionOffset);
    }

    static void CheckDuelWorldEvent(ElevatorMatch match, int dimension)
    {
        CheckWorldEvent(match.player, dimension);
        ElevatorManager owner = match.player, rival = match.Rival;
        var phase = typeof(ElevatorManager).GetField("phase", PrivateInstance);
        phase.SetValue(rival, Enum.Parse(phase.FieldType, "Moving"));
        Round(rival).TimeLeft = Round(owner).TimeLeft;
        Set(rival, "travelFloor", (float)Get(owner, "worldEventFloor"));
        Set(rival, "routeLateralReady", true);
        Set(rival, "routeLateralX", (float)Get(owner, "worldEventX"));
        Set(rival, "worldEventSeen", 0);
        Set(rival, "railRiding", false);
        Call(rival, "CheckWorldEventHit");
        Require((int)Get(rival, "worldEventSeen") == (int)Get(owner, "worldEventId")
            && (bool)Get(rival, "railRiding"),
            "The rival did not react independently to the shared world event in dimension " + dimension);
    }

    static void CheckUfoPassengerRelocation()
    {
        ElevatorRound round = new ElevatorRound();
        round.ConfigureDimension(4);
        SharedRider rider = round.Riders.Find(r => r.Kind == "ALIEN" && r.Origin == round.MinFloor);
        Require(rider != null, "Space world has no Alien for the UFO relocation check.");
        int oldFloor = rider.Origin;
        int newFloor = oldFloor + 2;
        round.RevealQueue(oldFloor, rider.OriginStop);
        Require(round.IsQueuedAtStop(rider), "Alien is not in its original revealed passenger queue.");
        Require(round.TryRelocateWaitingPassenger(rider, newFloor), "UFO could not move a waiting passenger.");
        Require(rider.Origin == newFloor && rider.Destination >= round.MinFloor
            && rider.Destination <= round.MaxFloor && rider.Destination != rider.Origin
            && round.IsQueuedAtStop(rider), "UFO relocation left the passenger at an invalid or hidden stop.");
    }

    static void CheckSharedPool(ElevatorMatch match)
    {
        ElevatorRound first = Round(match.player), second = Round(match.Rival);
        SharedRider rider = first.Riders.Find(r => r.Origin == first.MinFloor);
        Require(rider != null && second.Riders.Contains(rider), "Players do not share the same passenger.");
        first.Arrive(rider.Origin, rider.OriginStop);
        second.Arrive(rider.Origin, rider.OriginStop);
        first.RevealQueue(rider.Origin, rider.OriginStop);
        rider = first.Riders.Find(first.IsOffered);
        Require(rider != null && first.Board(rider) && !second.Board(rider), "Shared passenger boarding failed.");
        first.Arrive(rider.Destination, rider.DestinationStop);
        Require(first.Offboard(rider) != OffboardResult.None && first.Score > 0 && second.Score == 0,
            "Passenger delivery did not score for the correct player.");
    }

    static void CaptureIfRequested(ElevatorManager game, string name)
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-crazyCaptureWorlds") < 0
            || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        typeof(ElevatorManager).GetMethod("UpdateShaftCrowd", PrivateInstance).Invoke(game, new object[] { 0f });
        typeof(ElevatorManager).GetMethod("LateUpdate", PrivateInstance).Invoke(game, null);
        Camera camera = game.sceneView.exteriorCamera;
        var texture = new RenderTexture(1280, 720, 24);
        RenderTexture previous = RenderTexture.active;
        RenderTexture originalTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            image.Apply();
            File.WriteAllBytes("/tmp/crazy-world-" + name + ".png", image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        finally
        {
            camera.targetTexture = originalTarget;
            RenderTexture.active = previous;
            texture.Release();
            Object.DestroyImmediate(texture);
        }
    }

    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    static void Finish(bool success, string message)
    {
        if (SessionState.GetBool("NewWorlds.HadMask", false)) PlayerPrefs.SetInt(MaskKey, SessionState.GetInt("NewWorlds.Mask", 0));
        else PlayerPrefs.DeleteKey(MaskKey);
        if (SessionState.GetBool("NewWorlds.HadVersion", false)) PlayerPrefs.SetInt(VersionKey, SessionState.GetInt("NewWorlds.Version", 0));
        else PlayerPrefs.DeleteKey(VersionKey);
        PlayerPrefs.Save();
        SessionState.SetInt(StageKey, 0);
        Directory.CreateDirectory("Temp/NewWorldChecks");
        File.WriteAllText(Output, (success ? "PASS: " : "FAIL: ") + message);
        if (success) Debug.Log("New world checks passed: " + message);
        else Debug.LogError("New world checks failed: " + message);
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
    }
}
