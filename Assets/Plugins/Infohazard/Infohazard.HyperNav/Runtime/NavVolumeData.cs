// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Collections.Generic;
using Infohazard.Core;
using Infohazard.HyperNav.Jobs;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace Infohazard.HyperNav {
    /// <summary>
    /// The serialized data representing a single region in a NavVolume.
    /// </summary>
    [Serializable]
    public class NavRegionData {
        /// <summary>
        /// (Serialized) The ID of the region.
        /// </summary>
        [SerializeField] private int _id;
        
        /// <summary>
        /// (Serialized) The bounds of the region in local space of the volume.
        /// </summary>
        [SerializeField] private Bounds _bounds;
        
        /// <summary>
        /// (Serialized) The indices of the region's triangle vertices in the volume's vertices array.
        /// </summary>
        [SerializeField, NonReorderable] private int[] _indices;
        
        /// <summary>
        /// (Serialized) The links between this region and other regions in the same volume.
        /// </summary>
        [SerializeField, NonReorderable] private NavInternalLinkData[] _internalLinks;
        
        /// <summary>
        /// (Serialized) The links between this region and regions in other volumes.
        /// </summary>
        [SerializeField, NonReorderable] private NavExternalLinkData[] _externalLinks;
        
        /// <summary>
        /// (Serialized) The planes that form the boundaries of this region, to check if a point is inside or not.
        /// </summary>
        [SerializeField, NonReorderable] private NavRegionBoundPlane[] _boundPlanes;
        
        /// <summary>
        /// The ID of the region.
        /// </summary>
        public int ID => _id;
        
        /// <summary>
        /// The bounds of the region in local space of the volume.
        /// </summary>
        public Bounds Bounds => _bounds;
        
        /// <summary>
        /// The indices of the region's triangle vertices in the volume's vertices array.
        /// </summary>
        public IReadOnlyList<int> Indices => _indices;
        
        /// <summary>
        /// The links between this region and other regions in the same volume.
        /// </summary>
        public IReadOnlyList<NavInternalLinkData> InternalLinks => _internalLinks;
        
        /// <summary>
        /// The links between this region and regions in other volumes.
        /// </summary>
        public IReadOnlyList<NavExternalLinkData> ExternalLinks => _externalLinks;
        
        /// <summary>
        /// The planes that form the boundaries of this region, to check if a point is inside or not.
        /// </summary>
        public IReadOnlyList<NavRegionBoundPlane> BoundPlanes => _boundPlanes;

        /// <summary>
        /// Construct a new NavRegionData with the given values.
        /// </summary>
        /// <remarks>
        /// No value for <see cref="ExternalLinks"/> is provided here because that must be calculated later.
        /// </remarks>
        /// <param name="id">ID of the region.</param>
        /// <param name="indices">Indices of the region triangles.</param>
        /// <param name="bounds">Bounds of the region.</param>
        /// <param name="internalLinks">Links to other regions in same volume.</param>
        /// <param name="boundPlanes">Planes that form the boundaries of the region.</param>
        /// <returns>The created NavRegionData.</returns>
        public static NavRegionData Create(int id, int[] indices, Bounds bounds, NavInternalLinkData[] internalLinks,
                                           NavRegionBoundPlane[] boundPlanes) {
            return new NavRegionData {
                _id = id,
                _bounds = bounds,
                _indices = indices,
                _internalLinks = internalLinks,
                _externalLinks = Array.Empty<NavExternalLinkData>(),
                _boundPlanes = boundPlanes,
            };
        }

        /// <summary>
        /// Update the <see cref="ExternalLinks"/> of the region.
        /// </summary>
        /// <param name="externalConnections">The list of external links to use.</param>
        public void SetExternalLinks(NavExternalLinkData[] externalConnections) {
            _externalLinks = externalConnections;
        }
    }

    /// <summary>
    /// A plane forming one of the boundaries of a region.
    /// </summary>
    [Serializable]
    public struct NavRegionBoundPlane {
        /// <summary>
        /// (Serialized) Normal of the plane.
        /// </summary>
        [SerializeField] private Vector3 _normal;
        
        /// <summary>
        /// (Serialized) Index of a vertex in the volume that the plane intersects.
        /// </summary>
        [SerializeField] private int _intersectVertex;

        /// <summary>
        /// Normal of the plane.
        /// </summary>
        public Vector3 Normal => _normal;
        
        /// <summary>
        /// Index of a vertex in the volume that the plane intersects.
        /// </summary>
        public int IntersectVertex => _intersectVertex;

        /// <summary>
        /// Create a new NavRegionBoundPlane with the given properties.
        /// </summary>
        /// <param name="normal">Normal of the plane.</param>
        /// <param name="intersectVertex">Index of a vertex that the plane intersects.</param>
        /// <returns>The created NavRegionBoundPlane.</returns>
        public static NavRegionBoundPlane Create(Vector3 normal, int intersectVertex) {
            return new NavRegionBoundPlane {
                _normal = normal,
                _intersectVertex = intersectVertex,
            };
        }
    }

    /// <summary>
    /// A connection from one region to another region in the same volume.
    /// </summary>
    [Serializable]
    public class NavInternalLinkData {
        /// <summary>
        /// (Serialized) The ID of the connected region.
        /// </summary>
        [SerializeField] private int _connectedRegionID;
        
        /// <summary>
        /// (Serialized) The indices of vertices that the two regions share.
        /// </summary>
        [SerializeField, NonReorderable] private int[] _vertices;
        
        /// <summary>
        /// (Serialized) The indices of edges that the two regions share.
        /// </summary>
        [SerializeField, NonReorderable] private Edge[] _edges;
        
        /// <summary>
        /// (Serialized) The indices of triangles that the two regions share.
        /// </summary>
        [SerializeField, NonReorderable] private Triangle[] _triangles;

        /// <summary>
        /// The ID of the connected region.
        /// </summary>
        public int ConnectedRegionID => _connectedRegionID;
        
        /// <summary>
        /// The indices of vertices that the two regions share.
        /// </summary>
        public IReadOnlyList<int> Vertices => _vertices;
        
        /// <summary>
        /// The indices of edges that the two regions share.
        /// </summary>
        public IReadOnlyList<Edge> Edges => _edges;
        
        /// <summary>
        /// The indices of triangles that the two regions share.
        /// </summary>
        public IReadOnlyList<Triangle> Triangles => _triangles;

        /// <summary>
        /// Create a new NavInternalLinkData with the given properties.
        /// </summary>
        /// <param name="connectedRegionID">ID of the connected region.</param>
        /// <param name="vertices">Shared vertices.</param>
        /// <param name="edges">Shared edges.</param>
        /// <param name="triangles">Shared triangles.</param>
        /// <returns>The created NavInternalLinkData.</returns>
        public static NavInternalLinkData Create(int connectedRegionID,
                                                    int[] vertices, Edge[] edges, Triangle[] triangles) {
            return new NavInternalLinkData {
                _connectedRegionID = connectedRegionID,
                _vertices = vertices,
                _edges = edges,
                _triangles = triangles,
            };
        }
    }

    /// <summary>
    /// A connection from one region to another region in another volume.
    /// </summary>
    [Serializable]
    public class NavExternalLinkData {
        /// <summary>
        /// (Serialized) The <see cref="NavVolume.InstanceID"/> of the connected volume.
        /// </summary>
        [SerializeField] private long _connectedVolumeID;
        
        /// <summary>
        /// The ID of the connected region.
        /// </summary>
        [SerializeField] private int _connectedRegionID;
        
        /// <summary>
        /// The position at which the connection originates (local space).
        /// </summary>
        [SerializeField] private Vector3 _fromPosition;
        
        /// <summary>
        /// The position at which the connection ends (local space).
        /// </summary>
        [SerializeField] private Vector3 _toPosition;

        /// <summary>
        /// The <see cref="NavVolume.InstanceID"/> of the connected volume.
        /// </summary>
        public long ConnectedVolumeID {
            get => _connectedVolumeID;
            internal set => _connectedVolumeID = value;
        }

        /// <summary>
        /// The ID of the connected region.
        /// </summary>
        public int ConnectedRegionID => _connectedRegionID;

        /// <summary>
        /// The position at which the connection originates (local space).
        /// </summary>
        public Vector3 FromPosition => _fromPosition;

        /// <summary>
        /// The position at which the connection ends (local space).
        /// </summary>
        public Vector3 ToPosition => _toPosition;

        /// <summary>
        /// Create a new NavExternalLinkData with the given properties.
        /// </summary>
        /// <param name="connectedVolumeID">ID of the connected volume.</param>
        /// <param name="connectedRegionID">ID of the connected region.</param>
        /// <param name="fromPosition">Position at which the connection originates.</param>
        /// <param name="toPosition">Position at which the connection ends.</param>
        /// <returns>The created NavExternalLinkData.</returns>
        public static NavExternalLinkData Create(long connectedVolumeID, int connectedRegionID,
                                                 Vector3 fromPosition, Vector3 toPosition) {
            return new NavExternalLinkData {
                _connectedVolumeID = connectedVolumeID,
                _connectedRegionID = connectedRegionID,
                _fromPosition = fromPosition,
                _toPosition = toPosition,
            };
        }

        /// <summary>
        /// Convert to a native representation, transforming points to world space if necessary.
        /// </summary>
        /// <param name="volumeTransform">Transform of the NavVolume.</param>
        /// <param name="volumeLocalSpace">Whether link is stored in local space.</param>
        /// <param name="data">The created native data.</param>
        public void ToInternalData(Transform volumeTransform, bool volumeLocalSpace, out NativeNavExternalLinkData data) {
            float4 fromPosition;
            float4 toPosition;
            if (volumeLocalSpace) {
                fromPosition = volumeTransform.TransformPoint(FromPosition).ToV4Pos();
                toPosition = volumeTransform.TransformPoint(ToPosition).ToV4Pos();
            } else {
                fromPosition = FromPosition.ToV4Pos();
                toPosition = ToPosition.ToV4Pos();
            }

            data = new NativeNavExternalLinkData(ConnectedVolumeID, ConnectedRegionID, fromPosition, toPosition);
        }
    }
    
    /// <summary>
    /// The baked data of a <see cref="NavVolume"/>, saved as an asset.
    /// </summary>
    public class NavVolumeData : ScriptableObject {
        /// <summary>
        /// (Serialized) The vertex positions of all of the volume's regions, in local space.
        /// </summary>
        [SerializeField, NonReorderable] private Vector3[] _vertices;
        
        /// <summary>
        /// (Serialized) The regions of the volume.
        /// </summary>
        [SerializeField, NonReorderable] private NavRegionData[] _regions;
        
        /// <summary>
        /// (Serialized) The vertex indices of triangles that define impassible space in the volume.
        /// </summary>
        [SerializeField, NonReorderable] private int[] _blockingTriangleIndices;

        /// <summary>
        /// (Serialized) Whether the external links are in local space (false = world space).
        /// </summary>
        [SerializeField] private bool _externalLinksAreLocalSpace;

        /// <summary>
        /// The vertex positions of all of the volume's regions, in local space.
        /// </summary>
        public IReadOnlyList<Vector3> Vertices => _vertices;
        
        /// <summary>
        /// The regions of the volume.
        /// </summary>
        public IReadOnlyList<NavRegionData> Regions => _regions;
        
        /// <summary>
        /// The vertex indices of triangles that define impassible space in the volume.
        /// </summary>
        public IReadOnlyList<int> BlockingTriangleIndices => _blockingTriangleIndices;
        
        /// <summary>
        /// Whether the external links are in local space (false = world space).
        /// </summary>
        public bool ExternalLinksAreLocalSpace => _externalLinksAreLocalSpace;

        /// <summary>
        /// Populate the properties of this NavVolumeData.
        /// </summary>
        /// <param name="vertices">Vertex positions of the volume's regions.</param>
        /// <param name="regions">Regions of the volume.</param>
        /// <param name="blockingTriangleIndices">Indices of triangles that define impassible space.</param>
        public void Populate(Vector3[] vertices, NavRegionData[] regions, int[] blockingTriangleIndices) {
            _vertices = vertices;
            _regions = regions;
            _blockingTriangleIndices = blockingTriangleIndices;
            _externalLinksAreLocalSpace = true;
        }

        /// <summary>
        /// After updating external links, mark them as being in local space.
        /// </summary>
        public void MarkExternalLinksLocalSpace() {
            if (DebugUtility.CheckPlaying()) return;
            _externalLinksAreLocalSpace = true;
        }

        /// <summary>
        /// Clear the properties of this NavVolumeData.
        /// </summary>
        public void Clear() {
            _vertices = Array.Empty<Vector3>();
            _regions = Array.Empty<NavRegionData>();
            _blockingTriangleIndices = Array.Empty<int>();
        }

        /// <summary>
        /// Convert this NavVolumeData to the native format so that it can be used by jobs.
        /// </summary>
        /// <param name="volume">Volume that owns this data.</param>
        /// <param name="data">Created native data.</param>
        /// <param name="pointers">Created data structure references (must be kept in order to deallocate).</param>
        public void ToInternalData(NavVolume volume, out NativeNavVolumeData data, out NavDataInternalPointers pointers) {
            // Count the data in all regions in order to allocate fixed-size arrays.
            int internalLinkCount = 0;
            int externalLinkCount = 0;
            int linkVertexCount = 0;
            int linkEdgeCount = 0;
            int linkTriangleCount = 0;

            for (int i = 0; i < volume.Data._regions.Length; i++) {
                NavRegionData region = volume.Data._regions[i];

                internalLinkCount += region.InternalLinks.Count;
                externalLinkCount += region.ExternalLinks.Count;
                
                for (int j = 0; j < region.InternalLinks.Count; j++) {
                    NavInternalLinkData link = region.InternalLinks[j];

                    linkVertexCount += link.Vertices.Count;
                    linkEdgeCount += link.Edges.Count;
                    linkTriangleCount += link.Triangles.Count;
                }
            }
            
            // Keep track of current index as elements are added to the fixed-size arrays.
            int internalLinkIndex = 0;
            int externalLinkIndex = 0;
            int linkVertexIndex = 0;
            int linkEdgeIndex = 0;
            int linkTriangleIndex = 0;

            // Allocate data structures.
            pointers = default;
            pointers.RegionsData = new NativeArray<NativeNavRegionData>(_regions.Length, Allocator.Persistent);
            pointers.PositionsData = new NativeArray<float4>(_vertices.Length, Allocator.Persistent);
            pointers.BlockingTriIndices = new NativeArray<int>(_blockingTriangleIndices, Allocator.Persistent);
            pointers.InternalLinksData = new NativeArray<NativeNavInternalLinkData>(internalLinkCount, Allocator.Persistent);
            pointers.ExternalLinksData = new NativeArray<NativeNavExternalLinkData>(externalLinkCount, Allocator.Persistent);
            pointers.LinkVerticesData = new NativeArray<int>(linkVertexCount, Allocator.Persistent);
            pointers.LinkEdgesData = new NativeArray<int2>(linkEdgeCount, Allocator.Persistent);
            pointers.LinkTrianglesData = new NativeArray<int3>(linkTriangleCount, Allocator.Persistent);
            
            // Populate native vertices.
            for (int i = 0; i < _vertices.Length; i++) {
                Vector3 v = _vertices[i];
                pointers.PositionsData[i] = new float4(v.x, v.y, v.z, 1);
            }

            // Populate native pre-region data.
            for (int i = 0; i < _regions.Length; i++) {
                NavRegionData region = _regions[i];
                
                // Populate data of the region itself.
                pointers.RegionsData[i] = new NativeNavRegionData(region.ID, new NativeBounds(
                        region.Bounds.center.ToV4Pos(),
                        region.Bounds.extents.ToV4()),
                    internalLinkIndex, region.InternalLinks.Count,
                    externalLinkIndex, region.ExternalLinks.Count);

                // Populate native region internal links.
                for (int j = 0; j < region.InternalLinks.Count; j++) {
                    NavInternalLinkData connection = region.InternalLinks[j];

                    // Populate data of the link itself.
                    pointers.InternalLinksData[internalLinkIndex++] =
                        new NativeNavInternalLinkData(connection.ConnectedRegionID,
                            linkVertexIndex, connection.Vertices.Count,
                            linkEdgeIndex, connection.Edges.Count,
                            linkTriangleIndex, connection.Triangles.Count);

                    // Populate the link's shared vertex indices.
                    for (int k = 0; k < connection.Vertices.Count; k++) {
                        pointers.LinkVerticesData[linkVertexIndex++] = connection.Vertices[k];
                    }

                    // Populate the link's shared edge indices.
                    for (int k = 0; k < connection.Edges.Count; k++) {
                        Edge edge = connection.Edges[k];
                        pointers.LinkEdgesData[linkEdgeIndex++] = new int2(edge.Vertex1, edge.Vertex2);
                    }

                    // Populate the link's shared triangle indices.
                    for (int k = 0; k < connection.Triangles.Count; k++) {
                        Triangle triangle = connection.Triangles[k];
                        pointers.LinkTrianglesData[linkTriangleIndex++] = new int3(triangle.Vertex1, triangle.Vertex2, triangle.Vertex3);
                    }
                }

                // Populate native region external links.
                for (int j = 0; j < region.ExternalLinks.Count; j++) {
                    NavExternalLinkData connection = region.ExternalLinks[j];
                    
                    // Populate data of the link itself.
                    connection.ToInternalData(volume.transform, _externalLinksAreLocalSpace,
                                              out NativeNavExternalLinkData nativeLink);

                    pointers.ExternalLinksData[externalLinkIndex++] = nativeLink;
                }
            }

            // Assign populated region data to the NativeNavVolumeData.
            
            // To store these parameters in the NativeNavVolumeData,
            // pointers are used instead of direct references to the NativeArrays.
            // This is because the Burst Compiler does not allow nested native collections,
            // and the volumes will already be in one outer native collection.
            Transform transform = volume.transform;
            data = new NativeNavVolumeData(volume.InstanceID,
                transform.localToWorldMatrix,
                transform.worldToLocalMatrix,
                new NativeBounds(volume.Bounds.center.ToV4Pos(),
                    volume.Bounds.extents.ToV4()),
                UnsafeArrayPtr<float4>.ToPointer(pointers.PositionsData),
                UnsafeArrayPtr<NativeNavRegionData>.ToPointer(pointers.RegionsData),
                UnsafeArrayPtr<int>.ToPointer(pointers.BlockingTriIndices),
                UnsafeArrayPtr<NativeNavInternalLinkData>.ToPointer(pointers.InternalLinksData),
                UnsafeArrayPtr<NativeNavExternalLinkData>.ToPointer(pointers.ExternalLinksData),
                UnsafeArrayPtr<int>.ToPointer(pointers.LinkVerticesData),
                UnsafeArrayPtr<int2>.ToPointer(pointers.LinkEdgesData),
                UnsafeArrayPtr<int3>.ToPointer(pointers.LinkTrianglesData));
            
        }
    }

    /// <summary>
    /// References to the NativeArrays allocated for a <see cref="NativeNavVolumeData"/>.
    /// </summary>
    /// <remarks>
    /// In the NativeNavVolumeData itself, these arrays are kept as pointers,
    /// which cannot be used to deallocate the arrays under Unity's safe memory system.
    /// In order to play nicely with that system, the original references must be kept and disposed.
    /// </remarks>
    public struct NavDataInternalPointers : IDisposable {
        public NativeArray<NativeNavRegionData> RegionsData;
        public NativeArray<float4> PositionsData;
        public NativeArray<int> BlockingTriIndices;
        public NativeArray<NativeNavInternalLinkData> InternalLinksData;
        public NativeArray<NativeNavExternalLinkData> ExternalLinksData;
        public NativeArray<int> LinkVerticesData;
        public NativeArray<int2> LinkEdgesData;
        public NativeArray<int3> LinkTrianglesData;

        /// <summary>
        /// Dispose and nullify all of the native array references.
        /// </summary>
        public void Dispose() {
            RegionsData.Dispose();
            PositionsData.Dispose();
            BlockingTriIndices.Dispose();
            InternalLinksData.Dispose();
            ExternalLinksData.Dispose();
            LinkVerticesData.Dispose();
            LinkEdgesData.Dispose();
            LinkTrianglesData.Dispose();

            RegionsData = default;
            PositionsData = default;
            BlockingTriIndices = default;
            InternalLinksData = default;
            ExternalLinksData = default;
            LinkVerticesData = default;
            LinkEdgesData = default;
            LinkTrianglesData = default;
        }
    }
}