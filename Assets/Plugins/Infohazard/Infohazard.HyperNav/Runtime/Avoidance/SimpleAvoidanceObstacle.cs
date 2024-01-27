// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// An <see cref="IAvoidanceObstacle"/> that gets its <see cref="IAvoidanceObstacle.InputVelocity"/>
    /// by measuring its position/time delta.
    /// </summary>
    public class SimpleAvoidanceObstacle : AvoidanceObstacleBase {
        private Vector3 _lastPosition;
        private Vector3 _measuredVelocity;

        /// <inheritdoc/>
        public override Vector3 InputVelocity => _measuredVelocity;

        /// <inheritdoc/>
        protected override void OnEnable() {
            base.OnEnable();
            _measuredVelocity = Vector3.zero;
            _lastPosition = transform.position;
        }

        /// <summary>
        /// Update computed velocity.
        /// </summary>
        protected virtual void LateUpdate() {
            Vector3 currentPosition = transform.position;
            _measuredVelocity = (currentPosition - _lastPosition) / Time.deltaTime;
            
            _lastPosition = currentPosition;
        }
    }
}