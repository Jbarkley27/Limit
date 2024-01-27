// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System.Collections.Generic;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Static container that keeps track of all avoidance agents and obstacles.
    /// </summary>
    public static class Avoidance {
        /// <summary>
        /// All active obstacles (including agents).
        /// </summary>
        public static List<IAvoidanceObstacle> AllObstacles { get; } = new List<IAvoidanceObstacle>();
        
        /// <summary>
        /// All active agents.
        /// </summary>
        public static List<IAvoidanceAgent> AllAgents { get; } = new List<IAvoidanceAgent>();
    }
}