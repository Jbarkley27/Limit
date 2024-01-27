// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// Burst-compatible job used to find a HyperNav path.
    /// </summary>
    /// <remarks>
    /// The methods in this class can be used both as a job and called directly from managed code.
    /// This enables pathfinding to operate in different modes without duplicating any of this code.
    /// </remarks>
    [BurstCompile]
    public struct NavPathJob : IJob {
        /// <summary>
        /// Map containing all loaded NavVolumes, keyed by their instance ID>
        /// </summary>
        [ReadOnly] public NativeParallelHashMap<long, NativeNavVolumeData> Volumes;
        
        /// <summary>
        /// Position where the path starts (world space).
        /// </summary>
        [ReadOnly] public float4 StartPosition;
        
        /// <summary>
        /// Nav query result where the path starts.
        /// </summary>
        [ReadOnly] public NativeNavHit StartHit;
        
        /// <summary>
        /// Nav query result where the path ends.
        /// </summary>
        [ReadOnly] public NativeNavHit EndHit;

        /// <summary>
        /// Used to return the result path (as a list of waypoints) back to managed code.
        /// </summary>
        [WriteOnly] public NativeList<NativeNavWaypoint> OutPathWaypoints;

        /// <summary>
        /// Map containing all discovered nodes in the current pathfinding operation.
        /// </summary>
        public NativeParallelHashMap<PendingPathNode, VisitedNodeInfo> NodeTable;
        
        /// <summary>
        /// Internal list for holding the in-progress path waypoints.
        /// </summary>
        public NativeList<PendingPathNode> Waypoints;
        
        /// <summary>
        /// Internal heap for holding queue of nodes to visit.
        /// </summary>
        public NativeHeap<PendingPathNode> Frontier;

        /// <summary>
        /// Execute the pathfinding operation all the way through.
        /// </summary>
        public void Execute() {
            Initialize();
            UpdatePath(0, out _, out _);
        }

        /// <summary>
        /// Execute pathfinding algorithm up to the given number of steps.
        /// </summary>
        /// <remarks>
        /// When called directly from managed code, <see cref="Initialize"/> must be called first.
        /// </remarks>
        /// <param name="operationsLimit">Maximum number of pathfinding steps.</param>
        /// <param name="operationsUsed">Actual number of pathfinding steps used.</param>
        /// <param name="state">State of the pathfinding algorithm upon return.</param>
        public void UpdatePath(int operationsLimit, out int operationsUsed, out NavPathState state) {
            CalculateNodesTable(operationsLimit, out operationsUsed, out state, out PendingPathNode last);

            if (state == NavPathState.Success) {
                CompletePath(last);
            }
        }
        
        /// <summary>
        /// Initialize the pathfinding algorithm.
        /// </summary>
        /// <remarks>
        /// Must be called before <see cref="UpdatePath"/> can be called directly from managed code.
        /// </remarks>
        public void Initialize() {
            // Initial node to add to the frontier and visit first.
            PendingPathNode start = new PendingPathNode {
                Position = StartHit.Position,
                ToVolume = StartHit.Volume,
                ToRegion = StartHit.Region,
                FromVolume = -1,
                FromRegion = -1,
                IsExternalConnection = false,
                ConnectionIndex = -1,
            };
            
            // Add initial data to NodeTable.
            float heuristic = GetHeuristic(start);
            Frontier.Add(start, -heuristic);
            NodeTable[start] = new VisitedNodeInfo {
                CumulativeCost = 0,
                Heuristic = heuristic,
                HasPrevious = false,
                Previous = default,
                Visited = false,
                Position = start.Position,
            };
        }

        // Execute pathfinding algorithm up to the given number of steps.
        private void CalculateNodesTable(int operationLimit, out int operationsUsed,
            out NavPathState state, out PendingPathNode last) {
            
            operationsUsed = 0;
            
            // Update path until it completes, fails, or operation limit has been reached.
            while ((operationLimit == 0 || operationsUsed < operationLimit) && 
                   Frontier.TryRemove(out PendingPathNode node)) {
                operationsUsed++;
                
                if (NodeTable[node].HasPrevious) {
                    //Debug.DrawLine(NodeTable[node].Previous.Position.xyz, node.Position.xyz, Color.red, 5);
                }
                
                // Check if algorithm has reached the destination node.
                if (node.ToRegion == EndHit.Region && node.ToVolume == EndHit.Volume) {
                    last = node;
                    state = NavPathState.Success;
                    return;
                }
                
                // Visit the current node, adding its neighbors to the frontier if needed.
                Visit(node);
            }

            last = default;
            state = Frontier.Count > 0 ? NavPathState.Pending : NavPathState.Failure;
        }
        
        // Visit the current node, adding its neighbors to the frontier if needed.
        private void Visit(in PendingPathNode node) {
            // Mark node as visited.
            VisitedNodeInfo info = NodeTable[node];
            info.Visited = true;
            NodeTable[node] = info;

            int regionIndex = node.ToRegion;
            NativeNavVolumeData volume = Volumes[node.ToVolume];

            // Get region that node leads into.
            ref readonly NativeNavRegionData region = ref volume.Regions[regionIndex];

            // Loop through all the internal links of the region the node leads into.
            int linkCount = region.InternalLinkCount;
            int linkStart = region.InternalLinkStart;
            for (int i = 0; i < linkCount; i++) {
                // Ignore connection that leads back to region node originates from.
                int linkIndex = linkStart + i;
                ref readonly NativeNavInternalLinkData connection = ref volume.InternalLinks[linkIndex];
                if (connection.ToRegion == node.FromRegion) {
                    continue;
                }

                // Get nearest position on this link, then use that to calculate link cost.
                float4 nextPosition = GetNearestPointOnInternalLink(node.Position, in volume, linkIndex);
                float linkCost = math.distance(node.Position, nextPosition);
                float cumulativeCost = info.CumulativeCost + linkCost;

                // Create PendingPathNode for the connection.
                PendingPathNode to = new PendingPathNode {
                    FromRegion = node.ToRegion,
                    ToRegion = connection.ToRegion,
                    FromVolume = node.ToVolume,
                    ToVolume = node.ToVolume,
                    Position = nextPosition,
                    IsExternalConnection = false,
                    ConnectionIndex = linkIndex,
                };
                
                // Update cached node info for the PendingPathNode.
                UpdateNodeTable(node, to, cumulativeCost);
            }

            // Loop through all the external links of the region the node leads into.
            int externalLinkCount = region.ExternalLinkCount;
            int externalLinkStart = region.ExternalLinkStart;
            for (int i = 0; i < externalLinkCount; i++) {
                // Ignore connection that leads back to region node originates from.
                int linkIndex = externalLinkStart + i;
                ref readonly NativeNavExternalLinkData connection = ref volume.ExternalLinks[linkIndex];
                if (connection.ToRegion == node.FromRegion && connection.ToVolume == node.FromVolume) {
                    continue;
                }

                // External links may be invalid, or may be to a volume in an unloaded scene.
                // These links can be ignored.
                if (!Volumes.ContainsKey(connection.ToVolume)) {
                    continue;
                }

                // Calculate link cost.
                float4 nextPosition = connection.FromPosition;
                float linkCost = math.distance(node.Position, nextPosition) + connection.InternalCost;
                float cumulativeCost = info.CumulativeCost + linkCost;

                // Create PendingPathNode for the connection.
                PendingPathNode to = new PendingPathNode {
                    FromRegion = node.ToRegion,
                    ToRegion = connection.ToRegion,
                    FromVolume = node.ToVolume,
                    ToVolume = connection.ToVolume,
                    Position = nextPosition,
                    IsExternalConnection = true,
                    ConnectionIndex = linkIndex,
                };
                
                // Update cached node info for the PendingPathNode.
                UpdateNodeTable(node, to, cumulativeCost);
            }
        }

        // Update the info in the node table for the given node,
        // if the found cost is lower than known cost or node isn't discovered yet.
        private void UpdateNodeTable(in PendingPathNode node, in PendingPathNode to, float cumulativeCost) {
            if (NodeTable.TryGetValue(to, out VisitedNodeInfo toInfo)) {
                // If a shorter path to the node has been found, update it in the node table and the frontier.
                if (!toInfo.Visited && cumulativeCost < toInfo.CumulativeCost) {
                    toInfo.CumulativeCost = cumulativeCost;
                    toInfo.Previous = node;
                    toInfo.HasPrevious = true;
                    NodeTable[to] = toInfo;
                    Frontier.Update(to, -(toInfo.Heuristic + cumulativeCost), true, to);
                }
            } else {
                // If the node has not been discovered, add it to the node table and the frontier.
                float heuristic = GetHeuristic(to);
                    
                NodeTable[to] = new VisitedNodeInfo {
                    Previous = node,
                    CumulativeCost = cumulativeCost,
                    Heuristic = heuristic,
                    Visited = false,
                    Position = to.Position,
                    HasPrevious = true,
                };
                Frontier.Add(to, -(heuristic + cumulativeCost));
            }
        }
        
        // Process a completed (successful) path into the output waypoints list.
        private void CompletePath(in PendingPathNode lastNode) {
            // Table format provides the previous node in the path for each node.
            // Convert this to a list.
            ProcessTableIntoList(lastNode);
            
            // Remove unnecessary nodes from the path by raycasting.
            SimplifyPath();
            
            // Convert the internal waypoints to the output list.
            for (int i = 0; i < Waypoints.Length; i++) {
                PendingPathNode cur = Waypoints[i];
                float4 entryPoint = cur.Position;

                // Determine the node type based on its from and to volumes.
                NavWaypointType entryType;
                long volumeID;
                if (cur.IsExternalConnection) {
                    entryType = NavWaypointType.ExitVolume;
                    volumeID = cur.FromVolume;
                } else if (cur.FromVolume >= 0) {
                    entryType = NavWaypointType.InsideVolume;
                    volumeID = cur.FromVolume;
                } else if (cur.ToVolume >= 0) {
                    entryType = NavWaypointType.EnterVolume;
                    volumeID = cur.ToVolume;
                } else {
                    entryType = NavWaypointType.Outside;
                    volumeID = -1;
                }
                
                OutPathWaypoints.Add(new NativeNavWaypoint(entryPoint, entryType, volumeID));

                // External connections need two waypoints because they have a connection point at each volume.
                if (cur.IsExternalConnection) {
                    OutPathWaypoints.Add(new NativeNavWaypoint(
                                             Volumes[cur.FromVolume].ExternalLinks[cur.ConnectionIndex].ToPosition,
                                             NavWaypointType.EnterVolume, cur.ToVolume));
                }
            }
        }
        
        // Table format provides the previous node in the path for each node.
        // Convert this to a list.
        private void ProcessTableIntoList(in PendingPathNode lastNode) {
            // Add a waypoint for the end query hit.
            Waypoints.Add(new PendingPathNode {
                Position = EndHit.Position,
                ConnectionIndex = -1,
                FromRegion = EndHit.Region,
                FromVolume = EndHit.Volume,
                ToRegion = -1,
                ToVolume = -1,
                IsExternalConnection = false,
            });
            
            // Add all points along the path to the list, in reverse order.
            PendingPathNode current = lastNode;
            bool hasCurrent = true;
            while (hasCurrent && current.ConnectionIndex >= 0) {
                VisitedNodeInfo curInfo = NodeTable[current];
                current.Position = curInfo.Position;
                Waypoints.Add(current);
                hasCurrent = curInfo.HasPrevious;
                if (hasCurrent) current = curInfo.Previous;
            }

            // Add a waypoint for the start query hit.
            Waypoints.Add(new PendingPathNode {
                Position = StartHit.Position,
                ConnectionIndex = -1,
                FromRegion = -1,
                FromVolume = -1,
                ToRegion = StartHit.Region,
                ToVolume = StartHit.Volume,
                IsExternalConnection = false,
            });
            
            // If start query hit was not exactly the query point, add the exact start point.
            if (StartHit.IsOnEdge) {
                Waypoints.Add(new PendingPathNode {
                    Position = StartPosition,
                    ConnectionIndex = -1,
                    FromRegion = -1,
                    FromVolume = -1,
                    ToRegion = -1,
                    ToVolume = -1,
                    IsExternalConnection = false,
                });
            }
            
            // Reverse the list so the points are in the correct order.
            int count = Waypoints.Length;
            int halfCount = count / 2;
            for (int i = 0; i < halfCount; i++) {
                PendingPathNode temp = Waypoints[i];
                Waypoints[i] = Waypoints[count - i - 1];
                Waypoints[count - i - 1] = temp;
            }
        }
        
        // Remove unnecessary nodes from the path by raycasting against the volumes' blocking triangles.
        private void SimplifyPath() {
            // Index of point from which raycast starts.
            // Whenever a later waypoint is found with a clear path to this one,
            // all the waypoints in between can be moved.
            int startIndex = 0;

            // Each point needs to check if there is any later point that it has a clear path to.
            // This is an n^2 algorithm but the number of waypoints should be fairly low.
            while (startIndex < Waypoints.Length - 1) {
                PendingPathNode startWaypoint = Waypoints[startIndex];

                // If last waypoint in path, cannot remove more.
                if (startWaypoint.ToVolume < 0) {
                    startIndex++;
                    continue;
                }
                
                // Get position where path leaves the waypoint.
                float4 startPos = startWaypoint.Position;
                if (startWaypoint.IsExternalConnection) {
                    startPos = Volumes[startWaypoint.FromVolume]
                               .ExternalLinks[startWaypoint.ConnectionIndex].ToPosition;
                }
                
                // Loop through all waypoints after the current one.
                int nextIndex = startIndex + 1;
                int lastRemovableIndex = -1;

                while (nextIndex < Waypoints.Length - 1) {
                    // Waypoint after the one being considered for removal.
                    PendingPathNode nextNextWaypoint = Waypoints[nextIndex + 1];

                    // Don't allow removing a node representing a volume transition.
                    // Doing so could allow a part of the path to pass through space with no volume.
                    if (startWaypoint.ToVolume != nextNextWaypoint.FromVolume) {
                        break;
                    }
                    
                    // If line is clear, update last removable index.
                    // If not clear, keep going because a future node might be clear.
                    if (!NativeMathUtility.NavRaycast(startPos, nextNextWaypoint.Position, true, Volumes[startWaypoint.ToVolume], out _)) {
                        lastRemovableIndex = nextIndex;
                    }
                    
                    nextIndex++;
                }

                // If any removable nodes were discovered, remove them.
                if (lastRemovableIndex > 0) {
                    Waypoints.RemoveRange(startIndex + 1, lastRemovableIndex - startIndex);
                }

                startIndex++;
            }
        }

        // Get distance from a node to the destination.
        private float GetHeuristic(in PendingPathNode node) {
            float heuristic = math.distance(node.Position, EndHit.Position);
            return heuristic;
        }
        
        // Given an internal link and a previous position,
        // get the position on the internal link nearest the previous position.
        private float4 GetNearestPointOnInternalLink(float4 reference, in NativeNavVolumeData volume, int linkIndex) {
            
            float4 localPos = math.mul(volume.InverseTransform, reference);
            
            float4 closestPoint = default;
            float closestDistance = float.PositiveInfinity;
            
            // Helper function to check if a point is closer than the current best.
            static void TestPoint(float4 point, float4 localPos, ref float4 closestPoint, ref float closestDistance) {
                float dist2 = math.distancesq(point, localPos);
                if (dist2 < closestDistance) {
                    closestPoint = point;
                    closestDistance = dist2;
                }
            }

            ref readonly NativeNavInternalLinkData link = ref volume.InternalLinks[linkIndex];

            // Check all triangles.
            int triStart = link.TrianglesStart;
            int triCount = link.TrianglesCount;
            for (int i = 0; i < triCount; i++) {
                int3 tri = volume.LinkTriangles[triStart + i];

                if (NativeMathUtility.GetNearestPointOnTriangle(volume.Vertices[tri.x], volume.Vertices[tri.y], 
                                                               volume.Vertices[tri.z], localPos, 
                                                               out float4 triPoint)) {
                    TestPoint(triPoint, localPos, ref closestPoint, ref closestDistance);
                }
            }

            // Check all edges.
            int edgeStart = link.EdgesStart;
            int edgeCount = link.EdgesCount;
            for (int i = 0; i < edgeCount; i++) {
                int2 edge = volume.LinkEdges[edgeStart + i];

                if (NativeMathUtility.GetNearestPointOnSegment(volume.Vertices[edge.x], volume.Vertices[edge.y], 
                                                              localPos, out float4 edgePoint)) {
                    TestPoint(edgePoint, localPos, ref closestPoint, ref closestDistance);
                }
            }

            // Check all vertices.
            int vertexStart = link.VerticesStart;
            int vertexCount = link.VerticesCount;
            for (int i = 0; i < vertexCount; i++) {
                TestPoint(volume.Vertices[volume.LinkVertices[vertexStart + i]],
                          localPos, ref closestPoint, ref closestDistance);
            }

            float4 closest = math.mul(volume.Transform, closestPoint);
            return closest;
        }
    }
    
    /// <summary>
    /// A discovered node in a pending path, which serves as a key into the dictionary of per-node discovered info.
    /// </summary>
    /// <remarks>
    /// Each PendingPathNode represents a transition between two regions.
    /// </remarks>
    public struct PendingPathNode : IEquatable<PendingPathNode> {
        /// <summary>
        /// The region from which this node originates.
        /// </summary>
        public int FromRegion;
        
        /// <summary>
        /// The region to which this node leads.
        /// </summary>
        public int ToRegion;
        
        /// <summary>
        /// The volume from which this node originates.
        /// </summary>
        public long FromVolume;
        
        /// <summary>
        /// The volume to which this node leads.
        /// </summary>
        public long ToVolume;
        
        /// <summary>
        /// The position of this node.
        /// </summary>
        public float4 Position;
        
        /// <summary>
        /// Whether this node is an external connection (bridges two different volumes).
        /// </summary>
        public bool IsExternalConnection;
        
        /// <summary>
        /// Which connection in the originating region's connections array this node represents.
        /// </summary>
        public int ConnectionIndex;

        /// <summary>
        /// Compare to another PendingPathNode.
        /// </summary>
        /// <param name="other">Node to compare to.</param>
        /// <returns>Whether the two nodes are equal.</returns>
        public bool Equals(PendingPathNode other) {
            return FromRegion == other.FromRegion &&
                   ToRegion == other.ToRegion &&
                   FromVolume == other.FromVolume &&
                   ToVolume == other.ToVolume &&
                   IsExternalConnection == other.IsExternalConnection &&
                   ConnectionIndex == other.ConnectionIndex;
        }

        /// <summary>
        /// Compare to another object.
        /// </summary>
        /// <param name="obj">Object to compare to.</param>
        /// <returns>Whether the two objects are equal.</returns>
        public override bool Equals(object obj) {
            return obj is PendingPathNode other && Equals(other);
        }

        /// <summary>
        /// Get integer for use with hash table.
        /// </summary>
        /// <returns>Integer hash code.</returns>
        public override int GetHashCode() {
            return FromRegion ^ ToRegion ^ (int)FromVolume ^ (int)ToVolume ^ (IsExternalConnection ? 1 : 0) ^ ConnectionIndex;
        }
    }

    /// <summary>
    /// The information that has been discovered about a node during pathfinding, which is stored in a table.
    /// </summary>
    public struct VisitedNodeInfo {
        /// <summary>
        /// If false, this is the first node in the sequence.
        /// </summary>
        public bool HasPrevious;
        
        /// <summary>
        /// The node to travel from to get the shortest known path to this node.
        /// </summary>
        public PendingPathNode Previous;
        
        /// <summary>
        /// Distance from this node to the destination.
        /// </summary>
        public float Heuristic;
        
        /// <summary>
        /// The total path distance of the shortest known path to this node.
        /// </summary>
        public float CumulativeCost;
        
        /// <summary>
        /// If node has been visited, the shortest path to this node is finalized.
        /// </summary>
        public bool Visited;
        
        /// <summary>
        /// Position to enter the node when coming from the previous node in the best known shortest path.
        /// </summary>
        public float4 Position;
    }
}
