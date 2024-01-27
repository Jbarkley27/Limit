// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Collections.Generic;
using Infohazard.Core;
using Infohazard.HyperNav.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// A spline specialized for path following, created with a <see cref="NavPath"/>.
    /// </summary>
    /// <remarks>
    /// Unlike most spline tools, the tangents in this spline are calculated automatically.
    /// <para></para>
    /// This spline implementation uses two coordinate spaces: parameter and distance.
    /// Distance ranges from zero to the length of the spline, and values are distributed (approximately) evenly.
    /// Parameter ranges from zero to one and is the actual value supplied to the spline function,
    /// but values are not distributed evenly.
    /// </remarks>
    public struct SplinePath : IDisposable {
        #region Public Properties

        /// <summary>
        /// Length of the spline in world units.
        /// </summary>
        public float Length { get; private set; }

        /// <summary>
        /// Number of control points on the spline.
        /// </summary>
        public int PointCount { get; private set; }

        /// <summary>
        /// Whether an actual spline has been constructed.
        /// </summary>
        public bool IsCreated { get; private set; }

        /// <summary>
        /// List of all the control points of the spline.
        /// </summary>
        public NativeArray<SplinePoint> ControlPoints => _controlPoints;

        #endregion

        #region Private Fields

        // Positions of spline control points.
        private NativeArray<SplinePoint> _controlPoints;

        // Distance samples at uniform parameters along the spline, used to convert between parameter and distance.
        private NativeArray<float> _samples;

        // Used to store points temporarily when building the spline.
        private static readonly List<SplinePoint> TempPoints = new List<SplinePoint>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Create a new SplinePath with the given path.
        /// </summary>
        /// <param name="path">The input navigation path.</param>
        /// <param name="tangentScale">Scale to apply to spline tangents (lower values make the spline less curvy).</param>
        /// <param name="sampleCount">How many samples to take per segment of the spline when mapping the distance.</param>
        /// <param name="raycastTangents">Whether to shorten tangents by raycasting against NavVolume blocking triangles.</param>
        public SplinePath(NavPath path, float tangentScale, int sampleCount, bool raycastTangents) : this() {
            Initialize(path, tangentScale, sampleCount, raycastTangents);
        }

        /// <summary>
        /// Re-initialize an existing SplinePath with the given path.
        /// </summary>
        /// <param name="path">The input navigation path.</param>
        /// <param name="tangentScale">Scale to apply to spline tangents (lower values make the spline less curvy).</param>
        /// <param name="sampleCount">How many samples to take per segment of the spline when mapping the distance.</param>
        /// <param name="raycastTangents">Whether to shorten tangents by raycasting against NavVolume blocking triangles.</param>
        public void Initialize(NavPath path, float tangentScale, int sampleCount, bool raycastTangents) {
            // Create list of points from the path, removing any that are within a small range.
            CalculateTempPoints(path);

            // Allocate (if needed) and copy control points.
            CreateControlPointsArray();

            // Calculate tangents based on positions.
            // A tangent needs three positions to calculate, with tangents at the end being set to zero.
            CalculateTangents(tangentScale, raycastTangents);

            // Calculate a matrix for each segment to make position calculation faster.
            CalculateMatrices();

            // Build map of parameter to distance.
            Sample(sampleCount);

            IsCreated = true;
        }

        /// <summary>
        /// Dispose arrays allocated for this spline path.
        /// </summary>
        public void Dispose() {
            if (!IsCreated) return;
            _controlPoints.Dispose();
            _samples.Dispose();
            Length = 0;
            IsCreated = false;
        }

        /// <summary>
        /// Get the distance along the spline for a given parameter value.
        /// </summary>
        /// <param name="parameter">The parameter value in range [0, 1].</param>
        /// <returns>The distance value in range [0, <see cref="Length"/>].</returns>
        public float GetDistance(float parameter) {
            if (!IsCreated) return 0;

            // Parameter values of sample positions.
            float samplePos = math.clamp(parameter, 0, 1) * (_samples.Length - 1);
            int low = (int) math.floor(samplePos);
            int high = (int) math.ceil(samplePos);

            // If parameter is between two control points, lerp between their distances.
            return low == high
                ? _samples[low]
                : math.lerp(_samples[low], _samples[high], samplePos - low);
        }

        /// <summary>
        /// Get the parameter value for a given distance along the spline.
        /// </summary>
        /// <param name="distance">The distance value in range [0, <see cref="Length"/>].</param>
        /// <returns>The parameter value in range [0, 1].</returns>
        public float GetParameter(float distance) {
            return GetParameter(distance, 0, _samples.Length - 1);
        }

        /// <summary>
        /// Get the position of a given control point.
        /// </summary>
        /// <param name="index">Control point index.</param>
        /// <returns>Position of that control point, in world space.</returns>
        public Vector3 GetControlPosition(int index) => _controlPoints[index].Position.xyz;

        /// <summary>
        /// Get the tangent of a given control point.
        /// </summary>
        /// <param name="index">Control point index.</param>
        /// <returns>Tangent of that control point, in world space.</returns>
        public Vector3 GetControlTangent(int index) => _controlPoints[index].Tangent.xyz;

        /// <summary>
        /// Get the NavVolume that contains the given parameter value on the spline.
        /// </summary>
        /// <param name="parameter">Input parameter value.</param>
        /// <returns>The containing NavVolume.</returns>
        public NavVolume GetVolume(float parameter) {
            if (PointCount == 0) {
                return null;
            } else {
                // Determine which two control points the parameter is between.
                float pos = GetSegment(parameter, out int indexA, out int indexB);
                float t = pos - indexA;

                long volumeID;
                if (t < 0.5f) {
                    volumeID = _controlPoints[indexA].ToVolume;
                } else {
                    volumeID = _controlPoints[indexB].FromVolume;
                }

                return NavVolume.Volumes.TryGetValue(volumeID, out NavVolume volume) ? volume : null;
            }
        }

        /// <summary>
        /// Get the position at a given parameter value.
        /// </summary>
        /// <param name="parameter">The parameter value in range [0, 1].</param>
        /// <returns>Position along the spline, in world space.</returns>
        public Vector3 GetPosition(float parameter) {
            return GetPositionInternal(parameter).xyz;
        }

        internal float4 GetPositionInternal(float parameter) {
            if (PointCount == 0) {
                return float4.zero;
            }

            GetSegmentAndTimeVector(parameter, out SplinePoint segment, out float4 time);
            return math.mul(segment.PositionMatrix, time);
        }

        /// <summary>
        /// Get the tangent at a given parameter value.
        /// </summary>
        /// <param name="parameter">The parameter value in range [0, 1].</param>
        /// <returns>Tangent at that position, in world space.</returns>
        public Vector3 GetTangent(float parameter) {
            return GetTangentInternal(parameter).xyz;
        }

        internal float4 GetTangentInternal(float parameter) {
            if (PointCount < 2) {
                return new float4(0, 0, 1, 0);
            } else if (PointCount == 2) {
                // No calculated tangents since there are only two points.
                return _controlPoints[1].Position - _controlPoints[0].Position;
            }

            GetSegmentAndTimeVector(parameter, out SplinePoint segment, out float4 time);
            return math.mul(segment.TangentMatrix, time);
        }

        /// <summary>
        /// Sample the curvature at a given parameter value.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="GetPosition"/> and <see cref="GetTangent"/>, this does not return an exact value.
        /// </remarks>
        /// <param name="parameter">The parameter value in range [0, 1].</param>
        /// <param name="offset">Offset distance to sample derivative of tangent function.</param>
        /// <returns>The sampled curvature value (use magnitude to get scalar curvature).</returns>
        public Vector3 GetCurvature(float parameter, float offset = 0.01f) {
            return GetCurvatureInternal(parameter, offset).xyz;
        }

        internal float4 GetCurvatureInternal(float parameter, float offset) {
            if (PointCount < 3) {
                return float4.zero;
            } else {
                // Convert to distance so the correct offset can be used.
                float distance = GetDistance(parameter);
                float nextDistance = distance + offset;

                // Set end curvature as zero.
                if (nextDistance > Length) return float4.zero;

                // Get tangent at parameter and offset by distance.
                float4 tangent = math.normalize(GetTangentInternal(parameter));
                float4 nextTangent = math.normalize(GetTangentInternal(GetParameter(nextDistance)));

                // Numerical derivative calculation - delta y / delta x.
                return (nextTangent - tangent) / offset;
            }
        }

        /// <summary>
        /// Approximate the parameter value of the position along the spline nearest to the given position.
        /// </summary>
        /// <remarks>
        /// This uses Newton's method. Increasing the iteration count increases both accuracy and cost.
        /// </remarks>
        /// <param name="position">Position to project.</param>
        /// <param name="iterations">Number of Newton's method iterations.</param>
        /// <param name="debug">Whether to draw debug lines showing Newton's method iterations.</param>
        /// <returns>The approximate parameter along the spline in range [0, 1].</returns>
        public float ProjectPosition(Vector3 position, int iterations = 5, bool debug = false) {
            SplineProjectJob job = new SplineProjectJob {
                DebugProjection = debug,
                Iterations = iterations,
                OutPosition = new NativeArray<float>(1, Allocator.TempJob),
                Position = new float4(position, 1),
                Spline = this,
            };

            job.Run();

            float result = job.OutPosition[0];
            job.OutPosition.Dispose();
            return result;
        }

        #endregion

        #region Internal Methods
        
        private static void CalculateTempPoints(NavPath path) {
            float4 vLast = float4.zero;
            TempPoints.Clear();
            for (int i = 0; i < path.Waypoints.Count; i++) {
                NavWaypoint waypoint = path.Waypoints[i];
                float4 v = new float4(waypoint.Position, 1);
                long fromVolume = waypoint.VolumeID;
                long toVolume = waypoint.VolumeID;

                // For external connections, only one point should be added to the spline.
                if (waypoint.Type == NavWaypointType.ExitVolume &&
                    i < path.Waypoints.Count - 1 &&
                    path.Waypoints[i + 1].Type == NavWaypointType.EnterVolume) {
                    v = (v + new float4(path.Waypoints[i + 1].Position, 1)) / 2.0f;
                    toVolume = path.Waypoints[i + 1].VolumeID;
                    i++;
                }

                // Remove points within a small range of one another.
                if (i > 0 && math.distancesq(vLast, v) < 0.0001) continue;

                // Add point with position and volume initialized.
                TempPoints.Add(new SplinePoint {
                    Position = v,
                    FromVolume = fromVolume,
                    ToVolume = toVolume,
                });
                vLast = v;
            }
        }

        // Ensure _controlPoints has enough room then copy all control points over.
        private void CreateControlPointsArray() {
            PointCount = TempPoints.Count;

            if (!_controlPoints.IsCreated || _controlPoints.Length < PointCount) {
                if (_controlPoints.IsCreated) {
                    _controlPoints.Dispose();
                }

                _controlPoints = new NativeArray<SplinePoint>(PointCount, Allocator.Persistent);
            }

            for (int i = 0; i < PointCount; i++) {
                _controlPoints[i] = TempPoints[i];
            }
        }
        
        

        // Calculate the desired tangents at each control point, and use raycasts to ensure they are not too long.
        private void CalculateTangents(float tangentScale, bool raycastTangents) {
            for (int i = 1; i < PointCount - 1; i++) {
                SplinePoint info = _controlPoints[i];
                float4 position = info.Position;

                // Get previous point and next point.
                float4 pt1 = _controlPoints[i - 1].Position;
                float4 pt3 = _controlPoints[i + 1].Position;

                // Calculate and assign tangent.
                float4 tangent = CalculateTangent(pt1, position, pt3) * tangentScale;

                info.Tangent = tangent;
                _controlPoints[i] = info;
            }

            // Raycast check if it is enabled.
            if (raycastTangents) {
                DoTangentRaycastChecks();
            }
        }

        // Calculate the tangent for the control point at v2.
        private static float4 CalculateTangent(float4 v1, float4 v2, float4 v3) {
            float4 offset21 = v1 - v2;
            float4 offset23 = v3 - v2;

            float sqrMag21 = math.lengthsq(offset21);
            float sqrMag23 = math.lengthsq(offset23);

            // The simplest way to calculate spline tangents would just be v3 - v1.
            // However this produces bad results when the distance between control points varies significantly.

            // This is solved by using the offsets from v2 to each of those points,
            // and clamping the length of the longer offset to the length of the shorter one.
            if (sqrMag21 > sqrMag23) {
                offset21 *= math.sqrt(sqrMag23 / sqrMag21);
            } else {
                offset23 *= math.sqrt(sqrMag21 / sqrMag23);
            }

            return (offset23 - offset21) * 0.5f;
        }

        // Profile tangent raycasting.
        private static readonly ProfilerMarker MarkerRaycastSplineTangents =
            new ProfilerMarker("Raycast Spline Tangents");

        // Perform a raycast in the NavVolume for each tangent, ensuring that they don't penetrate blocking geometry.
        private void DoTangentRaycastChecks() {
            using ProfilerMarker.AutoScope scope = MarkerRaycastSplineTangents.Auto();

            // Two raycasts (forward and backward) for each tangent.
            // First and last points are not included since the tangents are always zero.
            int raycastCount = (PointCount - 2) * 2;
            NativeArray<NativeRaycastElement> raycasts =
                new NativeArray<NativeRaycastElement>(raycastCount, Allocator.TempJob);

            // Add all tangents to the raycasts array along with all needed info.
            for (int i = 1; i < PointCount - 1; i++) {
                SplinePoint info = _controlPoints[i];
                float4 position = info.Position;
                float4 tangent = info.Tangent;

                int r1 = (i - 1) * 2;
                int r2 = r1 + 1;

                if (math.lengthsq(tangent) > 0.0001f) {
                    raycasts[r1] = new NativeRaycastElement {
                        Start = position,
                        End = position - tangent,
                        VolumeID = info.FromVolume,
                    };

                    raycasts[r2] = new NativeRaycastElement {
                        Start = position,
                        End = position + tangent,
                        VolumeID = info.ToVolume,
                    };
                }
            }

            // Perform the raycasts in parallel using the job system.
            NavMultiRaycastJob job = new NavMultiRaycastJob {
                Raycasts = raycasts,
                Volumes = NavVolume.VolumeData,
            };

            JobHandle handle = job.Schedule(raycastCount, 1);
            handle.Complete();

            // Read results from the job and shorten tangents where necessary.
            for (int i = 1; i < PointCount - 1; i++) {
                SplinePoint info = _controlPoints[i];

                int r1 = (i - 1) * 2;
                int r2 = r1 + 1;

                NativeRaycastElement raycast1 = raycasts[r1];
                NativeRaycastElement raycast2 = raycasts[r2];

                // If both raycasts for a tangent hit, we need to use the shorter one
                // to shorten both the forward and backward directions.
                // Otherwise, just shorten both directions by the distance of the one that hit.
                if (raycast1.OutDistance >= 0 && raycast2.OutDistance >= 0) {
                    info.Tangent *= math.min(raycast1.OutDistance, raycast2.OutDistance);
                } else if (raycast1.OutDistance > 0) {
                    info.Tangent *= raycast1.OutDistance;
                } else if (raycast2.OutDistance > 0) {
                    info.Tangent *= raycast2.OutDistance;
                }

                _controlPoints[i] = info;
            }

            // Dispose array.
            job.Raycasts.Dispose();
        }

        // Calculate matrices which are used to calculate position/tangent on a segment.
        // Using a matrix is faster than doing the math manually, especially in Burst thanks to SIMD optimization.
        private void CalculateMatrices() {
            for (int i = 0; i < PointCount; i++) {
                SplinePoint cur = _controlPoints[i];
                
                float4 p1 = cur.Position;
                float4 t1 = cur.Tangent;

                if (i == PointCount - 1) {
                    // For last point there is no segment, we only need to be able to calculate the position
                    // at that exact point.
                    cur.PositionMatrix = new float4x4(float4.zero, float4.zero, float4.zero, p1);
                } else {
                    // The equation for calculating a position on a segment is P = at^3 + bt^2 + ct + d.
                    // The tangent (derivative) is T = 3at^2 + 2bt + c.
                    // If we consider a vector that has components <t^3, t^2, t, 1>,
                    // we can multiply that by a matrix whose columns are a, b, c, d to get the position at t.
                    // similarly, we can multiply by a matrix with columns 0, 3a, 2b, c to get the derivative at t.
                    
                    SplinePoint next = _controlPoints[i + 1];

                    float4 p2 = next.Position;
                    float4 t2 = next.Tangent;

                    // Calculate p1 - p2 here since it is used in two places.
                    float4 p1MinusP2 = p1 - p2;

                    // Calculate coefficients for cubic function.
                    float4 a = 2 * p1MinusP2 + 3 * (t1 + t2);
                    float4 b = -3 * p1MinusP2 - 3 * (2 * t1 + t2);
                    float4 c = 3 * t1;
                    float4 d = p1;

                    // Populate matrices, one coefficient is a column (where each row is one component x, y, z, or w).
                    cur.PositionMatrix = new float4x4(a, b, c, d);
                    cur.TangentMatrix = new float4x4(float4.zero, 3 * a, 2 * b, c);
                }
                
                _controlPoints[i] = cur;
            }
        }

        // Find the parameter for a given distance along the spline using binary search.
        private float GetParameter(float distance, int start, int end) {
            if (end == start) {
                Debug.LogError($"Error getting distance {distance} along spline.");
                return 0;
            }

            // Binary search finished, just lerp between the samples.
            if (end == start + 1) {
                return GetParameterAlongSegment(distance, start, end);
            }

            // Get midpoint of given range and perform next iteration of binary search.
            int mid = (start + end) / 2;

            float distanceMid = _samples[mid];
            if (distance < distanceMid) {
                return GetParameter(distance, start, mid);
            } else {
                return GetParameter(distance, mid, end);
            }
        }

        // Approximate the parameter value for the given distance by lerping between the two samples.
        private float GetParameterAlongSegment(float distance, int index1, int index2) {
            float d1 = _samples[index1];
            float d2 = _samples[index2];

            float onSegment = math.unlerp(d1, d2, distance);

            float div = _samples.Length - 1;
            float i1 = index1 / div;
            float i2 = index2 / div;

            return math.lerp(i1, i2, onSegment);
        }

        // Calculate samples to convert between distance and parameter.
        private void Sample(int sampleCount) {
            if (!_samples.IsCreated || _samples.Length < sampleCount) {
                if (_samples.IsCreated) {
                    _samples.Dispose();
                }

                _samples = new NativeArray<float>(sampleCount, Allocator.Persistent);
            }
            
            // Accumulate length by getting distance between sampled point and previous point.
            float4 lastPosition = float4.zero;
            float lengthAccumulated = 0;

            // Loop through number of samples.
            for (int i = 0; i < _samples.Length; i++) {
                float parameter = i / ((float) _samples.Length - 1);
                float4 pos = GetPositionInternal(parameter);

                // After the first sample, start accumulating distance.
                if (i > 0) {
                    float length = math.distance(lastPosition, pos);
                    lengthAccumulated += length;
                }

                _samples[i] = lengthAccumulated;
                lastPosition = pos;
            }

            Length = lengthAccumulated;
        }

        // For a given parameter value, find which segment it lies on and get the time vector.
        // This time vector is used to multiply by the position and/or tangent matrices of the segment.
        private void GetSegmentAndTimeVector(float parameter, out SplinePoint segment, out float4 timeVector) {
            float paramMultiplied = parameter * (PointCount - 1);
            int segmentIndex = (int) math.floor(paramMultiplied);
            float t = paramMultiplied - segmentIndex;
            
            // Time vector is <t^3, t^2, t, 1>
            timeVector = new float4(t * t * t, t * t, t, 1);
            segment = _controlPoints[segmentIndex];
        }

        // Find which two control points the given parameter value is between.
        private float GetSegment(float pos, out int indexA, out int indexB) {
            pos = math.clamp(pos, 0, 1) * (PointCount - 1);
            int rounded = (int) math.round(pos);
            if (math.abs(pos - rounded) < 0.0001f) {
                // Exactly at a single control point.
                indexA = indexB = (rounded == PointCount) ? 0 : rounded;
            } else {
                // In between two control points.
                indexA = (int) math.floor(pos);
                indexB = (int) math.ceil(pos);
                if (indexB >= PointCount) {
                    indexB = 0;
                }
            }

            return pos;
        }

        #endregion
    }

    /// <summary>
    /// Represents a point on a spline and the segment that starts with it.
    /// </summary>
    public struct SplinePoint {
        /// <summary>
        /// Position of the control point.
        /// </summary>
        public float4 Position;
        
        /// <summary>
        /// Tangent of the control point.
        /// </summary>
        public float4 Tangent;
        
        /// <summary>
        /// Matrix to multiply by a time vector along the segment to get a position.
        /// </summary>
        public float4x4 PositionMatrix;
        
        /// <summary>
        /// Matrix to multiply by a time vector along the segment to get a tangent.
        /// </summary>
        public float4x4 TangentMatrix;

        /// <summary>
        /// Volume that leads to the control point.
        /// </summary>
        public long FromVolume;
        
        /// <summary>
        /// Volume that the control point leads to.
        /// </summary>
        public long ToVolume;
    }
}