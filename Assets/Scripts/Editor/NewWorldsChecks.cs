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
                solo.StartMatchShift();
                CheckWorld(solo, 3);
                CaptureIfRequested(solo, "forest");
                PlayerPrefs.SetInt(MaskKey, 1 << 4);
                solo.StartMatchShift();
                CheckWorld(solo, 4);
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
            PlayerPrefs.SetInt(MaskKey, 1 << 4);
            match.StartMatch();
            CheckWorld(match.player, 4);
            CheckWorld(match.Rival, 4);
            CheckSharedPool(match);
            Finish(true, "Solo and two-player Main: both worlds, authored backgrounds and floors, all five passenger prefabs, same-world destinations, shared queues, boarding and scoring.");
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
            game.StartMatchShift();
            ElevatorRound round = Round(game);
            Require(round.ActiveDimension == dimension && round.Floor == dimension * 8
                && round.Riders.Exists(rider => rider.Kind == kinds[dimension]),
                "An existing world lost its expected starting floor or passengers.");
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
        foreach (string kind in kinds)
        {
            for (int floor = min; floor <= min + 7; floor++)
            {
                SharedRider waiting = round.Riders.Find(r => r.Kind == kind && r.Origin == floor);
                Require(waiting != null && waiting.Destination >= min && waiting.Destination <= min + 7
                    && waiting.Destination != floor, "Missing or invalid " + kind + " on floor " + floor);
            }
            SharedRider rider = round.Riders.Find(r => r.Kind == kind && r.Origin == min);
            foreach (PassengerFeature feature in Enum.GetValues(typeof(PassengerFeature)))
                if (feature != PassengerFeature.None) Require(!rider.HasFeature(feature), kind + " has an unexpected ability.");
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
