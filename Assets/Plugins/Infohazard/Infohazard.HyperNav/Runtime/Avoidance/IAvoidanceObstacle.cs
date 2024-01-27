// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System.Collections.Generic;
using Infohazard.Core;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Interface for objects that can be avoided using the avoidance system.
    /// </summary>
    public interface IAvoidanceObstacle {
        /// <summary>
        /// World-space position of the object.
        /// </summary>
        public Vector3 Position { get; }
        
        /// <summary>
        /// The object's desired (or actual) velocity.
        /// </summary>
        public Vector3 InputVelocity { get; }
        
        /// <summary>
        /// Maximum speed the object can travel at.
        /// </summary>
        public float MaxSpeed { get; }
        
        /// <summary>
        /// Radius of the object from its position.
        /// </summary>
        public float Radius { get; }
        
        /// <summary>
        /// Tag of the object for matching agents' <see cref="IAvoidanceAgent.AvoidedTags"/>.
        /// </summary>
        public TagMask TagMask { get; }
    }
}