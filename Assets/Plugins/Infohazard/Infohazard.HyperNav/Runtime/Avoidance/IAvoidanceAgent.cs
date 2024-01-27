// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Infohazard.Core;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Interface for objects that both can be avoided and themselves avoid other obstacles using the avoidance system.
    /// </summary>
    public interface IAvoidanceAgent : IAvoidanceObstacle {
        /// <summary>
        /// How much effort the agent will take to avoid obstacles and other agents.
        /// </summary>
        public float AvoidanceWeight { get; }
        
        /// <summary>
        /// How much extra space to leave when avoiding obstacles.
        /// </summary>
        public float AvoidancePadding { get; }
        
        /// <summary>
        /// The velocity the agent should have in order to avoid collisions with obstacles and other agents.
        /// </summary>
        public Vector3 AvoidanceVelocity { get; }
        
        /// <summary>
        /// Whether the agent should actively avoid obstacles. If false, will still behave as an obstacle.
        /// </summary>
        public bool IsActive { get; }
        
        /// <summary>
        /// Whether to draw debugging information in the scene view.
        /// </summary>
        public bool DebugAvoidance { get; }
        
        /// <summary>
        /// Tags of objects that the agent will try to avoid.
        /// </summary>
        public TagMask AvoidedTags { get; }

        /// <summary>
        /// Called by the system to update <see cref="AvoidanceVelocity"/>.
        /// </summary>
        /// <param name="newAvoidance">New avoidance velocity</param>
        public void UpdateAvoidanceVelocity(Vector3 newAvoidance);
    }
}