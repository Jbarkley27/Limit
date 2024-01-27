// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// An <see cref="IAvoidanceObstacle"/> that gets its <see cref="IAvoidanceObstacle.InputVelocity"/>
    /// from a Rigidbody.
    /// </summary>
    public class RigidbodyAvoidanceObstacle : AvoidanceObstacleBase {
        /// <summary>
        /// (Serialized) Rigidbody to get the velocity from.
        /// </summary>
        [SerializeField]
        [Tooltip("Rigidbody to get the velocity from.")]
        private Rigidbody _rigidbody;

        /// <summary>
        /// Rigidbody to get the velocity from.
        /// </summary>
        public Rigidbody Rigidbody {
            get => _rigidbody;
            set => _rigidbody = value;
        }

        /// <inheritdoc/>
        public override Vector3 InputVelocity => _rigidbody.velocity;

        /// <summary>
        /// Set <see cref="_rigidbody"/>.
        /// </summary>
        public virtual void Reset() {
            _rigidbody = GetComponent<Rigidbody>();
        }
    }
}