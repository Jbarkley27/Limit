// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Unity.Mathematics;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// Represents one obstacle (which may be an agent) in the avoidance system.
    /// </summary>
    public struct NativeAvoidanceObstacleData {
        /// <summary>
        /// Position of the obstacle.
        /// </summary>
        public float3 Position;
        
        /// <summary>
        /// Velocity of the obstacle (or desired velocity if it is an agent).
        /// </summary>
        public float3 InputVelocity;
        
        /// <summary>
        /// Radius of the obstacle.
        /// </summary>
        public float Radius;
        
        /// <summary>
        /// If an agent, extra padding to give when avoiding obstacles.
        /// </summary>
        public float Padding;
        
        /// <summary>
        /// If an agent, its contribution weight to avoidance. If not an agent, zero.
        /// </summary>
        public float Avoidance;
        
        /// <summary>
        /// The maximum speed that this obstacle can move at.
        /// </summary>
        public float Speed;
        
        /// <summary>
        /// The tag mask of this obstacle.
        /// </summary>
        public long TagMask;
        
        /// <summary>
        /// If an agent, the tag masks this agent will avoid.
        /// </summary>
        public long AvoidedTags;
        
        /// <summary>
        /// If an agent, whether to draw debug lines when calculating avoidance.
        /// </summary>
        public bool Debug;
    }

    /// <summary>
    /// A ray constructed using native math types.
    /// </summary>
    public struct NativeRay {
        /// <summary>
        /// Origin of the ray.
        /// </summary>
        public float4 Origin;
        
        /// <summary>
        /// Direction of the ray, which should be normalized.
        /// </summary>
        public float4 Direction;
    }

    /// <summary>
    /// A plane constructed using native math types.
    /// </summary>
    public struct NativePlane {
        /// <summary>
        /// Normal of the plane, which should be normalized.
        /// </summary>
        public float4 Normal;
        
        /// <summary>
        /// Distance from the origin to the nearest point on the plane.
        /// </summary>
        public float Distance;

        /// <summary>
        /// Nearest point to the origin on the plane.
        /// </summary>
        public float4 Center => Normal * -Distance;

        /// <summary>
        /// Construct a new NativePlane, calculating the distance based on any point in the plane.
        /// </summary>
        /// <param name="normal">Normal of the plane, which should be normalized.</param>
        /// <param name="point">Any point on the plane.</param>
        public NativePlane(float4 normal, float4 point) {
            Normal = normal;
            Distance = -math.dot(normal, point);
        }
    }
}