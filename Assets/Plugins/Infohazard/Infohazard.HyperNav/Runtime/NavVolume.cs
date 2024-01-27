// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Collections.Generic;
using System.Linq;
using Infohazard.Core;
using Infohazard.HyperNav.Jobs;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
#endif

namespace Infohazard.HyperNav {
    /// <summary>
    /// A volume of space in which HyperNav pathfinding can occur.
    /// </summary>
    /// <remarks>
    /// Each NavVolume is divided into convex regions that form pathfinding nodes.
    /// A volume's regions can have connections to each other, and to regions of other volumes.
    /// The information in a NavVolume must be baked in the editor - it cannot be calculated at runtime (for now).
    /// </remarks>
    [ExecuteAlways]
    public class NavVolume : MonoBehaviour {
        #region Serialized Fields

        // Runtime Settings
        
        /// <summary>
        /// (Serialized) The boundaries of the volume.
        /// </summary>
        [SerializeField, Tooltip("The boundaries of the volume.")]
        private Bounds _bounds = new Bounds(Vector3.zero, Vector3.one);
        
        /// <summary>
        /// (Serialized) The baked data for the volume.
        /// </summary>
        [SerializeField, Tooltip("The baked data for the volume.")]
        private NavVolumeData _data;
        
        /// <summary>
        /// (Serialized) The unique ID for this volume to identify it in pathfinding jobs and serialized data.
        /// </summary>
        [SerializeField, Tooltip("The unique ID for this volume to identify it in pathfinding jobs and serialized data.")]
        private long _instanceID;

        /// <summary>
        /// (Serialized) Whether to automatically update native data if the volume moves.
        /// </summary>
        [SerializeField, Tooltip("Whether to automatically update native data if the volume moves.")]
        private bool _autoDetectMovement = false;
        
        // Generation Settings
        
        /// <summary>
        /// (Serialized) Which layers are considered impassible for pathfinding.
        /// </summary>
        [SerializeField, Tooltip("Which layers are considered impassible for pathfinding.")]
        private LayerMask _blockingLayers = 1;
        
        /// <summary>
        /// (Serialized) Whether only static objects should be included in the baked data.
        /// </summary>
        [SerializeField, Tooltip("Whether only static objects should be included in the baked data.")]
        private bool _staticOnly = true;
        
        /// <summary>
        /// (Serialized) The maximum size of agents using this volume.
        /// </summary>
        [SerializeField]
        [Min(0)]
        [Tooltip("The maximum size of agents using this volume.")]
        private float _maxAgentRadius = 1;

        /// <summary>
        /// (Serialized) Whether to enable multiple physics queries per voxel to get a more accurate result.
        /// </summary>
        [SerializeField]
        [Tooltip("Whether to enable multiple physics queries per voxel to get a more accurate result.")]
        private bool _enableMultiQuery = true;
        
        /// <summary>
        /// (Serialized) The maximum distance that external links can extend outside of this volume.
        /// </summary>
        [SerializeField]
        [Min(0)]
        [Tooltip("The maximum distance that external links can extend outside of this volume.")]
        private float _maxExternalLinkDistance = 1;
        
        /// <summary>
        /// (Serialized) The voxel size of this volume, which determines the precision but also baking cost.
        /// </summary>
        [SerializeField]
        [Min(0)]
        [Tooltip("The voxel size of this volume, which determines the precision but also baking cost.")]
        private float _voxelSize = 1;
        
        /// <summary>
        /// (Serialized) Whether only regions connected to certain locations are considered valid.
        /// </summary>
        [SerializeField, Tooltip("Whether only regions connected to certain locations are considered valid.")]
        private bool _useStartLocations = false;
        
        /// <summary>
        /// (Serialized) If <see cref="_useStartLocations"/> is true, which start locations to use.
        /// </summary>
        [SerializeField, NonReorderable, Tooltip("Which start locations to use.")]
        private Vector3[] _startLocations;
        
        /// <summary>
        /// (Serialized) Whether to use multiple threads when baking the volume.
        /// </summary>
        [SerializeField]
        private bool _useMultithreading = true;
        
        // Visualization Settings
        
        /// <summary>
        /// (Serialized) Stage at which to visualize the volume bake process in the scene view.
        /// </summary>
        [SerializeField, Tooltip("Stage at which to visualize the volume bake process in the editor.")]
        private NavVolumeVisualizationMode _visualizationMode = NavVolumeVisualizationMode.Final;
        
        /// <summary>
        /// (Serialized) Whether to show the connections of a selected region in the scene view.
        /// </summary>
        [SerializeField, Tooltip("Whether to show the connections of a selected region.")]
        private bool _visualizeNeighbors;
        
        /// <summary>
        /// (Serialized) If <see cref="_visualizeNeighbors"/> is true, which region to visualize in the scene view.
        /// </summary>
        [SerializeField, Tooltip("Which region to visualize the neighbors of.")]
        private int _visualizeNeighborsRegion;
        
        /// <summary>
        /// (Serialized) Whether to show the vertex numbers of the preview mesh in the scene view (for debugging).
        /// </summary>
        [SerializeField, Tooltip("Whether to show the vertex numbers of the preview mesh in the scene view.")]
        private bool _showVertexNumbers;
        
        /// <summary>
        /// (Serialized) Max distance from the camera at which vertex numbers will be shown.
        /// </summary>
        [SerializeField, Tooltip("Max distance from the camera at which vertex numbers will be shown.")]
        private float _showVertexNumbersRange = 2;

        /// <summary>
        /// (Serialized) Whether to visualize the queries that are performed for a voxel when baking.
        /// </summary>
        [SerializeField, Tooltip("Whether to visualize the queries that are performed for a voxel when baking.")]
        private bool _visualizeVoxelQueries;
        
        /// <summary>
        /// This is used to refer to the names of private fields in this class from a custom Editor.
        /// </summary>
        public static class PropNames {
            public const string Bounds = nameof(_bounds);
            public const string Data = nameof(_data);
            public const string BlockingLayers = nameof(_blockingLayers);
            public const string StaticOnly = nameof(_staticOnly);
            public const string InstanceID = nameof(_instanceID);
            public const string AutoDetectMovement = nameof(_autoDetectMovement);
            public const string MaxAgentRadius = nameof(_maxAgentRadius);
            public const string EnableMultiQuery = nameof(_enableMultiQuery);
            public const string MaxExternalLinkDistance = nameof(_maxExternalLinkDistance);
            public const string VoxelSize = nameof(_voxelSize);
            public const string UseStartLocations = nameof(_useStartLocations);
            public const string StartLocations = nameof(_startLocations);
            public const string UseMultithreading = nameof(_useMultithreading);
            public const string VisualizationMode = nameof(_visualizationMode);
            public const string VisualizeNeighbors = nameof(_visualizeNeighbors);
            public const string VisualizeNeighborsRegion = nameof(_visualizeNeighborsRegion);
            public const string ShowVertexNumbers = nameof(_showVertexNumbers);
            public const string ShowVertexNumbersRange = nameof(_showVertexNumbersRange);
            public const string VisualizeVoxelQueries = nameof(_visualizeVoxelQueries);
        }

        #endregion
        
        #region Private Fields
        
        private bool _hasCheckedId;
        private NativeNavVolumeData _nativeData;
        private NavDataInternalPointers _pointers;
        private Vector3 _lastPosition;
        private Quaternion _lastRotation;

        #endregion

        #region Serialized Field Accessor Properties
        
        /// <summary>
        /// The boundaries of the volume.
        /// </summary>
        /// <remarks>
        /// This cannot be set while the game is running.
        /// </remarks>
        public Bounds Bounds {
            get => _bounds;
            set {
                if (DebugUtility.CheckPlaying(true)) return;
                _bounds = value;
            }
        }

        /// <summary>
        /// The baked data for the volume.
        /// </summary>
        /// <remarks>
        /// This cannot be set while the game is running.
        /// </remarks>
        public NavVolumeData Data {
            get => _data;
            set {
                if (DebugUtility.CheckPlaying(true)) return;
                _data = value;
            }
        }

        /// <summary>
        /// The unique ID for this volume to identify it in pathfinding jobs and serialized data.
        /// </summary>
        public long InstanceID => _instanceID;
        
        /// <summary>
        /// Whether to automatically update native data if the volume moves.
        /// </summary>
        /// <remarks>
        /// Note that if this is true and the volume moves every frame, pathfinding will never be able to occur.
        /// </remarks>
        public bool AutoDetectMovement {
            get => _autoDetectMovement;
            set => _autoDetectMovement = value;
        }

        /// <summary>
        /// The voxel size of this volume, which determines the precision but also baking cost.
        /// </summary>
        public float VoxelSize {
            get => _voxelSize;
            set => _voxelSize = value;
        }

        /// <summary>
        /// The maximum size of agents using this volume.
        /// </summary>
        public float MaxAgentRadius {
            get => _maxAgentRadius;
            set => _maxAgentRadius = value;
        }

        /// <summary>
        /// Whether to enable multiple physics queries per voxel to get a more accurate result.
        /// </summary>
        public bool EnableMultiQuery {
            get => _enableMultiQuery;
            set => _enableMultiQuery = value;
        }

        /// <summary>
        /// The maximum distance that external links can extend outside of this volume.
        /// </summary>
        public float MaxExternalLinkDistance {
            get => _maxExternalLinkDistance;
            set => _maxExternalLinkDistance = value;
        }

        /// <summary>
        /// Which layers are considered impassible for pathfinding.
        /// </summary>
        public LayerMask BlockingLayers {
            get => _blockingLayers;
            set => _blockingLayers = value;
        }

        /// <summary>
        /// Whether only static objects should be included in the baked data.
        /// </summary>
        public bool StaticOnly {
            get => _staticOnly;
            set => _staticOnly = value;
        }

        /// <summary>
        /// Whether only regions connected to certain locations are considered valid.
        /// </summary>
        /// <remarks>
        /// This can be used to exclude certain regions from a volume, such as regions that are outside reachable area.
        /// </remarks>
        public bool UseStartLocations {
            get => _useStartLocations;
            set => _useStartLocations = value;
        }

        /// <summary>
        /// If <see cref="_useStartLocations"/> is true, which start locations to use.
        /// </summary>
        public IReadOnlyList<Vector3> StartLocations {
            get => _startLocations;
            set => _startLocations = value is Vector3[] array ? array : value.ToArray();
        }

        /// <summary>
        /// Whether to use multiple threads when baking the volume.
        /// </summary>
        /// <remarks>
        /// This should only be turned off for debugging.
        /// </remarks>
        public bool UseMultithreading {
            get => _useMultithreading;
            set => _useMultithreading = value;
        }

        /// <summary>
        /// Stage at which to visualize the volume bake process in the scene view.
        /// </summary>
        public NavVolumeVisualizationMode VisualizationMode {
            get => _visualizationMode;
            set => _visualizationMode = value;
        }

        /// <summary>
        /// Whether to show the connections of a selected region in the scene view.
        /// </summary>
        public bool VisualizeNeighbors {
            get => _visualizeNeighbors;
            set => _visualizeNeighbors = value;
        }

        /// <summary>
        /// If <see cref="_visualizeNeighbors"/> is true, which region to visualize in the scene view.
        /// </summary>
        public int VisualizeNeighborsRegion {
            get => _visualizeNeighborsRegion;
            set => _visualizeNeighborsRegion = value;
        }

        /// <summary>
        /// Whether to show the vertex numbers of the preview mesh in the scene view (for debugging).
        /// </summary>
        public bool ShowVertexNumbers {
            get => _showVertexNumbers;
            set => _showVertexNumbers = value;
        }

        /// <summary>
        /// Max distance from the camera at which vertex numbers will be shown.
        /// </summary>
        public float ShowVertexNumbersRange {
            get => _showVertexNumbersRange;
            set => _showVertexNumbersRange = value;
        }

        /// <summary>
        /// Whether to visualize the queries that are performed for a voxel when baking.
        /// </summary>
        public bool VisualizeVoxelQueries {
            get => _visualizeVoxelQueries;
            set => _visualizeVoxelQueries = value;
        }

        #endregion

        #region Static Properties

        private static readonly Dictionary<long, NavVolume> InternalVolumes = new Dictionary<long, NavVolume>();
        
        /// <summary>
        /// Data for all loaded volumes in the format used by jobs.
        /// </summary>
        public static NativeParallelHashMap<long, NativeNavVolumeData> VolumeData;

        /// <summary>
        /// Event that is invoked immediately before active volume data changes.
        /// </summary>
        public static event Action VolumeDataChanging;

        /// <summary>
        /// Event that is invoked immediately after active volume data changes.
        /// </summary>
        public static event Action VolumeDataChanged;
        
        /// <summary>
        /// Number of places that are modifying volume data.
        /// </summary>
        public static int VolumeChangingCount { get; private set; }
        
        /// <summary>
        /// All currently loaded volumes.
        /// </summary>
        public static IReadOnlyDictionary<long, NavVolume> Volumes => InternalVolumes;

        #endregion

        #region Unity Callbacks

        /// <summary>
        /// Register this volume in the <see cref="Volumes"/> dictionary and perform initialization.
        /// </summary>
        protected virtual void OnEnable() {
            if (!Application.isPlaying || Data == null) return;
            
            // Create data in OnEnable instead of Awake, because Unity will not execute Awake when scene
            // reload is disabled.
            if (!_pointers.RegionsData.IsCreated) {
                Data.ToInternalData(this, out _nativeData, out _pointers);
            }
            
            InternalVolumes.Add(InstanceID, this);

            _lastPosition = transform.position;
            _lastRotation = transform.rotation;

            using (ChangeVolumeData.Instance()) {
                // Create native-side dictionary if needed.
                if (!VolumeData.IsCreated) {
                    VolumeData = new NativeParallelHashMap<long, NativeNavVolumeData>(8, Allocator.Persistent);
                }

                // Register in native-side dictionary.
                VolumeData[InstanceID] = _nativeData;
            }
        }
        
        /// <summary>
        /// Remove this volume from the <see cref="Volumes"/> dictionary.
        /// </summary>
        protected virtual void OnDisable() {
#if UNITY_EDITOR
            // Clear preview mesh so it is destroyed.
            EditorOnlyPreviewMesh = null;
#endif

            if (!Application.isPlaying) return;
            
            using (ChangeVolumeData.Instance()) {
                // Deregister from managed-side dictionary.
                InternalVolumes.Remove(InstanceID);

                // Deregister from native-side dictionary if it's created.
                if (!VolumeData.IsCreated) return;
                VolumeData.Remove(InstanceID);
                // ReSharper disable once UseMethodAny.2
                if (VolumeData.Count() == 0) {
                    VolumeData.Dispose();
                }
            }
        }

        /// <summary>
        /// Dispose native-side data for this volume.
        /// </summary>
        protected virtual void OnDestroy() {
#if UNITY_EDITOR
            // Clear preview mesh so it is destroyed.
            EditorOnlyPreviewMesh = null;
#endif

            if (Application.isPlaying && Data != null) {
                _pointers.Dispose();
                _nativeData = default; // Avoid dangling pointers
            }
        }
        
        /// <summary>
        /// Update UniqueID in editor, and check movement.
        /// </summary>
        protected virtual void Update() {
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                if (_instanceID == 0 || !_hasCheckedId) {
                    UpdateUniqueID();
                }
            }
#endif

            if (Application.isPlaying && _autoDetectMovement) {
                Vector3 pos = transform.position;
                Quaternion rot = transform.rotation;

                if (pos != _lastPosition || rot != _lastRotation) {
                    UpdateTransform();
                    _lastPosition = pos;
                    _lastRotation = rot;
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Perform a query to find the nearest point on this volume to the given point.
        /// </summary>
        /// <param name="position">The point at which to search.</param>
        /// <param name="hit">The resulting hit, containing the nearest point on this volume.</param>
        /// <param name="maxDistance">The radius in which to search (a larger value is more expensive).</param>
        /// <returns>Whether a hit on this volume could be found in the given radius.</returns>
        public virtual bool SamplePosition(Vector3 position, out NavHit hit, float maxDistance) {
            hit = new NavHit {
                Region = -1,
            };
            
            // If no data (not baked), nothing to do here.
            if (Data == null) return false;

            // Convert the position to inside the volume's coordinate space.
            // This makes the following math simpler and allows bounds checking without rotating the bounds.
            Vector3 localPos = transform.InverseTransformPoint(position);
            
            // Check if position is inside any regions.
            // If so, then the position is the same as the hit position and no complex math is needed.
            // There's no way a region can contain the point if the volume's bounds don't contain it.
            if (_bounds.Contains(localPos)) {
                for (int i = 0; i < Data.Regions.Count; i++) {
                    NavRegionData region = Data.Regions[i];

                    // Quick reject: just check the bounds of the region.
                    if (!region.Bounds.Contains(localPos)) continue;

                    // If inside the bounds, need to check each face to see if point is on the inside.
                    // This works because regions are always convex.
                    bool isOutside = false;
                    for (int j = 0; j < region.BoundPlanes.Count; j++) {
                        NavRegionBoundPlane plane = region.BoundPlanes[j];
                        Vector3 vertex = Data.Vertices[plane.IntersectVertex];
                        Vector3 offset = localPos - vertex;
                        
                        // Determine if the point is inside the plane by taking the dot product of the offset
                        // from a point on the plane to the target point and the plane's normal.
                        float dot = Vector3.Dot(offset, plane.Normal);
                        if (dot < 0) continue;

                        isOutside = true;
                        // Debug.DrawLine(transform.TransformPoint(vertex), transform.TransformPoint(vertex + plane.Normal * 2), Color.red);
                        break;
                    }

                    // If a region is found that contains the target position, no more work is needed.
                    if (!isOutside) {
                        hit = new NavHit {
                            Volume = this,
                            Region = i,
                            IsOnEdge = false,
                            Normal = Vector3.zero,
                            Position = position,
                        };
                        return true;
                    }
                }
            }

            // A maxDistance of zero means the target point must be inside a region to hit,
            // so the query fails at this point.
            if (maxDistance <= 0) return false;
            
            // The point can be rejected if the the sample radius doesn't overlap the volume bounds.
            // Instead of doing a complex sphere vs box test, just create a bounds at the target with the sample radius.
            Bounds intersectBounds = new Bounds(localPos, Vector3.one * (maxDistance * 2));
            if (!_bounds.Intersects(intersectBounds)) return false;

            // Need to find which region has the closest point, which means looping through all regions.
            // Initialize closestDistance2 to be maxDistance ^ 2 so any point over that distance is ignored.
            Vector3 closestPoint = default;
            float closestDistance2 = maxDistance * maxDistance;
            int closestRegion = -1;

            // Check regions within maxDistance range.
            for (int i = 0; i < Data.Regions.Count; i++) {
                NavRegionData region = Data.Regions[i];

                // Region can be skipped if the target bounds created earlier don't overlap it.
                if (!region.Bounds.Intersects(intersectBounds)) continue;

                // Loop through all of the region's triangles to find which one has the nearest point.
                int triCount = region.Indices.Count / 3;
                for (int triIndex = 0; triIndex < triCount; triIndex++) {
                    // First index of the triangle in the mesh indices array.
                    int triStart = triIndex * 3;
                    
                    // Get triangle vertex indices.
                    int v1 = region.Indices[triStart + 0];
                    int v2 = region.Indices[triStart + 1];
                    int v3 = region.Indices[triStart + 2];

                    // Get the triangle vertex positions.
                    Vector3 v1Pos = Data.Vertices[v1];
                    Vector3 v2Pos = Data.Vertices[v2];
                    Vector3 v3Pos = Data.Vertices[v3];

                    // Get the nearest point on the triangle, including its boundaries.
                    Vector3 nearestOnTriangle =
                        MathUtility.GetNearestPointOnTriangleIncludingBounds(v1Pos, v2Pos, v3Pos, localPos);
                    
                    // Check if that point is closer than the previous nearest point.
                    float dist2 = Vector3.SqrMagnitude(nearestOnTriangle - localPos);
                    if (dist2 < closestDistance2) {
                        closestPoint = nearestOnTriangle;
                        closestDistance2 = dist2;
                        closestRegion = i;
                    }
                }
            }

            // If any hit was found in range, return it.
            if (closestRegion >= 0) {
                hit = new NavHit {
                    Volume = this,
                    Region = closestRegion,
                    IsOnEdge = true,
                    Normal = Vector3.zero,
                    Position = transform.TransformPoint(closestPoint),
                };
                return true;
            }

            return false;
        }
        
        /// <summary>
        /// Cast a ray against the blocking triangles of the volume, and return the nearest hit.
        /// </summary>
        /// <param name="start">The position (in world space) to start the query at.</param>
        /// <param name="end">The position (in world space) to end the query at.</param>
        /// <param name="hit">If the query hits a triangle, the ratio between start and end at which the hit occurred.</param>
        /// <returns>Whether a triangle was hit.</returns>
        public bool Raycast(Vector3 start, Vector3 end, out float hit) {
            NavRaycastJob job = new NavRaycastJob {
                Start = new float4(start, 1),
                End = new float4(end, 1),
                OutDistance = new NativeArray<float>(1, Allocator.TempJob),
                Volume = _nativeData,
            };

            job.Run();
            hit = job.OutDistance[0];
            job.OutDistance.Dispose();
            
            return hit >= 0;
        }

        /// <summary>
        /// Update the native data on all loaded NavVolumes.
        /// </summary>
        /// <remarks>
        /// Use this after moving all volumes when <see cref="AutoDetectMovement"/> is disabled.
        /// </remarks>
        public static void UpdateAllTransforms() {
            using ChangeVolumeData change = ChangeVolumeData.Instance();
            
            foreach (var pair in InternalVolumes) {
                pair.Value.InternalUpdateTransform();
            }
        }

        /// <summary>
        /// Update the native data of this NavVolume.
        /// </summary>
        /// <remarks>
        /// This is called automatically if <see cref="AutoDetectMovement"/> is enabled.
        /// </remarks>
        public void UpdateTransform() {
            using ChangeVolumeData change = ChangeVolumeData.Instance();
            
            InternalUpdateTransform();
        }

        private void InternalUpdateTransform() {
            int linkIndex = 0;
            Transform t = transform;
            foreach (NavRegionData region in _data.Regions) {
                foreach (NavExternalLinkData link in region.ExternalLinks) {
                    ref NativeNavExternalLinkData nativeLink = ref _nativeData.ExternalLinks[linkIndex++];
                    link.ToInternalData(t, _data.ExternalLinksAreLocalSpace, out nativeLink);
                }
            }
            
            VolumeData[InstanceID] = _nativeData = new NativeNavVolumeData(
                _nativeData.ID,
                t.localToWorldMatrix,
                t.worldToLocalMatrix,
                _nativeData.Bounds,
                _nativeData.Vertices,
                _nativeData.Regions,
                _nativeData.BlockingTriangleIndices,
                _nativeData.InternalLinks,
                _nativeData.ExternalLinks,
                _nativeData.LinkVertices,
                _nativeData.LinkEdges,
                _nativeData.LinkTriangles);
        }

        #endregion

        #region Editor Code

#if UNITY_EDITOR

        private Mesh _editorOnlyPreviewMesh;
        
        /// <summary>
        /// (Editor Only) Preview mesh that is rendered to visualize the volume.
        /// </summary>
        /// <remarks>
        /// When set, the old mesh will be destroyed.
        /// The supplied mesh will have its HideFlags set to HideAndDontSave,
        /// meaning it will not be saved with the scene, but will not be destroyed except manually here.
        /// Therefore it is important that this value be set to null when the NavVolume is destroyed,
        /// in order to avoid leaking memory.
        /// </remarks>
        public Mesh EditorOnlyPreviewMesh {
            get => _editorOnlyPreviewMesh;
            set {
                // If no change, do nothing.
                if (_editorOnlyPreviewMesh == value) return;
                
                // Destroy the old mesh.
                if (_editorOnlyPreviewMesh) {
                    DestroyImmediate(_editorOnlyPreviewMesh);
                }
                
                // Set the value and set its hide flags.
                _editorOnlyPreviewMesh = value;
                if (value) {
                    value.hideFlags = HideFlags.HideAndDontSave;
                }
            }
        }
        
        /// <summary>
        /// (Editor Only) List of materials to use for drawing the preview mesh.
        /// </summary>
        /// <remarks>
        /// These should be references to assets so that they don't need to be destroyed.
        /// </remarks>
        public Material[] EditorOnlyPreviewMaterials { get; set; }
        
        /// <summary>
        /// Update the unique ID of the volume based on its object ID, and ensure its data is named correctly.
        /// </summary>
        public virtual void UpdateUniqueID(bool promptMigrate = true) {
            // Get the object identifier of this component.
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(this);
            long newID = (long)(id.targetObjectId ^ id.targetPrefabId ^ (ulong)id.assetGUID.GetHashCode());
            newID = Math.Abs(newID);
            
            // Mark it as checked so this is only done once per script load.
            _hasCheckedId = true;

            if (promptMigrate && newID != _instanceID) {
                long oldID = (long)(id.targetObjectId ^ id.targetPrefabId);
                oldID = Math.Abs(oldID);
                if (oldID == _instanceID) {
                    PromptMigration();
                    return;
                }
            }
            
            // Allow undo and mark the scene/prefab as dirty.
            Undo.RecordObject(this, "Set Unique ID");
            _instanceID = newID;
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
            
            // Ensure that two volumes do not share the same data, such as if a volume is duplicated.
            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(gameObject);

            if (status == PrefabInstanceStatus.NotAPrefab) {
                string dataName = $"HyperNavVolume_{_instanceID}";
                if (_data != null && _data.name != dataName) {
                    _data = null;
                }
            }
        }

        private static void PromptMigration() {
            bool migrate = EditorUtility.DisplayDialog(
                "HyperNav Migration",
                "NavVolume IDs have changed to fix a duplicate ID bug. " +
                "Migrate the IDs to preserve existing baked data. " +
                "Note that not migrating will cause all volumes to need to be re-baked.",
                "Migrate", "Don't Migrate");

            NavVolume[] volumes = FindObjectsOfType<NavVolume>();

            Dictionary<long, long> idMap = new Dictionary<long, long>();
            foreach (NavVolume volume in volumes) {
                GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(volume);
                long oldID = (long)(id.targetObjectId ^ id.targetPrefabId);
                oldID = Math.Abs(oldID);
            
                if (oldID == volume._instanceID) {
                    if (migrate) {
                        long newID = (long)(id.targetObjectId ^ id.targetPrefabId ^ (ulong)id.assetGUID.GetHashCode());
                        newID = Math.Abs(newID);

                        Undo.RecordObject(volume, "Migrate Volume IDs");
                        volume._instanceID = newID;
                        if (volume._data == null) continue;
                        string dataPath = AssetDatabase.GetAssetPath(volume._data);
                        if (string.IsNullOrEmpty(dataPath)) {
                            volume._data = null;
                            continue;
                        }
                        string dataName = $"HyperNavVolume_{newID}";
                        AssetDatabase.RenameAsset(dataPath, dataName);
                        EditorUtility.SetDirty(volume._data);
                        idMap[oldID] = newID;
                    } else {
                        volume.UpdateUniqueID(false);
                    }
                }
            }
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            if (migrate) {
                NavVolumeData[] datas = AssetDatabase.FindAssets($"t:{nameof(NavVolumeData)}")
                                                     .Select(AssetDatabase.GUIDToAssetPath)
                                                     .Select(AssetDatabase.LoadMainAssetAtPath)
                                                     .OfType<NavVolumeData>()
                                                     .ToArray();
                
                Undo.RecordObjects(datas, "Migrate Volume IDs");
                foreach (NavVolumeData volumeData in datas) {
                    foreach (NavRegionData regionData in volumeData.Regions) {
                        foreach (NavExternalLinkData externalLink in regionData.ExternalLinks) {
                            if (idMap.TryGetValue(externalLink.ConnectedVolumeID, out long newID)) {
                                externalLink.ConnectedVolumeID = newID;
                            }
                        }
                    }
                    EditorUtility.SetDirty(volumeData);
                }
            }
                
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Check if the script is in a prefab (not a scene object).
        /// </summary>
        /// <returns>Whether the script is in a prefab asset (not a prefab instance).</returns>
        protected virtual bool IsPrefab() {
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(gameObject);
            PrefabAssetType type = PrefabUtility.GetPrefabAssetType(gameObject);
            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(gameObject);
            
            return (stage != null && transform.parent == null) ||
                   ((type == PrefabAssetType.Regular || type == PrefabAssetType.Variant) &&
                    status == PrefabInstanceStatus.NotAPrefab);
        }
#endif

        #endregion
        
        private struct ChangeVolumeData : IDisposable {
            public static ChangeVolumeData Instance() {
                VolumeChangingCount++;
                
                if (VolumeChangingCount == 1) {
                    VolumeDataChanging?.Invoke();
                }
                
                return new ChangeVolumeData();
            }
            
            public void Dispose() {
                if (VolumeChangingCount < 1) {
                    Debug.LogError("Over-disposing ChangeVolumeData.");
                    return;
                }
                
                VolumeChangingCount--;

                if (VolumeChangingCount == 0) {
                    VolumeDataChanged?.Invoke();
                }
            }
        }
    }
    
    /// <summary>
    /// The various modes available to generate a preview mesh in the editor for visualization.
    /// </summary>
    public enum NavVolumeVisualizationMode {
        /// <summary>
        /// Do not generate a preview mesh.
        /// </summary>
        None,
        /// <summary>
        /// (Requires Re-bake) Show open and blocked voxels.
        /// </summary>
        Voxels,
        /// <summary>
        /// (Requires Re-bake) Show which region each voxel belongs to before regions are split to be convex.
        /// </summary>
        InitialRegions,
        /// <summary>
        /// (Requires Re-bake) Show which region each voxel belongs to after regions are split to be convex.
        /// </summary>
        ConvexRegions,
        /// <summary>
        /// (Requires Re-bake) Show which region each voxel belongs to after compatible regions are merged.
        /// </summary>
        CombinedRegions,
        /// <summary>
        /// (Requires Re-bake) Show the meshes generated by triangulating the voxel regions.
        /// </summary>
        RegionTriangulation,
        /// <summary>
        /// (Requires Re-bake) Show the triangulation meshes with unnecessary vertices removed.
        /// </summary>
        Decimation,
        /// <summary>
        /// Show the blocking (impassible) areas.
        /// </summary>
        Blocking,
        /// <summary>
        /// Show the final serialized data for the volume.
        /// </summary>
        Final,
    }
}