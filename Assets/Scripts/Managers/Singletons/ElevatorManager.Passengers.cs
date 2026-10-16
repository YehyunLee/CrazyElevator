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
        // Vector2Int.x = first column, Vector2Int.y = row.
        readonly Dictionary<Rider, Vector2Int> cabinTileRuns = new Dictionary<Rider, Vector2Int>();
        readonly Dictionary<Renderer, MaterialPropertyBlock> selectionMaterials = new Dictionary<Renderer, MaterialPropertyBlock>();
        static readonly Color BoardingValid = new Color32(69, 231, 137, 255);
        static readonly Color BoardingInvalid = new Color32(255, 82, 74, 255);
        const float CabinSideEdge = 2.75f;
        const float CabinBackEdge = 5.00f;
        const int CabinColumns = 3;
        const int CabinRows = 3;
        // The authored cabin floor is approximately 5.62 x 5.08 units. Keep
        // a small wall/door margin, but use most of that floor for placement.
        static readonly float[] CabinTileXBounds = { -2.55f, -.85f, .85f, 2.55f };
        static readonly float[] CabinTileZBounds = { .30f, 1.85f, 3.40f, 4.95f };
        static readonly float[] CabinTileX = { -1.70f, 0f, 1.70f };
        static readonly float[] CabinTileZ = { 1.075f, 2.625f, 4.175f };
        static readonly Color GridTileOpen = new Color32(69, 231, 137, 150);
        static readonly Color GridTileOccupied = new Color32(255, 82, 74, 190);
        static readonly Color GridTilePreview = new Color32(255, 205, 82, 220);
        Rider selectedRider;
        Rider pendingSelectionAfterBoarding;
        bool selectFirstWaitingWhenAvailable;
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
            return Mathf.Abs(position.x) <= CabinSideEdge - radius * .45f
                && position.z >= rearEdge && position.z <= CabinBackEdge - radius * .25f;
        }
        bool CabinPlacementClear(Rider rider, Vector3 position)
        {
            return TryCabinTilePlacement(rider, position, true, out _);
        }

        Vector3 CabinTileCenter(int row, int start, int width)
        {
            return new Vector3(
                (CabinTileXBounds[start] + CabinTileXBounds[start + width]) * .5f,
                .12f,
                (CabinTileZBounds[row] + CabinTileZBounds[row + 1]) * .5f);
        }

        void FindNearestCabinTileRun(Rider rider, Vector3 desired, out int row, out int start)
        {
            int width = Mathf.Clamp(rider != null ? rider.Space : 1, 1, CabinColumns);
            row = 0;
            start = 0;
            float bestDistance = float.MaxValue;
            for (int candidateRow = 0; candidateRow < CabinRows; candidateRow++)
            for (int candidateStart = 0; candidateStart <= CabinColumns - width; candidateStart++)
            {
                Vector3 center = CabinTileCenter(candidateRow, candidateStart, width);
                float distance = new Vector2(desired.x - center.x, desired.z - center.z).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                row = candidateRow;
                start = candidateStart;
            }
        }

        void RememberCabinTileRun(Rider rider, Vector3 placed)
        {
            if (rider == null) return;
            FindNearestCabinTileRun(rider, placed, out int row, out int start);
            cabinTileRuns[rider] = new Vector2Int(start, row);
        }

        Vector3 PassengerGridAnchor(Rider rider, Transform figure, float inset)
        {
            if (rider == null || figure == null || !rider.Boarded || stage == null)
                return figure != null ? figure.position + Vector3.up * .05f : Vector3.zero;

            Vector3 placed = figure.localPosition;
            int row, start;
            if (draggedRider == rider)
            {
                FindNearestCabinTileRun(rider, placed, out row, out start);
            }
            else if (cabinTileRuns.TryGetValue(rider, out Vector2Int run))
            {
                start = run.x;
                row = run.y;
            }
            else
            {
                FindNearestCabinTileRun(rider, placed, out row, out start);
            }

            int width = Mathf.Clamp(rider.Space, 1, CabinColumns);
            float left = CabinTileXBounds[start];
            float right = CabinTileXBounds[start + width];
            float front = CabinTileZBounds[row];
            float back = CabinTileZBounds[row + 1];
            Vector3[] corners =
            {
                stage.TransformPoint(new Vector3(left + inset, .10f, front + inset)),
                stage.TransformPoint(new Vector3(left + inset, .10f, back - inset)),
                stage.TransformPoint(new Vector3(right - inset, .10f, front + inset)),
                stage.TransformPoint(new Vector3(right - inset, .10f, back - inset))
            };

            // Choose the visually lower-left corner once from the camera's
            // resting pose. Do not use the live shaken camera here: its
            // changing projection can swap corners and make PIP numbers jump.
            Vector3 cameraRight = cameraHomeRotation * Vector3.right;
            Vector3 cameraUp = cameraHomeRotation * Vector3.up;
            Vector3 anchor = corners[0];
            float bestScore = float.MaxValue;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 offset = corners[i] - cameraHome;
                float score = Vector3.Dot(offset, cameraRight) + Vector3.Dot(offset, cameraUp);
                if (score >= bestScore) continue;
                bestScore = score;
                anchor = corners[i];
            }
            return anchor;
        }

        Vector3 PassengerDestinationAnchor(Rider rider, Transform figure)
            => PassengerGridAnchor(rider, figure, .34f);

        Vector3 PassengerFloorMarkerAnchor(Rider rider, Transform figure)
            => PassengerGridAnchor(rider, figure, .34f);

        // Magnetic placement should select a tile run, not erase the player's
        // precise drop location. Keep the passenger inside the selected cells
        // with enough margin for its model, while preserving movement freedom.
        Vector3 ConstrainToCabinTiles(Rider rider, Vector3 desired, int row, int start, int width)
        {
            float radius = PartyRadius(rider);
            float left = CabinTileXBounds[start];
            float right = CabinTileXBounds[start + width];
            float front = CabinTileZBounds[row];
            float back = CabinTileZBounds[row + 1];
            float xInset = Mathf.Min(radius * .72f, (right - left) * .34f);
            float zInset = Mathf.Min(radius * .62f, (back - front) * .34f);
            return new Vector3(
                Mathf.Clamp(desired.x, left + xInset, right - xInset),
                .12f,
                Mathf.Clamp(desired.z, front + zInset, back - zInset));
        }

        void SetPassengerGridPreview(Rider rider, Vector3 desired, bool valid)
        {
            if (passengerGridTiles == null || passengerGridTiles.Count != CabinColumns * CabinRows)
                return;

            bool[] occupied = new bool[CabinColumns * CabinRows];
            if (round != null)
                foreach (Rider other in round.Riders)
                {
                    if (other == rider || !round.Owns(other)) continue;
                    Vector3 placed = cabinPositions.TryGetValue(other, out Vector3 saved)
                        ? saved : RiderPosition(other);
                    MarkNearestCabinTiles(other, placed, occupied);
                }
            for (int i = 0; i < passengerGridTiles.Count; i++)
            {
                Renderer renderer = passengerGridTiles[i] ? passengerGridTiles[i].GetComponent<Renderer>() : null;
                if (renderer != null)
                    renderer.sharedMaterial = Mat(occupied[i] ? GridTileOccupied : GridTileOpen);
            }
            if (rider == null || !IsInsideCabin(rider, desired)) return;

            int width = Mathf.Clamp(rider.Space, 1, CabinColumns);
            FindNearestCabinTileRun(rider, desired, out int bestRow, out int bestStart);

            Color previewColor = valid ? GridTilePreview : GridTileOccupied;
            for (int column = bestStart; column < bestStart + width; column++)
            {
                int index = bestRow * CabinColumns + column;
                Renderer renderer = passengerGridTiles[index]
                    ? passengerGridTiles[index].GetComponent<Renderer>() : null;
                if (renderer != null) renderer.sharedMaterial = Mat(previewColor);
            }
        }

        // Resolve a passenger to one horizontal run of the 3x3 cabin grid.
        // Multi-space passengers occupy adjacent tiles in the same row.
        bool TryCabinTilePlacement(Rider rider, Vector3 desired, bool nearestOnly, out Vector3 placement)
        {
            placement = desired;
            if (rider == null || round == null) return false;

            bool[] occupied = new bool[CabinColumns * CabinRows];
            foreach (Rider other in round.Riders)
            {
                if (other == rider || !round.Owns(other)) continue;
                Vector3 placed = cabinPositions.TryGetValue(other, out Vector3 saved)
                    ? saved : RiderPosition(other);
                MarkNearestCabinTiles(other, placed, occupied);
            }

            int width = Mathf.Clamp(rider.Space, 1, CabinColumns);
            float nearestDistance = float.MaxValue;
            float nearestOpenDistance = float.MaxValue;
            Vector3 nearest = desired;
            Vector3 nearestOpen = desired;
            bool nearestIsOpen = false;
            bool foundOpen = false;
            int nearestRow = 0, nearestStart = 0;
            for (int row = 0; row < CabinRows; row++)
            for (int start = 0; start <= CabinColumns - width; start++)
            {
                // Use the larger cabin bounds from the current branch, but keep
                // Zoe's magnetic behavior: once the cursor enters a cell, the
                // passenger is pulled to that cell (or adjacent cell run).
                Vector3 candidate = CabinTileCenter(row, start, width);
                float distance = new Vector2(desired.x - candidate.x, desired.z - candidate.z).sqrMagnitude;
                bool open = true;
                for (int column = start; column < start + width; column++)
                    if (occupied[row * CabinColumns + column]) { open = false; break; }

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = candidate;
                    nearestIsOpen = open;
                    nearestRow = row;
                    nearestStart = start;
                }
                if (open && distance < nearestOpenDistance)
                {
                    nearestOpenDistance = distance;
                    nearestOpen = candidate;
                    foundOpen = true;
                }
            }

            if (nearestOnly)
            {
                placement = nearestIsOpen
                    ? CabinTileCenter(nearestRow, nearestStart, width)
                    : nearest;
                return nearestIsOpen;
            }
            placement = nearestOpen;
            return foundOpen;
        }

        void MarkNearestCabinTiles(Rider rider, Vector3 position, bool[] occupied)
        {
            int width = Mathf.Clamp(rider.Space, 1, CabinColumns);
            int bestRow = 0;
            int bestStart = 0;
            float bestDistance = float.MaxValue;
            for (int row = 0; row < CabinRows; row++)
            for (int start = 0; start <= CabinColumns - width; start++)
            {
                float centerX = (CabinTileX[start] + CabinTileX[start + width - 1]) * .5f;
                float distance = new Vector2(position.x - centerX, position.z - CabinTileZ[row]).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestRow = row;
                bestStart = start;
            }
            for (int column = bestStart; column < bestStart + width; column++)
                occupied[bestRow * CabinColumns + column] = true;
        }
        bool PassengerFigureInView(Transform figure)
        {
            if (eye == null || figure == null) return false;
            Vector3 point = eye.WorldToScreenPoint(figure.position + Vector3.up * .95f);
            return point.z > 0f && eye.pixelRect.Contains(new Vector2(point.x, point.y));
        }

        bool CanSelect(Rider rider)
        {
            if (rider == null || phase != Phase.Boarding || rider.Resolved
                || boardingTransfers.ContainsKey(rider) || exiting.ContainsKey(rider)
                || rider == repairingHandyman || !figures.TryGetValue(rider, out var figure)
                || figure == null || !figure.gameObject.activeInHierarchy) return false;
            if (round.Owns(rider)) return true;
            return !rider.Boarded && rider.Origin == round.Floor && rider.Arrival <= 0
                && round.IsOffered(rider)
                && (draggedRider == rider || PassengerFigureInView(figure));
        }

        bool HasBoardingSpace(Rider rider)
        {
            if (rider.Boarded) return true;
            if (!round.HasCapacityFor(rider)) return false;
            return TryCabinTilePlacement(rider, new Vector3(0f, .12f, CabinTileZ[0]), false, out _);
        }

        void SelectRider(Rider rider)
        {
            if (rider != null && !CanSelect(rider)) rider = null;
            if (rider != null)
            {
                pendingSelectionAfterBoarding = null;
                selectFirstWaitingWhenAvailable = false;
            }
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
            if (draggedRider != null && !CanSelect(draggedRider)) CancelPassengerDrag();
            Mouse mouse = MenuManager.TutorialActive ? null : Mouse.current;
            InputManager.SeatInput input = SeatControls;
            if (draggedRider != null)
            {
                Vector2 cursor = mouse != null ? mouse.position.ReadValue() : lastMousePosition;
                UpdatePassengerDrag(cursor);
                if (mouse != null && mouse.leftButton.wasReleasedThisFrame) FinishPassengerDrag(cursor);
                return;
            }
            if (phase != Phase.Boarding) { SelectRider(null); return; }
            if (pendingSelectionAfterBoarding != null
                && !boardingTransfers.ContainsKey(pendingSelectionAfterBoarding)
                && CanSelect(pendingSelectionAfterBoarding))
            {
                Rider pending = pendingSelectionAfterBoarding;
                pendingSelectionAfterBoarding = null;
                SelectRider(pending);
            }
            if (selectFirstWaitingWhenAvailable && selectedRider == null)
            {
                Rider firstWaiting = FindFirstWaitingSelection();
                if (firstWaiting != null)
                {
                    selectFirstWaitingWhenAvailable = false;
                    SelectRider(firstWaiting);
                }
            }
            if (!CanSelect(selectedRider)) SelectRider(null);
            Vector2 navigation = input.Select;
            bool navigationActive = navigation.magnitude >= .55f;
            // Keyboard/controller selection wins immediately, even if the
            // mouse happens to be parked outside this player's split-screen.
            if (navigationActive) controllerSelection = true;
            if (mouse != null)
            {
                Vector2 cursor = mouse.position.ReadValue();
                if (Match != null && !Match.ViewRect(Seat).Contains(cursor))
                {
                    if (!controllerSelection) { SelectRider(null); return; }
                }
                else
                {
                    if ((cursor - lastMousePosition).sqrMagnitude > 4 && !navigationActive)
                        controllerSelection = false;
                    bool overControl = IsOverMouseControl(cursor);
                    if (mouse.leftButton.wasPressedThisFrame && !overControl)
                    {
                        Rider clicked = PickHoveredRider(cursor);
                        if (clicked != null) { BeginPassengerDrag(clicked, cursor); return; }
                    }
                    if (!overControl && !controllerSelection) SelectRider(PickHoveredRider(cursor));
                }
                lastMousePosition = cursor;
            }
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
            local.x = Mathf.Clamp(local.x, -2.85f, 2.85f);
            // Match the full cabin placement depth. The old 3.45 limit made
            // the new rear row visually available but unreachable by dragging.
            local.z = Mathf.Clamp(local.z, -2.65f, CabinBackEdge);
            local.y = .12f;
            // The door is near z = .15. Leave a margin so a rider must be
            // clearly outside before release counts as a kick.
            dragKickReady = draggedWasBoarded && local.z < KickOutBoundaryZ;
            bool inside = IsInsideCabin(draggedRider, local);
            bool hasCapacity = draggedWasBoarded || round.HasCapacityFor(draggedRider);
            Vector3 placement = local;
            bool hasSpace = hasCapacity && TryCabinTilePlacement(draggedRider, local, true, out placement);
            SetSelectionTint(dragKickReady || !inside ? Gold : hasSpace ? BoardingValid : BoardingInvalid);
            SetPassengerGridPreview(draggedRider, local, inside && hasSpace && !dragKickReady);
            figure.localPosition = inside && hasSpace ? placement : local;
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
            SetPassengerGridPreview(null, Vector3.zero, false);
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
                Vector2Int movedRun = CabinRunForSelection(rider);
                cabinPositions.Remove(rider);
                cabinTileRuns.Remove(rider);
                SelectRider(null);
                kickoutCount++;
                QueueRiderExit(rider, released);
                SelectAfterOffboarding(rider, movedRun, released);
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
                    RememberCabinTileRun(rider, released);
                    if (!draggedWasBoarded)
                    {
                        PlayPassengerBoardingVoice(rider);
                        BeginHandymanRepair(rider);
                    }
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
            SetPassengerGridPreview(null, Vector3.zero, false);
        }

        Rider PickHoveredRider(Vector2 cursor)
        {
            if (!eye.pixelRect.Contains(cursor)) return null;
            Rider closest = null; float distance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(eye.ScreenPointToRay(cursor), 100))
                if (riderHits.TryGetValue(hit.collider, out var rider) && CanSelect(rider))
                {
                    // Use depth for a direct hit. Waiting riders used to be
                    // preferred here, which made a front collider steal clicks
                    // from a visible boarded rider behind it.
                    if (hit.distance < distance)
                    { closest = rider; distance = hit.distance; }
                }

            // The enlarged cabin view can show a rear passenger through the
            // gap above or beside a front passenger. Pick by the projected
            // figure position as well, so that visible space remains usable
            // even when the physics colliders overlap in depth.
            Rider screenClosest = null;
            float screenDistance = float.MaxValue;
            float radius = Mathf.Clamp(Screen.height * .06f, 48f, 96f);
            foreach (var pair in figures)
            {
                if (!CanSelect(pair.Key) || pair.Value == null || !pair.Value.gameObject.activeInHierarchy) continue;
                Transform figure = pair.Value;
                Vector3 point = eye.WorldToScreenPoint(figure.position + Vector3.up * .95f);
                float delta = ((Vector2)point - cursor).sqrMagnitude;
                if (point.z > 0 && delta < radius * radius && delta < screenDistance)
                { screenDistance = delta; screenClosest = pair.Key; }
            }

            // Prefer the screen-space candidate when it is clearly the one the
            // cursor is over; otherwise keep the precise collider hit.
            float colliderScreenDistance = float.MaxValue;
            if (closest != null && figures.TryGetValue(closest, out var colliderFigure)
                && colliderFigure != null)
            {
                Vector3 point = eye.WorldToScreenPoint(colliderFigure.position + Vector3.up * .95f);
                colliderScreenDistance = ((Vector2)point - cursor).sqrMagnitude;
            }
            return screenClosest != null && (closest == null || screenDistance < colliderScreenDistance)
                ? screenClosest : closest;
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

        Dictionary<Rider, Vector2Int> CaptureWaitingSelectionSlots()
        {
            var slots = new Dictionary<Rider, Vector2Int>();
            int slot = 0;
            int columns = OfficeInterior ? OfficeQueueX.Length : int.MaxValue;
            foreach (Rider rider in round.Riders)
            {
                if (rider.Boarded || rider.Resolved || rider.Origin != round.Floor) continue;
                slots[rider] = new Vector2Int(
                    columns == int.MaxValue ? slot : slot % columns,
                    columns == int.MaxValue ? 0 : slot / columns);
                slot++;
            }
            return slots;
        }

        Rider FindWaitingSelectionAfter(Rider moved, Vector2Int movedSlot,
            Dictionary<Rider, Vector2Int> waitingSlots)
        {
            Rider right = null;
            int rightDistance = int.MaxValue;
            Rider left = null;
            int leftDistance = int.MaxValue;
            foreach (Rider candidate in round.Riders)
            {
                if (candidate == moved || candidate.Boarded || !CanSelect(candidate)
                    || !waitingSlots.TryGetValue(candidate, out Vector2Int slot)
                    || slot.y != movedSlot.y) continue;

                int distance = slot.x - movedSlot.x;
                if (distance > 0 && distance < rightDistance)
                {
                    right = candidate;
                    rightDistance = distance;
                }
                else if (distance < 0 && -distance < leftDistance)
                {
                    left = candidate;
                    leftDistance = -distance;
                }
            }
            if (right != null) return right;
            if (left != null) return left;

            // A sparse waiting row can leave available riders on another row.
            // Exhaust those before moving the selection into the cabin.
            Rider nextRow = null;
            int nextRowRank = int.MaxValue;
            int nextColumn = int.MaxValue;
            foreach (Rider candidate in round.Riders)
            {
                if (candidate == moved || candidate.Boarded || !CanSelect(candidate)
                    || !waitingSlots.TryGetValue(candidate, out Vector2Int slot)) continue;
                int rowRank = slot.y >= movedSlot.y
                    ? slot.y - movedSlot.y : CabinRows + movedSlot.y - slot.y;
                if (rowRank < nextRowRank || rowRank == nextRowRank && slot.x < nextColumn)
                {
                    nextRow = candidate;
                    nextRowRank = rowRank;
                    nextColumn = slot.x;
                }
            }
            return nextRow;
        }

        Rider FindOnboardSelectionAfter(Rider moved)
        {
            if (!cabinTileRuns.TryGetValue(moved, out Vector2Int movedRun))
            {
                FindNearestCabinTileRun(moved, RiderPosition(moved), out int row, out int start);
                movedRun = new Vector2Int(start, row);
            }

            for (int row = movedRun.y; row < CabinRows; row++)
            {
                Rider right = null;
                int rightDistance = int.MaxValue;
                Rider left = null;
                int leftDistance = int.MaxValue;
                Rider first = null;
                int firstColumn = int.MaxValue;
                foreach (Rider candidate in round.Riders)
                {
                    if (candidate == moved || !candidate.Boarded || !CanSelect(candidate)) continue;
                    if (!cabinTileRuns.TryGetValue(candidate, out Vector2Int run))
                    {
                        FindNearestCabinTileRun(candidate, RiderPosition(candidate), out int candidateRow, out int candidateStart);
                        run = new Vector2Int(candidateStart, candidateRow);
                    }
                    if (run.y != row) continue;
                    if (row == movedRun.y)
                    {
                        int distance = run.x - movedRun.x;
                        if (distance > 0 && distance < rightDistance)
                        {
                            right = candidate;
                            rightDistance = distance;
                        }
                        else if (distance < 0 && -distance < leftDistance)
                        {
                            left = candidate;
                            leftDistance = -distance;
                        }
                    }
                    else if (run.x < firstColumn)
                    {
                        first = candidate;
                        firstColumn = run.x;
                    }
                }
                if (right != null) return right;
                if (left != null) return left;
                if (first != null) return first;
            }
            return null;
        }

        Vector2Int CabinRunForSelection(Rider rider)
        {
            if (cabinTileRuns.TryGetValue(rider, out Vector2Int run)) return run;
            FindNearestCabinTileRun(rider, RiderPosition(rider), out int row, out int start);
            return new Vector2Int(start, row);
        }

        Rider FindOnboardSelectionAfterOffboarding(Rider moved, Vector2Int movedRun)
        {
            float movedColumn = movedRun.x + Mathf.Clamp(moved.Space, 1, CabinColumns) * .5f - .5f;
            for (int row = movedRun.y; row >= 0; row--)
            {
                Rider best = null;
                float bestDistance = float.MaxValue;
                bool bestIsRight = false;
                foreach (Rider candidate in round.Riders)
                {
                    if (candidate == moved || !candidate.Boarded || !CanSelect(candidate)) continue;
                    Vector2Int run = CabinRunForSelection(candidate);
                    if (run.y != row) continue;
                    float candidateColumn = run.x + Mathf.Clamp(candidate.Space, 1, CabinColumns) * .5f - .5f;
                    float distance = Mathf.Abs(candidateColumn - movedColumn);
                    bool isRight = candidateColumn > movedColumn;
                    if (distance < bestDistance || distance == bestDistance && isRight && !bestIsRight)
                    {
                        best = candidate;
                        bestDistance = distance;
                        bestIsRight = isRight;
                    }
                }
                if (best != null) return best;
            }
            return null;
        }

        Rider FindFirstWaitingSelection()
        {
            foreach (Rider candidate in round.Riders)
                if (!candidate.Boarded && CanSelect(candidate)) return candidate;
            return null;
        }

        Rider FindNearestOnboardSelection(Rider moved, Vector2Int movedRun)
        {
            float movedColumn = movedRun.x + Mathf.Clamp(moved.Space, 1, CabinColumns) * .5f - .5f;
            Rider best = null;
            float bestDistance = float.MaxValue;
            bool bestIsRight = false;
            foreach (Rider candidate in round.Riders)
            {
                if (candidate == moved || !candidate.Boarded || !CanSelect(candidate)) continue;
                Vector2Int run = CabinRunForSelection(candidate);
                float candidateColumn = run.x + Mathf.Clamp(candidate.Space, 1, CabinColumns) * .5f - .5f;
                Vector2 delta = new Vector2(candidateColumn - movedColumn, run.y - movedRun.y);
                float distance = delta.sqrMagnitude;
                bool isRight = candidateColumn > movedColumn;
                if (distance < bestDistance || distance == bestDistance && isRight && !bestIsRight)
                {
                    best = candidate;
                    bestDistance = distance;
                    bestIsRight = isRight;
                }
            }
            return best;
        }

        Rider FindNearestWaitingSelection(Rider moved, Vector3 from)
        {
            Rider best = null;
            float bestDistance = float.MaxValue;
            foreach (Rider candidate in round.Riders)
            {
                if (candidate == moved || candidate.Boarded || !CanSelect(candidate)) continue;
                Vector3 position = figures.TryGetValue(candidate, out var figure) && figure != null
                    ? figure.localPosition : RiderPosition(candidate);
                float distance = (position.x - from.x) * (position.x - from.x)
                    + (position.z - from.z) * (position.z - from.z);
                if (distance >= bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }
            return best;
        }

        void SelectAfterOffboarding(Rider moved, Vector2Int movedRun, Vector3 movedPosition)
        {
            Rider next = movedRun.y == 0
                ? FindNearestOnboardSelection(moved, movedRun)
                : FindOnboardSelectionAfterOffboarding(moved, movedRun);
            if (next != null) SelectRider(next);
            else
            {
                bool elevatorEmpty = true;
                foreach (Rider candidate in round.Riders)
                    if (candidate != moved && candidate.Boarded && round.Owns(candidate))
                    { elevatorEmpty = false; break; }
                if (!elevatorEmpty) return;

                Rider nearestWaiting = FindNearestWaitingSelection(moved, movedPosition);
                if (nearestWaiting != null) SelectRider(nearestWaiting);
                else selectFirstWaitingWhenAvailable = true;
            }
        }

        void SelectAfterBoarding(Rider moved, Vector2Int movedSlot,
            Dictionary<Rider, Vector2Int> waitingSlots)
        {
            Rider next = FindWaitingSelectionAfter(moved, movedSlot, waitingSlots);
            if (next == null) next = FindOnboardSelectionAfter(moved);
            if (next != null)
            {
                pendingSelectionAfterBoarding = null;
                SelectRider(next);
                return;
            }

            // Extended-interior boarding temporarily blocks interaction with
            // the moving figure. Restore the selection as soon as its transfer
            // finishes; otherwise keep it immediately on the moved rider.
            pendingSelectionAfterBoarding = moved;
            if (!boardingTransfers.ContainsKey(moved))
            {
                pendingSelectionAfterBoarding = null;
                SelectRider(moved);
            }
        }

        void ConfirmPassenger()
        {
            if (!CanSelect(selectedRider)) { notice = "Highlight a passenger with the mouse or stick first."; return; }
            Rider rider = selectedRider;
            bool waiting = !rider.Boarded;
            Vector2Int movedSlot = default;
            Dictionary<Rider, Vector2Int> waitingSlots = null;
            if (waiting)
            {
                waitingSlots = CaptureWaitingSelectionSlots();
                waitingSlots.TryGetValue(rider, out movedSlot);
            }
            SelectRider(null);
            if (rider.Boarded)
            {
                Vector2Int movedRun = CabinRunForSelection(rider);
                Vector3 movedPosition = figures[rider].localPosition;
                cabinPositions.Remove(rider); cabinTileRuns.Remove(rider);
                QueueRiderExit(rider, figures[rider].localPosition);
                notice = "Helping " + rider.Name + " out at floor " + round.Floor + ".";
                SelectAfterOffboarding(rider, movedRun, movedPosition);
            }
            else
            {
                BoardPassengerWithPersona(rider);
                if (rider.Boarded) SelectAfterBoarding(rider, movedSlot, waitingSlots);
            }
        }
        void QueueRiderExit(Rider rider, Vector3 start)
        {
            if (rider == null || !round.Owns(rider) || !figures.TryGetValue(rider, out var figure)) return;
            int scoreBefore = round.Score;
            OffboardResult result = round.Offboard(rider);
            if (result == OffboardResult.None) return;
            passengerExitCount++;
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
            public SpriteRenderer FeedbackIcon;
            public Sprite FeedbackSprite;
            public Texture2D FeedbackTexture;
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
                : points == 0 ? "NO POINTS" : "LATE DELIVERY";
            // Correct-delivery points now live in the crisp 2D reward popup.
            // Keep only status on the moving 3D rider; wrong-floor penalties
            // remain attached to the mistake so the consequence is obvious.
            if (result != OffboardResult.WrongFloor && points == 0)
            {
                kick.FeedbackIcon = CreateKickFeedbackIcon(start + Vector3.up * 2.25f,
                    Coral, out kick.FeedbackTexture, out kick.FeedbackSprite);
            }
            else
            {
                string worldFeedback = result == OffboardResult.WrongFloor
                    ? caption + "\n" + points : caption;
                kick.Feedback = Sign(worldFeedback,
                    start + Vector3.up * 2.25f, .025f, points >= 0 ? Gold : Coral);
            }
            RegisterDeliveryPopup(start, result, points);
            kicks[rider] = kick;
        }

        SpriteRenderer CreateKickFeedbackIcon(Vector3 position, Color accent,
            out Texture2D texture, out Sprite sprite)
        {
            texture = PassengerIconLibrary.CreateAngryFace(accent, Ink);
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), texture.width);
            sprite.name = "MAD delivery icon sprite";
            GameObject iconObject = new GameObject("MAD delivery icon");
            iconObject.transform.SetParent(stage, false);
            iconObject.transform.localPosition = position;
            iconObject.transform.localScale = Vector3.one * .48f;
            SpriteRenderer renderer = iconObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 3;
            return renderer;
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
            // Keep the score/icon readable instead of rotating it with the passenger.
            Vector3 feedbackPosition = Vector3.Lerp(kick.Start, kick.Target, .6f)
                + Vector3.up * (2.2f + t * .55f);
            if (kick.Feedback != null)
            {
                kick.Feedback.transform.localPosition = feedbackPosition;
                kick.Feedback.transform.rotation = Quaternion.LookRotation(
                    kick.Feedback.transform.position - eye.transform.position);
                Color color = kick.Feedback.color;
                color.a = 1 - Mathf.Clamp01((t - .8f) / .2f);
                kick.Feedback.color = color;
            }
            else if (kick.FeedbackIcon != null)
            {
                kick.FeedbackIcon.transform.localPosition = feedbackPosition;
                kick.FeedbackIcon.transform.rotation = Quaternion.LookRotation(
                    kick.FeedbackIcon.transform.position - eye.transform.position);
                Color color = kick.FeedbackIcon.color;
                color.a = 1 - Mathf.Clamp01((t - .8f) / .2f);
                kick.FeedbackIcon.color = color;
            }
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
            if (kick.Feedback != null) Destroy(kick.Feedback.gameObject);
            if (kick.FeedbackIcon != null) Destroy(kick.FeedbackIcon.gameObject);
            if (kick.FeedbackSprite != null) Destroy(kick.FeedbackSprite);
            if (kick.FeedbackTexture != null) Destroy(kick.FeedbackTexture);
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
        sealed class FloorMarker
        {
            public Transform Root;
            public Transform Border;
            public TextMesh Label;
        }
        readonly Dictionary<Rider, FloorMarker> floorMarkers = new Dictionary<Rider, FloorMarker>();
        readonly Dictionary<Rider, float> exiting = new Dictionary<Rider, float>();
        readonly Dictionary<Rider, Vector3> exitStarts = new Dictionary<Rider, Vector3>();
        const float SharedPickupDepartureSeconds = .85f;
        sealed class SharedPickupDeparture
        {
            public Vector3 Start, Scale;
            public Quaternion Rotation;
            public float Time;
        }
        readonly Dictionary<Rider, SharedPickupDeparture> sharedPickupDepartures =
            new Dictionary<Rider, SharedPickupDeparture>();
        readonly Dictionary<Collider, Rider> riderHits = new Dictionary<Collider, Rider>();
        sealed class PassengerEffectView
        {
            public readonly Transform[] Tears = new Transform[2];
            public readonly Transform[] ComfortHearts = new Transform[3];
            public float Clock;
        }
        readonly Dictionary<Rider, PassengerEffectView> passengerEffects =
            new Dictionary<Rider, PassengerEffectView>();
        static readonly float[] OfficeQueueX = { -1.2f, 0f, 1.2f };
        static readonly Color BabyTearColor = new Color32(75, 220, 255, 255);
        static readonly Color GrandmaComfortColor = new Color32(255, 211, 61, 255);

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
            if (extendedInterior) CreateFloorMarker(p);
            foreach (var collider in root.GetComponentsInChildren<Collider>()) riderHits[collider] = p;
            AttachPatienceBar(p, root);
            EnsurePassengerEffect(p, root);
            // Extended mode uses one screen-space callout system in both solo
            // and 1v1. Keep the authored world bubble only for legacy layouts.
            if (Match != null && !extendedInterior) AttachSpeechBubble(p, root);
            return root;
        }

        void CreateFloorMarker(Rider rider)
        {
            if (rider == null || stage == null || floorMarkers.ContainsKey(rider)) return;
            var root = new GameObject(rider.Name + " floor marker").transform;
            root.SetParent(stage, false);

            Transform border = Shape("Destination floor marker border", PrimitiveType.Cube,
                new Vector3(0, -.018f, 0), new Vector3(.54f, .022f, .40f), Cream, root);
            Transform panel = Shape("Destination floor marker panel", PrimitiveType.Cube,
                new Vector3(0, .002f, 0), new Vector3(.44f, .028f, .30f), Ink, root);
            DisableCollider(border);
            DisableCollider(panel);

            var labelObject = new GameObject("Destination floor marker number");
            labelObject.transform.SetParent(root, false);
            labelObject.transform.localPosition = new Vector3(0, .026f, 0);
            // Lay the number face-up on the cabin floor, then turn it around
            // the floor normal so its top points away from the rear camera.
            labelObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f)
                * Quaternion.Euler(90f, 0f, 0f);
            labelObject.transform.localScale = Vector3.one;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.fontSize = 72;
            label.characterSize = .030f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.color = Cream;
            GameTypography.Apply(label, true);
            MeshRenderer renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 30;
            }

            root.gameObject.SetActive(false);
            floorMarkers[rider] = new FloorMarker { Root = root, Border = border, Label = label };
        }

        void UpdateFloorMarker(Rider rider, Transform figure, bool hidden)
        {
            if (!floorMarkers.TryGetValue(rider, out FloorMarker marker) || marker == null) return;
            bool show = extendedInterior && rider.Boarded && !hidden
                && !boardingTransfers.ContainsKey(rider);
            marker.Root.gameObject.SetActive(show);
            if (!show) return;

            marker.Root.position = PassengerFloorMarkerAnchor(rider, figure);
            marker.Root.localRotation = Quaternion.identity;
            marker.Label.text = rider.Destination.ToString();
            bool selected = rider == selectedRider || rider == draggedRider;
            Color borderColor = selected
                ? selectionTint == BoardingInvalid ? BoardingInvalid : BoardingValid
                : Cream;
            Renderer borderRenderer = marker.Border.GetComponent<Renderer>();
            if (borderRenderer != null) borderRenderer.sharedMaterial = Mat(borderColor);
        }

        // Make the baby state readable without relying on audio or text alone.
        void EnsurePassengerEffect(Rider rider, Transform figure)
        {
            if (rider == null || figure == null || passengerEffects.ContainsKey(rider)) return;
            var effect = new PassengerEffectView();
            if (rider.HasFeature(PassengerFeature.CryingBaby))
            {
                Transform baby = figure.Find("Baby");
                Transform tearParent = baby != null ? baby : figure;
                effect.Tears[0] = EffectPart(tearParent, "Baby left tear", PrimitiveType.Sphere,
                    new Vector3(-.10f, 1.16f, -.79f), new Vector3(.065f, .12f, .05f), BabyTearColor);
                effect.Tears[1] = EffectPart(tearParent, "Baby right tear", PrimitiveType.Sphere,
                    new Vector3(.10f, 1.16f, -.79f), new Vector3(.065f, .12f, .05f), BabyTearColor);
            }
            if (rider.HasFeature(PassengerFeature.CalmsBaby))
            {
                effect.ComfortHearts[0] = ComfortHeart(figure, "Grandma comfort heart left", new Vector3(-.52f, 1.72f, -.43f));
                effect.ComfortHearts[1] = ComfortHeart(figure, "Grandma comfort heart center", new Vector3(0f, 2.02f, -.43f));
                effect.ComfortHearts[2] = ComfortHeart(figure, "Grandma comfort heart right", new Vector3(.52f, 1.72f, -.43f));
            }
            passengerEffects.Add(rider, effect);
        }

        Transform EffectPart(Transform parent, string name, PrimitiveType kind, Vector3 position,
            Vector3 scale, Color color)
        {
            Transform part = Shape(name, kind, position, scale, color, parent);
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            return part;
        }

        Transform ComfortHeart(Transform parent, string name, Vector3 position)
        {
            var heart = new GameObject(name).transform;
            heart.SetParent(parent, false);
            heart.localPosition = position;
            EffectPart(heart, "Heart left", PrimitiveType.Sphere,
                new Vector3(-.065f, .035f, 0f), new Vector3(.10f, .10f, .06f), GrandmaComfortColor);
            EffectPart(heart, "Heart right", PrimitiveType.Sphere,
                new Vector3(.065f, .035f, 0f), new Vector3(.10f, .10f, .06f), GrandmaComfortColor);
            EffectPart(heart, "Heart point", PrimitiveType.Cube,
                new Vector3(0f, -.035f, 0f), new Vector3(.105f, .105f, .06f), GrandmaComfortColor);
            return heart;
        }

        void UpdatePassengerEffects(float dt)
        {
            foreach (var pair in passengerEffects)
            {
                Rider rider = pair.Key;
                PassengerEffectView effect = pair.Value;
                if (!figures.TryGetValue(rider, out Transform figure) || figure == null)
                    continue;

                effect.Clock += Mathf.Max(0f, dt);
                bool owned = round != null && round.Owns(rider) && figure.gameObject.activeInHierarchy;
                bool babyCrying = owned && rider.HasFeature(PassengerFeature.CryingBaby)
                    && !round.BabyCalmedByGrandma;
                for (int i = 0; i < effect.Tears.Length; i++)
                {
                    Transform tear = effect.Tears[i];
                    if (tear == null) continue;
                    tear.gameObject.SetActive(babyCrying);
                    if (!babyCrying) continue;
                    float cycle = Mathf.Repeat(effect.Clock * 1.65f + i * .47f, 1f);
                    float side = i == 0 ? -.10f : .10f;
                    tear.localPosition = new Vector3(side + Mathf.Sin(effect.Clock * 5f + i) * .025f,
                        1.16f - cycle * .36f, -.79f);
                    float size = Mathf.Lerp(1.05f, .72f, cycle);
                    tear.localScale = new Vector3(.065f, .12f, .05f) * size;
                }

                bool grandmaHelping = owned && rider.HasFeature(PassengerFeature.CalmsBaby)
                    && round.BabyCalmedByGrandma;
                for (int i = 0; i < effect.ComfortHearts.Length; i++)
                {
                    Transform heart = effect.ComfortHearts[i];
                    if (heart == null) continue;
                    heart.gameObject.SetActive(grandmaHelping);
                    if (!grandmaHelping) continue;
                    float phase = effect.Clock * 2.2f + i * 2.1f;
                    Vector3 basePosition = i == 0 ? new Vector3(-.52f, 1.72f, -.43f)
                        : i == 1 ? new Vector3(0f, 2.02f, -.43f)
                        : new Vector3(.52f, 1.72f, -.43f);
                    heart.localPosition = basePosition + new Vector3(Mathf.Sin(phase) * .08f,
                        Mathf.Sin(phase * 1.3f) * .10f, 0f);
                    float pulse = 1f + Mathf.Sin(phase * 1.5f) * .16f;
                    heart.localScale = Vector3.one * pulse;
                }
            }
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

        // A shared rider claimed by the other car walks out of this hallway
        // before its local visual is hidden. The Rider itself is already boarded.
        void BeginSharedPickupDeparture(Rider rider, Transform figure)
        {
            if (draggedRider == rider) CancelPassengerDrag();
            if (selectedRider == rider) SelectRider(null);
            sharedPickupDepartures.Add(rider, new SharedPickupDeparture
            {
                Start = figure.localPosition,
                Scale = figure.localScale,
                Rotation = figure.localRotation
            });
            foreach (var collider in figure.GetComponentsInChildren<Collider>()) collider.enabled = false;
        }

        void AnimateSharedPickupDeparture(Rider rider, Transform figure, float dt)
        {
            SharedPickupDeparture departure = sharedPickupDepartures[rider];
            departure.Time += Mathf.Max(0f, dt);
            float progress = Mathf.Clamp01(departure.Time / SharedPickupDepartureSeconds);
            float stride = Mathf.Sin(progress * Mathf.PI * 6f);
            float side = Seat == 0 ? 1f : -1f;
            Vector3 destination = new Vector3(side * 3.35f, departure.Start.y, departure.Start.z - .35f);
            figure.localPosition = Vector3.Lerp(departure.Start, destination,
                Mathf.SmoothStep(0f, 1f, progress))
                + Vector3.up * (Mathf.Abs(stride) * .10f * (1f - progress));
            Quaternion walking = Quaternion.Euler(0f, side * 90f, stride * 5f);
            figure.localRotation = Quaternion.Slerp(departure.Rotation, walking,
                Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .2f)));
            float shrink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1f, progress));
            figure.localScale = departure.Scale * Mathf.Lerp(1f, .12f, shrink);
            if (progress < 1f) return;
            sharedPickupDepartures.Remove(rider);
            figure.gameObject.SetActive(false);
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
                bool hasFigure = figures.TryGetValue(p, out var figure);
                if (Match != null && hasFigure && figure.gameObject.activeInHierarchy
                    && hallVisible && p.Boarded && !p.Resolved && p.BoardedBySeat != Seat
                    && p.Origin == round.Floor && !sharedPickupDepartures.ContainsKey(p))
                    BeginSharedPickupDeparture(p, figure);
                bool departing = sharedPickupDepartures.ContainsKey(p);
                bool visible = departing || isExiting || !p.Resolved && (round.Owns(p)
                    || !p.Boarded && p.Origin == round.Floor && hallVisible && round.IsOffered(p));
                if (visible && !departing && !isExiting && !p.Boarded && round.IsOffered(p))
                    DiscoverPassenger(p);
                if (!hasFigure)
                {
                    if (!visible) continue;
                    figure = MakeRider(p); figure.localPosition = RiderPosition(p); figures.Add(p, figure);
                }
                figure.gameObject.SetActive(visible);
                if (!visible)
                {
                    UpdateFloorMarker(p, figure, true);
                    continue;
                }

                if (departing)
                {
                    AnimateSharedPickupDeparture(p, figure, dt);
                }
                else if (isExiting)
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
                        // Mom keeps facing the player so the carried baby and
                        // crying/calming effects remain readable after boarding.
                        float facing = p.HasFeature(PassengerFeature.CryingBaby) ? 180f
                            : p.Boarded ? 0f : 180f;
                        figure.localRotation = Quaternion.Euler(0, facing, 0);
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
                        bubble.gameObject.SetActive(!isExiting && !departing);
                        bubble.text = BubbleText(p);
                        bubble.color = IsComplaining(p) ? Coral : Cream;
                        bubble.transform.rotation = Quaternion.LookRotation(bubble.transform.position - eye.transform.position);
                    }
                }
                if (destinationTags.TryGetValue(p, out var destinationTag))
                {
                    bool useScreenCallout = extendedInterior;
                    destinationTag.gameObject.SetActive(!useScreenCallout && !isExiting && !departing);
                    destinationTag.text = p.Destination.ToString();
                    // Keep the actionable number a single, dark high-contrast
                    // color. Mood is already communicated by the patience bar
                    // and speech bubble, so it should not reduce legibility.
                    destinationTag.color = Ink;
                    destinationTag.transform.position = PassengerDestinationAnchor(p, figure);
                    destinationTag.transform.rotation = Quaternion.LookRotation(destinationTag.transform.position - eye.transform.position);
                }
                UpdateFloorMarker(p, figure, isExiting || departing);
                UpdatePatienceBar(p, isExiting);
            }
            UpdateOccasionalSpeech(dt);
            foreach (var p in completedExits)
            {
                EndKick(p);
                exiting.Remove(p); exitStarts.Remove(p);
                if (figures.TryGetValue(p, out var figure)) figure.gameObject.SetActive(false);
            }
            UpdatePassengerEffects(dt);
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
            PassengerView view = figure.GetComponent<PassengerView>();
            // Sit just above the authored passenger's head, below the small
            // destination badge, instead of floating near the ceiling.
            bar.transform.localPosition = new Vector3(0, view != null ? view.patienceBarHeight : 1.88f, 0);
            bar.Bind(eye, view != null ? view.patienceBarCameraOffset : 0f);
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
                bool babyPersistent = rider.Boarded && rider.HasFeature(PassengerFeature.CryingBaby);
                // MAD is already rendered as the passenger's single 2D
                // callout icon. Never create a second world-space callout
                // for a rider whose patience has run out.
                if (rider.Boarded && rider.Remaining <= 0)
                {
                    bubble.Hide();
                    continue;
                }

                string message = temporaryAlert ? rider.Remaining <= 0 ? "I'M MAD!" : "PLEASE HURRY!"
                    : missedPersistent ? "MISSED MY FLOOR!"
                    : destinationPersistent ? "MY STOP!"
                    : babyPersistent ? round.BabyCalmedByGrandma ? "GRANDMA CALMED ME!" : "WAAAH! WAAAH!"
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
            if (rider.Kind == "PREGNANT") return "BABY ON BOARD!";
            if (rider.Kind == "ELDERLY") return "I CAN CALM BABIES!";
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
