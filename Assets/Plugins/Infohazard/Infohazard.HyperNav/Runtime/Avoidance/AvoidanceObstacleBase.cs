// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using Infohazard.Core;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Base class for obstacles that are not expected to perform avoidance, but are avoided by agents.
    /// </summary>
    /// <remarks>
    /// Objects that do perform avoidance (which are obstacles as well)
    /// should inherit from <see cref="AvoidanceAgent"/> instead,
    /// which is a derived class of <see cref="AvoidanceObstacleBase"/>.
    /// </remarks>
    public class AvoidanceObstacleBase : MonoBehaviour, IAvoidanceObstacle {
        #region Serialized Fields
        
        /// <summary>
        /// (Serialized) Radius of the obstacle.
        /// </summary>
        [SerializeField]
        [Tooltip("Radius of the obstacle.")]
        private float _radius = 0.5f;
        
        /// <summary>
        /// (Serialized) Maximum speed the obstacle can travel at.
        /// </summary>
        [SerializeField]
        [Tooltip("Maximum speed the obstacle can travel at.")]
        private float _maxSpeed = 0;

        #endregion

        #region Serialized Field Accessor Properties
        
        /// <inheritdoc/>
        public virtual float MaxSpeed {
            get => _maxSpeed;
            set => _maxSpeed = value;
        }

        /// <inheritdoc/>
        public virtual float Radius {
            get => _radius;
            set => _radius = value;
        }

        #endregion

        #region Additional Properties
        
        /// <inheritdoc/>
        public virtual Vector3 Position => transform.position;

        /// <inheritdoc/>
        public virtual Vector3 InputVelocity => Vector3.zero;

        /// <inheritdoc/>
        public TagMask TagMask { get; private set; }

        #endregion

        #region Unity Callbacks

        /// <summary>
        /// Resets desired velocity and adds self to list of all obstacles.
        /// </summary>
        protected virtual void OnEnable() {
            TagMask = gameObject.GetTagMask();
            Avoidance.AllObstacles.Add(this);
        }

        /// <summary>
        /// Removes self from list of all obstacles.
        /// </summary>
        protected virtual void OnDisable() {
            Avoidance.AllObstacles.Remove(this);
        }

        #endregion
    }
}