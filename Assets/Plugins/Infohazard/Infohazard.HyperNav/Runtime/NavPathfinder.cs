// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Infohazard.Core;
using Infohazard.HyperNav.Jobs;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Infohazard.HyperNav {
    /// <summary>
    /// A script used to calculate HyperNav paths.
    /// </summary>
    /// <remarks>
    /// Can be used as a singleton, or you can have more than one if needed.
    /// </remarks>
    public class NavPathfinder : MonoBehaviour {
        #region Serialized Fields

        /// <summary>
        /// (Serialized) Whether to set NavPathfinder.Instance to this instance.
        /// </summary>
        [SerializeField, Tooltip("Whether to set NavPathfinder.Instance to this instance.")]
        private bool _isMainInstance = true;
        
        /// <summary>
        /// (Serialized) The mode to use for calculating paths.
        /// </summary>
        [SerializeField, Tooltip("The mode to use for calculating paths.")]
        private NavPathfindingMode _pathfindingMode = NavPathfindingMode.JobThread;

        // Settings for MainThreadAsynchronous
        
        /// <summary>
        /// (Serialized) If greater than zero, limit on the number of requests actively being worked on.
        /// </summary>
        [SerializeField, Tooltip("If greater than zero, limit on the number of requests actively being worked on.")] 
        private int _maxExecutingRequests = 0;
        
        /// <summary>
        /// (Serialized) Maximum total number of pathfinding steps that can be performed per frame by this instance.
        /// </summary>
        [SerializeField, Tooltip("Maximum total number of pathfinding steps that can be performed per frame by this instance.")]
        private int _maxPathOpsPerFrame = 100;
        
        // Settings for JobThread
        
        /// <summary>
        /// (Serialized) Maximum number of pathfinding jobs that can be actively running at once.
        /// </summary>
        [SerializeField, Tooltip("Maximum number of pathfinding jobs that can be actively running at once.")]
        private int _maxConcurrentJobs = 1;
        
        /// <summary>
        /// (Serialized) Maximum number of frames a job can take before the main thread must wait for it.
        /// </summary>
        [SerializeField, Tooltip("Maximum number of frames a job can take before the main thread must wait for it.")]
        private int _maxCompletionFrames = 3;
        
        /// <summary>
        /// This is used to refer to the names of private fields in this class from a custom Editor.
        /// </summary>
        public static class PropNames {
            public const string IsMainInstance = nameof(_isMainInstance);
            public const string Mode = nameof(_pathfindingMode);
            public const string MaxExecutingRequests = nameof(_maxExecutingRequests);
            public const string MaxPathOpsPerFrame = nameof(_maxPathOpsPerFrame);
            public const string MaxConcurrentJobs = nameof(_maxConcurrentJobs);
            public const string MaxCompletionFrames = nameof(_maxCompletionFrames);
        }
        
        #endregion

        #region Private Fields
        
        private ListQueue<PendingPath> _pendingPaths = new ListQueue<PendingPath>();
        
        private List<PendingPath> _executingPaths = new List<PendingPath>();
        
        private long _currentPathID = 0;
        
        private int _frameIndex;
        
        private Stopwatch _forceCompleteStopwatch;
        
        private Pool<PendingPath> _pendingPathPool = new Pool<PendingPath>(
            () => new PendingPath(),
            null,
            path => {
                path.ID = -1;
                path.StartFrame = -1;
                path.Receiver = null;
                path.Handle = default;
                path.Job = default;
            });

        private Pool<NavPath> _pathPool = new Pool<NavPath>(
            () => new NavPath(),
            null,
            path => {
                path.ID = -1;
                path.InternalWaypoints.Clear();
                path.StartHit = path.EndHit = default;
                path.HasBeenDisposed = true;
            });

        #endregion

        #region Serialized Field Accessor Properties

        /// <summary>
        /// Whether to set NavPathfinder.Instance to this instance.
        /// </summary>
        /// <remarks>
        /// This cannot be set while the game is running.
        /// </remarks>
        public bool IsMainInstance {
            get => _isMainInstance;
            set {
                if (DebugUtility.CheckPlaying(true)) return;
                _isMainInstance = value;
            }
        }

        /// <summary>
        /// The mode to use for calculating paths.
        /// </summary>
        /// <remarks>
        /// This cannot be set while the game is running.
        /// </remarks>
        public NavPathfindingMode PathfindingMode {
            get => _pathfindingMode;
            set {
                if (DebugUtility.CheckPlaying(true)) return;
                _pathfindingMode = value;
            }
        }

        /// <summary>
        /// (MainThreadAsynchronous Mode ONLY)
        /// If greater than zero, limit on the number of requests actively being worked on.
        /// </summary>
        public int MaxExecutingRequests {
            get => _maxExecutingRequests;
            set => _maxExecutingRequests = value;
        }

        /// <summary>
        /// (MainThreadAsynchronous Mode ONLY)
        /// Maximum total number of pathfinding steps that can be performed per frame by this instance.
        /// </summary>
        /// <remarks>
        /// This limit is shared by all executing paths in this instance.
        /// </remarks>
        public int MaxPathOpsPerFrame {
            get => _maxPathOpsPerFrame;
            set => _maxPathOpsPerFrame = value;
        }
        
        /// <summary>
        /// (JobThread Mode ONLY)
        /// Maximum number of pathfinding jobs that can be actively running at once.
        /// </summary>
        /// <remarks>
        /// Requests beyond this number are queued.
        /// You should keep this number fairly low, as each job has the potential to take up a CPU thread.
        /// </remarks>
        public int MaxConcurrentJobs {
            get => _maxConcurrentJobs;
            set => _maxConcurrentJobs = value;
        }
        
        /// <summary>
        /// (JobThread Mode ONLY)
        /// Maximum number of frames a job can take before the main thread must wait for it.
        /// </summary>
        /// <remarks>
        /// Unity imposes a limit of 3 frames for faster TempJob allocations,
        /// so increasing this beyond 3 will slightly decrease memory performance.
        /// If a job takes longer than this and is forced to block the main thread,
        /// a warning will be logged showing you how long it blocked the main thread for.
        /// This cannot be set while any paths are executing.
        /// </remarks>
        public int MaxCompletionFrames {
            get => _maxCompletionFrames;
            set {
                if (_executingPaths?.Count > 0) {
                    Debug.LogError($"{nameof(MaxCompletionFrames)} cannot be set while paths are executing.");
                    return;
                }
                _maxCompletionFrames = value;
            }
        }

        #endregion

        #region Static Properties
        
        /// <summary>
        /// The main instance, which should be used in most situations.
        /// </summary>
        /// <remarks>
        /// If you need more than one NavPathfinder, you can use direct references to other instances with
        /// <see cref="IsMainInstance"/> set to false.
        /// </remarks>
        public static NavPathfinder MainInstance { get; private set; }

        #endregion

        #region Unity Callbacks

        /// <summary>
        /// If <see cref="IsMainInstance"/> is true, set <see cref="MainInstance"/> or log an error if it is already set.
        /// </summary>
        protected virtual void OnEnable() {
            if (_isMainInstance) {
                if (MainInstance != null && MainInstance.isActiveAndEnabled) {
                    Debug.LogError($"Error: Duplicate NavPathfinder main instance {this}.", this);
                } else {
                    MainInstance = this;
                }
            }
            
            NavVolume.VolumeDataChanging += NavVolume_VolumeDataChanging;
        }

        /// <summary>
        /// Dispose the pools of pending paths and completed paths, and all memory allocated for pathfinding jobs.
        /// </summary>
        protected virtual void OnDisable() {
            // Dispose all paths waiting to execute.
            foreach (PendingPath pendingPath in _pendingPaths) {
                DisposePendingPath(pendingPath, true);
            }

            // Dispose all executing paths.
            foreach (PendingPath executingPath in _executingPaths) {
                // Must force all the pathfinding jobs to complete in order to safely dispose their memory.
                if (_pathfindingMode == NavPathfindingMode.JobThread) {
                    ForcePathJobToComplete(executingPath);
                }
                
                DisposePendingPath(executingPath, true);
            }
            
            _pendingPaths.Clear();
            _executingPaths.Clear();
            
            _pendingPathPool.Dispose();
            _pathPool.Dispose();
            
            NavVolume.VolumeDataChanging -= NavVolume_VolumeDataChanging;
        }

        /// <summary>
        /// If mode is JobThread, check job completion.
        /// If mode is MainThreadAsynchronous, perform pathfinding work.
        /// </summary>
        protected virtual void Update() {
            if (_pathfindingMode == NavPathfindingMode.MainThreadEndOfFrame) return;
            
            // Used to move executing paths to the first open spot in the list.
            // The finished ones can then be removed with a single RemoveRange call.
            int lastEmpty = 0;
            if (_pathfindingMode == NavPathfindingMode.JobThread) {
                // In JobThread mode, just have to check if jobs have finished.
                for (int i = 0; i < _executingPaths.Count; i++) {
                    PendingPath path = _executingPaths[i];

                    // For paths that are not complete and have been executing for less than the max completion frames,
                    // just let them keep running.
                    int framesExecuting = _frameIndex - path.StartFrame;
                    if (!path.Handle.IsCompleted && framesExecuting < _maxCompletionFrames) {
                        // Move path to first open spot in the list.
                        _executingPaths[lastEmpty++] = path;
                        continue;
                    }

                    // Ensure that the path is complete and the job is cleaned up.
                    ForcePathJobToComplete(path);
                    
                    // Build the finished Path object and pass it to the receiver.
                    FinalizePath(path);
                }
            } else if (_pathfindingMode == NavPathfindingMode.MainThreadAsynchronous) {
                // In asynchronous mode, need to actually run code to update the paths.
                
                // Limit the total operations per frame.
                int remainingOps = _maxPathOpsPerFrame;
                for (int i = 0; i < _executingPaths.Count; i++) {
                    PendingPath path = _executingPaths[i];
                    
                    // Divide remaining operations evenly among remaining paths.
                    int remainingPaths = _executingPaths.Count - i;
                    int maxOpsForThisPath = remainingOps / remainingPaths;
                    
                    // Always make sure to do at least one op per executing path.
                    maxOpsForThisPath = Mathf.Max(maxOpsForThisPath, 1);
                    
                    // Call the job code directly - the job is just a struct after all,
                    // so its code can be run outside of a job like this.
                    path.Job.UpdatePath(maxOpsForThisPath, out int opsUsed, out NavPathState state);
                    
                    // Subtract ops used, which may be lower than maxOpsForThisPath if the path finished.
                    remainingOps -= opsUsed;

                    if (state == NavPathState.Pending) {
                        // Move path to first open spot in the list.
                        _executingPaths[lastEmpty++] = path;
                        continue;
                    }
                    
                    // Path finished (because state is not pending), 
                    // so build the finished Path object and pass it to the receiver.
                    FinalizePath(path);
                }
            }
            
            // Since all still-executing paths were shifted to the beginning of the list,
            // the finished ones can be removed more efficiently with a single RemoveRange.
            _executingPaths.RemoveRange(lastEmpty, _executingPaths.Count - lastEmpty);
            
            _frameIndex++;
        }

        /// <summary>
        /// Move paths from the pending queue and start executing them.
        /// </summary>
        private void LateUpdate() {
            while (CanStartMorePaths() && _pendingPaths.TryDequeue(out PendingPath pendingPath)) {
                NavVolume startVolume = pendingPath.StartHit.Volume;
                
                // If start volume was unloaded after path was requested, auto-fail.
                if (startVolume == null || !NavVolume.VolumeData.ContainsKey(startVolume.InstanceID)) {
                    pendingPath.Receiver?.Invoke(pendingPath.ID, null);
                    DisposePendingPath(pendingPath, true);
                    continue;
                }
                
                if (_pathfindingMode == NavPathfindingMode.JobThread) {
                    ExecutePathAsJob(pendingPath);
                } else if (_pathfindingMode == NavPathfindingMode.MainThreadAsynchronous) {
                    InitializePathOnMainThread(pendingPath);
                }  else if (_pathfindingMode == NavPathfindingMode.MainThreadEndOfFrame) {
                    ExecutePathOnMainThread(pendingPath);
                }
            }
        }
        
        #endregion

        #region Internal Methods

        private void NavVolume_VolumeDataChanging() {
            // Dispose all executing paths and reset them to pending.
            for (int i = 0; i < _executingPaths.Count; i++) {
                PendingPath executingPath = _executingPaths[i];
                // Must force all the pathfinding jobs to complete in order to safely dispose their memory.
                if (_pathfindingMode == NavPathfindingMode.JobThread) {
                    ForcePathJobToComplete(executingPath);
                }

                DisposePendingPath(executingPath, false);

                _pendingPaths.Insert(i, executingPath);
            }
            
            _executingPaths.Clear();
        }
        
        private void ForcePathJobToComplete(PendingPath path) {
            if (!path.Handle.IsCompleted) {
                // If path job was not yet completed, the main thread will be blocked while waiting for it.
                // In this scenario, a warning message will be printed showing the time that this took.
                _forceCompleteStopwatch ??= new Stopwatch();
                _forceCompleteStopwatch.Restart();
                
                // Force path to complete.
                path.Handle.Complete();
                
                // Measure time taken to complete and log it.
                long ticks = _forceCompleteStopwatch.ElapsedTicks;
                double ms = ticks / (double) TimeSpan.TicksPerMillisecond;
                Debug.LogWarning($"Forcing a path to complete took {ms:0.##} ms.");
            } else {
                // Complete must still be called even if the job finished, but will not block the main thread
                // for a significant time.
                path.Handle.Complete();
            }
        }
        
        private bool CanStartMorePaths() {
            if (NavVolume.VolumeChangingCount > 0) return false;
            
            if (_pathfindingMode == NavPathfindingMode.JobThread) {
                return _executingPaths.Count < _maxConcurrentJobs;
            }

            if (_pathfindingMode == NavPathfindingMode.MainThreadAsynchronous) {
                return _maxExecutingRequests == 0 || _executingPaths.Count < _maxExecutingRequests;
            }

            return true;
        }

        /// <summary>
        /// Called by <see cref="NavPath.Dispose"/>.
        /// </summary>
        /// <param name="path">The path to dispose.</param>
        internal void DisposePath(NavPath path) {
            // Release Path back to the pool.
            _pathPool.Release(path);
        }

        private void DisposePendingPath(PendingPath pendingPath, bool freePath) {
            // Ensure native data structures are disposed if they've been created.
            if (pendingPath.Job.OutPathWaypoints.IsCreated) pendingPath.Job.OutPathWaypoints.Dispose();
            if (pendingPath.Job.Frontier.IsCreated) pendingPath.Job.Frontier.Dispose();
            if (pendingPath.Job.Waypoints.IsCreated) pendingPath.Job.Waypoints.Dispose();
            if (pendingPath.Job.NodeTable.IsCreated) pendingPath.Job.NodeTable.Dispose();

            if (freePath) {
                // Release PendingPath back to the pool.
                _pendingPathPool.Release(pendingPath);
            }
        }

        private NavPath GetOutputPath(PendingPath pendingPath) {
            // Get a pooled instance.
            NavPath path = _pathPool.Get();

            // Initialize basic info for the result path.
            path.ID = pendingPath.ID;
            path.HasBeenDisposed = false;
            path.StartHit = pendingPath.StartHit;
            path.StartPos = pendingPath.EndPos;
            path.EndHit = pendingPath.EndHit;
            path.StartPos = pendingPath.EndPos;
            path.Pathfinder = this;
            
            // Copy the internal waypoints from the job output to the public list.
            for (int i = 0; i < pendingPath.Job.OutPathWaypoints.Length; i++) {
                NativeNavWaypoint waypoint = pendingPath.Job.OutPathWaypoints[i];
                path.InternalWaypoints.Add(new NavWaypoint {
                    Position = waypoint.Position.xyz,
                    Type = waypoint.Type,
                    VolumeID = waypoint.VolumeID,
                });
            }

            return path;
        }

        private void AllocatePathDataStructures(PendingPath pendingPath, Allocator allocator) {
            // Allocate all the data structures that the job (or main thread) needs to process the path.
            pendingPath.Job.OutPathWaypoints = new NativeList<NativeNavWaypoint>(16, allocator);
            pendingPath.Job.NodeTable = new NativeParallelHashMap<PendingPathNode, VisitedNodeInfo>(256, allocator);
            pendingPath.Job.Frontier = new NativeHeap<PendingPathNode>(32, allocator);
            pendingPath.Job.Waypoints = new NativeList<PendingPathNode>(16, allocator);
        }

        private void SetupPathJobParameters(PendingPath pendingPath) {
            // Setup a path job's basic parameters.
            pendingPath.Job = new NavPathJob {
                Volumes = NavVolume.VolumeData,
                StartPosition = pendingPath.StartPos.ToV4Pos(),
                StartHit = pendingPath.StartHit.ToInternal(),
                EndHit = pendingPath.EndHit.ToInternal(),
            };
        }

        private void ExecutePathAsJob(PendingPath pendingPath) {
            // Setup job parameters and data structures.
            // Allocator.TempJob is faster than Allocator.Persistent,
            // but can only be used if the job is running for three or fewer frames.
            SetupPathJobParameters(pendingPath);
            AllocatePathDataStructures(pendingPath, _maxCompletionFrames > 3 ? Allocator.Persistent : Allocator.TempJob);
            
            // Save start frame to keep track of how long it's been executing.
            pendingPath.StartFrame = _frameIndex;
            
            // Start the job on a worker thread.
            pendingPath.Handle = pendingPath.Job.Schedule();
            
            // This line is needed to make the job start executing immediately.
            JobHandle.ScheduleBatchedJobs();
            
            _executingPaths.Add(pendingPath);
        }

        private void ExecutePathOnMainThread(PendingPath pendingPath) {
            // Setup job parameters and data structures.
            // Allocator.Temp is very fast and can be used here because the path will be executed synchronously.
            SetupPathJobParameters(pendingPath);
            AllocatePathDataStructures(pendingPath, Allocator.Temp);
            
            // Complete the entire path and send it to the receiver.
            pendingPath.Job.Execute();
            FinalizePath(pendingPath);
        }

        private void InitializePathOnMainThread(PendingPath pendingPath) {
            // Setup job parameters and data structures.
            // Allocator.Persistent must be used here because there is no limit on how long the path will take.
            SetupPathJobParameters(pendingPath);
            AllocatePathDataStructures(pendingPath, Allocator.Persistent);
            
            // Just initialize the job and prepare it to execute next frame.
            pendingPath.Job.Initialize();
            _executingPaths.Add(pendingPath);
        }

        private void FinalizePath(PendingPath pendingPath) {
            // If path has a receiver, compute the externally visible Path and invoke the receiver.
            if (pendingPath.Receiver != null) {
                bool success = pendingPath.Job.OutPathWaypoints.Length > 0;
                pendingPath.Receiver.Invoke(pendingPath.ID, success ? GetOutputPath(pendingPath) : null);
            }
            
            // Dispose and cleanup the pendingPath.
            DisposePendingPath(pendingPath, true);
        }

        #endregion

        #region Public Methods
        
        /// <summary>
        /// Find a path between two positions, and invoke the receiver when it is completed.
        /// </summary>
        /// <remarks>
        /// If pathfinding cannot occur, for example because there are no volumes,
        /// or because a query at start or end fails,
        /// this method will return -1 and the receiver will not be invoked.
        /// If no path can be found, the receiver will be invoked with a null Path argument.
        /// </remarks>
        /// <param name="start">Start position for the path.</param>
        /// <param name="end">Destination for the path.</param>
        /// <param name="receiver">Callback to receive the path when it has been calculated.</param>
        /// <param name="sampleRadius">Radius to search for volumes at the start and end locations./></param>
        /// <returns>The ID of the pending path, or -1 if pathfinding cannot occur.</returns>
        public long FindPath(Vector3 start, Vector3 end, HyperNavPathCallback receiver, float sampleRadius = 0) {
            
            if (NavUtil.SamplePosition(start, out NavHit startHit, sampleRadius) &&
                NavUtil.SamplePosition(end, out NavHit endHit, sampleRadius)) {
                return FindPath(startHit, endHit, start, end, receiver);
            }
            
            return -1;
        }

        /// <summary>
        /// Find a path between two already-calculated nav query results, and invoke the receiver when it is completed.
        /// </summary>
        /// <remarks>
        /// If pathfinding cannot occur, for example because there are no volumes,
        /// this method will return -1 and the receiver will not be invoked.
        /// If no path can be found, the receiver will be invoked with a null Path argument.
        /// </remarks>
        /// <param name="startHit">Query result for the start of the path.</param>
        /// <param name="endHit">Query result for the end of the path.</param>
        /// <param name="startPos">Start position for the path.</param>
        /// <param name="endPos">Destination for the path.</param>
        /// <param name="receiver">Callback to receive the path when it has been calculated.</param>
        /// <returns>The ID of the pending path, or -1 if pathfinding cannot occur.</returns>
        public long FindPath(NavHit startHit, NavHit endHit, Vector3 startPos, Vector3 endPos,
                             HyperNavPathCallback receiver) {

            if (!NavVolume.VolumeData.IsCreated) {
                Debug.LogError("Trying to find path before any volumes are loaded.");
                return -1;
            }
            
            // Simple incrementor to get path ID.
            long id = _currentPathID++;

            // Setup pending path params.
            PendingPath pendingPath = _pendingPathPool.Get();
            pendingPath.ID = id;
            pendingPath.Receiver = receiver;
            pendingPath.StartPos = startPos;
            pendingPath.StartHit = startHit;
            pendingPath.EndPos = endPos;
            pendingPath.EndHit = endHit;
            _pendingPaths.Enqueue(pendingPath);
            return id;
        }

        /// <summary>
        /// Cancel a pending path with the given ID.
        /// </summary>
        /// <remarks>
        /// If the mode is set to JobThread and the requested path is already executing,
        /// the actual work thread cannot be cancelled.
        /// However, this will still remove the receiver, so no matter what that will not be called for the path.
        /// </remarks>
        /// <param name="id">The path ID to cancel.</param>
        /// <param name="logError">Whether to log an error if the path is not running.</param>
        public void CancelPath(long id, bool logError = true) {
            // Search pending non-executing paths, which can always be cancelled because work has not started.
            for (int i = 0; i < _pendingPaths.Count; i++) {
                PendingPath path = _pendingPaths[i];
                if (path.ID != id) continue;
                _pendingPaths.RemoveAt(i);
                
                // Ensure the path is cleaned up.
                DisposePendingPath(path, true);
                return;
            }

            // Search executing paths.
            for (int i = 0; i < _executingPaths.Count; i++) {
                PendingPath path = _executingPaths[i];
                if (path.ID != id) continue;

                if (_pathfindingMode == NavPathfindingMode.JobThread) {
                    // Cannot cancel a job once it is started, so just prevent the receiver being called.
                    path.Receiver = null;
                } else {
                    // In MainThreadAsynchronous mode, executing paths can still be cancelled,
                    // just need to clean up the memory allocated for them.
                    _executingPaths.RemoveAt(i);
                    DisposePendingPath(path, true);
                }
                return;
            }

            if (logError) {
                Debug.LogError($"Trying to cancel path with ID {id} which is not queued or executing.");
            }
        }

        #endregion

        #region Internal Data Types

        // Represents a path that is queued or currently executing.
        private class PendingPath {
            public long ID = -1;
            public int StartFrame = -1;

            public Vector3 StartPos;
            public Vector3 EndPos;
            public NavHit StartHit;
            public NavHit EndHit;
            
            public JobHandle Handle;
            public NavPathJob Job;
            public HyperNavPathCallback Receiver;
        }

        #endregion
    }

    #region External Data Types
    
    /// <summary>
    /// A callback that receives a path when it is complete.
    /// </summary>
    public delegate void HyperNavPathCallback(long id, NavPath path);

    /// <summary>
    /// The modes in which pathfinding can be executed.
    /// </summary>
    public enum NavPathfindingMode {
        /// <summary>
        /// Pathfinding runs to completion at the end of the frame in which it was scheduled.
        /// </summary>
        MainThreadEndOfFrame,
        
        /// <summary>
        /// Pathfinding runs in the main thread during the <see cref="NavPathfinder"/>'s Update method,
        /// with a limited number of operations per frame.
        /// </summary>
        MainThreadAsynchronous,
        
        /// <summary>
        /// Pathfinding runs on a worker thread using the C# Job System and (optionally) Burst Compiler.
        /// </summary>
        [InspectorName("Worker Thread (Job)")] JobThread,
    }
    
    /// <summary>
    /// Types of waypoint in a completed path.
    /// </summary>
    public enum NavWaypointType {
        /// <summary>
        /// Waypoint is not inside a volume (used for the first waypoint if the path starts outside a volume).
        /// </summary>
        Outside,
        
        /// <summary>
        /// Waypoint is inside a volume, as are the next and previous waypoints.
        /// </summary>
        InsideVolume,
        
        /// <summary>
        /// Waypoint is inside a volume, and the previous waypoint is not, or is in a different volume.
        /// </summary>
        EnterVolume,
        
        /// <summary>
        /// Waypoint is inside a volume, and the next waypoint is not, or is in a different volume.
        /// </summary>
        ExitVolume,
    }
    
    /// <summary>
    /// A waypoint in a completed path.
    /// </summary>
    public struct NavWaypoint {
        /// <summary>
        /// The type of waypoint in relation to its containing volume.
        /// </summary>
        public NavWaypointType Type { get; internal set; }
        
        /// <summary>
        /// The position of the waypoint in world space.
        /// </summary>
        public Vector3 Position { get; internal set; }
        
        /// <summary>
        /// Identifier of the NavVolume that contains this waypoint, or -1.
        /// </summary>
        public long VolumeID { get; internal set; }
    }
    
    /// <summary>
    /// A completed, valid path.
    /// </summary>
    public class NavPath : IDisposable {
        /// <summary>
        /// ID of the path.
        /// </summary>
        public long ID { get; internal set; } = -1;
        
        /// <summary>
        /// Whether the path has been disposed.
        /// </summary>
        public bool HasBeenDisposed { get; internal set; } = true;
        
        /// <summary>
        /// The position where the path originates from.
        /// </summary>
        public Vector3 StartPos { get; internal set; }
        
        /// <summary>
        /// The destination of the path.
        /// </summary>
        public Vector3 EndPos { get; internal set; }
        
        /// <summary>
        /// The navigation query result at the start of the path.
        /// </summary>
        public NavHit StartHit { get; internal set; }
        
        /// <summary>
        /// The navigation query result at the end of the path.
        /// </summary>
        public NavHit EndHit { get; internal set; }

        /// <summary>
        /// The <see cref="NavPathfinder"/> that was used to calculate the path.
        /// </summary>
        internal NavPathfinder Pathfinder { get; set; }
        
        /// <summary>
        /// Mutable list of waypoints of the path.
        /// </summary>
        internal List<NavWaypoint> InternalWaypoints { get; } = new List<NavWaypoint>();
        
        /// <summary>
        /// List of waypoints of the path.
        /// </summary>
        public IReadOnlyList<NavWaypoint> Waypoints => InternalWaypoints;

        /// <summary>
        /// Dispose the path, returning it to an object pool.
        /// </summary>
        public void Dispose() {
            if (Pathfinder != null) Pathfinder.DisposePath(this);
        }
    }

    #endregion
}