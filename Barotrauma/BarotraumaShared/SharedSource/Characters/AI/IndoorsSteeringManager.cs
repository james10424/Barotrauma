﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using FarseerPhysics;

namespace Barotrauma
{
    class IndoorsSteeringManager : SteeringManager
    {
        private PathFinder pathFinder;
        private SteeringPath currentPath;

        private bool canOpenDoors;
        public bool CanBreakDoors { get; set; }

        private bool ShouldBreakDoor(Door door) =>
            CanBreakDoors &&
            !door.Item.Indestructible && !door.Item.InvulnerableToDamage &&
            (door.Item.Submarine == null || door.Item.Submarine.TeamID != character.TeamID);

        private Character character;

        private Vector2 currentTarget;

        private float findPathTimer;

        private float buttonPressCooldown;

        const float ButtonPressInterval = 0.25f;

        public SteeringPath CurrentPath
        {
            get { return currentPath; }
        }

        public PathFinder PathFinder
        {
            get { return pathFinder; }
        }

        public Vector2 CurrentTarget
        {
            get { return currentTarget; }
        }

        public bool IsPathDirty
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns true if the current or the next node is in ladders.
        /// </summary>
        public bool InLadders =>
            currentPath != null && currentPath.CurrentNode != null &&
            (currentPath.CurrentNode.Ladders != null && currentPath.CurrentNode.Ladders.Item.IsInteractable(character) ||
             (currentPath.NextNode != null && currentPath.NextNode.Ladders != null && currentPath.NextNode.Ladders.Item.IsInteractable(character)));

        /// <summary>
        /// Returns true if any node in the path is in stairs
        /// </summary>
        public bool InStairs => currentPath != null && currentPath.Nodes.Any(n => n.Stairs != null);

        public bool IsNextNodeLadder => GetNextLadder() != null;

        public bool IsNextLadderSameAsCurrent
        {
            get
            {
                if (currentPath == null) { return false; }
                if (currentPath.CurrentNode == null) { return false; }
                if (currentPath.NextNode == null) { return false; }
                var currentLadder = currentPath.CurrentNode.Ladders;
                if (currentLadder == null) { return false; }
                if (!currentLadder.Item.IsInteractable(character)) { return false; }
                var nextLadder = GetNextLadder();
                return nextLadder != null && nextLadder == currentLadder;
            }
        }

        public IndoorsSteeringManager(ISteerable host, bool canOpenDoors, bool canBreakDoors) : base(host)
        {
            pathFinder = new PathFinder(WayPoint.WayPointList.FindAll(wp => wp.SpawnType == SpawnType.Path), true);
            pathFinder.GetNodePenalty = GetNodePenalty;

            this.canOpenDoors = canOpenDoors;
            this.CanBreakDoors = canBreakDoors;

            character = (host as AIController).Character;

            findPathTimer = Rand.Range(0.0f, 1.0f);
        }

        public override void Update(float speed)
        {
            base.Update(speed);

            buttonPressCooldown -= 1.0f / 60.0f;
            findPathTimer -= 1.0f / 60.0f;
        }

        public void SetPath(SteeringPath path)
        {
            currentPath = path;
            if (path.Nodes.Any()) currentTarget = path.Nodes[path.Nodes.Count - 1].SimPosition;
            findPathTimer = Math.Min(findPathTimer, 1.0f);
            IsPathDirty = false;
        }

        public void ResetPath()
        {
            currentPath = null;
            IsPathDirty = true;
        }

        public void SteeringSeekSimple(Vector2 targetSimPos, float weight = 1)
        {
            steering += base.DoSteeringSeek(targetSimPos, weight);
        }

        public void SteeringSeek(Vector2 target, float weight, float minGapWidth = 0, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null, bool checkVisiblity = true)
        {
            steering += CalculateSteeringSeek(target, weight, minGapWidth, startNodeFilter, endNodeFilter, nodeFilter, checkVisiblity);
        }

        /// <summary>
        /// Seeks the ladder from the next and next + 1 nodes.
        /// </summary>
        public Ladder GetNextLadder()
        {
            if (currentPath == null) { return null; }
            if (currentPath.NextNode == null) { return null; }
            if (currentPath.NextNode.Ladders != null && currentPath.NextNode.Ladders.Item.IsInteractable(character))
            {
                return currentPath.NextNode.Ladders;
            }
            else
            {
                int index = currentPath.CurrentIndex + 2;
                if (currentPath.Nodes.Count > index)
                {
                    var node = currentPath.Nodes[index];
                    if (node == null) { return null; }
                    if (node.Ladders != null && node.Ladders.Item.IsInteractable(character))
                    {
                        return node.Ladders;
                    }
                    //if the next node is a hatch, check if the node after that is a ladder
                    else if (node.ConnectedDoor != null && node.ConnectedDoor.IsHorizontal)
                    {
                        index++;
                        if (currentPath.Nodes.Count > index)
                        {
                            node = currentPath.Nodes[index];
                            if (node == null) { return null; }
                            if (node.Ladders != null && node.Ladders.Item.IsInteractable(character))
                            {
                                return node.Ladders;
                            }
                        }
                    }

                }
                return null;
            }
        }

        private Vector2 CalculateSteeringSeek(Vector2 target, float weight, float minGapSize = 0, Func<PathNode, bool> startNodeFilter = null, Func<PathNode, bool> endNodeFilter = null, Func<PathNode, bool> nodeFilter = null, bool checkVisibility = true)
        {
            bool needsNewPath = currentPath == null || currentPath.Unreachable || currentPath.Finished;
            if (!needsNewPath && character.Submarine != null && character.Params.PathFinderPriority > 0.5f)
            {
                Vector2 targetDiff = target - currentTarget;
                if (currentPath != null && currentPath.Nodes.Any() && character.Submarine != null)
                {
                    //target in a different sub than where the character is now
                    //take that into account when calculating if the target has moved
                    Submarine currentPathSub = currentPath?.CurrentNode?.Submarine;
                    if (currentPathSub == character.Submarine) { currentPathSub = currentPath?.Nodes.LastOrDefault()?.Submarine; }
                    if (currentPathSub != character.Submarine && targetDiff.LengthSquared() > 1 && currentPathSub != null)
                    {
                        Vector2 subDiff = character.Submarine.SimPosition - currentPathSub.SimPosition;
                        targetDiff += subDiff;
                    }
                }
                if (targetDiff.LengthSquared() > 1)
                {
                    needsNewPath = true;
                }
            }
            //find a new path if one hasn't been found yet or the target is different from the current target
            if (needsNewPath || findPathTimer < -1.0f)
            {
                IsPathDirty = true;
                if (findPathTimer < 0)
                {
                    SkipCurrentPathNodes();
                    currentTarget = target;
                    Vector2 currentPos = host.SimPosition;
                    pathFinder.InsideSubmarine = character.Submarine != null && !character.Submarine.Info.IsRuin;
                    pathFinder.ApplyPenaltyToOutsideNodes = character.Submarine !=  null && character.PressureProtection <= 0;
                    var newPath = pathFinder.FindPath(currentPos, target, character.Submarine, "(Character: " + character.Name + ")", minGapSize, startNodeFilter, endNodeFilter, nodeFilter, checkVisibility: checkVisibility);
                    bool useNewPath = needsNewPath || currentPath == null || currentPath.CurrentNode == null || character.Submarine != null && findPathTimer < -1 && Math.Abs(character.AnimController.TargetMovement.X) <= 0;
                    if (newPath.Unreachable || newPath.Nodes.None())
                    {
                        useNewPath = false;
                    }
                    else if (!useNewPath && currentPath != null && currentPath.CurrentNode != null)
                    {
                        // Check if the new path is the same as the old, in which case we just ignore it and continue using the old path (or the progress would reset).
                        if (IsIdenticalPath())
                        {
                            useNewPath = false;
                        }
                        else
                        {
                            // Use the new path if it has significantly lower cost (don't change the path if it has marginally smaller cost. This reduces navigating backwards due to new path that is calculated from the node just behind us).
                            float t = (float)currentPath.CurrentIndex / (currentPath.Nodes.Count - 1);
                            useNewPath = newPath.Cost < currentPath.Cost * MathHelper.Lerp(0.95f, 0, t);
                            if (!useNewPath && character.Submarine != null)
                            {
                                // It's possible that the current path was calculated from a start point that is no longer valid.
                                // Therefore, let's accept also paths with a greater cost than the current, if the current node is much farther than the new start node.
                                useNewPath = Vector2.DistanceSquared(character.WorldPosition, currentPath.CurrentNode.WorldPosition) > Math.Pow(Vector2.Distance(character.WorldPosition, newPath.Nodes.First().WorldPosition) * 3, 2);
                            }
                        }

                        bool IsIdenticalPath()
                        {
                            int nodeCount = newPath.Nodes.Count;
                            if (nodeCount == currentPath.Nodes.Count)
                            {
                                for (int i = 0; i < nodeCount - 1; i++)
                                {
                                    if (newPath.Nodes[i] != currentPath.Nodes[i])
                                    {
                                        return false;
                                    }
                                }
                                return true;
                            }
                            return false;
                        }
                    }
                    if (useNewPath)
                    {
                        if (currentPath != null)
                        {
                            CheckDoorsInPath();
                        }
                        currentPath = newPath;
                    }
                    float priority = MathHelper.Lerp(3, 1, character.Params.PathFinderPriority);
                    findPathTimer = priority * Rand.Range(1.0f, 1.2f);
                    IsPathDirty = false;
                    return DiffToCurrentNode();

                    void SkipCurrentPathNodes()
                    {
                        if (!character.AnimController.InWater || character.Submarine != null) { return; }
                        if (CurrentPath == null || CurrentPath.Unreachable || CurrentPath.Finished) { return; }
                        if (CurrentPath.CurrentIndex < 0 || CurrentPath.CurrentIndex >= CurrentPath.Nodes.Count - 1) { return; }
                        // Check if we could skip ahead to NextNode when the character is swimming and using waypoints outside.
                        // Do this to optimize the old path before creating and evaluating a new path.
                        // In general, this is to avoid behavior where:
                        // a) the character goes back to first reach CurrentNode when the second node would be closer; or
                        // b) the character moves along the path when they could cut through open space to reduce the total distance.
                        float pathDistance = Vector2.Distance(character.WorldPosition, CurrentPath.CurrentNode.WorldPosition);
                        pathDistance += CurrentPath.GetLength(startIndex: CurrentPath.CurrentIndex);
                        for (int i = CurrentPath.Nodes.Count - 1; i > CurrentPath.CurrentIndex + 1; i--)
                        {
                            var waypoint = CurrentPath.Nodes[i];
                            float directDistance = Vector2.DistanceSquared(character.WorldPosition, waypoint.WorldPosition);
                            if (directDistance > (pathDistance * pathDistance) || Submarine.PickBody(host.SimPosition, waypoint.SimPosition, collisionCategory: Physics.CollisionLevel | Physics.CollisionWall) != null)
                            {
                                pathDistance -= CurrentPath.GetLength(startIndex: i - 1, endIndex: i);
                                continue;
                            }
                            CurrentPath.SkipToNode(i);
                            break;
                        }
                    }
                }
            }

            Vector2 diff = DiffToCurrentNode();
            var collider = character.AnimController.Collider;
            // Only humanoids can climb ladders
            bool canClimb = character.AnimController is HumanoidAnimController;
            //if not in water and the waypoint is between the top and bottom of the collider, no need to move vertically
            if (canClimb && !character.AnimController.InWater && !character.IsClimbing && diff.Y < collider.height / 2 + collider.radius)
            {
                diff.Y = 0.0f;
            }
            if (diff == Vector2.Zero) { return Vector2.Zero; }
            return Vector2.Normalize(diff) * weight;
        }

        protected override Vector2 DoSteeringSeek(Vector2 target, float weight) => CalculateSteeringSeek(target, weight);

        private Vector2 DiffToCurrentNode()
        {
            if (currentPath == null || currentPath.Unreachable)
            {
                return Vector2.Zero;
            }
            if (currentPath.Finished)
            {
                Vector2 pos2 = host.SimPosition;
                if (character != null && character.Submarine == null &&
                    CurrentPath.Nodes.Count > 0 && CurrentPath.Nodes.Last().Submarine != null)
                {
                    pos2 -= CurrentPath.Nodes.Last().Submarine.SimPosition;
                }
                return currentTarget - pos2;
            }
            bool doorsChecked = false;
            if (!character.LockHands && buttonPressCooldown <= 0.0f)
            {
                CheckDoorsInPath();
                doorsChecked = true;
            }       
            Vector2 pos = host.SimPosition;
            if (character != null && CurrentPath.CurrentNode != null)
            {
                var nodeSub = CurrentPath.CurrentNode.Submarine;
                if (nodeSub != null)
                {
                    if (character.Submarine == null)
                    {
                        // Going inside
                        pos -= ConvertUnits.ToSimUnits(nodeSub.Position);
                    }
                    else if (character.Submarine != nodeSub)
                    {
                        // Different subs
                        pos -= ConvertUnits.ToSimUnits(nodeSub.Position - character.Submarine.Position);
                    }
                }
                else if (character.Submarine != null)
                {
                    // Going outside
                    pos += ConvertUnits.ToSimUnits(character.Submarine.Position);
                }
            }
            bool isDiving = character.AnimController.InWater && character.AnimController.HeadInWater;
            // Only humanoids can climb ladders
            bool canClimb = character.AnimController is HumanoidAnimController && !character.LockHands;
            Ladder currentLadder = currentPath.CurrentNode.Ladders;
            if (currentLadder != null && !currentLadder.Item.IsInteractable(character))
            {
                currentLadder = null;
            }
            Ladder nextLadder = GetNextLadder();
            var ladders = currentLadder ?? nextLadder;
            bool useLadders = canClimb && ladders != null && (!isDiving || Math.Abs(steering.X) < 0.1f && Math.Abs(steering.Y) > 1);
            if (useLadders && character.SelectedConstruction != ladders.Item)
            {
                if (character.CanInteractWith(ladders.Item))
                {
                    ladders.Item.TryInteract(character, false, true);
                }
                else
                {
                    // Cannot interact with the current (or next) ladder,
                    // Try to select the previous ladder, unless it's already selected, unless the previous ladder is not adjacent to the current ladder.
                    // The intention of this code is to prevent the bots from dropping from the "double ladders".
                    var previousLadders = currentPath.PrevNode?.Ladders;
                    if (previousLadders != null && previousLadders != ladders && character.SelectedConstruction != previousLadders.Item &&
                        character.CanInteractWith(previousLadders.Item) && Math.Abs(previousLadders.Item.WorldPosition.X - ladders.Item.WorldPosition.X) < 5)
                    {
                        previousLadders.Item.TryInteract(character, false, true);
                    }
                }
            }
            var collider = character.AnimController.Collider;
            if (character.IsClimbing && !useLadders)
            {
                character.AnimController.Anim = AnimController.Animation.None;
                character.SelectedConstruction = null;
            }
            if (character.IsClimbing && useLadders)
            {
                Vector2 diff = currentPath.CurrentNode.SimPosition - pos;
                bool nextLadderSameAsCurrent = IsNextLadderSameAsCurrent;
                if (nextLadderSameAsCurrent)
                {
                    //climbing ladders -> don't move horizontally
                    diff.X = 0.0f;
                }
                //at the same height as the waypoint
                if (Math.Abs(collider.SimPosition.Y - currentPath.CurrentNode.SimPosition.Y) < (collider.height / 2 + collider.radius) * 1.25f)
                {
                    float heightFromFloor = character.AnimController.GetColliderBottom().Y - character.AnimController.FloorY;
                    if (heightFromFloor <= 0.0f)
                    {
                        diff.Y = Math.Max(diff.Y, 1.0f);
                    }
                    // We need some margin, because if a hatch has closed, it's possible that the height from floor is slightly negative.
                    bool isAboveFloor = heightFromFloor > -0.1f;
                    // If the next waypoint is horizontally far, we don't want to keep holding the ladders
                    if (isAboveFloor && (nextLadder == null || Math.Abs(currentPath.CurrentNode.WorldPosition.X - currentPath.NextNode.WorldPosition.X) > 50))
                    {
                        character.AnimController.Anim = AnimController.Animation.None;
                        character.SelectedConstruction = null;
                    }
                    else if (nextLadder != null && !nextLadderSameAsCurrent)
                    {
                        // Try to change the ladder (hatches between two submarines)
                        if (character.SelectedConstruction != nextLadder.Item && nextLadder.Item.IsInsideTrigger(character.WorldPosition))
                        {
                            nextLadder.Item.TryInteract(character, false, true);
                        }
                    }
                    if (isAboveFloor || nextLadderSameAsCurrent)
                    {
                        NextNode(!doorsChecked);
                    }
                }
                else if (nextLadder != null)
                {
                    //if the current node is below the character and the next one is above (or vice versa)
                    //and both are on ladders, we can skip directly to the next one
                    //e.g. no point in going down to reach the starting point of a path when we could go directly to the one above
                    if (Math.Sign(currentPath.CurrentNode.WorldPosition.Y - character.WorldPosition.Y) != Math.Sign(currentPath.NextNode.WorldPosition.Y - character.WorldPosition.Y))
                    {
                        NextNode(!doorsChecked);
                    }
                }
                return diff;
            }
            else if (character.AnimController.InWater)
            {
                var door = currentPath.CurrentNode.ConnectedDoor;
                if (door == null || door.CanBeTraversed)
                {
                    float margin = MathHelper.Lerp(1, 5, MathHelper.Clamp(collider.LinearVelocity.Length() / 10, 0, 1));
                    Vector2 colliderSize = collider.GetSize();
                    float targetDistance = Math.Max(Math.Max(colliderSize.X, colliderSize.Y) / 2 * margin, 0.5f);
                    float horizontalDistance = Math.Abs(character.WorldPosition.X - currentPath.CurrentNode.WorldPosition.X);
                    float verticalDistance = Math.Abs(character.WorldPosition.Y - currentPath.CurrentNode.WorldPosition.Y);
                    if (character.CurrentHull != currentPath.CurrentNode.CurrentHull)
                    {
                        verticalDistance *= 2;
                    }
                    float distance = horizontalDistance + verticalDistance;
                    if (ConvertUnits.ToSimUnits(distance) < targetDistance)
                    {
                        NextNode(!doorsChecked);
                    }
                }
            }
            else
            {
                // Walking horizontally
                Vector2 colliderBottom = character.AnimController.GetColliderBottom();
                Vector2 colliderSize = collider.GetSize();
                Vector2 velocity = collider.LinearVelocity;
                // If the character is very short, it would fail to use the waypoint nodes because they are always too high.
                // If the character is very thin, it would often fail to reach the waypoints, because the horizontal distance is too small.
                // Both values are based on the human size. So basically anything smaller than humans are considered as equal in size.
                float minHeight = 1.6125001f;
                float minWidth = 0.3225f;
                // Cannot use the head position, because not all characters have head or it can be below the total height of the character
                float characterHeight = Math.Max(colliderSize.Y + character.AnimController.ColliderHeightFromFloor, minHeight);
                float horizontalDistance = Math.Abs(collider.SimPosition.X - currentPath.CurrentNode.SimPosition.X);
                bool isAboveFeet = currentPath.CurrentNode.SimPosition.Y > colliderBottom.Y;
                bool isNotTooHigh = currentPath.CurrentNode.SimPosition.Y < colliderBottom.Y + characterHeight;
                var door = currentPath.CurrentNode.ConnectedDoor;
                float margin = MathHelper.Lerp(1, 10, MathHelper.Clamp(Math.Abs(velocity.X) / 5, 0, 1));
                float targetDistance = Math.Max(colliderSize.X / 2 * margin, minWidth / 2);
                if (horizontalDistance < targetDistance && isAboveFeet && isNotTooHigh && (door == null || door.CanBeTraversed))
                {
                    NextNode(!doorsChecked);
                }
            }
            if (currentPath.CurrentNode == null)
            {
                return Vector2.Zero;
            }
            return currentPath.CurrentNode.SimPosition - pos;
        }

        private void NextNode(bool checkDoors)
        {
            if (checkDoors)
            {
                CheckDoorsInPath();
            }
            currentPath.SkipToNextNode();
        }

        private bool CanAccessDoor(Door door, Func<Controller, bool> buttonFilter = null)
        {
            if (door.IsBroken) { return true; }
            if (!door.IsOpen)
            {
                if (!door.Item.IsInteractable(character)) { return false; }
                if (!ShouldBreakDoor(door))
                {
                    if (door.IsStuck || door.IsJammed) { return false; }
                    if (!canOpenDoors || character.LockHands) { return false; }
                }
            }
            if (door.HasIntegratedButtons)
            {
                return door.IsOpen || door.HasAccess(character) || ShouldBreakDoor(door);
            }
            else
            {
                // We'll want this to run each time, because the delegate is used to find a valid button component.
                bool canAccessButtons = door.Item.GetConnectedComponents<Controller>(true).Any(b => b.HasAccess(character) && (buttonFilter == null || buttonFilter(b)));
                return canAccessButtons || door.IsOpen || ShouldBreakDoor(door);
            }
        }

        private Vector2 GetColliderSize() => ConvertUnits.ToDisplayUnits(character.AnimController.Collider.GetSize());

        private float GetColliderLength()
        {
            Vector2 colliderSize = character.AnimController.Collider.GetSize();
            return ConvertUnits.ToDisplayUnits(Math.Max(colliderSize.X, colliderSize.Y));
        }

        private void CheckDoorsInPath()
        {
            if (!canOpenDoors) { return; }
            for (int i = 0; i < 5; i++)
            {
                WayPoint currentWaypoint = null;
                WayPoint nextWaypoint = null;
                Door door = null;
                bool shouldBeOpen = false;

                if (currentPath.Nodes.Count == 1)
                {
                    door = currentPath.Nodes.First().ConnectedDoor;
                    shouldBeOpen = door != null;
                    if (i > 0) { break; }
                }
                else
                {
                    bool closeDoors = character.IsBot && character.IsInFriendlySub || character.Params.AI != null && character.Params.AI.KeepDoorsClosed;
                    if (i == 0 || !closeDoors)
                    {
                        currentWaypoint = currentPath.CurrentNode;
                        nextWaypoint = currentPath.NextNode;
                    }
                    else
                    {
                        int previousIndex = currentPath.CurrentIndex - i;
                        if (previousIndex < 0) { break; }
                        currentWaypoint = currentPath.Nodes[previousIndex];
                        nextWaypoint = currentPath.CurrentNode;
                    }
                    if (currentWaypoint?.ConnectedDoor == null) { continue; }

                    if (nextWaypoint == null)
                    {
                        //the node we're heading towards is the last one in the path, and at a door
                        //the door needs to be open for the character to reach the node
                        if (currentWaypoint.ConnectedDoor.LinkedGap != null)
                        {
                            // Keep the airlock doors closed, but not in ruins/wrecks
                            if (currentWaypoint.ConnectedDoor.LinkedGap.IsRoomToRoom || currentWaypoint.Submarine?.Info.IsRuin != null || currentWaypoint.Submarine?.Info.IsWreck != null)
                            {
                                shouldBeOpen = true;
                                door = currentWaypoint.ConnectedDoor;
                            }
                        }
                    }
                    else
                    {
                        float colliderLength = GetColliderLength();
                        door = currentWaypoint.ConnectedDoor;
                        if (door.LinkedGap.IsHorizontal)
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.X - door.Item.WorldPosition.X);
                            float size = character.AnimController.InWater ? colliderLength : GetColliderSize().X;
                            shouldBeOpen = (door.Item.WorldPosition.X - character.WorldPosition.X) * dir > -size;
                        }
                        else
                        {
                            int dir = Math.Sign(nextWaypoint.WorldPosition.Y - door.Item.WorldPosition.Y);
                            shouldBeOpen = (door.Item.WorldPosition.Y - character.WorldPosition.Y) * dir > -colliderLength;
                        }
                    }
                }

                if (door == null) { return; }
                
                //toggle the door if it's the previous node and open, or if it's current node and closed
                if ((door.IsOpen || door.IsBroken) != shouldBeOpen)
                {
                    Controller closestButton = null;
                    float closestDist = 0;
                    bool canAccess = CanAccessDoor(door, button =>
                    {
                        if (currentWaypoint == null) { return true; }
                        // Check that the button is on the right side of the door.
                        if (door.LinkedGap.IsHorizontal)
                        {
                            int dir = Math.Sign((nextWaypoint ?? currentWaypoint).WorldPosition.X - door.Item.WorldPosition.X);
                            if (button.Item.WorldPosition.X * dir > door.Item.WorldPosition.X * dir) { return false; }
                        }
                        else
                        {
                            int dir = Math.Sign((nextWaypoint ?? currentWaypoint).WorldPosition.Y - door.Item.WorldPosition.Y);
                            if (button.Item.WorldPosition.Y * dir > door.Item.WorldPosition.Y * dir) { return false; }
                        }
                        float distance = Vector2.DistanceSquared(button.Item.WorldPosition, character.WorldPosition);
                        if (closestButton == null || distance < closestDist)
                        {
                            closestButton = button;
                            closestDist = distance;
                        }
                        return true;
                    });
                    if (canAccess)
                    {
                        if (door.HasIntegratedButtons)
                        {
                            door.Item.TryInteract(character, false, true);
                            buttonPressCooldown = ButtonPressInterval;
                            break;
                        }
                        else if (closestButton != null)
                        {
                            if (Vector2.DistanceSquared(closestButton.Item.WorldPosition, character.WorldPosition) < MathUtils.Pow(closestButton.Item.InteractDistance + GetColliderLength(), 2))
                            {
                                closestButton.Item.TryInteract(character, false, true);
                                buttonPressCooldown = ButtonPressInterval;
                                break;
                            }
                            else
                            {
                                // Can't reach the button closest to the character.
                                // It's possible that we could reach another buttons.
                                // If this becomes an issue, we could go through them here and check if any of them are reachable
                                // (would have to cache a collection of buttons instead of a single reference in the CanAccess filter method above)
                                var body = Submarine.PickBody(character.SimPosition, character.GetRelativeSimPosition(closestButton.Item), collisionCategory: Physics.CollisionWall | Physics.CollisionLevel);
                                if (body != null)
                                {
                                    if (body.UserData is Item item)
                                    {
                                        var d = item.GetComponent<Door>();
                                        if (d == null || d.IsOpen) { return; }
                                    }
                                    // The button is on the wrong side of the door or a wall
                                    currentPath.Unreachable = true;
                                }
                                return;
                            }
                        }
                    }
                    else if (shouldBeOpen)
                    {
#if DEBUG
                        DebugConsole.NewMessage($"{character.Name}: Pathfinding error: Cannot access the door", Color.Yellow);
#endif
                        currentPath.Unreachable = true;
                        return;
                    }
                }
            }
        }

        private float? GetNodePenalty(PathNode node, PathNode nextNode)
        {
            if (character == null) { return 0.0f; }
            if (nextNode.Waypoint.isObstructed) { return null; }
            float penalty = 0.0f;
            if (nextNode.Waypoint.ConnectedGap != null && nextNode.Waypoint.ConnectedGap.Open < 0.9f)
            {
                var door = nextNode.Waypoint.ConnectedDoor;
                if (door == null)
                {
                    penalty = 100.0f;
                }
                else
                {
                    if (!CanAccessDoor(door, button =>
                        {
                            // Ignore buttons that are on the wrong side of the door
                            if (door.IsHorizontal)
                            {
                                if (Math.Sign(button.Item.WorldPosition.Y - door.Item.WorldPosition.Y) != Math.Sign(character.WorldPosition.Y - door.Item.WorldPosition.Y))
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                if (Math.Sign(button.Item.WorldPosition.X - door.Item.WorldPosition.X) != Math.Sign(character.WorldPosition.X - door.Item.WorldPosition.X))
                                {
                                    return false;
                                }
                            }
                            return true;
                        }))
                    {
                        return null;
                    }
                }
            }

            bool nextNodeAboveWaterLevel = nextNode.Waypoint.CurrentHull != null && nextNode.Waypoint.CurrentHull.Surface < nextNode.Waypoint.Position.Y;
            //non-humanoids can't climb up ladders
            if (!(character.AnimController is HumanoidAnimController))
            {
                if (node.Waypoint.Ladders != null && nextNode.Waypoint.Ladders != null && (!nextNode.Waypoint.Ladders.Item.IsInteractable(character) || character.LockHands)||
                    (nextNode.Position.Y - node.Position.Y > 1.0f && //more than one sim unit to climb up
                    nextNodeAboveWaterLevel)) //upper node not underwater
                {
                    return null;
                }
            }

            if (node.Waypoint.CurrentHull != null)
            {
                var hull = node.Waypoint.CurrentHull;
                if (hull.FireSources.Count > 0)
                {
                    foreach (FireSource fs in hull.FireSources)
                    {
                        penalty += fs.Size.X * 10.0f;
                    }
                }
                if (character.NeedsAir)
                {
                    if (hull.WaterVolume / hull.Rect.Width > 100.0f)
                    {
                        if (!HumanAIController.HasDivingSuit(character))
                        {
                            penalty += 500.0f;
                        }
                    }
                    if (character.PressureProtection < 10.0f && hull.WaterVolume > hull.Volume)
                    {
                        penalty += 1000.0f;
                    }
                }

                float yDist = Math.Abs(node.Position.Y - nextNode.Position.Y);
                if (nextNodeAboveWaterLevel && node.Waypoint.Ladders == null && nextNode.Waypoint.Ladders == null && node.Waypoint.Stairs == null && nextNode.Waypoint.Stairs == null)
                {
                    penalty += yDist * 10.0f;
                }
            }

            return penalty;
        }

        public static float smallRoomSize = 500;
        public void Wander(float deltaTime, float wallAvoidDistance = 150, bool stayStillInTightSpace = true)
        {
            //steer away from edges of the hull
            bool wander = false;
            bool inWater = character.AnimController.InWater;
            var currentHull = character.CurrentHull;
            if (currentHull != null && !inWater)
            {
                float roomWidth = currentHull.Rect.Width;
                if (stayStillInTightSpace && roomWidth < Math.Max(wallAvoidDistance * 3, smallRoomSize))
                {
                    Reset();
                }
                else
                {
                    float leftDist = character.Position.X - currentHull.Rect.X;
                    float rightDist = currentHull.Rect.Right - character.Position.X;
                    if (leftDist < wallAvoidDistance && rightDist < wallAvoidDistance)
                    {
                        if (Math.Abs(rightDist - leftDist) > wallAvoidDistance / 2)
                        {
                            SteeringManual(deltaTime, Vector2.UnitX * Math.Sign(rightDist - leftDist));
                            return;
                        }
                        else if (stayStillInTightSpace)
                        {
                            Reset();
                            return;
                        }
                    }
                    if (leftDist < wallAvoidDistance)
                    {
                        float speed = (wallAvoidDistance - leftDist) / wallAvoidDistance;
                        SteeringManual(deltaTime, Vector2.UnitX * MathHelper.Clamp(speed, 0.25f, 1));
                        WanderAngle = 0.0f;
                    }
                    else if (rightDist < wallAvoidDistance)
                    {
                        float speed = (wallAvoidDistance - rightDist) / wallAvoidDistance;
                        SteeringManual(deltaTime, -Vector2.UnitX * MathHelper.Clamp(speed, 0.25f, 1));
                        WanderAngle = MathHelper.Pi;
                    }
                    else
                    {
                        wander = true;
                    }
                }
            }
            else
            {
                wander = true;
            }
            if (wander)
            {
                SteeringWander();
                if (inWater)
                {
                    SteeringAvoid(deltaTime, lookAheadDistance: ConvertUnits.ToSimUnits(wallAvoidDistance), 5);
                }
            }
            if (!inWater)
            {
                //reset vertical steering to prevent dropping down from platforms etc
                ResetY();
            }
        }
    }  
}
