using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CrazyElevator.Shared;

// --- from PassengerInput.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    public sealed partial class ElevatorManager
    {
        readonly Dictionary<Rider, Vector3> cabinPositions = new Dictionary<Rider, Vector3>();
        readonly Dictionary<Renderer, MaterialPropertyBlock> selectionMaterials = new Dictionary<Renderer, MaterialPropertyBlock>();
        static readonly Color BoardingValid = new Color32(69, 231, 137, 255);
        static readonly Color BoardingInvalid = new Color32(255, 82, 74, 255);
        Rider selectedRider;
        Color selectionTint;
        Rider draggedRider;
        const float KickOutBoundaryZ = -.4f;
        Vector3 dragStart, dragOffset, dragOriginalScale;
        Quaternion dragOriginalRotation;
        Vector2 dragScreenStart;
        float dragPlaneLocalY;
        bool draggedWasBoarded, dragKickReady;
        Vector2 lastMousePosition, lastSelectionDirection;
        bool controllerSelection;
        float PartyRadius(Rider rider) => rider.Space >= 3 ? .72f : rider.Space == 2 ? .52f : .34f;
        bool IsInsideCabin(Rider rider, Vector3 position)
        {
            float radius = PartyRadius(rider);
            float rearEdge = keepDoorwayClear ? .28f : .42f + radius * .20f;
            return Mathf.Abs(position.x) <= 2.18f - radius * .45f
                && position.z >= rearEdge && position.z <= 3.48f - radius * .25f;
        }
        bool CabinPlacementClear(Rider rider, Vector3 position)
        {
            // The compact interior uses a real three-seat grid per row. A 2X
            // party reserves its own seat plus one neighbour; a 3X party must
            // stand in a centre seat and reserves the seat on both sides.
            if (keepDoorwayClear)
            {
                if (!TryGetCabinSeatSpan(rider, position, out int row, out int firstSeat, out int lastSeat))
                    return false;
                foreach (var other in round.Riders)
                {
                    if (other == rider || !round.Owns(other)) continue;
                    Vector3 placed = cabinPositions.TryGetValue(other, out var spot) ? spot : RiderPosition(other);
                    if (!TryGetCabinSeatSpan(other, placed, out int otherRow, out int otherFirst, out int otherLast))
                        continue;
                    if (row == otherRow && firstSeat <= otherLast && lastSeat >= otherFirst) return false;
                }
                return true;
            }

            foreach (var other in round.Riders)
            {
                if (other == rider || !round.Owns(other)) continue;
                Vector3 placed = cabinPositions.TryGetValue(other, out var spot) ? spot : RiderPosition(other);
                if (new Vector2(position.x - placed.x, position.z - placed.z).magnitude < PartyRadius(rider) + PartyRadius(other)) return false;
            }
            return true;
        }

        bool TryGetCabinSeatSpan(Rider rider, Vector3 position, out int row, out int firstSeat, out int lastSeat)
        {
            row = Mathf.RoundToInt((position.z - .32f) / 1.16f);
            int seat = Mathf.RoundToInt((position.x + 1.12f) / 1.12f);
            if (row < 0 || row > 2 || seat < 0 || seat > 2)
            {
                firstSeat = lastSeat = -1;
                return false;
            }

            int spaces = Mathf.Clamp(rider.Space, 1, 3);
            if (spaces == 1)
            {
                firstSeat = lastSeat = seat;
                return true;
            }
            if (spaces == 2)
            {
                firstSeat = seat < 3 ? seat : seat - 1;
                lastSeat = firstSeat + 1;
                return true;
            }

            // A three-space party needs an actual seat on its left and right.
            if (seat == 0 || seat == 2)
            {
                firstSeat = lastSeat = -1;
                return false;
            }
            firstSeat = seat - 1;
            lastSeat = seat + 1;
            return true;
        }
        bool CanSelect(Rider rider) => rider != null && phase == Phase.Boarding && !rider.Resolved
            && !boardingTransfers.ContainsKey(rider) && !exiting.ContainsKey(rider)
            && rider != repairingHandyman
            && figures.TryGetValue(rider, out var figure) && figure.gameObject.activeInHierarchy
            && (round.Owns(rider) || !rider.Boarded && rider.Origin == round.Floor
                && rider.Arrival <= 0 && round.IsOffered(rider));

        bool HasBoardingSpace(Rider rider)
        {
            if (rider.Boarded) return true;
            if (!round.HasCapacityFor(rider)) return false;
            if (keepDoorwayClear)
            {
                // Check every seat the player can actually drop into, including
                // seats outside the automatic boarding animation's preferred spots.
                for (int row = 0; row < 3; row++)
                for (int seat = 0; seat < 3; seat++)
                {
                    Vector3 spot = new Vector3(-1.12f + seat * 1.12f, .12f, .48f + row * 1.16f);
                    if (IsInsideCabin(rider, spot) && CabinPlacementClear(rider, spot)) return true;
                }
                return false;
            }
            for (float z = 1f; z <= 3f; z += .7f)
            for (float x = -1.4f; x <= 1.41f; x += .7f)
            {
                Vector3 spot = new Vector3(x, .12f, z);
                if (IsInsideCabin(rider, spot) && CabinPlacementClear(rider, spot)) return true;
            }
            return false;
        }

        void SelectRider(Rider rider)
        {
            if (rider != null && !CanSelect(rider)) rider = null;
            if (selectedRider == rider)
            {
                if (rider != null && draggedRider == null)
                    SetSelectionTint(HasBoardingSpace(rider) ? BoardingValid : BoardingInvalid);
                return;
            }
            foreach (var pair in selectionMaterials) if (pair.Key) pair.Key.SetPropertyBlock(pair.Value);
            selectionMaterials.Clear(); selectedRider = rider;
            if (rider == null) return;
            foreach (var renderer in figures[rider].GetComponentsInChildren<MeshRenderer>())
            {
                if (renderer.GetComponent<TextMesh>() != null) continue;
                var saved = new MaterialPropertyBlock(); renderer.GetPropertyBlock(saved);
                selectionMaterials[renderer] = saved;
            }
            selectionTint = Color.clear;
            SetSelectionTint(HasBoardingSpace(rider) ? BoardingValid : BoardingInvalid);
        }

        void SetSelectionTint(Color tint)
        {
            if (selectedRider == null || selectionTint == tint) return;
            selectionTint = tint;
            foreach (var pair in selectionMaterials)
            {
                var renderer = pair.Key;
                if (!renderer) continue;
                var highlight = new MaterialPropertyBlock(); renderer.GetPropertyBlock(highlight);
                Color original = renderer.sharedMaterial && renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? renderer.sharedMaterial.GetColor("_BaseColor") : Color.white;
                Color color = Color.Lerp(original, tint, .82f);
                highlight.SetColor("_BaseColor", color); highlight.SetColor("_Color", color);
                renderer.SetPropertyBlock(highlight);
            }
        }

        // Mouse hover highlights; click-drag boards, rearranges, or ejects.
        void UpdatePassengerSelection()
        {
            if (draggedRider != null && phase != Phase.Boarding) CancelPassengerDrag();
            var mouse = Mouse.current;
            if (draggedRider != null)
            {
                Vector2 cursor = mouse != null ? mouse.position.ReadValue() : lastMousePosition;
                UpdatePassengerDrag(cursor);
                if (mouse != null && mouse.leftButton.wasReleasedThisFrame) FinishPassengerDrag(cursor);
                return;
            }
            if (phase != Phase.Boarding) { SelectRider(null); return; }
            if (!CanSelect(selectedRider)) SelectRider(null);
            if (mouse != null)
            {
                Vector2 cursor = mouse.position.ReadValue();
                if (Match != null && !Match.ViewRect(Seat).Contains(cursor))
                {
                    SelectRider(null);
                    return;
                }
                if ((cursor - lastMousePosition).sqrMagnitude > 4) controllerSelection = false;
                lastMousePosition = cursor;
                bool overControl = IsOverMouseControl(cursor);
                if (mouse.leftButton.wasPressedThisFrame && !overControl)
                {
                    Rider clicked = PickHoveredRider(cursor);
                    if (clicked != null) { BeginPassengerDrag(clicked, cursor); return; }
                }
                if (!overControl && !controllerSelection) SelectRider(PickHoveredRider(cursor));
            }
            Vector2 navigation = InputManager.Instance != null ? InputManager.Instance.Select : Vector2.zero;
            // Mouse raycast/drag still reads the device directly; stick/WASD come from InputManager.
            Vector2 direction = navigation.magnitude < .55f ? Vector2.zero
                : Mathf.Abs(navigation.x) > Mathf.Abs(navigation.y) ? new Vector2(Mathf.Sign(navigation.x), 0) : new Vector2(0, Mathf.Sign(navigation.y));
            if (direction != Vector2.zero && direction != lastSelectionDirection)
            { controllerSelection = true; SelectInDirection(direction); }
            lastSelectionDirection = direction;
        }

        bool IsOverMouseControl(Vector2 screenCursor)
        {
            Vector2 guiCursor = new Vector2(screenCursor.x, Screen.height - screenCursor.y);
            if (Match != null)
            {
                Rect view = Match.ViewRect(Seat);
                guiCursor.x -= view.x;
                guiCursor.y -= view.y;
            }
            GetMouseControlRects(out var up, out var down, out var stop, out var close, out var hold, out var repair);
            return up.Contains(guiCursor) || down.Contains(guiCursor) || stop.Contains(guiCursor)
                || close.Contains(guiCursor) || hold.Contains(guiCursor) || repair.Contains(guiCursor);
        }

        // Preserve the rider's grab point so dragging feels direct and stable.
        void BeginPassengerDrag(Rider rider, Vector2 cursor)
        {
            if (!CanSelect(rider) || !figures.TryGetValue(rider, out var figure)) return;
            SelectRider(rider);
            draggedRider = rider;
            draggedWasBoarded = rider.Boarded;
            dragKickReady = false;
            dragStart = figure.localPosition;
            dragScreenStart = cursor;
            dragOriginalScale = figure.localScale;
            dragOriginalRotation = figure.localRotation;

            Vector3 startWorld = stage.TransformPoint(dragStart);
            Vector3 grabbedWorld = startWorld;
            Ray ray = eye.ScreenPointToRay(cursor);
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, 100f))
            {
                if (!riderHits.TryGetValue(hit.collider, out var hitRider) || hitRider != rider || hit.distance >= nearest) continue;
                nearest = hit.distance;
                grabbedWorld = hit.point;
            }
            if (nearest == float.MaxValue)
            {
                float handleY = destinationTags.TryGetValue(rider, out var badge)
                    ? stage.InverseTransformPoint(badge.transform.position).y : dragStart.y + 1f;
                var handlePlane = new Plane(stage.up, stage.TransformPoint(new Vector3(0, handleY, 0)));
                if (handlePlane.Raycast(ray, out float distance)) grabbedWorld = ray.GetPoint(distance);
            }
            dragPlaneLocalY = stage.InverseTransformPoint(grabbedWorld).y;
            dragOffset = startWorld - grabbedWorld;
        }

        void UpdatePassengerDrag(Vector2 cursor)
        {
            if (!figures.TryGetValue(draggedRider, out var figure)) return;
            var plane = new Plane(stage.up, stage.TransformPoint(new Vector3(0, dragPlaneLocalY, 0)));
            Ray ray = eye.ScreenPointToRay(cursor);
            if (!plane.Raycast(ray, out float distance)) return;
            Vector3 local = stage.InverseTransformPoint(ray.GetPoint(distance) + dragOffset);
            local.x = Mathf.Clamp(local.x, -2.55f, 2.55f);
            local.z = Mathf.Clamp(local.z, -2.65f, 3.45f);
            local.y = .12f;
            // The door is near z = .15. Leave a margin so a rider must be
            // clearly outside before release counts as a kick.
            dragKickReady = draggedWasBoarded && local.z < KickOutBoundaryZ;
            bool inside = IsInsideCabin(draggedRider, local);
            bool hasSpace = (draggedWasBoarded || round.HasCapacityFor(draggedRider))
                && CabinPlacementClear(draggedRider, local);
            SetSelectionTint(dragKickReady || !inside ? Gold : hasSpace ? BoardingValid : BoardingInvalid);
            figure.localPosition = local;
            figure.localScale = dragKickReady
                ? Vector3.Scale(dragOriginalScale, new Vector3(1.12f, .85f, 1.12f)) : dragOriginalScale;
            figure.localRotation = dragKickReady
                ? dragOriginalRotation * Quaternion.Euler(-12f, 0, 0) : dragOriginalRotation;
            SetControlStatus(dragKickReady ? "RELEASE TO KICK!"
                : !inside ? "DRAG INSIDE" : hasSpace ? "RELEASE TO PLACE" : "NO SPACE HERE");
        }

        void FinishPassengerDrag(Vector2 cursor)
        {
            Rider rider = draggedRider;
            draggedRider = null;
            if (rider == null || !figures.TryGetValue(rider, out var figure)) return;
            figure.localScale = dragOriginalScale;
            figure.localRotation = dragOriginalRotation;
            if ((cursor - dragScreenStart).sqrMagnitude < 100f)
            {
                figure.localPosition = dragStart;
                SetControlStatus("DRAG TO BOARD / EXIT");
                return;
            }

            Vector3 released = figure.localPosition;
            if (draggedWasBoarded && dragKickReady && released.z < KickOutBoundaryZ)
            {
                cabinPositions.Remove(rider);
                SelectRider(null);
                QueueRiderExit(rider, released);
                return;
            }
            if (IsInsideCabin(rider, released))
            {
                if (!CabinPlacementClear(rider, released)
                    || (!draggedWasBoarded && !round.HasCapacityFor(rider)))
                {
                    figure.localPosition = dragStart;
                    SetControlStatus("NO SPACE HERE"); Play(buzz);
                    notice = "This passenger needs an open cabin space and enough capacity.";
                    return;
                }
                if (draggedWasBoarded || round.Board(rider))
                {
                    cabinPositions[rider] = released;
                    if (!draggedWasBoarded) BeginHandymanRepair(rider);
                    SelectRider(null);
                    SetControlStatus(draggedWasBoarded ? "REARRANGED" : "ON BOARD");
                    notice = draggedWasBoarded ? "Cabin rearranged." : "Passenger boarded. Choose another rider or close the doors.";
                    Play(click);
                    return;
                }
            }
            figure.localPosition = dragStart;
            SetControlStatus("DROP INSIDE");
            notice = "Drag a waiting passenger into the cabin, or an onboard passenger through the doorway to exit.";
        }

        void CancelPassengerDrag()
        {
            if (draggedRider != null && figures.TryGetValue(draggedRider, out var figure))
            {
                figure.localPosition = dragStart;
                figure.localScale = dragOriginalScale;
                figure.localRotation = dragOriginalRotation;
            }
            draggedRider = null;
            dragKickReady = false;
        }

        Rider PickHoveredRider(Vector2 cursor)
        {
            if (!eye.pixelRect.Contains(cursor)) return null;
            Rider closest = null; float distance = float.MaxValue; int bestPriority = int.MaxValue;
            foreach (var hit in Physics.RaycastAll(eye.ScreenPointToRay(cursor), 100))
                if (riderHits.TryGetValue(hit.collider, out var rider) && CanSelect(rider))
                {
                    int priority = keepDoorwayClear && !rider.Boarded ? 0 : 1;
                    if (priority < bestPriority || priority == bestPriority && hit.distance < distance)
                    { closest = rider; distance = hit.distance; bestPriority = priority; }
                }
            if (closest != null) return closest;
            float radius = Mathf.Clamp(Screen.height * .025f, 22, 48), best = radius * radius;
            bestPriority = int.MaxValue;
            foreach (var pair in destinationTags)
            {
                if (!CanSelect(pair.Key) || !pair.Value.gameObject.activeInHierarchy) continue;
                Vector3 point = eye.WorldToScreenPoint(pair.Value.transform.position);
                float delta = ((Vector2)point - cursor).sqrMagnitude;
                int priority = keepDoorwayClear && !pair.Key.Boarded ? 0 : 1;
                if (point.z > 0 && delta < radius * radius
                    && (priority < bestPriority || priority == bestPriority && delta < best))
                { best = delta; bestPriority = priority; closest = pair.Key; }
            }
            return closest;
        }
        void SelectInDirection(Vector2 direction)
        {
            Vector2 from = selectedRider != null ? SelectionScreenPoint(selectedRider) : new Vector2(Screen.width, Screen.height) * .5f;
            Rider best = null; float bestScore = float.MaxValue;
            foreach (var rider in round.Riders)
            {
                if (!CanSelect(rider) || rider == selectedRider) continue;
                Vector2 delta = SelectionScreenPoint(rider) - from;
                if (selectedRider != null && Vector2.Dot(delta, direction) <= 5) continue;
                float score = selectedRider == null ? delta.sqrMagnitude : delta.magnitude + Mathf.Abs(delta.x * direction.y - delta.y * direction.x) * 2;
                if (score < bestScore) { bestScore = score; best = rider; }
            }
            if (best != null) SelectRider(best);
        }
        Vector2 SelectionScreenPoint(Rider rider) => eye.WorldToScreenPoint(figures[rider].position + Vector3.up);
        void ConfirmPassenger()
        {
            if (!CanSelect(selectedRider)) { notice = "Highlight a passenger with the mouse or stick first."; return; }
            Rider rider = selectedRider; SelectRider(null);
            if (rider.Boarded)
            {
                cabinPositions.Remove(rider); QueueRiderExit(rider, figures[rider].localPosition);
                notice = "Helping " + rider.Name + " out at floor " + round.Floor + ".";
            }
            else BoardPassengerWithPersona(rider);
        }
        void QueueRiderExit(Rider rider, Vector3 start)
        {
            if (rider == null || !round.Owns(rider) || !figures.TryGetValue(rider, out var figure)) return;
            int scoreBefore = round.Score;
            OffboardResult result = round.Offboard(rider);
            if (result == OffboardResult.None) return;
            if (rider == repairingHandyman) ClearHandymanRepair();
            exitStarts[rider] = start; exiting[rider] = 0;
            BeginKick(rider, figure, start, result, round.Score - scoreBefore);
            phaseTime = 0;
            PlayDeliveryReaction(rider, result, round.Score - scoreBefore);
        }
    }
}

// --- from PassengerKick.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    // Presentation only: scoring remains in ElevatorRound.Offboard().
    public sealed partial class ElevatorManager
    {
        const float KickDuration = .78f;
        const float DeliveryComboWindow = 3.5f;
        const float DeliveryPopupDuration = 1.35f;
        readonly Dictionary<Rider, KickVisual> kicks = new Dictionary<Rider, KickVisual>();
        readonly List<DeliveryPopup> deliveryPopups = new List<DeliveryPopup>();
        float lastRewardedDeliveryAt = -1000f;
        int deliveryCombo, deliveryPopupSerial;

        sealed class KickVisual
        {
            public Vector3 Start, Target, Scale;
            public Quaternion Rotation;
            public bool Gentle, WrongFloor;
            public Transform[] Puffs;
            public TextMesh Feedback;
        }

        sealed class DeliveryPopup
        {
            public Vector2 Viewport;
            public float StartedAt;
            public int Points, Combo, Serial;
            public bool Happy;
        }

        // Capture the art's original pose so replacement models keep their scale.
        void BeginKick(Rider rider, Transform figure, Vector3 start, OffboardResult result, int points)
        {
            var kick = new KickVisual
            {
                Start = start,
                WrongFloor = result == OffboardResult.WrongFloor,
                Gentle = FriendlyInterior && result != OffboardResult.WrongFloor,
                Target = new Vector3(-1.3f + (rider.Color % 3) * 1.3f, .12f, -2.5f),
                Scale = figure.localScale,
                Rotation = figure.localRotation,
                Puffs = new Transform[5]
            };
            for (int i = 0; i < kick.Puffs.Length; i++)
            {
                kick.Puffs[i] = Shape("Kick landing puff", PrimitiveType.Sphere,
                    kick.Target, Vector3.one * .12f, kick.WrongFloor ? Coral : Cream, stage);
                // Effects never block dragging or button clicks.
                Destroy(kick.Puffs[i].GetComponent<Collider>());
                kick.Puffs[i].gameObject.SetActive(false);
            }
            string caption = result == OffboardResult.WrongFloor ? "WRONG FLOOR"
                : result == OffboardResult.Happy ? "DELIVERED!"
                : points == 0 ? "MAD - NO POINTS" : "LATE DELIVERY";
            // Correct-delivery points now live in the crisp 2D reward popup.
            // Keep only status on the moving 3D rider; wrong-floor penalties
            // remain attached to the mistake so the consequence is obvious.
            string worldFeedback = result == OffboardResult.WrongFloor
                ? caption + "\n" + points : caption;
            kick.Feedback = Sign(worldFeedback,
                start + Vector3.up * 2.25f, .025f, points >= 0 ? Gold : Coral);
            RegisterDeliveryPopup(start, result, points);
            kicks[rider] = kick;
        }

        // The combo celebrates quick correct drop-offs without changing the score.
        void RegisterDeliveryPopup(Vector3 localPosition, OffboardResult result, int points)
        {
            if (result == OffboardResult.WrongFloor || points <= 0)
            {
                deliveryCombo = 0;
                lastRewardedDeliveryAt = -1000f;
                return;
            }

            float now = Time.unscaledTime;
            deliveryCombo = now - lastRewardedDeliveryAt <= DeliveryComboWindow
                ? deliveryCombo + 1 : 1;
            lastRewardedDeliveryAt = now;

            Vector2 viewport = new Vector2(.5f, .48f);
            if (eye != null && stage != null)
            {
                Vector3 point = eye.WorldToViewportPoint(stage.TransformPoint(localPosition + Vector3.up * 1.45f));
                if (point.z > 0)
                    viewport = new Vector2(Mathf.Clamp(point.x, .18f, .82f), Mathf.Clamp(point.y, .28f, .76f));
            }

            if (deliveryPopups.Count >= 4) deliveryPopups.RemoveAt(0);
            deliveryPopups.Add(new DeliveryPopup
            {
                Viewport = viewport,
                StartedAt = now,
                Points = points,
                Combo = deliveryCombo,
                Serial = deliveryPopupSerial++,
                Happy = result == OffboardResult.Happy
            });
        }

        // Wind-up, airborne tumble, then a quick landing bounce.
        void AnimateKick(Rider rider, Transform figure, float progress)
        {
            if (!kicks.TryGetValue(rider, out var kick)) return;
            float t = Mathf.Clamp01(progress);
            if (extendedInterior && kick.Gentle)
            {
                ElevatorPersonaRig.PassengerPose(kick.Start, kick.Target, t, kick.Gentle, out var position, out float lean, out float squash);
                figure.localPosition = position;
                figure.localRotation = kick.Rotation * Quaternion.Euler(lean, 0, 0);
                figure.localScale = Vector3.Scale(kick.Scale, new Vector3(1 / Mathf.Sqrt(squash), squash, 1 / Mathf.Sqrt(squash)));
                if (persona) persona.GuidePassenger(position, t, kick.Gentle, false);
                if (t > .75f) AnimateKickPuffs(kick, (t - .75f) / .25f);
            }
            else if (t < .12f)
            {
                float windup = t / .12f;
                figure.localPosition = kick.Start + Vector3.forward * (.12f * windup);
                figure.localScale = Vector3.Scale(kick.Scale, new Vector3(1 + .18f * windup, 1 - .22f * windup, 1 + .18f * windup));
                figure.localRotation = kick.Rotation * Quaternion.Euler(-15f * windup, 0, 0);
            }
            else if (t < .75f)
            {
                float flight = (t - .12f) / .63f;
                // Fast launch, then ease into the landing. No physics dependency.
                float distance = 1 - Mathf.Pow(1 - flight, 2);
                float launchHeight = kick.WrongFloor ? 1.7f : 1.25f;
                figure.localPosition = Vector3.Lerp(kick.Start, kick.Target, distance)
                    + Vector3.up * (Mathf.Sin(flight * Mathf.PI) * launchHeight);
                figure.localScale = Vector3.Scale(kick.Scale, new Vector3(.9f, 1.12f, .9f));
                figure.localRotation = Quaternion.Euler(0, 180, 0)
                    * Quaternion.Euler(0, 0, Mathf.Sin(flight * Mathf.PI)
                        * (kick.WrongFloor ? 220f : 95f) * (rider.Color % 2 == 0 ? 1 : -1));
                if (extendedInterior && persona)
                    persona.GuidePassenger(figure.localPosition, t, false, false);
            }
            else
            {
                float landing = (t - .75f) / .25f;
                figure.localPosition = kick.Target + Vector3.up * (Mathf.Sin(landing * Mathf.PI) * .18f);
                float squash = Mathf.Sin(landing * Mathf.PI);
                figure.localScale = Vector3.Scale(kick.Scale, new Vector3(1 + .2f * squash, 1 - .22f * squash, 1 + .2f * squash));
                figure.localRotation = Quaternion.Euler(0, 180, 0);
                AnimateKickPuffs(kick, landing);
            }
            // Keep the score readable instead of rotating it with the passenger.
            kick.Feedback.transform.localPosition = Vector3.Lerp(kick.Start, kick.Target, .6f)
                + Vector3.up * (2.2f + t * .55f);
            kick.Feedback.transform.rotation = Quaternion.LookRotation(kick.Feedback.transform.position - eye.transform.position);
            Color color = kick.Feedback.color;
            color.a = 1 - Mathf.Clamp01((t - .8f) / .2f);
            kick.Feedback.color = color;
        }

        // Landing puffs make the ejection readable without camera shake.
        void AnimateKickPuffs(KickVisual kick, float progress)
        {
            for (int i = 0; i < kick.Puffs.Length; i++)
            {
                float angle = i * Mathf.PI * 2 / kick.Puffs.Length;
                Vector3 direction = new Vector3(Mathf.Cos(angle), .12f, Mathf.Sin(angle));
                var puff = kick.Puffs[i];
                puff.gameObject.SetActive(true);
                puff.localPosition = kick.Target + direction * (.2f + progress * .65f);
                float size = kick.WrongFloor ? .38f : .26f;
                puff.localScale = Vector3.one * (size * (1 - progress));
            }
        }

        // Clean up presentation objects after the rider has left.
        void EndKick(Rider rider)
        {
            if (!kicks.TryGetValue(rider, out var kick)) return;
            foreach (var puff in kick.Puffs) Destroy(puff.gameObject);
            Destroy(kick.Feedback.gameObject);
            if (figures.TryGetValue(rider, out var figure)) figure.localScale = kick.Scale;
            kicks.Remove(rider);
            if (persona) persona.RestHands();
        }

        // Restart must not leave old score popups or landing effects behind.
        void ClearKicks()
        {
            foreach (var rider in new List<Rider>(kicks.Keys)) EndKick(rider);
            deliveryPopups.Clear();
            deliveryCombo = 0;
            lastRewardedDeliveryAt = -1000f;
        }
    }
}

// --- from PassengerVisuals.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;
    // Passenger prefab instances, badges, speech and exit animation.
    public sealed partial class ElevatorManager
    {
        // State owned by this part of the prototype.
        readonly Dictionary<Rider, Transform> figures = new Dictionary<Rider, Transform>();
        readonly Dictionary<Rider, TextMesh> bubbles = new Dictionary<Rider, TextMesh>();
        readonly Dictionary<Rider, TextMesh> destinationTags = new Dictionary<Rider, TextMesh>();
        readonly Dictionary<Rider, float> exiting = new Dictionary<Rider, float>();
        readonly Dictionary<Rider, Vector3> exitStarts = new Dictionary<Rider, Vector3>();
        readonly Dictionary<Collider, Rider> riderHits = new Dictionary<Collider, Rider>();
        static readonly float[] OfficeQueueX = { -1.2f, 0f, 1.2f };

        // Spawn authored art and register its clickable labels.
        Transform MakeRider(Rider p)
        {
            PassengerView template = null;
            foreach (var art in sceneView.passengers)
                if (art != null && art.ResolvedKind == p.Kind) template = art;
            if (template == null)
                throw new System.InvalidOperationException("Assign passenger art for " + p.Kind + " in Elevator Scene.");
            var view = Instantiate(template, stage, false);
            var root = view.transform;
            root.name = p.Name + " • " + p.Kind;
            Color color = Palette[p.Color % Palette.Length];
            foreach (var jacket in view.jackets) if (jacket != null) jacket.sharedMaterial = Mat(color);
            bubbles[p] = view.speech;
            destinationTags[p] = view.destination;
            StyleDestinationTag(view.destination);
            foreach (var collider in root.GetComponentsInChildren<Collider>()) riderHits[collider] = p;
            AttachPatienceBar(p, root);
            // Extended mode uses one screen-space callout system in both solo
            // and 1v1. Keep the authored world bubble only for legacy layouts.
            if (Match != null && !extendedInterior) AttachSpeechBubble(p, root);
            return root;
        }

        // Keep the destination cue short, bold, and readable against every
        // cabin band. The number is the actionable information; the persona
        // badge belongs in the passenger art and no longer needs repeating.
        static void StyleDestinationTag(TextMesh tag)
        {
            if (tag == null) return;
            tag.fontSize = 72;
            tag.characterSize = .034f;
            tag.anchor = TextAnchor.MiddleCenter;
            tag.alignment = TextAlignment.Center;
            tag.color = Ink;
            GameTypography.Apply(tag, true);
            MeshRenderer renderer = tag.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 20;
            }
        }

        // Use the saved cabin position or waiting-queue slot.
        Vector3 RiderPosition(Rider p)
        {
            int slot = 0;
            foreach (var other in round.Riders)
            {
                if (other == p) break;
                if (p.Boarded ? round.Owns(other) : !other.Boarded && !other.Resolved && other.Origin == round.Floor) slot++;
            }
            if (p.Boarded)
            {
                if (cabinPositions.TryGetValue(p, out Vector3 placed)) return placed;
                // Defensive fallback for restored/runtime-created riders. Normal
                // boarding keeps the exact continuous position chosen by the player.
                float[] cabinX = { -1.65f, -.55f, .55f, 1.65f };
                return new Vector3(cabinX[Mathf.Min(slot, cabinX.Length - 1)], .12f, 1.08f);
            }
            if (OfficeInterior)
            {
                // Office cubicles hide the old extreme queue positions. Keep
                // waiting riders across the visible entrance aisle instead.
                int queueColumn = slot % OfficeQueueX.Length;
                int queueRow = slot / OfficeQueueX.Length;
                float arrivalOffset = p.Kind == "ELDERLY"
                    ? .7f * Mathf.Clamp01(p.Arrival / ElevatorRound.ElderlyArrivalSeconds)
                    : Mathf.Min(.7f, p.Arrival * .12f);
                return new Vector3(OfficeQueueX[queueColumn], .12f,
                    -1.1f - queueRow * .65f - arrivalOffset);
            }
            float hallwayArrivalOffset = p.Kind == "ELDERLY"
                ? 1.2f * Mathf.Clamp01(p.Arrival / ElevatorRound.ElderlyArrivalSeconds)
                : Mathf.Min(1.2f, p.Arrival * .20f);
            return new Vector3(-2f + slot * 2f, .12f, -1.45f - hallwayArrivalOffset);
        }

        // Match visible models to passenger state.
        void SyncFigures(float dt)
        {
            var completedExits = new List<Rider>();
            foreach (var p in round.Riders)
            {
                if (p == repairingHandyman && (!round.Owns(p) || phase != Phase.Boarding))
                    ClearHandymanRepair();
                bool isExiting = exiting.TryGetValue(p, out float exitTime);
                // TextMesh labels do not use the same occlusion as the solid
                // doors, so explicitly hide the hall queue unless the doorway
                // is open enough to interact with it.
                bool hallVisible = phase == Phase.Boarding || phase == Phase.Opening && doors > .72f;
                bool visible = isExiting || !p.Resolved && (round.Owns(p)
                    || !p.Boarded && p.Origin == round.Floor && hallVisible && round.IsOffered(p));
                if (!figures.TryGetValue(p, out var figure))
                {
                    if (!visible) continue;
                    figure = MakeRider(p); figure.localPosition = RiderPosition(p); figures.Add(p, figure);
                }
                figure.gameObject.SetActive(visible);
                if (!visible) continue;

                if (isExiting)
                {
                    // An already-launched rider finishes even if the doors close.
                    bool gentleExit = kicks.TryGetValue(p, out var kick) && kick.Gentle;
                    float duration = extendedInterior && gentleExit ? PersonaExitDuration : KickDuration;
                    exitTime += dt / duration;
                    exiting[p] = exitTime;
                    if (exitTime >= 0)
                    {
                        AnimateKick(p, figure, exitTime);
                        if (exitTime >= 1f) completedExits.Add(p);
                    }
                }
                else
                {
                    // Selected riders stay in place until confirmed.
                    if (p != draggedRider && !AnimatePersonaBoard(p, figure, dt)
                        && !AnimateHandymanRepair(p, figure, dt))
                    {
                        var target = RiderPosition(p);
                        figure.localPosition = Vector3.MoveTowards(figure.localPosition, target, dt * 5);
                        figure.localRotation = Quaternion.Euler(0, p.Boarded ? 0 : 180, 0);
                    }
                }
                if (bubbles.TryGetValue(p, out var bubble))
                {
                    if (passengerSpeechBubblePrefab != null)
                    {
                        // Dialogue is presented by the occasional speech-bubble
                        // prefab instead of permanent floating text in this scene.
                        bubble.gameObject.SetActive(false);
                    }
                    else
                    {
                        // Preserve the original extended scene's presentation.
                        bubble.gameObject.SetActive(!isExiting);
                        bubble.text = BubbleText(p);
                        bubble.color = IsComplaining(p) ? Coral : Cream;
                        bubble.transform.rotation = Quaternion.LookRotation(bubble.transform.position - eye.transform.position);
                    }
                }
                if (destinationTags.TryGetValue(p, out var destinationTag))
                {
                    bool useScreenCallout = extendedInterior;
                    destinationTag.gameObject.SetActive(!useScreenCallout && !isExiting);
                    destinationTag.text = p.Destination.ToString();
                    // Keep the actionable number a single, dark high-contrast
                    // color. Mood is already communicated by the patience bar
                    // and speech bubble, so it should not reduce legibility.
                    destinationTag.color = Ink;
                    destinationTag.transform.localPosition = new Vector3(
                        0, 2.16f, p.Boarded ? -.08f : .08f);
                    destinationTag.transform.rotation = Quaternion.LookRotation(destinationTag.transform.position - eye.transform.position);
                }
                UpdatePatienceBar(p, isExiting);
            }
            UpdateOccasionalSpeech(dt);
            foreach (var p in completedExits)
            {
                EndKick(p);
                exiting.Remove(p); exitStarts.Remove(p);
                if (figures.TryGetValue(p, out var figure)) figure.gameObject.SetActive(false);
            }
        }

        // Choose dialogue from the rider's request and mood.
        string BubbleText(Rider p)
        {
            if (p.Kind == "HANDYMAN") return p.Boarded ? "\"Full power! I've got the gears covered.\"" : "\"Take me aboard to clear all rust!\"";
            if (exiting.ContainsKey(p)) return "\"" + (string.IsNullOrEmpty(p.Status) ? "Made it!" : p.Status) + "\"";
            if (p.Boarded && p.Remaining <= 0) return "\"I'm MAD! No points now.\"";
            if (p.Boarded && p.Destination == round.Floor && phase == Phase.Boarding) return "\"My stop! Select me and confirm.\"";
            if (p.Boarded && !string.IsNullOrEmpty(p.Status)) return "\"" + p.Status + "\"";
            if (p.Boarded && p.HoldRequired > 0 && !p.HoldSatisfied) return "\"Hold B / H!\"";
            if (p.Boarded) return IsComplaining(p) ? "\"Please hurry!\"" : "\"Floor " + p.Destination.ToString() + "\"";
            return "\"" + (p.Kind == "BOSS" ? "Hold B / H for my bonus." : p.Request) + "\"";
        }

        // Warn when mood drops or patience gets low.
        bool IsComplaining(Rider p)
        {
            return p.Mood < 2 || p.Remaining < p.Patience * .35f;
        }

    }
}

// --- from PassengerPatienceFeature.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;

    // Optional patience-bar presentation, enabled by assigning its prefab in
    // the single-player scene. Keep the runtime safe when a variant omits it.
    public sealed partial class ElevatorManager
    {
        [Header("Passenger patience")]
        public PassengerPatienceBar passengerPatienceBarPrefab;
        [Tooltip("When enabled, a passenger whose bar is fully red awards no delivery points.")]
        public bool zeroScoreWhenMad;
        [Min(1f), Tooltip("Multiplies every passenger's patience duration for this scene.")]
        public float patienceDurationMultiplier = 1f;
        [Tooltip("Uses the compact segmented top HUD instead of the prototype status panels.")]
        public bool compactTopHud;

        readonly Dictionary<Rider, PassengerPatienceBar> patienceBars =
            new Dictionary<Rider, PassengerPatienceBar>();

        void AttachPatienceBar(Rider rider, Transform figure)
        {
            if (passengerPatienceBarPrefab == null || rider == null || figure == null) return;
            PassengerPatienceBar bar = Instantiate(passengerPatienceBarPrefab, figure, false);
            bar.name = rider.Name + " patience";
            // Sit just above the authored passenger's head, below the small
            // destination badge, instead of floating near the ceiling.
            bar.transform.localPosition = new Vector3(0, 1.88f, 0);
            bar.Bind(eye);
            bar.gameObject.SetActive(false);
            patienceBars[rider] = bar;
        }

        void UpdatePatienceBar(Rider rider, bool isExiting)
        {
            if (!patienceBars.TryGetValue(rider, out PassengerPatienceBar bar) || bar == null) return;
            bool show = round.Owns(rider) && !isExiting
                && !boardingTransfers.ContainsKey(rider);
            bar.gameObject.SetActive(show);
            if (!show) return;

            float remaining = rider.Patience <= 0 ? 0 : rider.Remaining / rider.Patience;
            bar.SetState(remaining, rider.Remaining <= 0);
        }

        void ClearPatienceBars()
        {
            patienceBars.Clear();
        }
    }
}

// --- from PassengerSpeechFeature.cs ---
namespace CrazyElevator.Managers
{
    using Rider = CrazyElevator.Shared.Rider;

    public sealed partial class ElevatorManager
    {
        [Header("Passenger speech")]
        public PassengerSpeechBubble passengerSpeechBubblePrefab;
        [Min(.5f)] public float speechDuration = 2.2f;
        [Tooltip("Keeps boarded passengers near the side walls and prioritizes hall passengers when selecting overlaps.")]
        public bool keepDoorwayClear;
        [Min(0f), Tooltip("Raises the cabin camera so hall passengers remain visible behind boarded riders.")]
        public float cameraLift;
        [Range(45f, 90f), Tooltip("Vertical cabin lens angle. Lower values make the playable cabin fill more of the screen.")]
        public float cabinVerticalFieldOfView = 90f;
        [Range(0f, 12f), Tooltip("Tilts the camera down so the doorway and passengers use the empty upper screen space.")]
        public float cabinAimDown = 9f;

        readonly Dictionary<Rider, PassengerSpeechBubble> speechBubbleViews =
            new Dictionary<Rider, PassengerSpeechBubble>();
        readonly Dictionary<Rider, float> speechUntil = new Dictionary<Rider, float>();
        readonly HashSet<Rider> announcedLowPatience = new HashSet<Rider>();
        readonly HashSet<Rider> announcedMad = new HashSet<Rider>();
        readonly HashSet<Rider> missedDestination = new HashSet<Rider>();
        float speechClock;

        void AttachSpeechBubble(Rider rider, Transform figure)
        {
            if (passengerSpeechBubblePrefab == null || rider == null || figure == null) return;
            PassengerSpeechBubble bubble = Instantiate(passengerSpeechBubblePrefab, figure, false);
            bubble.name = rider.Name + " speech bubble";
            bubble.transform.localPosition = new Vector3(0, 2.08f, 0);
            bubble.Bind(eye);
            bubble.Hide();
            speechBubbleViews[rider] = bubble;
        }

        void UpdateOccasionalSpeech(float dt)
        {
            if (passengerSpeechBubblePrefab == null || round == null) return;
            speechClock += Mathf.Max(0, dt);

            foreach (var pair in speechBubbleViews)
            {
                Rider rider = pair.Key;
                PassengerSpeechBubble bubble = pair.Value;
                if (!IsVisibleSpeaker(rider))
                {
                    bubble.Hide();
                    continue;
                }

                float patience = rider.Patience <= 0 ? 0 : rider.Remaining / rider.Patience;
                if (rider.Boarded && rider.Remaining > 0 && patience <= .25f
                    && announcedLowPatience.Add(rider))
                {
                    speechUntil[rider] = speechClock + speechDuration;
                }
                if (rider.Boarded && rider.Remaining <= 0 && announcedMad.Add(rider))
                {
                    speechUntil[rider] = speechClock + speechDuration;
                }

                bool temporaryAlert = speechUntil.TryGetValue(rider, out float end) && speechClock < end;
                bool waitingPersistent = false;
                if (rider.Boarded && !string.IsNullOrEmpty(rider.Status)
                    && rider.Status.IndexOf("passed my floor", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    missedDestination.Add(rider);
                bool missedPersistent = rider.Boarded && missedDestination.Contains(rider);
                bool destinationPersistent = rider.Boarded && rider.Destination == round.Floor
                    && (phase == Phase.Opening || phase == Phase.Boarding || phase == Phase.Closing);

                string message = temporaryAlert ? rider.Remaining <= 0 ? "I'M MAD!" : "PLEASE HURRY!"
                    : missedPersistent ? "MISSED MY FLOOR!"
                    : destinationPersistent ? "MY STOP!"
                    : waitingPersistent ? WaitingSpeechText(rider)
                    : null;
                if (string.IsNullOrEmpty(message)) { bubble.Hide(); continue; }
                PositionSpeechBubble(rider, bubble);
                bubble.Show(message);
            }
        }

        bool IsVisibleSpeaker(Rider rider)
        {
            return rider != null && !rider.Resolved && !exiting.ContainsKey(rider)
                && figures.TryGetValue(rider, out Transform figure) && figure.gameObject.activeInHierarchy
                && (round.Owns(rider) || !rider.Boarded && rider.Origin == round.Floor && round.IsOffered(rider));
        }

        static bool CanSpeakWhileWaiting(Rider rider)
        {
            // Box-carrying courier plus distinctive/special passenger types.
            return rider.Kind == "COURIER" || rider.Kind == "HANDYMAN" || rider.Kind == "BOSS"
                || rider.Kind == "GROUP" || rider.Kind == "PREGNANT" || rider.Kind == "ELDERLY";
        }

        void PositionSpeechBubble(Rider rider, PassengerSpeechBubble bubble)
        {
            if (!figures.TryGetValue(rider, out Transform figure)) return;
            float side = figure.localPosition.x < 0 ? -1f : 1f;
            // Waiting characters face the opposite direction, so invert their
            // local offset to keep the bubble on the same screen-side as them.
            float localSide = rider.Boarded ? side : -side;
            // Keep the quote tight to the head — a light nudge, not a far float.
            float lateral = Mathf.Abs(figure.localPosition.x) < .4f ? .16f : .26f;
            bubble.transform.localPosition = new Vector3(
                localSide * lateral, rider.Boarded ? 1.78f : 1.9f, 0);
            bubble.SetSide(localSide);
        }

        static string WaitingSpeechText(Rider rider)
        {
            if (rider.Kind == "COURIER") return "BOX DELIVERY!";
            if (rider.Kind == "HANDYMAN") return "NEED A FIX?";
            if (rider.Kind == "BOSS") return "HOLD THE DOOR!";
            if (rider.Kind == "GROUP") return "ALL TOGETHER!";
            if (rider.Kind == "PREGNANT") return "TWO SPACES!";
            if (rider.Kind == "ELDERLY") return "PLEASE WAIT!";
            return "FLOOR " + rider.Destination;
        }

        void ClearSpeechBubbles()
        {
            foreach (var bubble in speechBubbleViews.Values) if (bubble != null) bubble.Hide();
            speechBubbleViews.Clear();
            speechUntil.Clear();
            announcedLowPatience.Clear();
            announcedMad.Clear();
            missedDestination.Clear();
            speechClock = 0;
        }
    }
}
