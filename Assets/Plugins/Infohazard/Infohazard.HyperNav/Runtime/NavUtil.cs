// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

// Parts of this file were adapted from the RVO2-3D library from University of North Carolina.
// https://github.com/snape/RVO2-3D/blob/main/src/Agent.cc.

using System.Collections.Generic;
using Infohazard.Core;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Contains utility methods for working with HyperNav navigation.
    /// </summary>
    public static class NavUtil {
        public const float Epsilon = 0.00001f;
        
        private static List<Plane> _tempProjPlanes = new List<Plane>();
        
        /// <summary>
        /// Perform a query to find the nearest point on any <see cref="NavVolume"/> to the given point.
        /// </summary>
        /// <param name="position">The point at which to search.</param>
        /// <param name="hit">The resulting hit, containing the nearest point on a volume.</param>
        /// <param name="maxDistance">The radius in which to search (a larger value is more expensive).</param>
        /// <returns>Whether a hit on any volume could be found in the given radius.</returns>
        public static bool SamplePosition(Vector3 position, out NavHit hit, float maxDistance) {
            foreach (NavVolume volume in NavVolume.Volumes.Values) {
                if (volume.SamplePosition(position, out hit, maxDistance)) return true;
            }

            hit = default;
            return false;
        }

        private static readonly List<Plane> TempPlanes = new List<Plane>();
        
        
    }
}