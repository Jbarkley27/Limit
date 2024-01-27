// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Collections;
using System.Collections.Generic;
using Infohazard.Core;
using Infohazard.HyperNav.Jobs;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Handles calculating the avoidance velocities for all <see cref="IAvoidanceAgent"/>s.
    /// </summary>
    /// <remarks>
    /// There should only be one <see cref="AvoidanceManager"/> active at a time,
    /// as it will handle all agents together.
    /// This is done using the C# Job System and Burst compiler to calculate avoidance very quickly.
    /// If it is still not fast enough,
    /// try changing the <see cref="TimeHorizon"/> and/or <see cref="MaxObstaclesConsidered"/> values.
    /// </remarks>
    public class AvoidanceManager : Singleton<AvoidanceManager> {
        #region Serialized Fields

        /// <summary>
        /// (Serialized) When to update the avoidance velocities of agents.
        /// </summary>
        [SerializeField]
        [Tooltip("When to update the avoidance velocities of agents.")]
        private AvoidanceManagerUpdateMode _updateMode;

        /// <summary>
        /// (Serialized) How far in the future to look when avoiding collisions.
        /// </summary>
        /// <remarks>
        /// A lower value reduces the number of calculations per agent,
        /// with the drawback of being able to plan less far ahead.
        /// </remarks>
        [SerializeField]
        [Tooltip("How far in the future to look when avoiding collisions.")]
        private float _timeHorizon = 5;

        /// <summary>
        /// (Serialized) The maximum number of obstacles that can be considered by each agent for avoidance.
        /// </summary>
        /// <remarks>
        /// This value caps the number of avoidance calculations per agent.
        /// Generally it should be equal to the max number of obstacles you expect to be within
        /// <see cref="TimeHorizon"/> * <see cref="IAvoidanceObstacle.MaxSpeed"/> of an agent.
        /// </remarks>
        [SerializeField]
        [Tooltip("The maximum number of obstacles that can be considered by each agent for avoidance.")]
        private int _maxObstaclesConsidered = 10;

        /// <summary>
        /// (Serialized) How much to grow data structures by when they are not large enough.
        /// </summary>
        /// <remarks>
        /// Higher values reduce the number of allocations if agent count is steadily growing,
        /// but may also lead to wasted memory.
        /// </remarks>
        [SerializeField]
        [Tooltip("How much to grow data structures by when they are not large enough.")]
        [Range(1, 2)]
        private float _dataGrowRatio = 1.2f;

        /// <summary>
        /// (Serialized) Whether to use the C# Job System/Burst Compiler or to just run updates on the main thread.
        /// </summary>
        [SerializeField]
        [Tooltip("Whether to use the C# Job System/Burst Compiler or to just run updates on the main thread.")]
        private bool _useJob = true;

        #endregion

        #region Private Fields

        // Length of allocated obstacles array.
        private int _allocatedObstacleCount = 0;
        
        // Length of allocated per-agent arrays.
        private int _allocatedAgentCount = 0;
        
        // Length of allocated plane arrays.
        private int _allocatedPlaneCount = 0;
        
        // Coroutine for running updates.
        private Coroutine _updateCoroutine;
        
        // Template job that stores reusable data structures.
        private AvoidanceJob _jobTemplate;

        #endregion

        #region Serialized Field Accessor Properties
        
        /// <summary>
        /// When to update the avoidance velocities of agents.
        /// </summary>
        public AvoidanceManagerUpdateMode UpdateMode {
            get => _updateMode;
            set {
                if (_updateMode == value) return;
                _updateMode = value;
                Reschedule();
            }
        }

        /// <summary>
        /// How far in the future to look when avoiding collisions.
        /// </summary>
        /// <remarks>
        /// A lower value reduces the number of calculations per agent,
        /// with the drawback of being able to plan less far ahead.
        /// </remarks>
        public float TimeHorizon {
            get => _timeHorizon;
            set => _timeHorizon = value;
        }

        /// <summary>
        /// The maximum number of obstacles that can be considered by each agent for avoidance.
        /// </summary>
        /// <remarks>
        /// This value caps the number of avoidance calculations per agent.
        /// Generally it should be equal to the max number of obstacles you expect to be within
        /// <see cref="TimeHorizon"/> * <see cref="IAvoidanceObstacle.MaxSpeed"/> of an agent.
        /// </remarks>
        public int MaxObstaclesConsidered {
            get => _maxObstaclesConsidered;
            set => _maxObstaclesConsidered = value;
        }

        /// <summary>
        /// How much to grow data structures by when they are not large enough.
        /// </summary>
        /// <remarks>
        /// Higher values reduce the number of allocations if agent count is steadily growing,
        /// but may also lead to wasted memory.
        /// </remarks>
        public float DataGrowRatio {
            get => _dataGrowRatio;
            set => _dataGrowRatio = Mathf.Clamp(value, 1, 2);
        }

        /// <summary>
        /// Whether to use the C# Job System/Burst Compiler or to just run updates on the main thread.
        /// </summary>
        public bool UseJob {
            get => _useJob;
            set => _useJob = value;
        }

        #endregion

        #region Public Methods

        // Profile markers to track avoidance performance.
        private static readonly ProfilerMarker MarkerUpdateAvoidance =
            new ProfilerMarker("AvoidanceManager.UpdateAvoidance");

        private static readonly ProfilerMarker MarkerUpdateDataStructures =
            new ProfilerMarker("AvoidanceManager.UpdateDataStructures");

        private static readonly ProfilerMarker MarkerObstacleLoop =
            new ProfilerMarker("AvoidanceManager.UpdateAvoidance Obstacle Loop");

        private static readonly ProfilerMarker MarkerGetInputVelocity =
            new ProfilerMarker("AvoidanceManager.UpdateAvoidance Get Input Velocity");

        private static readonly ProfilerMarker MarkerRunJob =
            new ProfilerMarker("AvoidanceManager.UpdateAvoidance Run Job");

        private static readonly ProfilerMarker MarkerAgentLoop =
            new ProfilerMarker("AvoidanceManager.UpdateAvoidance Agent Loop");

        /// <summary>
        /// Update the avoidance of all agents. Can be called manually if <see cref="UpdateMode"/> is set to manual.
        /// </summary>
        /// <param name="deltaTime">Time delta since last call.</param>
        public virtual void UpdateAvoidance(float deltaTime) {
            using ProfilerMarker.AutoScope autoScope = MarkerUpdateAvoidance.Auto();
            if (Avoidance.AllAgents.Count == 0) return;

            // Reallocate data structures if necessary.
            using (MarkerUpdateDataStructures.Auto()) {
                UpdateDataStructures();
            }

            // Copy data structure references.
            AvoidanceJob job = _jobTemplate;
            job.DeltaTime = deltaTime;
            job.TimeHorizon = _timeHorizon;
            job.MaxObstaclesConsidered = _maxObstaclesConsidered;
            job.ObstacleCount = 0;

            // Loop through all obstacles and add their info to the obstacles array.
            int agentCount = 0;
            using (MarkerObstacleLoop.Auto()) {
                for (int i = 0; i < Avoidance.AllObstacles.Count; i++) {
                    IAvoidanceObstacle obstacle = Avoidance.AllObstacles[i];

                    // Calculate input velocity.
                    // Could potentially be slow depending on the provided delegate, so profile here.
                    Vector3 inputVelocity;
                    using (MarkerGetInputVelocity.Auto()) {
                        inputVelocity = obstacle.InputVelocity;
                    }

                    // Assign basic obstacle info.
                    NativeAvoidanceObstacleData obstacleData = new NativeAvoidanceObstacleData {
                        Avoidance = 0,
                        Padding = 0,
                        Position = obstacle.Position,
                        InputVelocity = inputVelocity,
                        Speed = obstacle.MaxSpeed,
                        Radius = obstacle.Radius,
                        TagMask = obstacle.TagMask.Value,
                        AvoidedTags = 0,
                        Debug = false,
                    };

                    // If obstacle is an agent, there is additional info that needs to be assigned.
                    if (obstacle is IAvoidanceAgent { IsActive: true } agent) {
                        // If the agent's avoidance weight is zero, it will behave like a basic obstacle.
                        // However, it still needs to have its UpdateAvoidanceVelocity called for consistency.
                        if (agent.AvoidanceWeight > 0) {
                            obstacleData.Avoidance = agent.AvoidanceWeight;
                            obstacleData.Padding = agent.AvoidancePadding;
                            obstacleData.AvoidedTags = agent.AvoidedTags.Value;
                            obstacleData.Debug = agent.DebugAvoidance;

                            // When using a job, the indices of agents in the main obstacle array
                            // are stored in a secondary index array.
                            if (_useJob) {
                                job.AgentIndices[agentCount++] = job.ObstacleCount;
                            }
                        } else {
                            agent.UpdateAvoidanceVelocity(inputVelocity);
                        }
                    }

                    job.Obstacles[job.ObstacleCount++] = obstacleData;
                }
            }

            if (_useJob) {
                // Profile the actual time it takes for the job to complete.
                using ProfilerMarker.AutoScope jobScope = MarkerRunJob.Auto();
                
                // Run job.
                JobHandle handle = job.Schedule(agentCount, 1);
                handle.Complete();

                // Once the job is completed, read back the velocities of each agent and update them.
                for (int i = 0; i < agentCount; i++) {
                    int agentIndex = job.AgentIndices[i];
                    AvoidanceAgent agent = (AvoidanceAgent) Avoidance.AllObstacles[agentIndex];
                    agent.UpdateAvoidanceVelocity(job.AvoidanceVelocities[i]);
                }
            } else {
                // Profile main thread performance.
                using ProfilerMarker.AutoScope loopScope = MarkerAgentLoop.Auto();
                
                // Loop through all obstacles and call job code directly.
                for (int i = 0; i < job.ObstacleCount; i++) {
                    NativeAvoidanceObstacleData obstacle = job.Obstacles[i];
                    if (obstacle.Avoidance == 0) continue;

                    Vector3 avoidanceVelocity =
                        job.OrcaAvoidance(i, ref job.TempPlanes, ref job.TempProjPlanes);
                    IAvoidanceAgent agent = (IAvoidanceAgent) Avoidance.AllObstacles[i];
                    agent.UpdateAvoidanceVelocity(avoidanceVelocity);
                }
            }
        }

        #endregion

        #region Internal Methods

        // Reallocate data structures in case the number of agents has increased beyond their capacity.
        private void UpdateDataStructures() {
            // Global obstacles array used to store all obstacles.
            int obstacleCount = Avoidance.AllObstacles.Count;
            if (_allocatedObstacleCount < obstacleCount) {
                if (_jobTemplate.Obstacles.IsCreated) {
                    _jobTemplate.Obstacles.Dispose();
                }

                _allocatedObstacleCount = Mathf.RoundToInt(obstacleCount * _dataGrowRatio);
                _jobTemplate.Obstacles =
                    new NativeArray<NativeAvoidanceObstacleData>(_allocatedObstacleCount, Allocator.Persistent);
            }

            // Only need per-agent allocations if using a job. Otherwise, agents are processed one at a time.
            if (_useJob) {
                // Agent indices array (indices of agents in main obstacles array).
                // Avoidance velocities array (where the output velocities are stored by the job).
                int agentCount = Avoidance.AllAgents.Count;
                if (_allocatedAgentCount < agentCount) {
                    if (_jobTemplate.AgentIndices.IsCreated) {
                        _jobTemplate.AgentIndices.Dispose();
                    }

                    if (_jobTemplate.AvoidanceVelocities.IsCreated) {
                        _jobTemplate.AvoidanceVelocities.Dispose();
                    }

                    _allocatedAgentCount = Mathf.RoundToInt(agentCount * _dataGrowRatio);
                    _jobTemplate.AgentIndices = new NativeArray<int>(_allocatedAgentCount, Allocator.Persistent);
                    _jobTemplate.AvoidanceVelocities = new NativeArray<float3>(_allocatedAgentCount, Allocator.Persistent);
                }
                
                // Temporary planes arrays, used for the linear programming algorithm in the job.
                // Since the job runs in parallel for each agent,
                // we need a separate space in the temp storage for each agent.
                // Max number of planes needed for each agent is one per obstacle.
                int planeCount = agentCount * _maxObstaclesConsidered;
                if (_allocatedPlaneCount < planeCount) {
                    if (_jobTemplate.TempPlanes.IsCreated) {
                        _jobTemplate.TempPlanes.Dispose();
                    }

                    if (_jobTemplate.TempProjPlanes.IsCreated) {
                        _jobTemplate.TempProjPlanes.Dispose();
                    }

                    _allocatedPlaneCount = Mathf.RoundToInt(planeCount * _dataGrowRatio);
                    _jobTemplate.TempPlanes = new NativeArray<NativePlane>(_allocatedPlaneCount, Allocator.Persistent);
                    _jobTemplate.TempProjPlanes = new NativeArray<NativePlane>(_allocatedPlaneCount, Allocator.Persistent);
                }
            } else {
                // When not using a job, we can have a single planes array that is reused for all agents.
                int planeCount = _maxObstaclesConsidered;
                if (_allocatedPlaneCount < planeCount) {
                    if (_jobTemplate.TempPlanes.IsCreated) {
                        _jobTemplate.TempPlanes.Dispose();
                    }

                    if (_jobTemplate.TempProjPlanes.IsCreated) {
                        _jobTemplate.TempProjPlanes.Dispose();
                    }

                    _allocatedPlaneCount = Mathf.RoundToInt(planeCount);
                    _jobTemplate.TempPlanes = new NativeArray<NativePlane>(_allocatedPlaneCount, Allocator.Persistent);
                    _jobTemplate.TempProjPlanes = new NativeArray<NativePlane>(_allocatedPlaneCount, Allocator.Persistent);
                }
            }
        }

        // Stop the current update coroutine and schedule a new one based on the update mode.
        private void Reschedule() {
            if (_updateCoroutine != null) {
                StopCoroutine(_updateCoroutine);
            }

            _updateCoroutine = _updateMode switch {
                AvoidanceManagerUpdateMode.Update => StartCoroutine(CrtUpdate()),
                AvoidanceManagerUpdateMode.FixedUpdate => StartCoroutine(CrtFixedUpdate()),
                AvoidanceManagerUpdateMode.LateUpdate => StartCoroutine(CrtLateUpdate()),
                AvoidanceManagerUpdateMode.Manual => null,
                _ => throw new ArgumentOutOfRangeException(),
            };
        }

        // Update coroutine used if the mode is Update.
        private IEnumerator CrtUpdate() {
            while (true) {
                yield return null;
                UpdateAvoidance(Time.deltaTime);
            }
        }

        // Update coroutine used if the mode is FixedUpdate.
        private IEnumerator CrtFixedUpdate() {
            YieldInstruction instruction = new WaitForFixedUpdate();
            while (true) {
                yield return instruction;
                UpdateAvoidance(Time.fixedDeltaTime);
            }
        }

        // Update coroutine used if the mode is LateUpdate.
        private IEnumerator CrtLateUpdate() {
            YieldInstruction instruction = new WaitForEndOfFrame();
            while (true) {
                yield return instruction;
                UpdateAvoidance(Time.deltaTime);
            }
        }

        #endregion

        #region Unity Callbacks
        
        /// <summary>
        /// Schedule coroutine when the manager is enabled.
        /// </summary>
        protected virtual void OnEnable() {
            Reschedule();
        }

        /// <summary>
        /// Dispose data structures when disabled. Coroutine will stop automatically.
        /// </summary>
        protected void OnDisable() {
            if (_jobTemplate.Obstacles.IsCreated) {
                _jobTemplate.Obstacles.Dispose();
            }

            if (_jobTemplate.AgentIndices.IsCreated) {
                _jobTemplate.AgentIndices.Dispose();
            }

            if (_jobTemplate.AvoidanceVelocities.IsCreated) {
                _jobTemplate.AvoidanceVelocities.Dispose();
            }

            if (_jobTemplate.TempPlanes.IsCreated) {
                _jobTemplate.TempPlanes.Dispose();
            }

            if (_jobTemplate.TempProjPlanes.IsCreated) {
                _jobTemplate.TempProjPlanes.Dispose();
            }

            _jobTemplate = default;
        }

        #endregion
    }
        
    /// <summary>
    /// The update modes in which AvoidanceManager can operate.
    /// </summary>
    public enum AvoidanceManagerUpdateMode {
        Update,
        FixedUpdate,
        LateUpdate,
        Manual,
    }
}