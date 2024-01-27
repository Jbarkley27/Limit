// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Unity.Mathematics;
using Unity.Profiling;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// Provides math operations that are compatible with Burst.
    /// </summary>
    /// <remarks>
    /// Managed side versions are available in the Infohazard.Core library under MathUtility.
    /// </remarks>
    public static class NativeMathUtility {
        /// <summary>
        /// Project a vector onto a the plane defined by a normal.
        /// </summary>
        /// <param name="vector">The vector to project.</param>
        /// <param name="normal">The normal of the plane.</param>
        /// <returns>The projected vector.</returns>
        public static float4 ProjectOnPlane(in float4 vector, in float3 normal) {
            float3 proj = math.project(vector.xyz, normal);
            return vector - new float4(proj, 0);
        }

        /// <summary>
        /// Find the point on a bounded line segment where it is nearest to a position,
        /// and return whether that point is in the segment's bounds.
        /// </summary>
        /// <remarks>
        /// Does not return points on the ends of the segment.
        /// If the nearest point on the segment's line is outside the segment,
        /// will fail and not return a valid point.
        /// </remarks>
        /// <param name="v1">The start of the segment.</param>
        /// <param name="v2">The end of the segment.</param>
        /// <param name="point">The point to search for.</param>
        /// <param name="pointOnSegment">The point on the segment closest to the input point.</param>
        /// <returns>Whether the nearest point is within the segment's bounds.</returns>
        public static bool GetNearestPointOnSegment(in float4 v1, in float4 v2, in float4 point,
                                                    out float4 pointOnSegment) {
            pointOnSegment = default;

            float4 v1ToV2 = v2 - v1;

            if (math.dot(v1ToV2, point - v1) < 0) return false;
            if (math.dot(-v1ToV2, point - v2) < 0) return false;

            float4 proj = math.project(point - v1, v1ToV2);
            pointOnSegment = v1 + proj;
            return true;
        }

        /// <summary>
        /// Find the point on a triangle where it is nearest to a position,
        /// and return whether that point is in the triangle's bounds.
        /// </summary>
        /// <remarks>
        /// Does not return points on the edge of the triangle.
        /// If the nearest point on the triangle's plane is outside the triangle,
        /// will fail and not return a valid point.
        /// </remarks>
        /// <param name="v1">The first triangle point.</param>
        /// <param name="v2">The second triangle point.</param>
        /// <param name="v3">The third triangle point.</param>
        /// <param name="point">The point to search for.</param>
        /// <param name="pointOnTriangle">The point on the triangle closest to the input point.</param>
        /// <returns>Whether the nearest point is within the triangle's bounds.</returns>
        public static bool GetNearestPointOnTriangle(in float4 v1, in float4 v2, in float4 v3, in float4 point,
                                                     out float4 pointOnTriangle) {
            pointOnTriangle = default;

            float3 normal = math.cross((v3 - v2).xyz, (v1 - v2).xyz);

            if (!IsPointInsideBound(v1, v2, normal, point) ||
                !IsPointInsideBound(v2, v3, normal, point) ||
                !IsPointInsideBound(v3, v1, normal, point)) {
                return false;
            }

            float4 proj = ProjectOnPlane(point - v1, normal);
            pointOnTriangle = v1 + proj;
            return true;
        }

        /// <summary>
        /// Returns true if a given point is on the inner side (defined by a given normal) of a segment.
        /// </summary>
        /// <param name="v1">The start of the segment.</param>
        /// <param name="v2">The end of the segment.</param>
        /// <param name="normal">The normal, defining which side is inside.</param>
        /// <param name="point">The point to search for.</param>
        /// <returns>Whether the point is on the inner side.</returns>
        public static bool IsPointInsideBound(in float4 v1, in float4 v2, in float3 normal, in float4 point) {
            float3 edge = (v2 - v1).xyz;
            float3 cross = math.normalize(math.cross(normal, edge));
            float4 pointOffset = math.normalize(point - v1);

            float dot = math.dot(pointOffset.xyz, cross);
            return dot > -.00001f;
        }

        /// <summary>
        /// Raycast a line segment against a triangle, and return whether they intersect.
        /// </summary>
        /// <param name="v1">The first triangle point.</param>
        /// <param name="v2">The second triangle point.</param>
        /// <param name="v3">The third triangle point.</param>
        /// <param name="s1">The start of the segment.</param>
        /// <param name="s2">The end of the segment.</param>
        /// <param name="t">The point along the input segment where it intersects the triangle, or -1.</param>
        /// <returns>Whether the segment intersects the triangle.</returns>
        public static bool DoesSegmentIntersectTriangle(in float4 v1, in float4 v2, in float4 v3, in float4 s1,
                                                        in float4 s2, out float t) {
            // Implements the Möller–Trumbore intersection algorithm
            // Ported from Wikipedia's C++ implementation:
            // https://en.wikipedia.org/wiki/M%C3%B6ller%E2%80%93Trumbore_intersection_algorithm

            float4 rayVector = s2 - s1;
            float4 rayOrigin = s1;
            t = -1;

            const float epsilon = float.Epsilon;

            float4 edge1 = v2 - v1;
            float4 edge2 = v3 - v1;
            float4 h = new float4(math.cross(rayVector.xyz, edge2.xyz), 0);
            float a = math.dot(edge1, h);
            if (a > -epsilon && a < epsilon) {
                return false; // This ray is parallel to this triangle.
            }

            float f = 1.0f / a;
            float4 s = rayOrigin - v1;
            float u = f * math.dot(s, h);
            if (u < 0.0 || u > 1.0) {
                return false;
            }

            float4 q = new float4(math.cross(s.xyz, edge1.xyz), 0);
            float v = f * math.dot(rayVector, q);
            if (v < 0.0 || u + v > 1.0) {
                return false;
            }

            // At this stage we can compute t to find out where the intersection point is on the line.
            t = f * math.dot(edge2, q);
            return t > 0 && t < 1;
        }

        /// <summary>
        /// Cast a ray against the blocking triangles of the volume, and return the nearest hit.
        /// </summary>
        /// <param name="start">The position (in world space) to start the query at.</param>
        /// <param name="end">The position (in world space) to end the query at.</param>
        /// <param name="earlyReturn">If true, will return true as soon as any triangle is hit, not necessarily giving you the closest hit point.</param>
        /// <param name="volume">The volume in which to raycast.</param>
        /// <param name="t">If the query hits a triangle, the ratio between start and end at which the hit occurred.</param>
        /// <returns>Whether a triangle was hit.</returns>
        public static unsafe bool NavRaycast(float4 start, float4 end, bool earlyReturn, in NativeNavVolumeData volume,
                                             out float t) {
            float4 localStart = math.mul(volume.InverseTransform, start);
            float4 localEnd = math.mul(volume.InverseTransform, end);

            float4 offset = localEnd - localStart;
            float offsetSq = math.lengthsq(offset);

            t = -1;
            bool didHit = false;

            // Loop through all triangles and perform line check.
            int indexCount = volume.BlockingTriangleIndices.Length;
            int triCount = indexCount / 3;
            for (int triIndex = 0; triIndex < triCount; triIndex++) {
                // Squeezing out as much performance as possible by reading the pointers directly.
                int3* indexPtr = (int3*) volume.BlockingTriangleIndices.Pointer;
                int3 i = indexPtr[triIndex];
                float4* vertexPointer = (float4*) volume.Vertices.Pointer;

                float4 v1 = vertexPointer[i.x];
                float4 v2 = vertexPointer[i.y];
                float4 v3 = vertexPointer[i.z];

                // Project all points of the triangle on the line axis.
                float d1 = math.dot(v1 - localStart, offset);
                float d2 = math.dot(v2 - localStart, offset);
                float d3 = math.dot(v3 - localStart, offset);

                // If they are all outside the segment on its own axis and in the same direction,
                // there must be no hit and this triangle can be skipped.
                // Overall this additional check improves performance because it is significantly faster than
                // the call to DoesSegmentIntersectTriangle, and filters out the vast majority of triangles in a volume.
                if ((d1 < 0 && d2 < 0 && d3 < 0) || (d1 > offsetSq && d2 > offsetSq && d3 > offsetSq)) continue;

                // Check if the segment intersects the triangle, and if it does,
                // check if it is closer than the current nearest hit.
                if (DoesSegmentIntersectTriangle(v1, v2, v3, localStart, localEnd, out float tempHit) &&
                    tempHit > 0.01f &&
                    (!didHit || tempHit < t)) {
                    t = tempHit;
                    didHit = true;

                    // Any line that intersects with the triangle means it's not clear.
                    if (earlyReturn) return true;
                }
            }

            return didHit;
        }

        /// <summary>
        /// Returns an arbitrary vector that is perpendicular to the given vector.
        /// </summary>
        /// <param name="vector">Input vector.</param>
        /// <returns>A perpendicular vector.</returns>
        public static float4 GetPerpendicularVector(float4 vector) {
            float4 crossRight = new float4(math.cross(vector.xyz, new float3(1, 0, 0)), 0);
            if (math.lengthsq(crossRight) > 0) return math.normalize(crossRight);
            return math.normalize(new float4(math.cross(vector.xyz, new float3(0, 1, 0)), 0));
        }
    }
}