// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using Infohazard.Core;
using UnityEngine;
using UnityEngine.Events;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Base implementation of IAvoidanceAgent that should work in most scenarios.
    /// </summary>
    /// <remarks>
    /// It gets its desired velocity from a delegate, so you can point it to whatever system you need.
    /// </remarks>
    public class AvoidanceAgent : AvoidanceObstacleBase, IAvoidanceAgent {
        #region Serialized Fields
        
        /// <summary>
        /// (Serialized) How much effort the agent will take to avoid obstacles and other agents.
        /// </summary>
        [SerializeField]
        [Tooltip("How much effort the agent will take to avoid obstacles and other agents.")]
        private float _avoidanceWeight = 1;

        /// <summary>
        /// (Serialized) How much extra space to leave when avoiding obstacles.
        /// </summary>
        [SerializeField]
        [Tooltip("How much extra space to leave when avoiding obstacles.")]
        private float _avoidancePadding = 0.1f;

        /// <summary>
        /// (Serialized) Whether to draw debugging information in the scene view.
        /// </summary>
        [SerializeField]
        [Tooltip("Whether to draw debugging information in the scene view.")]
        private bool _debugAvoidance = false;

        /// <summary>
        /// (Serialized) Tags of objects that the agent will try to avoid.
        /// </summary>
        [SerializeField]
        [Tooltip("Tags of objects that the agent will try to avoid.")]
        private TagMask _avoidedTags = ~0;

        #endregion

        #region Private Fields
        
        // Whether the agent is active.
        private bool _isActive = true;
        
        // Whether the agent is currently registered in Avoidance.AllAgents.
        private bool _isRegistered = false;
        
        // Calculated optimal avoidance velocity.
        private Vector3 _avoidanceVelocity;

        #endregion

        #region Events
        
        /// <summary>
        /// Invoked when avoidance is updated.
        /// </summary>
        public event Action<Vector3> AvoidanceUpdated;
        
        #endregion

        #region Serialized Field Accessor Properties

        /// <inheritdoc/>
        public virtual float AvoidanceWeight {
            get => _avoidanceWeight;
            set => _avoidanceWeight = value;
        }

        /// <inheritdoc/>
        public virtual float AvoidancePadding {
            get => _avoidancePadding;
            set => _avoidancePadding = value;
        }
        
        /// <inheritdoc/>
        public virtual bool DebugAvoidance {
            get => _debugAvoidance;
            set => _debugAvoidance = value;
        }

        /// <inheritdoc/>
        public TagMask AvoidedTags {
            get => _avoidedTags;
            set => _avoidedTags = value;
        }

        #endregion

        #region Additional Properties
        
        /// <summary>
        /// Function used to calculate InputVelocity.
        /// </summary>
        public Func<Vector3> InputVelocityFunc { get; set; }

        /// <inheritdoc/>
        public override Vector3 InputVelocity => InputVelocityFunc?.Invoke() ?? Vector3.zero;

        /// <inheritdoc/>
        public virtual bool IsActive {
            get => _isActive;
            set {
                if (_isActive == value) return;
                _isActive = value;
                UpdateRegistered();
            }
        }
        
        /// <inheritdoc/>
        public virtual Vector3 AvoidanceVelocity => _avoidanceVelocity;

        /// <summary>
        /// Avoidance velocity divided by max speed, so it is in [0, 1] range.
        /// </summary>
        public virtual Vector3 NormalizedAvoidanceVelocity => AvoidanceVelocity / MaxSpeed;

        #endregion

        #region Public Methods

        /// <inheritdoc/>
        public void UpdateAvoidanceVelocity(Vector3 newAvoidance) {
            _avoidanceVelocity = newAvoidance;
            AvoidanceUpdated?.Invoke(newAvoidance);
        }

        #endregion

        #region Unity Callbacks
        
        /// <inheritdoc/>
        protected override void OnEnable() {
            base.OnEnable();
            _avoidanceVelocity = Vector3.zero;
            UpdateRegistered();

            if (AvoidanceManager.Instance == null) {
                Debug.LogError($"AvoidanceAgent {name} detected in scene, but no AvoidanceManager is present.", this);
            }
        }

        /// <inheritdoc/>
        protected override void OnDisable() {
            base.OnDisable();
            UpdateRegistered();
        }

        #endregion

        #region Internal Methods

        // Update whether the agent is registered in the Avoidance.AllAgents list.
        private void UpdateRegistered() {
            bool shouldBeRegistered = isActiveAndEnabled && _isActive;
            if (shouldBeRegistered == _isRegistered) return;
            _isRegistered = shouldBeRegistered;

            if (shouldBeRegistered) {
                Avoidance.AllAgents.Add(this);
            } else {
                Avoidance.AllAgents.Remove(this);
            }
        }

        #endregion
    }
}