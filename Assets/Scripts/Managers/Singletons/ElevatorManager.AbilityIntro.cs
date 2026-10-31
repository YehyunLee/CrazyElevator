using CrazyElevator.Shared;
using UnityEngine;

namespace CrazyElevator.Managers
{
    public sealed partial class ElevatorManager
    {
        const float AbilityIntroDuration = 3.5f;
        Transform abilityIntroPassenger, abilityIntroOpponent, abilityIntroOwnCar;
        string abilityIntroKind;
        Texture2D abilityIntroShield;

        // Reuse the existing offscreen preview camera and the editable game
        // prefabs, so this animation never touches the live car or passengers.
        public bool PrepareAbilityIntro(string kind)
        {
            if (sceneView == null || sceneView.passengers == null) return false;
            string passengerKind = kind == "CALM" ? "ELDERLY" : kind;
            GameObject passenger = FindPassengerTemplate(passengerKind);
            GameObject opponent = kind == "HUNTER" ? sceneView.monster
                : kind == "DIVER" ? sceneView.tornado
                : kind == "HANDYMAN" && impairmentGaugePrefab != null ? impairmentGaugePrefab.gameObject
                : kind == "CALM" ? FindPassengerTemplate("PREGNANT")
                : kind == "PREGNANT" ? FindPassengerTemplate("BOSS")
                : (kind == "BOUNCER" || kind == "GROUP" || kind == "ELDERLY")
                    && sceneView.exteriorCar != null ? sceneView.exteriorCar.gameObject : null;
            if (passenger == null || opponent == null) return false;

            ClearEncounterPreviewModel();
            EnsureEncounterPreview();
            encounterPreviewModel = new GameObject("Passenger ability introduction");
            encounterPreviewModel.hideFlags = HideFlags.HideAndDontSave;
            encounterPreviewModel.transform.SetParent(encounterPreviewPivot, false);
            encounterPreviewPivot.localRotation = Quaternion.identity;
            abilityIntroPassenger = CreateAbilityIntroModel(passenger, "Passenger", 2.3f);
            abilityIntroOpponent = CreateAbilityIntroModel(opponent, "Opponent", kind == "BOUNCER" ? 2.5f : 2.3f);
            if (kind == "BOUNCER")
                abilityIntroOwnCar = CreateAbilityIntroModel(sceneView.exteriorCar.gameObject, "Your elevator", 2.5f);
            if (abilityIntroPassenger == null || abilityIntroOpponent == null)
            {
                FinishAbilityIntro();
                return false;
            }
            abilityIntroKind = kind;
            if (kind == "DIVER" && abilityIntroShield == null)
                abilityIntroShield = PassengerIconLibrary.CreatePassengerBadge("DIVER", Teal, Ink, Cream);
            encounterPreviewCamera.backgroundColor = kind == "HUNTER" || kind == "ELDERLY" ? new Color(.12f, .17f, .26f)
                : kind == "DIVER" || kind == "HANDYMAN" ? new Color(.04f, .24f, .30f) : new Color(.30f, .16f, .27f);
            encounterPreviewCamera.nearClipPlane = .03f;
            encounterPreviewCamera.farClipPlane = 80f;
            encounterPreviewCamera.enabled = true;
            AnimateAbilityIntro(0f);
            return true;
        }

        Transform CreateAbilityIntroModel(GameObject source, string label, float height)
        {
            var slot = new GameObject(label + " ability animation");
            slot.hideFlags = HideFlags.HideAndDontSave;
            slot.transform.SetParent(encounterPreviewModel.transform, false);
            var model = Instantiate(source, slot.transform, false);
            model.name = label + " ability preview";
            model.SetActive(true);
            var stars = model.GetComponent<StarfishImpairmentView>();
            if (stars != null)
            {
                stars.SetSeverity(3);
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            MakeStillPreviewModel(model, Teal);
            if (!TryGetEncounterPreviewBounds(model, out Bounds bounds)) return null;
            float size = Mathf.Max(bounds.size.y, Mathf.Max(bounds.size.x * .75f, bounds.size.z * .75f));
            model.transform.localScale *= height / Mathf.Max(.01f, size);
            if (!TryGetEncounterPreviewBounds(model, out bounds)) return null;
            model.transform.localPosition -= slot.transform.InverseTransformPoint(bounds.center);
            return slot.transform;
        }

        public void AnimateAbilityIntro(float time)
        {
            if (abilityIntroPassenger == null || abilityIntroOpponent == null || encounterPreviewCamera == null) return;
            float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.75f, 1.5f, time));
            float impact = Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(1.5f, 2.15f, time)) * Mathf.PI);
            float recoil = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.55f, 2.2f, time));
            abilityIntroPassenger.localPosition = new Vector3(-1.25f + impact * .32f, impact * .10f,
                abilityIntroOwnCar != null ? -.8f : 0f);
            abilityIntroPassenger.localRotation = Quaternion.Euler(0f, 10f + impact * 18f, -impact * 8f);
            if (abilityIntroOwnCar != null)
            {
                abilityIntroOwnCar.localPosition = new Vector3(-1.25f + impact * .32f, -.15f, .9f);
                abilityIntroOwnCar.localRotation = Quaternion.Euler(0f, 0f, -impact * 5f);
            }
            bool speed = abilityIntroKind == "GROUP" || abilityIntroKind == "ELDERLY";
            float opponentX = speed ? (abilityIntroKind == "GROUP" ? Mathf.Lerp(.7f, 2.1f, approach)
                : Mathf.Lerp(1f, 1.3f, approach)) : 2.2f - approach * .75f + recoil * .5f;
            abilityIntroOpponent.localPosition = new Vector3(opponentX, impact * .12f, 0f);
            abilityIntroOpponent.localRotation = abilityIntroKind == "DIVER"
                ? Quaternion.Euler(0f, time * 260f, impact * 18f)
                : Quaternion.Euler(0f, -12f, speed || abilityIntroKind == "CALM" ? 0f : recoil * 22f);
            if (abilityIntroKind == "HANDYMAN")
                abilityIntroOpponent.localScale = Vector3.one * (1f - recoil * .85f);
            // Start on the passenger, then pivot/zoom out to reveal the threat.
            float reveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.25f, 1.2f, time));
            encounterPreviewCamera.orthographicSize = Mathf.Lerp(1.2f, 2.1f, reveal);
            float yaw = Mathf.Lerp(4f, 0f, reveal);
            float focusX = Mathf.Lerp(-1.1f, .12f, reveal);
            encounterPreviewCamera.transform.localPosition = new Vector3(
                focusX - 12f * Mathf.Tan(yaw * Mathf.Deg2Rad), 0f, -12f);
            encounterPreviewCamera.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void DrawAbilityIntro(float time, Rect view, bool showPlayer, bool playerPane)
        {
            if (encounterPreviewTexture == null) return;
            float fade = Mathf.Min(Mathf.Clamp01(time / .25f), Mathf.Clamp01((AbilityIntroDuration - time) / .35f));
            Color old = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -2000;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(view, Texture2D.whiteTexture);
            float bar = view.height * .105f;
            Rect frame;
            if (playerPane)
            {
                // Fit the landscape preview inside one pane without cropping
                // either the passenger or the approaching hazard.
                float height = Mathf.Min(view.height - bar * 2f, view.width * 9f / 16f);
                frame = new Rect(view.x, view.center.y - height * .5f, view.width, height);
            }
            else frame = new Rect(view.x, view.y + bar, view.width, view.height - bar * 2f);
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.DrawTexture(frame, encounterPreviewTexture,
                playerPane ? ScaleMode.ScaleToFit : ScaleMode.ScaleAndCrop);

            // A visible strike/shield complements the moving 3D characters.
            float effect = Mathf.Clamp01(Mathf.Min((time - 1.35f) / .15f, (2.25f - time) / .25f));
            if (effect > 0f && abilityIntroKind == "DIVER" && abilityIntroShield != null)
            {
                float size = frame.height * .32f;
                GUI.color = new Color(1f, 1f, 1f, effect * fade);
                GUI.DrawTexture(new Rect(frame.center.x - size * .70f, frame.center.y - size * .5f, size, size), abilityIntroShield);
            }
            else if (effect > 0f && abilityIntroKind == "HUNTER")
            {
                GUI.color = new Color(.55f, 1f, .9f, effect * fade);
                GUI.DrawTexture(new Rect(frame.center.x - frame.width * .12f, frame.center.y - 5f,
                    frame.width * .25f, 10f), Texture2D.whiteTexture);
            }
            else if (effect > 0f && (abilityIntroKind == "CALM" || abilityIntroKind == "PREGNANT"))
            {
                GameTypography.ApplyToSkin(GUI.skin);
                Styles();
                var symbol = new GUIStyle(large) { alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.Clamp(Mathf.RoundToInt(frame.height * .20f), 28, 110) };
                symbol.normal.textColor = abilityIntroKind == "CALM" ? Coral : Gold;
                GUI.color = new Color(1f, 1f, 1f, effect * fade);
                GUI.Label(new Rect(frame.center.x - frame.width * .1f, frame.y, frame.width * .2f, frame.height * .55f),
                    abilityIntroKind == "CALM" ? "♥" : "!", symbol);
            }
            GUI.color = new Color(1f, 1f, 1f, fade);
            GameTypography.ApplyToSkin(GUI.skin);
            Styles();
            var textStyle = new GUIStyle(large) { alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(view.height * .038f, view.width * .048f)), 18, 42), wordWrap = false };
            string message = abilityIntroKind == "HUNTER" ? "STUNS MONSTERS"
                : abilityIntroKind == "DIVER" ? "BLOCKS TORNADOES"
                : abilityIntroKind == "BOUNCER" ? "STRONGER BUMPS"
                : abilityIntroKind == "GROUP" ? "SPEED BOOST"
                : abilityIntroKind == "ELDERLY" ? "SLOWS ELEVATOR"
                : abilityIntroKind == "PREGNANT" ? "PATIENCE DRAINS FASTER"
                : abilityIntroKind == "CALM" ? "CALMS THE BABY" : "CLEARS RUST";
            GUI.Label(new Rect(view.x, frame.yMax, view.width, view.yMax - frame.yMax), message, textStyle);
            textStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(view.height * .022f), 14, 24);
            string heading = showPlayer ? (IsNpc ? "CPU  ·  " : "PLAYER " + (Seat + 1) + "  ·  ") : "";
            string name = abilityIntroKind == "GROUP" ? "THE TRIO" : abilityIntroKind == "ELDERLY" ? "GRANDMA"
                : abilityIntroKind == "PREGNANT" ? "MOM + BABY" : abilityIntroKind == "CALM" ? "GRANDMA + BABY"
                : abilityIntroKind;
            GUI.Label(new Rect(view.x, view.y, view.width, frame.y - view.y), heading + name, textStyle);
            GUI.color = old;
            GUI.matrix = oldMatrix;
            GUI.depth = oldDepth;
        }

        void IntroduceBoardedPassenger(CrazyElevator.Shared.Rider rider)
        {
            if (rider == null || round == null || !round.Owns(rider)) return;
            string kind = round.BabyCalmedByGrandma
                && (rider.HasFeature(PassengerFeature.CryingBaby) || rider.HasFeature(PassengerFeature.CalmsBaby))
                ? "CALM" : rider.HasFeature(PassengerFeature.CryingBaby) ? "PREGNANT"
                : rider.HasFeature(PassengerFeature.SpeedSlow) ? "ELDERLY"
                : rider.HasFeature(PassengerFeature.SpeedBoost) ? "GROUP" : null;
            if (kind != null) GameManager.Instance?.RequestAbilityIntro(this, kind);
        }

        public void FinishAbilityIntro()
        {
            abilityIntroPassenger = abilityIntroOpponent = abilityIntroOwnCar = null;
            abilityIntroKind = null;
            ClearEncounterPreviewModel();
        }

        void ReleaseAbilityIntro()
        {
            if (abilityIntroShield != null) Destroy(abilityIntroShield);
            abilityIntroShield = null;
        }
    }
}
