// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// A script that can be used to calculate paths by any entity that needs to use HyperNav for navigation.
    /// </summary>
    /// <remarks>
    /// While a NavAgent is not necessary to use HyperNav, it makes pathfinding easier.
    /// The NavAgent does not impose any restrictions on how movement occurs, nor does it actually perform any movement.
    /// It simply provides a desired movement velocity, which other scripts on the object are responsible
    /// for using however they need.
    /// <p/>
    /// The agent can have one active path (the path it is currently following),
    /// but can have multiple pending paths (paths in the process of being calculated by a NavPathfinder).
    /// <p/>
    /// If you desire smoother movement then what the NavAgent provides, see <see cref="SplineNavAgent"/>.
    /// </remarks>
    public class NavAgent : MonoBehaviour {
        #region Serialized Fields

        /// <summary>
        /// (Serialized) How close the agent must get to a destination before it is considered to have arrived.
        /// </summary>
        [SerializeField]
        [Tooltip("How close the agent must get to a destination before it is considered to have arrived.")]
        private float _acceptance = 1;
        
        /// <summary>
        /// (Serialized) This should be set to the maximum acceleration of your agent (can be set dynamically as well).
        /// </summary>
        [SerializeField]
        [Tooltip("This should be set to the maximum acceleration of your agent (can be set dynamically as well).")]
        private float _accelerationEstimate = 0;
        
        /// <summary>
        /// (Serialized) The radius to search when finding the nearest NavVolume.
        /// </summary>
        [SerializeField] 
        [Tooltip("The radius to search when finding the nearest NavVolume.")]
        private float _sampleRadius = 2;

        /// <summary>
        /// (Serialized) The desired fraction of the maximum speed to travel at.
        /// </summary>
        [SerializeField]
        [Tooltip("The desired fraction of the maximum speed to travel at.")]
        [Range(0, 1)]
        private float _desiredSpeedRatio = 1;
        
        /// <summary>
        /// (Serialized) Whether to draw a debug line in the scene view showing the agent's current path.
        /// </summary>
        [SerializeField] 
        [Tooltip("Whether to draw a debug line in the scene view showing the agent's current path.")]
        private bool _debugPath = true;
        
        /// <summary>
        /// (Serialized) Whether to keep following the current path while waiting for a new path to finish calculating.
        /// </summary>
        [SerializeField]
        [Tooltip("Whether to keep following the current path while waiting for a new path to finish calculating.")]
        private bool _keepPathWhileCalculating = true;

        /// <summary>
        /// (Serialized) <see cref="AvoidanceAgent"/> that this agent uses for avoidance (can be null).
        /// </summary>
        [SerializeField]
        [Tooltip(nameof(AvoidanceAgent) + " that this agent uses for avoidance (can be null).")]
        private AvoidanceAgent _avoidanceAgent;

        /// <summary>
        /// (Serialized) If true, the <see cref="Infohazard.HyperNav.AvoidanceAgent.IsActive"/> state of the
        /// <see cref="AvoidanceAgent"/> is set based on whether there is a current valid path.
        /// </summary>
        [SerializeField]
        [Tooltip("If true, the IsActive state of the AvoidanceAgent is set based on whether there is a current valid path.")]
        private bool _controlAvoidanceIsActive = true;
        
        /// <summary>
        /// This is used to refer to the names of private fields in this class from a custom Editor.
        /// </summary>
        public static class PropNames {
            public const string Acceptance = nameof(_acceptance);
            public const string AccelerationEstimate = nameof(_accelerationEstimate);
            public const string SampleRadius = nameof(_sampleRadius);
            public const string DesiredSpeedRatio = nameof(_desiredSpeedRatio);
            public const string DebugPath = nameof(_debugPath);
            public const string KeepPathWhileCalculating = nameof(_keepPathWhileCalculating);
            public const string AvoidanceAgent = nameof(_avoidanceAgent);
        }
        
        #endregion

        #region Private Fields
        
        // Pending path requests in order from oldest to newest.
        private readonly List<long> _pendingPathIDs = new List<long>();

        // Current destination of the agent.
        private Vector3 _destination;
        
        // Position at the previous frame, which is used for measuring velocity.
        private Vector3 _positionLastFrame;

        // Which point in the path the agent is at.
        private int _currentIndexInPath;

        // The current path that the agent is following.
        private NavPath _currentPath;

        #endregion

        #region Events

        /// <summary>
        /// Invoked when the agent finds a path to the destination.
        /// </summary>
        public event Action PathReady;
        
        /// <summary>
        /// Invoked when the agent fails to find a path to the destination.
        /// </summary>
        public event Action PathFailed;

        #endregion

        #region Serialized Field Accessor Properties

        /// <summary>
        /// How close the agent must get to a destination before it is considered to have arrived.
        /// </summary>
        /// <remarks>
        /// Note that setting acceptance too low may prevent the agent from ever stopping,
        /// but setting it to high can make the agent stop too far from the destination.
        /// </remarks>
        public float Acceptance {
            get => _acceptance;
            set => _acceptance = value;
        }
        
        /// <summary>
        /// (Serialized) This should be set to the maximum acceleration of your agent.
        /// </summary>
        /// <remarks>
        /// This is used to determine when the agent needs to start slowing down when approaching its destination.
        /// </remarks>
        public float AccelerationEstimate {
            get => _accelerationEstimate;
            set => _accelerationEstimate = value;
        }
        
        /// <summary>
        /// The radius to search when finding the nearest NavVolume.
        /// </summary>
        public float SampleRadius {
            get => _sampleRadius;
            set => _sampleRadius = value;
        }

        /// <summary>
        /// The desired fraction of the maximum speed to travel at.
        /// </summary>
        public float DesiredSpeedRatio {
            get => _desiredSpeedRatio;
            set => _desiredSpeedRatio = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Whether to draw a debug line in the scene view showing the agent's current path.
        /// </summary>
        public bool DebugPath {
            get => _debugPath;
            set => _debugPath = value;
        }

        /// <summary>
        /// Whether to keep following the current path while waiting for a new path to finish calculating.
        /// </summary>
        /// <remarks>
        /// If true, there can be two pending paths at the same time - the most and least recently requested ones.
        /// This ensures that even when the agent is receiving pathfinding requests faster than they can be calculated,
        /// they will still finish and the agent will not be deadlocked and unable to ever complete a path.
        /// </remarks>
        public bool KeepPathWhileCalculating {
            get => _keepPathWhileCalculating;
            set => _keepPathWhileCalculating = value;
        }

        /// <summary>
        /// <see cref="AvoidanceAgent"/> that this agent uses for avoidance (can be null).
        /// </summary>
        public AvoidanceAgent AvoidanceAgent {
            get => _avoidanceAgent;
            set => _avoidanceAgent = value;
        }

        /// <summary>
        /// If true, the <see cref="Infohazard.HyperNav.AvoidanceAgent.IsActive"/> state of the
        /// <see cref="AvoidanceAgent"/> is set based on whether there is a current valid path.
        /// </summary>
        public bool ControlAvoidanceIsActive {
            get => _controlAvoidanceIsActive;
            set => _controlAvoidanceIsActive = true;
        }

        #endregion

        #region Additional Properties

        public Vector3 DesiredVelocity {
            get {
                if (CurrentPath == null) return Vector3.zero;
                if (_avoidanceAgent) return _avoidanceAgent.NormalizedAvoidanceVelocity;
                return CalculateDesiredNavigationVelocity();
            }
        }

        /// <summary>
        /// Whether a path is currently in the process of being calculated for this agent.
        /// </summary>
        public bool IsPathPending => _pendingPathIDs.Count > 0;

        /// <summary>
        /// The distance that it will take the agent to come to a stop from its current velocity,
        /// determined using the <see cref="AccelerationEstimate"/>.
        /// </summary>
        public virtual float StoppingDistance => MeasuredVelocity.sqrMagnitude / (2 * _accelerationEstimate);

        /// <summary>
        /// The current path waypoint that the agent is trying to move towards.
        /// </summary>
        /// <remarks>
        /// If there is no active path, will return the agent's current position.
        /// </remarks>
        public Vector3 NextWaypoint {
            get {
                if (CurrentPath != null && _currentIndexInPath < CurrentPath.Waypoints.Count) {
                    return CurrentPath.Waypoints[_currentIndexInPath].Position;
                }

                // Return current position if there is no active path.
                return transform.position;
            }
        }

        /// <summary>
        /// True if the agent has no active or pending path.
        /// </summary>
        public bool Arrived { get; private set; }
        
        /// <summary>
        /// Get or set the agent's destination (the position it is trying to get to).
        /// </summary>
        /// <remarks>
        /// If set within the <see cref="_acceptance"/> radius of the current position, will abort all movement.
        /// </remarks>
        public Vector3 Destination {
            get => _destination;
            set {
                _destination = value;
                UpdatePath();
            }
        }
        
        /// <summary>
        /// Velocity of the agent measured as delta position / delta time over the last frame,
        /// which is used to determine stopping distance.
        /// </summary>
        /// <remarks>
        /// This value is calculated in <see cref="UpdateMeasuredVelocity"/>.
        /// You can override that method to implement your own logic for calculating velocity.
        /// </remarks>
        public Vector3 MeasuredVelocity { get; protected set; }

        /// <summary>
        /// The current path that the agent is following.
        /// </summary>
        public NavPath CurrentPath {
            get => _currentPath;
            set {
                if (_currentPath == value) return;
                
                _currentPath?.Dispose();
                _currentPath = value;
                if (_avoidanceAgent != null && _controlAvoidanceIsActive) {
                    _avoidanceAgent.IsActive = value != null;
                }
                Arrived = value == null;
                _currentIndexInPath = value == null ? -1 : 0;
            }
        }

        /// <summary>
        /// Maximum speed possible by this agent when avoiding obstacles.
        /// </summary>
        public float AvoidanceMaxSpeed {
            get => _avoidanceAgent ? _avoidanceAgent.MaxSpeed : 0;
            set {
                if (_avoidanceAgent) _avoidanceAgent.MaxSpeed = value;
            }
        }

        #endregion

        #region Unity Callbacks

        /// <summary>
        /// Sets the <see cref="AvoidanceAgent"/>.<see cref="Infohazard.HyperNav.AvoidanceAgent.InputVelocityFunc"/>.
        /// </summary>
        protected virtual void Awake() {
            if (_avoidanceAgent) _avoidanceAgent.InputVelocityFunc = CalculateDesiredNavigationVelocityTimesMaxSpeed;
        }
        
        /// <summary>
        /// Resets <see cref="MeasuredVelocity"/> and sets <see cref="Arrived"/> to true.
        /// </summary>
        protected virtual void OnEnable() {
            _positionLastFrame = transform.position;
            MeasuredVelocity = Vector3.zero;
            Arrived = true;
        }

        /// <summary>
        /// Stops all pathfinding and cancels path requests.
        /// </summary>
        protected virtual void OnDisable() {
            Stop(true);
        }

        /// <summary>
        /// Updates measured velocity and current index in path.
        /// </summary>
        protected virtual void Update() {
            UpdateMeasuredVelocity();
            UpdatePathIndex();
        }

        /// <summary>
        /// Draws the current path as a sequence of debug lines if <see cref="DebugPath"/> is true.
        /// </summary>
        protected virtual void OnDrawGizmos() {
            if (!_debugPath || CurrentPath == null) return;

            Color c = Gizmos.color;
            Gizmos.color = Color.magenta;
            
            Vector3 currentPos = transform.position;
            Vector3 next = NextWaypoint;
            NavWaypoint? prev = null;
            foreach (NavWaypoint waypoint in CurrentPath.Waypoints) {
                if (prev.HasValue) {
                    Gizmos.DrawLine(prev.Value.Position, waypoint.Position);
                }
                prev = waypoint;
            }
            Gizmos.DrawLine(currentPos, next);

            Gizmos.color = c;
        }

        #endregion

        #region Public Methods
        
        /// <summary>
        /// Stop following the current path, and optionally cancel all path requests.
        /// </summary>
        /// <param name="abortPaths">Whether to cancel pending path requests.</param>
        public virtual void Stop(bool abortPaths) {
            _currentPath?.Dispose();
            _currentPath = null;
            _currentIndexInPath = -1;
            if (_avoidanceAgent) _avoidanceAgent.IsActive = false;
            Arrived = true;

            if (abortPaths) {
                _destination = Vector3.zero;
                foreach (long pathID in _pendingPathIDs) {
                    // Don't log errors here to avoid error during shutdown.
                    NavPathfinder.MainInstance.CancelPath(pathID, false);
                }
                _pendingPathIDs.Clear();
            }
        }
        
        /// <summary>
        /// Request a new path from the current position to the desired destination.
        /// </summary>
        /// <remarks>
        /// It is usually not necessary to call this yourself, as it is called when setting <see cref="Destination"/>.
        /// However, if the agent gets stuck or pushed off course, you may wish to use this to get a new path.
        /// </remarks>
        public virtual void UpdatePath() {
            // If we are within acceptance radius of destination, stop moving.
            if (Vector3.SqrMagnitude(_destination - transform.position) < _acceptance * _acceptance) {
                Stop(true);
                return;
            }
            
            // Ensure both current position and destination are within sample radius of a NavVolume.
            if (!NavUtil.SamplePosition(transform.position, out NavHit startHit, _sampleRadius) ||
                !NavUtil.SamplePosition(Destination, out NavHit endHit, _sampleRadius)) {
                PathFailed?.Invoke();
                return;
            }
            
            // If KeepPathWhileCalculating is true, we can have two paths being calculated at the same time.
            int maxPendingIDs = _keepPathWhileCalculating ? 1 : 0;
            if (_pendingPathIDs.Count > maxPendingIDs) {
                // If there are too many pending paths, keep the oldest to ensure it gets to finished.
                // If the newest request was kept instead and requests came in faster than they could be calculated,
                // the agent would deadlock and never actually finish a path.
                for (int i = _pendingPathIDs.Count - 1; i >= maxPendingIDs; i--) {
                    NavPathfinder.MainInstance.CancelPath(_pendingPathIDs[i]);
                }
                _pendingPathIDs.RemoveRange(maxPendingIDs, _pendingPathIDs.Count - maxPendingIDs);
            }

            long id = NavPathfinder.MainInstance.FindPath(startHit, endHit, transform.position, Destination, OnPathReady);
            
            // NavPathfinder returns a negative ID if pathfinding is not possible, such as if no volumes are loaded.
            if (id < 0) {
                PathFailed?.Invoke();
                return;
            }
            _pendingPathIDs.Add(id);

            // If KeepPathWhileCalculating is false, the current path needs to be aborted.
            if (!_keepPathWhileCalculating) {
                CurrentPath = null;
                _currentIndexInPath = -1;
            }
                    
            Arrived = false;
        }


        #endregion

        #region Internal Methods

        private Vector3 CalculateDesiredNavigationVelocityTimesMaxSpeed() =>
            CalculateDesiredNavigationVelocity() * AvoidanceMaxSpeed;

        /// <summary>
        /// Calculate the velocity the agent wants to move in, in the range [0, 1].
        /// </summary>
        protected virtual Vector3 CalculateDesiredNavigationVelocity() {
            // Always return no desired velocity if there is no path.
            if (CurrentPath == null) return Vector3.zero;
            
            // Get direction to the next path waypoint.
            Vector3 direction = Vector3.Normalize(NextWaypoint - transform.position);
            Vector3 result;
            
            if (_accelerationEstimate == 0) {
                // No acceleration estimate provided, so assume the agent can stop instantly.
                // That means it can just continue at full speed until reaching the target.
                result = direction;
            } else {
                // Acceleration is limited, so if agent is within stopping distance, it needs to slow down.
                Vector3 toDest = NextWaypoint - transform.position;
                float stoppingDistance = StoppingDistance;
                if (toDest.sqrMagnitude > stoppingDistance * stoppingDistance) {
                    // Not within stopping distance, so proceed at full speed.
                    result = direction;
                } else {
                    // Within stopping distance, so start slowing down to zero.
                    result = Vector3.zero;
                }
            }

            return result * _desiredSpeedRatio;
        }
        
        /// <summary>
        /// Update the value of <see cref="MeasuredVelocity"/>, which is used to determine <see cref="StoppingDistance"/>.
        /// </summary>
        protected virtual void UpdateMeasuredVelocity() {
            Vector3 currentPos = transform.position;
            
            MeasuredVelocity = (currentPos - _positionLastFrame) / Time.deltaTime;
            _positionLastFrame = currentPos;
        }

        /// <summary>
        /// Update the current path index, which is used to determine <see cref="NextWaypoint"/>.
        /// </summary>
        protected virtual void UpdatePathIndex() {
            // If the agent has an active path and is within acceptance radius of next waypoint, move to the next waypoint.
            if (CurrentPath != null && _currentIndexInPath < CurrentPath.Waypoints.Count &&
                Vector3.SqrMagnitude(transform.position - NextWaypoint) < _acceptance * _acceptance) {
                
                _currentIndexInPath++;
                
                // Stop moving if the path is finished.
                if (_currentIndexInPath >= CurrentPath.Waypoints.Count) {
                    Stop(false);
                }
            }
        }
        
        /// <summary>
        /// Callback that is received when a pathfinding request completes, which should start moving along that path.
        /// </summary>
        /// <param name="id">The id of the path request.</param>
        /// <param name="path">The completed path, which is null if no path was found.</param>
        protected virtual void OnPathReady(long id, NavPath path) {
            int index = _pendingPathIDs.IndexOf(id);
            if (index < 0) return;

            // Cancel any path requests that are older than the received path.
            for (int i = 0; i < index; i++) {
                NavPathfinder.MainInstance.CancelPath(_pendingPathIDs[i]);
            }
            // Remove all older paths plus the just completed one.
            _pendingPathIDs.RemoveRange(0, index + 1);
            
            // Start following the found path.
            CurrentPath = path;

            if (path != null) {
                PathReady?.Invoke();
            } else {
                PathFailed?.Invoke();
            }
        }

        #endregion
    }
}
