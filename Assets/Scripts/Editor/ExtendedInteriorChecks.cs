using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Game = CrazyElevator.Managers.ElevatorManager;
using Round = CrazyElevator.Shared.ElevatorRound;

// Explicitly requested checks only. Never opens, saves, or rebuilds another scene.
[InitializeOnLoad]
public static class ExtendedInteriorChecks
{
    const string Work = "Temp/ExtendedInterior";
    const string Scene = "Assets/Scenes/Main.unity";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static ExtendedInteriorChecks() { EditorApplication.update += Poll; }
    static object Get(Game game, string name) => typeof(Game).GetField(name, Flags).GetValue(game);
    static void Set(Game game, string name, object value) => typeof(Game).GetField(name, Flags).SetValue(game, value);
    static void Call(Game game, string name, params object[] args) => typeof(Game).GetMethod(name, Flags).Invoke(game, args);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string request = Work + "/request.txt";
        if (!File.Exists(request)) return;
        string action = File.ReadAllText(request).Trim();
        if (!EditorApplication.isPlaying)
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != Scene) return;
            EditorApplication.isPlaying = true;
            return;
        }
        var game = UnityEngine.Object.FindAnyObjectByType<Game>();
        if (!game || game.gameObject.scene.path != Scene || Get(game, "round") == null) return;
        File.Delete(request);
        try
        {
            if (action == "checks") { Run(game); RunBoost(game); }
            else if (action == "rust-inside" || action == "rust-outside")
            {
                game.enabled = true; Call(game, "Restart");
                ((Round)Get(game, "round")).Floor = 8;
                Set(game, "rustExposure", game.secondsPerRustLevel * 2);
                game.sceneView.floorDisplay.text = "8";
                Call(game, "SyncFigures", 0f); Call(game, "UpdateInteriorPersona", 0f);
                if (action == "rust-outside")
                {
                    Call(game, "CloseAndTravel"); Call(game, "SetDoors", 0f); Call(game, "BeginBuildingTravel");
                    Set(game, "travelFloor", 9.1f); Set(game, "travelVelocity", 0f);
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
                ((Round)Get(game, "round")).Floor = water ? 8 : 0;
                game.sceneView.floorDisplay.text = water ? "8" : "0";
                Call(game, "SyncFigures", 0f); Call(game, "UpdateInteriorPersona", 0f);
                if (action.EndsWith("-transfer"))
                {
                    Call(game, "SelectRider", ((Round)Get(game, "round")).Riders.Find(r => r.Origin == (water ? 8 : 0) && r.Kind == "COURIER"));
                    Call(game, "ConfirmPassenger");
                    Call(game, "SyncFigures", water ? .3f : .6f);
                    game.enabled = false;
                }
            }
            else throw new Exception("Unknown interior check request: " + action);
        }
        catch (Exception e) { File.WriteAllText(Work + "/result.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Crazy Elevator/Check Extended Interior (Play Mode)")]
    static void RequestChecks()
    {
        Directory.CreateDirectory(Work); File.WriteAllText(Work + "/request.txt", "checks");
    }

    static void Input(Game game, Gamepad pad, GamepadState state, float dt = .02f)
    {
        InputSystem.QueueStateEvent(pad, state);
        InputSystem.Update();
        Call(game, "UpdatePassengerSelection");
        Call(game, "HandleInteriorController", dt);
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
        Require(Get(game, "phase").ToString() == "Moving" && game.sceneView.exteriorCamera.enabled && !game.sceneView.cabinCamera.enabled,
            "Closed doors must switch to full-screen building travel.");
    }

    static void Run(Game game)
    {
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
            Require(round.Delivered == 1 && ((IDictionary)Get(game, "exiting")).Count == 1, "Selected passenger did not unload.");
            Call(game, "SyncFigures", 1.5f);
            Require(round.Score == score && ((IDictionary)Get(game, "exiting")).Count == 0, "Unload scored twice or failed to finish.");
            Input(game, pad, new GamepadState()); Close(game);
            Call(game, "AdvanceBuildingTravel", 100f);
            Require((float)Get(game, "travelFloor") == 11 && Get(game, "phase").ToString() == "Moving", "Top boundary opened automatically or exceeded the shaft.");
            Input(game, pad, new GamepadState { leftStick = Vector2.down });
            TravelTo(game, 8.1f); Call(game, "RequestFloorStop"); FinishDock(game);
            Require(round.Floor == 8 && rig.waterBackdrop.activeSelf, "Upper floor did not become underwater.");
            for (int i = 0; i < 4; i++) Input(game, pad, new GamepadState().WithButton(GamepadButton.RightShoulder), .5f);
            Require((float)Get(game, "repairedFor") > 19, "Repair no longer works.");
            Input(game, pad, new GamepadState()); Close(game);
            TravelTo(game, 7.1f); Call(game, "RequestFloorStop"); FinishDock(game);
            Require(round.Floor == 7 && rig.candyBackdrop.activeSelf, "Lower floor did not restore candy.");
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
            File.WriteAllText(Work + "/result.txt", "PASS: exact hover selection/highlight; click versus drag handling; drag-to-board; moving away clears highlight; joystick selection and confirm remain available; safe doors; interior while closing; full-screen building during travel; no automatic floor stops; stop rejected between floors; confirm docks at nearby floor; doors open before interior return; pause preserves travel view; top/bottom bounds remain closed; up/down reversal; candy below/water above; repair; single scoring.");
        }
        finally
        {
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(pad);
            Call(game, "Restart"); game.enabled = true;
        }
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
            Require(handyman != null && round.Riders.FindAll(r => r.Kind == "HANDYMAN").Count == 12, "Missing handyman passengers.");
            Call(game, "CloseAndTravel"); Call(game, "BeginBuildingTravel"); Set(game, "travelFloor", 2f);
            float cruise = (float)Property(game, "CruiseSpeed");
            Input(game, pad, new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.LeftShoulder));
            Call(game, "AdvanceBuildingTravel", .4f); float first = (float)Get(game, "travelVelocity");
            Call(game, "AdvanceBuildingTravel", .4f); float second = (float)Get(game, "travelVelocity");
            Require(first > cruise && second > first, "Boost must accumulate velocity over time.");
            Call(game, "AdvanceBuildingTravel", .8f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - game.maximumTravelSpeed) < .002f, "Boost speed cap failed.");
            Input(game, pad, new GamepadState()); float fast = (float)Get(game, "travelVelocity");
            Call(game, "AdvanceBuildingTravel", .15f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - fast) < .001f, "Boost momentum disappeared on release.");
            Set(game, "travelFloor", 3f); Set(game, "travelVelocity", 1.5f);
            Input(game, pad, new GamepadState { leftStick = Vector2.down }.WithButton(GamepadButton.LeftShoulder));
            Call(game, "AdvanceBuildingTravel", .2f);
            Require((float)Get(game, "travelVelocity") > 0 && (float)Get(game, "travelVelocity") < 1.5f, "Opposite boost should first brake, not instantly flip velocity.");
            Call(game, "AdvanceBuildingTravel", 1.5f);
            Require((float)Get(game, "travelVelocity") < 0, "Sustained opposite boost did not reverse.");
            Set(game, "travelFloor", 2f); Set(game, "travelVelocity", cruise); Set(game, "travelDirection", 1);
            Input(game, pad, new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.LeftShoulder));
            Call(game, "AdvanceBuildingTravel", .2f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - cruise) < .001f, "Horizontal stick input must not add vertical boost.");
            Input(game, pad, new GamepadState());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.W)); InputSystem.Update();
            Call(game, "HandleInteriorController", .02f); Call(game, "AdvanceBuildingTravel", .3f);
            Require((float)Get(game, "travelVelocity") > cruise + .25f, "Shift + keyboard direction did not accelerate.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();

            // Same acceleration interval in one call and across 36 frames.
            Set(game, "travelFloor", 2f); Set(game, "travelVelocity", cruise); Set(game, "boostAxis", 1f); Set(game, "boostHeld", true);
            Call(game, "AdvanceBuildingTravel", .6f);
            float singlePosition = (float)Get(game, "travelFloor"), singleSpeed = (float)Get(game, "travelVelocity");
            Set(game, "travelFloor", 2f); Set(game, "travelVelocity", cruise);
            for (int i = 0; i < 36; i++) Call(game, "AdvanceBuildingTravel", 1f / 60);
            Require(Mathf.Abs((float)Get(game, "travelFloor") - singlePosition) < .002f && Mathf.Abs((float)Get(game, "travelVelocity") - singleSpeed) < .002f,
                "Boost behavior changed with frame rate.");
            Set(game, "travelFloor", 8.1f); Set(game, "rustExposure", 0f);
            float waterCruise = (float)Property(game, "CruiseSpeed"), waterAcceleration = (float)Property(game, "CurrentAcceleration");
            Require(waterCruise < cruise && waterAcceleration < game.boostAcceleration, "Underwater must reduce default speed and acceleration.");
            Set(game, "travelVelocity", waterCruise); Call(game, "AdvanceBuildingTravel", .5f);
            Require((float)Get(game, "travelVelocity") - waterCruise < game.boostAcceleration * .5f, "Underwater boost grew as fast as candy boost.");

            Phase(game, "Boarding"); round.Floor = 8; Set(game, "rustExposure", 0f); Call(game, "SyncFigures", 0f);
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
            Require(handyman.Boarded && (bool)Property(game, "HasHandyman") && inside.DisplayedSeverity == 0 && outside.DisplayedSeverity == 0,
                "Boarding a handyman did not clear both impairment meters.");
            Require(Mathf.Abs((float)Property(game, "CruiseSpeed") - cruise) < .001f
                && Mathf.Abs((float)Property(game, "CurrentAcceleration") - game.boostAcceleration) < .001f
                && Mathf.Abs((float)Property(game, "SpeedLimit") - game.maximumTravelSpeed) < .001f,
                "Handyman must restore the exact candy movement settings.");
            Call(game, "SyncFigures", 1.5f); Phase(game, "Moving");
            Set(game, "travelFloor", 8.1f); Set(game, "travelVelocity", cruise); Set(game, "boostHeld", true); Set(game, "boostAxis", 1f);
            float exposure = (float)Get(game, "rustExposure"); Call(game, "AdvanceBuildingTravel", .5f);
            Require(Mathf.Abs((float)Get(game, "travelVelocity") - (cruise + game.boostAcceleration * .5f)) < .003f
                && (float)Get(game, "rustExposure") == exposure, "Handyman protection did not restore acceleration or prevent rust growth.");
            Set(game, "travelFloor", 8.1f); Call(game, "RequestFloorStop");
            Require((float)Get(game, "travelVelocity") == 0 && !(bool)Get(game, "boostHeld"), "Docking did not clear boost momentum.");
            Call(game, "AdvanceBuildingTravel", .5f); Phase(game, "Boarding");
            Call(game, "SelectRider", handyman); Call(game, "ConfirmPassenger"); Call(game, "UpdateInteriorPersona", 0f);
            Require(!handyman.Boarded && !(bool)Property(game, "HasHandyman") && inside.DisplayedSeverity == 3 && outside.DisplayedSeverity == 3,
                "Handyman protection remained after unloading.");
            File.AppendAllText(Work + "/result.txt", "\nPASS: cumulative boost; speed cap; momentum retention; opposite-direction braking/reversal; horizontal/neutral boost ignored; Shift + keyboard boost; frame-rate independence; lower underwater speed and acceleration; matching 1/2/3 starfish meters; handyman spawned and visible; immunity only while aboard; exact candy speed/acceleration/cap restored; rust growth suppressed; unloading restores impairment; docking resets momentum.");
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(pad);
            Call(game, "Restart"); game.enabled = true;
        }
    }

}
