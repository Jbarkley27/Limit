// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// Job used to find the parameter along a spline that is nearest to the given point.
    /// </summary>
    [BurstCompile]
    public struct SplineProjectJob : IJob {
        /// <summary>
        /// Spline to query.
        /// </summary>
        [ReadOnly] public SplinePath Spline;
        
        /// <summary>
        /// Position to find nearest parameter to.
        /// </summary>
        public float4 Position;
        
        /// <summary>
        /// How many iterations of Newton's Method to perform.
        /// </summary>
        public int Iterations;
        
        /// <summary>
        /// Whether to draw debug lines showing each iteration of Newton's Method.
        /// </summary>
        public bool DebugProjection;

        /// <summary>
        /// Where the calculated nearest parameter value is written.
        /// </summary>
        [WriteOnly] public NativeArray<float> OutPosition;

        /// <summary>
        /// Execute the job.
        /// </summary>
        public void Execute() {
            OutPosition[0] = ProjectPosition(Position);
        }
        
        // Approximate the parameter value of the position along the spline nearest to the given position.
        private float ProjectPosition(float4 position) {
            NativeArray<SplinePoint> controlPoints = Spline.ControlPoints;
            float minDistSqr = float.PositiveInfinity;
            int bestSegment = -1;
            float bestT = -1;
            
            // Test each segment to find the nearest one.
            for (int i = 0; i < Spline.PointCount - 1; i++) {
                // Define a line between the control points of the segment.
                float4 point1 = controlPoints[i].Position;
                float4 point2 = controlPoints[i + 1].Position;
                float4 offset = point2 - point1;

                // Calculate the closest position to the input position on that line.
                float t = math.clamp(math.dot(position - point1, offset) / math.lengthsq(offset), 0, 1);
                float4 pt = math.lerp(point1, point2, t);
                
                // Find the segment with the projected line position that is closest to the input position.
                // That is where we will perform Newton's Method to find the nearest parameter.
                float sqrDist = math.distancesq(pt, position);
                if (sqrDist >= minDistSqr) continue;
                
                if (DebugProjection) {
                    Debug.DrawLine(point1.xyz, point2.xyz, Color.magenta);
                    Debug.DrawLine(pt.xyz, position.xyz, Color.magenta);
                }

                minDistSqr = sqrDist;
                bestSegment = i;
                bestT = t;
            }

            // Save some cycles by only dividing once.
            float invPointCount = 1.0f / (Spline.PointCount - 1.0f);

            // Define limits very near, but not at, the ends of the spline.
            // This is important because at the very ends the tangents are zero and the math will fail.
            float startLimit = 0.01f * invPointCount;
            float endLimit = 1.0f - startLimit;
            
            // Cannot sample at ends of spline as the tangent is zero.
            float startParam = math.max(bestSegment * invPointCount, startLimit);
            float endParam = math.min((bestSegment + 1) * invPointCount, endLimit);

            // Start Newton's Method in the middle of the nearest segment.
            float pMid = (startParam + endParam) * 0.5f;

            // If nearest point is at the very end of a segment, we also need to check on the adjacent segment.
            // In these cases, we perform Newton's Method for both segments, and take the closer result.
            if (bestT == 0 && bestSegment > 0) {
                // Projected point was at the start of the segment, so check previous segment too.
                float prevParam = math.max((bestSegment - 1) * invPointCount, startLimit);
                float prevMid = (prevParam + startParam) * 0.5f;
                
                // Perform Newton's Method twice and take the closer result.
                float p1 = NewtonsMethod(position, prevMid, prevParam, startParam);
                float p2 = NewtonsMethod(position, pMid, startParam, endParam);
                return CloserParam(position, p1, p2);
            } else if (bestT == 1 && bestSegment < Spline.PointCount - 1) {
                // Projected point was at the end of the segment, so check next segment too.
                float nextParam = math.min((bestSegment + 2) * invPointCount, endLimit);
                float nextMid = (endParam + nextParam) * 0.5f;
                
                // Perform Newton's Method twice and take the closer result.
                float p1 = NewtonsMethod(position, pMid, startParam, endParam);
                float p2 = NewtonsMethod(position, nextMid, endParam, nextParam);
                return CloserParam(position, p1, p2);
            } else {
                // Projected point was not at the end of a segment so we only do Newton's Method once.
                return NewtonsMethod(position, pMid, startParam, endParam);
            }
        }
        
        // Perform Newton's method to find the parameter value nearest the given position.
        private float NewtonsMethod(float4 position, float startParameter, float minParameter, float maxParameter) {
            float parameter = startParameter;
            
            // Define deltas to use for sampling the derivative and second derivative.
            // These derivatives are of the distance function (distance from point on spline to given point),
            // not of the spline function itself.
            float dParam = 0.005f / (Spline.PointCount - 1);
            float halfDParam = dParam * 0.5f;
            
            for (int i = 0; i < Iterations; i++) {
                // Calculate positions on spline at current parameter and two very close-by parameters.
                float4 v1 = Spline.GetPositionInternal(parameter - halfDParam);
                float4 v2 = Spline.GetPositionInternal(parameter);
                float4 v3 = Spline.GetPositionInternal(parameter + halfDParam);
                
                if (DebugProjection) {
                    Color dColor = Color.Lerp(Color.red, Color.blue, i / (Iterations - 1.0f));
                    Debug.DrawLine(v1.xyz, position.xyz, dColor);
                    Debug.DrawLine(v2.xyz, position.xyz, dColor);
                    Debug.DrawLine(v3.xyz, position.xyz, dColor);
                }

                // Calculate distances for the three points.
                float d1 = math.distancesq(v1, position);
                float d2 = math.distancesq(v2, position);
                float d3 = math.distancesq(v3, position);

                // Compute first derivative: delta distance / delta param.
                float derivative = (d3 - d1) / dParam;

                // Compute two values of first derivative.
                float derivative1 = (d2 - d1) / halfDParam;
                float derivative2 = (d3 - d2) / halfDParam;
                
                // Compute second derivative: delta (delta distance / delta param) / delta param.
                float secondDerivative = (derivative2 - derivative1) / halfDParam;

                // Newton's Method - move sampled parameter by derivative / second derivative,
                // in order to find zeroes of the second derivative.
                float delta = -derivative / secondDerivative;

                // Move parameter by delta. If it is already on the end and delta is trying to push it further,
                // we can early-abort because the value will never change after this.
                float oldParam = parameter;
                parameter = math.clamp(parameter + delta, minParameter + halfDParam, maxParameter - halfDParam);
                if (oldParam == parameter) break;
            }

            return parameter;
        }
        
        // Return which of the two given params is closer to the given point.
        private float CloserParam(float4 position, float param1, float param2) {
            float4 v1 = Spline.GetPositionInternal(param1);
            float4 v2 = Spline.GetPositionInternal(param2);

            if (math.distancesq(position, v1) > math.distancesq(position, v2)) {
                return param2;
            } else {
                return param1;
            }
        }
    }
}