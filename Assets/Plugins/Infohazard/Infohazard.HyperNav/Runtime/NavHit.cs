// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Infohazard.Core;
using Infohazard.HyperNav.Jobs;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Structure that is used to report the nearest point on a <see cref="NavVolume"/> to a query.
    /// </summary>
    public struct NavHit {
        /// <summary>
        /// The <see cref="NavVolume"/> that was hit.
        /// </summary>
        public NavVolume Volume { get; set; }
        
        /// <summary>
        /// The region index within the hit <see cref="Volume"/>.
        /// </summary>
        public int Region { get; set; }
        
        /// <summary>
        /// If true, query point was outside the region and thus this result is the nearest point.
        /// If false, query point was inside the region and the hit was at that exact position.
        /// </summary>
        public bool IsOnEdge { get; set; }
        
        /// <summary>
        /// The position of the query result point.
        /// </summary>
        public Vector3 Position { get; set; }
        
        /// <summary>
        /// Currently not used and always Vector3.zero.
        /// </summary>
        public Vector3 Normal { get; set; }

        /// <summary>
        /// Whether a valid <see cref="Volume"/> and <see cref="Region"/> were hit.
        /// </summary>
        public bool IsValid => Volume != null;

        internal NativeNavHit ToInternal() {
            // Convert to internal representation to be used in the job system.
            return new NativeNavHit(Volume.InstanceID, Region, IsOnEdge, Position.ToV4Pos());
        }
    }
}