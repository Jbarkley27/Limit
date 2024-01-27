// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// Job that calculates the <see cref="IAvoidanceAgent.AvoidanceVelocity"/> of all <see cref="IAvoidanceAgent"/>s.
    /// </summary>
    [BurstCompile]
    public struct AvoidanceJob : IJobParallelFor {
        // Small float that is considered "basically zero".
        private const float Epsilon = 0.00001f;
        
        // Input
        
        /// <summary>
        /// Indices in the <see cref="Obstacles"/> array that are agents that need updating.
        /// </summary>
        [ReadOnly] public NativeArray<int> AgentIndices;
        
        /// <summary>
        /// All obstacles in the world that agents must consider.
        /// </summary>
        [ReadOnly] public NativeArray<NativeAvoidanceObstacleData> Obstacles;

        /// <summary>
        /// Number of valid obstacles in the <see cref="Obstacles"/> array.
        /// </summary>
        public int ObstacleCount;
        
        /// <summary>
        /// The maximum number of obstacles each agent will consider.
        /// </summary>
        public int MaxObstaclesConsidered;

        /// <summary>
        /// How much time has passed since the last avoidance update.
        /// </summary>
        public float DeltaTime;
        
        /// <summary>
        /// How far in the future to look when considering avoidance.
        /// </summary>
        public float TimeHorizon;

        // Output
        
        /// <summary>
        /// The calculated avoidance velocity for each agent in <see cref="AgentIndices"/>.
        /// </summary>
        [WriteOnly] public NativeArray<float3> AvoidanceVelocities;
        
        // Local Storage
        
        /// <summary>
        /// Used to store the obstacle planes for performing linear programming.
        /// </summary>
        public NativeArray<NativePlane> TempPlanes;
        
        /// <summary>
        /// Used to store a subset of obstacle planes for linear programming in four dimensions.
        /// </summary>
        public NativeArray<NativePlane> TempProjPlanes;

        /// <summary>
        /// Run on a single agent index.
        /// </summary>
        /// <param name="index">The index in the <see cref="AgentIndices"/> array.</param>
        public void Execute(int index) {
            // Get index of agent in the obstacles array.
            int agentIndex = AgentIndices[index];
            
            // Get the regions of the temp plane lists that this agent will use.
            NativeArray<NativePlane> agentTempPlanes =
                TempPlanes.GetSubArray(index * MaxObstaclesConsidered, MaxObstaclesConsidered);
            NativeArray<NativePlane> agentTempProjPlanes =
                TempProjPlanes.GetSubArray(index * MaxObstaclesConsidered, MaxObstaclesConsidered);
            
            // Perform avoidance calculation.
            AvoidanceVelocities[index] = OrcaAvoidance(agentIndex, ref agentTempPlanes, ref agentTempProjPlanes);
        }
        
        /// <summary>
        /// Perform avoidance calculation for an agent using the ORCA
        /// (optimal reciprocal collision avoidance) algorithm.
        /// </summary>
        /// <remarks>
        /// Adapted from the RVO2-3D library from University of North Carolina, which is licensed under Apache 2.0.
        /// </remarks>
        /// <param name="agentIndex">Index of the agent in the <see cref="Obstacles"/> array.</param>
        /// <param name="agentTempPlanes">Array to store temp planes for linear programming.</param>
        /// <param name="agentTempProjPlanes">Array to store a subset of temp planes for 4D linear programming.</param>
        /// <returns>The calculated best velocity for the agent.</returns>
        public float3 OrcaAvoidance(int agentIndex,
                                    ref NativeArray<NativePlane> agentTempPlanes,
                                    ref NativeArray<NativePlane> agentTempProjPlanes) {
            float invTimeHorizon = 1.0f / TimeHorizon;

            NativeAvoidanceObstacleData agent = Obstacles[agentIndex];

            int tempPlaneCount = 0;
            
            float4 desiredVel = new float4(agent.InputVelocity, 0);

            // Parameters to decide if it will consider an obstacle.
            float agentConsiderationRadius = agent.Radius + agent.Speed * TimeHorizon;
            long agentAvoidedTags = agent.AvoidedTags;
            long agentTagMask = agent.TagMask;
            
            // Loop through all obstacles and determine if each one will be considered.
            // If it will be, calculate an ORCA plane and add it to the planes array.
            for (int i = 0; i < ObstacleCount && tempPlaneCount < MaxObstaclesConsidered; i++) {
                if (i == agentIndex) continue;
                NativeAvoidanceObstacleData obstacle = Obstacles[i];
                if ((agentAvoidedTags & obstacle.TagMask) == 0) continue;
                
                // Calculate relative position, velocity, and distance.
                float4 otherDesiredVel = new float4(obstacle.InputVelocity, 0);
                float4 relativePosition = new float4(obstacle.Position - agent.Position, 0);
                float4 relativeVelocity = desiredVel - otherDesiredVel;
                float distSq = math.lengthsq(relativePosition);

                // If agents are too far from each other, do not consider avoidance between them.
                float obstacleConsiderationRadius = obstacle.Radius + obstacle.Speed * TimeHorizon;
                float maxDist = agentConsiderationRadius + obstacleConsiderationRadius + agent.Padding;
                float maxDistSq = maxDist * maxDist;
                if (distSq > maxDistSq) continue;
                
                float combinedRadius = agent.Radius + obstacle.Radius + agent.Padding;
                float combinedRadiusSq = combinedRadius * combinedRadius;

                // Account for the case where agents are moving directly towards one another.
                // In this case, the avoidance direction is arbitrary and doesn't matter,
                // as long as both agents do not choose the same direction.
                // After a frame or two, they should no longer be moving towards each other
                // and this will no longer be needed.
                if (math.lengthsq(relativeVelocity) > Epsilon &&
                    math.lengthsq(math.cross(relativeVelocity.xyz, relativePosition.xyz)) < Epsilon) {
                    
                    float4 offset = NativeMathUtility.GetPerpendicularVector(desiredVel) * 0.01f;
                    desiredVel += offset;
                    relativeVelocity += offset;
                }

                float4 planeNormal;
                float4 planePoint;
                float4 u;

                if (distSq > combinedRadiusSq) {
                    // No collision.
                    float4 w = relativeVelocity - invTimeHorizon * relativePosition;
                    // Vector from cutoff center to relative velocity.
                    float wLengthSq = math.lengthsq(w);

                    float dotProduct = math.dot(w, relativePosition);

                    if (dotProduct < 0.0f &&
                        dotProduct * dotProduct > combinedRadiusSq * wLengthSq) {
                        // Project on cut-off circle.
                        float wLength = math.sqrt(wLengthSq);
                        float4 unitW = w / wLength;

                        planeNormal = unitW;
                        u = (combinedRadius * invTimeHorizon - wLength) * unitW;
                    } else {
                        // Project on cone.
                        float a = distSq;
                        float b = math.dot(relativePosition, relativeVelocity);
                        float c = math.lengthsq(relativeVelocity) -
                                  math.lengthsq(new float4(math.cross(relativePosition.xyz, relativeVelocity.xyz), 0)) /
                                  (distSq - combinedRadiusSq);
                            
                        // Original code here had trouble with occasionally getting a very low negative number here.
                        // Assuming its zero should be safe I think?
                        float sqr = b * b - a * c;
                        float sqrt = sqr > 0 ? math.sqrt(sqr) : 0;
                        float t = (b + sqrt) / a;
                        float4 ww = relativeVelocity - t * relativePosition;
                        float wwLength = math.length(ww);
                        float4 unitWW = ww / wwLength;

                        planeNormal = unitWW;
                        u = (combinedRadius * t - wwLength) * unitWW;
                    }
                } else {
                    // Collision.
                    float invTimeStep = 1.0f / DeltaTime;
                    float4 w = relativeVelocity - invTimeStep * relativePosition;
                    float wLength = math.length(w);
                    float4 unitW = w / wLength;

                    planeNormal = unitW;
                    u = (combinedRadius * invTimeStep - wLength) * unitW;
                }

                // Calculate avoidance factor for other obstacle (if it is also an agent).
                float otherAvoidance = obstacle.Avoidance;
                if ((obstacle.AvoidedTags & agentTagMask) == 0) {
                    otherAvoidance = 0;
                }
                
                // The avoidance values for both objects should add to 1.
                float avoidance = agent.Avoidance / (agent.Avoidance + otherAvoidance);
                planePoint = desiredVel + avoidance * u;
                NativePlane plane = new NativePlane(planeNormal, planePoint);
                agentTempPlanes[tempPlaneCount++] = plane;

                if (agent.Debug) {
                    Debug.DrawLine(agent.Position, agent.Position - otherDesiredVel.xyz, new Color(1, 0.5f, 0));
                    Debug.DrawLine(agent.Position - otherDesiredVel.xyz, agent.Position - otherDesiredVel.xyz + desiredVel.xyz, Color.red);

                    float3 planeCross = math.cross(planeNormal.xyz, math.forward());
                    Debug.DrawLine(agent.Position + planePoint.xyz, 
                                   agent.Position + planePoint.xyz + planeNormal.xyz, Color.green);
                    Debug.DrawLine(agent.Position + planePoint.xyz,
                                   agent.Position + planePoint.xyz + planeCross, Color.green);
                    Debug.DrawLine(agent.Position + planePoint.xyz,
                                               agent.Position + planePoint.xyz - planeCross, Color.green);
                }
                
            }

            // Run linear programming in 3D. If it succeeds, that gives us the optimal velocity.
            int planeFail = LinearProgram3D(agentTempPlanes, tempPlaneCount, agent.Speed, desiredVel, 
                                            false, out desiredVel);

            // If the 3D linear programming fails, there is no velocity that is guaranteed to avoid collisions.
            // In that case, we use 4D linear programming to find the best option.
            if (planeFail < tempPlaneCount) {
                LinearProgram4D(agentTempPlanes, tempPlaneCount, ref agentTempProjPlanes, planeFail, agent.Speed, ref desiredVel);
            }

            if (agent.Debug) {
                Debug.DrawLine(agent.Position, agent.Position + agent.InputVelocity, Color.red);
                Debug.DrawLine(agent.Position, agent.Position + desiredVel.xyz, Color.blue);
            }

            return desiredVel.xyz;
        }
        
        // Adapted from the RVO2-3D library from University of North Carolina, which is licensed under Apache 2.0.
        private bool LinearProgram1D(in NativeArray<NativePlane> planes, int planeNo, in NativeRay line,
                                     float radius, in float4 optVelocity, bool directionOpt, out float4 result) {
            
            float dotProduct = math.dot(line.Origin, line.Direction);
            float discriminant = dotProduct * dotProduct + radius * radius - math.lengthsq(line.Origin);

            if (discriminant < 0.0f) {
                result = float4.zero;
                return false;
            }

            float sqrtDiscriminant = math.sqrt(discriminant);
            float tLeft = -dotProduct - sqrtDiscriminant;
            float tRight = -dotProduct + sqrtDiscriminant;

            for (int i = 0; i < planeNo; ++i) {
                NativePlane plane = planes[i];
                
                float4 planePoint = plane.Center;
                float numerator = math.dot(planePoint - line.Origin, plane.Normal);
                float denominator = math.dot(line.Direction, plane.Normal);

                if (denominator * denominator <= Epsilon) {
                    // Line is (almost) parallel to plane i.
                    if (numerator > 0.0f) {
                        result = float4.zero;
                        return false;
                    }

                    continue;
                }

                float t = numerator / denominator;

                if (denominator >= 0.0f) {
                    // Plane i bounds line on the left.
                    tLeft = math.max(tLeft, t);
                } else {
                    // Plane i bounds line on the right.
                    tRight = math.min(tRight, t);
                }

                if (tLeft > tRight) {
                    result = float4.zero;
                    return false;
                }
            }

            if (directionOpt) {
                // Optimize direction.
                if (math.dot(optVelocity, line.Direction) > 0.0f) {
                    // Take right extreme.
                    result = line.Origin + tRight * line.Direction;
                } else {
                    // Take left extreme.
                    result = line.Origin + tLeft * line.Direction;
                }
            } else {
                // Optimize closest point.
                float t = math.dot(line.Direction, optVelocity - line.Origin);

                if (t < tLeft) {
                    result = line.Origin + tLeft * line.Direction;
                } else if (t > tRight) {
                    result = line.Origin + tRight * line.Direction;
                } else {
                    result = line.Origin + t * line.Direction;
                }
            }

            return true;
        }

        // Adapted from the RVO2-3D library from University of North Carolina, which is licensed under Apache 2.0.
        private bool LinearProgram2D(in NativeArray<NativePlane> planes, int planeNo, float radius,
                                     float4 optVelocity, bool directionOpt, out float4 result) {

            NativePlane plane = planes[planeNo];
            float planeDist = -plane.Distance;
            float planeDistSq = planeDist * planeDist;
            float radiusSq = radius * radius;

            if (planeDistSq > radiusSq) {
                // Max speed sphere fully invalidates plane planeNo.
                result = float4.zero;
                return false;
            }

            float planeRadiusSq = radiusSq - planeDistSq;

            float4 planeCenter = plane.Center;

            if (directionOpt) {
                // Project direction optVelocity on plane planeNo.
                float4 planeOptVelocity =
                    optVelocity - math.dot(optVelocity, plane.Normal) * plane.Normal;
                float planeOptVelocityLengthSq = math.lengthsq(planeOptVelocity);

                if (planeOptVelocityLengthSq <= Epsilon) {
                    result = planeCenter;
                } else {
                    result = planeCenter + math.sqrt(planeRadiusSq / planeOptVelocityLengthSq) * planeOptVelocity;
                }
            } else {
                // Project point optVelocity on plane planeNo.
                result = optVelocity + math.dot(planeCenter - optVelocity, plane.Normal) *
                    plane.Normal;

                // If outside planeCircle, project on planeCircle.
                if (math.lengthsq(result) > radiusSq) {
                    float4 planeResult = result - planeCenter;
                    float planeResultLengthSq = math.lengthsq(planeResult);
                    result = planeCenter + math.sqrt(planeRadiusSq / planeResultLengthSq) * planeResult;
                }
            }

            for (int i = 0; i < planeNo; ++i) {
                NativePlane planeI = planes[i];
                float4 planeICenter = planeI.Center;
                if (math.dot(planeI.Normal, planeICenter - result) > 0.0f) {
                    // Result does not satisfy constraint i. Compute new optimal result.
                    // Compute intersection line of plane i and plane planeNo.
                    float4 crossProduct = new float4(math.cross(planeI.Normal.xyz, plane.Normal.xyz), 0);

                    if (math.lengthsq(crossProduct) <= Epsilon) {
                        // Planes planeNo and i are (almost) parallel, and plane i fully
                        // invalidates plane planeNo.
                        return false;
                    }

                    NativeRay line = default;
                    line.Direction = math.normalize(crossProduct);
                    float4 lineNormal = new float4(math.cross(line.Direction.xyz, plane.Normal.xyz), 0);
                    line.Origin = planeCenter +
                                  math.dot(planeICenter - planeCenter, planeI.Normal) /
                                  math.dot(lineNormal, planeI.Normal) * lineNormal;

                    if (!LinearProgram1D(planes, i, line, radius, optVelocity, directionOpt, out result)) {
                        return false;
                    }
                }
            }

            return true;
        }

        // Adapted from the RVO2-3D library from University of North Carolina, which is licensed under Apache 2.0.
        private int LinearProgram3D(in NativeArray<NativePlane> planes, int count, float radius, float4 optVelocity,
                                    bool directionOpt, out float4 result) {
            if (directionOpt) {
                // Optimize direction. Note that the optimization velocity is of unit length in this case.
                result = optVelocity * radius;
            } else if (math.lengthsq(optVelocity) > radius * radius) {
                // Optimize closest point and outside circle.
                result = math.normalize(optVelocity) * radius;
            } else {
                // Optimize closest point and inside circle.
                result = optVelocity;
            }

            for (int i = 0; i < count; ++i) {
                NativePlane planeI = planes[i];
                float4 planeICenter = planeI.Center;
                if (math.dot(planeI.Normal, planeICenter - result) > 0.0F) {
                    // Result does not satisfy constraint i. Compute new optimal result.
                    float4 tempResult = result;

                    if (!LinearProgram2D(planes, i, radius, optVelocity, directionOpt, out result)) {
                        result = tempResult;
                        return i;
                    }
                }
            }

            return count;
        }

        // Adapted from the RVO2-3D library from University of North Carolina, licensed under Apache 2.0.
        private void LinearProgram4D(in NativeArray<NativePlane> planes, int count, 
                                     ref NativeArray<NativePlane> projPlanes,
                                     int beginPlane, float radius, ref float4 result) {
            float distance = 0.0F;

            for (int i = beginPlane; i < count; ++i) {
                NativePlane planeI = planes[i];
                float4 planeICenter = planeI.Center;
                if (math.dot(planeI.Normal, planeICenter - result) > distance) {
                    // Result does not satisfy constraint of plane i.
                    int projPlaneCount = 0;

                    for (int j = 0; j < i; ++j) {
                        NativePlane planeJ = planes[j];
                        float4 planeJCenter = planeJ.Center;
                        float4 planePoint;

                        float3 crossProduct = math.cross(planeJ.Normal.xyz, planeI.Normal.xyz);

                        if (math.lengthsq(crossProduct) <= Epsilon) {
                            // Plane i and plane j are (almost) parallel.
                            if (math.dot(planeI.Normal, planeJ.Normal) > 0.0f) {
                                // Plane i and plane j point in the same direction.
                                continue;
                            }

                            // Plane i and plane j point in opposite direction.
                            planePoint = 0.5f * (planeICenter + planeJCenter);
                        } else {
                            // Plane.point is point on line of intersection between plane i and plane j.
                            float4 lineNormal = new float4(math.cross(crossProduct, planeI.Normal.xyz), 0);
                            planePoint = planeICenter +
                                         math.dot(planeJCenter - planeICenter, planeJ.Normal) /
                                         math.dot(lineNormal, planeJ.Normal) * lineNormal;
                        }

                        float4 planeNormal = math.normalize(planeJ.Normal - planeI.Normal);
                        projPlanes[projPlaneCount++] = new NativePlane(planeNormal, planePoint);
                    }

                    float4 tempResult = result;

                    if (LinearProgram3D(projPlanes, projPlaneCount, radius, planeI.Normal, true, out result) <
                        projPlaneCount) {
                        // This should in principle not happen. The result is by definition
                        // already in the feasible region of this linear program. If it fails,
                        // it is due to small floating point error, and the current result is
                        // kept.
                        result = tempResult;
                    }

                    distance = math.dot(planeI.Normal, planeICenter - result);
                }
            }
        }
    }
}