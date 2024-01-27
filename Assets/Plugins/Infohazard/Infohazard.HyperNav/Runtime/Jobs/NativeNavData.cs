// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Unity.Mathematics;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// The baked data of a <see cref="NavVolume"/>, converted to a form compatible with Burst.
    /// </summary>
    public readonly struct NativeNavVolumeData {
        /// <summary>
        /// ID of the volume.
        /// </summary>
        public readonly long ID;
        
        /// <summary>
        /// Transform matrix of the volume.
        /// </summary>
        public readonly float4x4 Transform;
        
        /// <summary>
        /// Inverse transform matrix of the volume.
        /// </summary>
        public readonly float4x4 InverseTransform;
        
        /// <summary>
        /// Bounds of the volume in local space.
        /// </summary>
        public readonly NativeBounds Bounds;
        
        /// <summary>
        /// The vertex positions of all of the volume's regions, in local space.
        /// </summary>
        public readonly UnsafeArrayPtr<float4> Vertices;
        
        /// <summary>
        /// The regions of the volume.
        /// </summary>
        public readonly UnsafeArrayPtr<NativeNavRegionData> Regions;
        
        /// <summary>
        /// The vertex indices of triangles that define impassible space in the volume.
        /// </summary>
        public readonly UnsafeArrayPtr<int> BlockingTriangleIndices;
        
        /// <summary>
        /// The internal links of all of the volume's regions.
        /// </summary>
        public readonly UnsafeArrayPtr<NativeNavInternalLinkData> InternalLinks;
        
        /// <summary>
        /// The external links of all of the volume's regions.
        /// </summary>
        public readonly UnsafeArrayPtr<NativeNavExternalLinkData> ExternalLinks;
        
        /// <summary>
        /// The shared vertices of all of the volume's internal links.
        /// </summary>
        public readonly UnsafeArrayPtr<int> LinkVertices;
        
        /// <summary>
        /// The shared edges of all of the volume's internal links.
        /// </summary>
        public readonly UnsafeArrayPtr<int2> LinkEdges;
        
        /// <summary>
        /// The shared triangles of all of the volume's internal links.
        /// </summary>
        public readonly UnsafeArrayPtr<int3> LinkTriangles;
        
        /// <summary>
        /// Initialize a new NativeNavVolumeData with the given data.
        /// </summary>
        /// <param name="id">ID of the volume.</param>
        /// <param name="transform">Transform matrix of the volume.</param>
        /// <param name="inverseTransform">Inverse transform matrix of the volume.</param>
        /// <param name="bounds">Bounds of the volume in local space.</param>
        /// <param name="vertices">The vertex positions of all of the volume's regions.</param>
        /// <param name="regions">The regions of the volume.</param>
        /// <param name="blockingTriangleIndices">The indices of triangles that define impassible space in the volume.</param>
        /// <param name="internalLinks">The internal links of all of the volume's regions.</param>
        /// <param name="externalLinks">The external links of all of the volume's regions.</param>
        /// <param name="linkVertices">The shared vertices of all of the volume's internal links.</param>
        /// <param name="linkEdges">The shared edges of all of the volume's internal links.</param>
        /// <param name="linkTriangles">The shared triangles of all of the volume's internal links.</param>
        public NativeNavVolumeData(long id, float4x4 transform, float4x4 inverseTransform, NativeBounds bounds, 
            UnsafeArrayPtr<float4> vertices, UnsafeArrayPtr<NativeNavRegionData> regions, 
            UnsafeArrayPtr<int> blockingTriangleIndices, 
            UnsafeArrayPtr<NativeNavInternalLinkData> internalLinks, UnsafeArrayPtr<NativeNavExternalLinkData> externalLinks, 
            UnsafeArrayPtr<int> linkVertices, UnsafeArrayPtr<int2> linkEdges, UnsafeArrayPtr<int3> linkTriangles) {
            
            ID = id;
            Transform = transform;
            InverseTransform = inverseTransform;
            Bounds = bounds;
            Vertices = vertices;
            Regions = regions;
            BlockingTriangleIndices = blockingTriangleIndices;
            InternalLinks = internalLinks;
            ExternalLinks = externalLinks;
            LinkVertices = linkVertices;
            LinkEdges = linkEdges;
            LinkTriangles = linkTriangles;
        }
    }

    /// <summary>
    /// The native-friendly data representing a single region in a NavVolume.
    /// </summary>
    public readonly struct NativeNavRegionData {
        /// <summary>
        /// The ID of the region.
        /// </summary>
        public readonly int ID;
        
        /// <summary>
        /// The bounds of the region in local space of the volume.
        /// </summary>
        public readonly NativeBounds Bounds;
        
        /// <summary>
        /// The index of the region's first internal link in the volume's
        /// <see cref="NativeNavVolumeData.InternalLinks"/> list.
        /// </summary>
        public readonly int InternalLinkStart;
        
        /// <summary>
        /// The number of internal links.
        /// </summary>
        public readonly int InternalLinkCount;
        
        /// <summary>
        /// The index of the region's first external link in the volume's
        /// <see cref="NativeNavVolumeData.ExternalLinks"/> list.
        /// </summary>
        public readonly int ExternalLinkStart;
        
        /// <summary>
        /// The number of external links.
        /// </summary>
        public readonly int ExternalLinkCount;

        /// <summary>
        /// Initialize a new NativeNavRegionData with the given data.
        /// </summary>
        /// <param name="id">The ID of the region.</param>
        /// <param name="bounds">The bounds of the region in local space of the volume.</param>
        /// <param name="internalLinkStart">The index of the region's first internal link.</param>
        /// <param name="internalLinkCount">The number of internal links.</param>
        /// <param name="externalLinkStart">The index of the region's first external link.</param>
        /// <param name="externalLinkCount">The number of external links.</param>
        public NativeNavRegionData(int id, NativeBounds bounds, 
            int internalLinkStart, int internalLinkCount, int externalLinkStart, int externalLinkCount) {
            ID = id;
            Bounds = bounds;
            InternalLinkStart = internalLinkStart;
            InternalLinkCount = internalLinkCount;
            ExternalLinkStart = externalLinkStart;
            ExternalLinkCount = externalLinkCount;
        }
    }
    
    /// <summary>
    /// The native-friendly data representing a connection from one region to another region in the same volume.
    /// </summary>
    public readonly struct NativeNavInternalLinkData {
        /// <summary>
        /// The ID of the connected region.
        /// </summary>
        public readonly int ToRegion;
        
        /// <summary>
        /// The index of the link's first vertex in the volume's
        /// <see cref="NativeNavVolumeData.LinkVertices"/> list.
        /// </summary>
        public readonly int VerticesStart;
        
        /// <summary>
        /// The number of link vertices.
        /// </summary>
        public readonly int VerticesCount;
        
        /// <summary>
        /// The index of the link's first edge in the volume's
        /// <see cref="NativeNavVolumeData.LinkEdges"/> list.
        /// </summary>
        public readonly int EdgesStart;
        
        /// <summary>
        /// The number of link edges.
        /// </summary>
        public readonly int EdgesCount;
        
        /// <summary>
        /// The index of the link's first triangle in the volume's
        /// <see cref="NativeNavVolumeData.LinkTriangles"/> list.
        /// </summary>
        public readonly int TrianglesStart;
        
        /// <summary>
        /// The number of link triangles.
        /// </summary>
        public readonly int TrianglesCount;

        /// <summary>
        /// Initialize a new NativeNavInternalLinkData with the given data.
        /// </summary>
        /// <param name="toRegion">The ID of the connected region.</param>
        /// <param name="verticesStart">The index of the link's first vertex.</param>
        /// <param name="verticesCount">The number of link vertices.</param>
        /// <param name="edgesStart">The index of the link's first edge.</param>
        /// <param name="edgesCount">The number of link edges.</param>
        /// <param name="trianglesStart">The index of the link's first triangle.</param>
        /// <param name="trianglesCount">The number of link triangles.</param>
        public NativeNavInternalLinkData(int toRegion,
                                         int verticesStart, int verticesCount,
                                         int edgesStart, int edgesCount,
                                         int trianglesStart, int trianglesCount) {
            ToRegion = toRegion;
            VerticesStart = verticesStart;
            VerticesCount = verticesCount;
            EdgesStart = edgesStart;
            EdgesCount = edgesCount;
            TrianglesStart = trianglesStart;
            TrianglesCount = trianglesCount;
        }
    }

    /// <summary>
    /// The native-friendly data representing a connection from one region to another region in another volume.
    /// </summary>
    public readonly struct NativeNavExternalLinkData {
        /// <summary>
        /// The ID of the connected volume.
        /// </summary>
        public readonly long ToVolume;
        
        /// <summary>
        /// The ID of the connected region.
        /// </summary>
        public readonly int ToRegion;
        
        /// <summary>
        /// The position at which the connection originates (world space).
        /// </summary>
        public readonly float4 FromPosition;
        
        /// <summary>
        /// The position at which the connection ends (world space).
        /// </summary>
        public readonly float4 ToPosition;
        
        /// <summary>
        /// The distance from <see cref="FromPosition"/> to <see cref="ToPosition"/>.
        /// </summary>
        public readonly float InternalCost;
        
        /// <summary>
        /// Initialize a new NativeNavExternalLinkData with the given data.
        /// </summary>
        /// <param name="toVolume">The ID of the connected volume.</param>
        /// <param name="toRegion">The ID of the connected region.</param>
        /// <param name="fromPosition">The position at which the connection originates.</param>
        /// <param name="toPosition">The position at which the connection ends.</param>
        public NativeNavExternalLinkData(long toVolume, int toRegion, float4 fromPosition, float4 toPosition) {
            ToVolume = toVolume;
            ToRegion = toRegion;
            FromPosition = fromPosition;
            ToPosition = toPosition;
            InternalCost = math.distance(fromPosition, toPosition);
        }
    }
    
    /// <summary>
    /// A native-friendly representation of a navigation query result.
    /// </summary>
    public readonly struct NativeNavHit {
        /// <summary>
        /// ID of the volume that was hit.
        /// </summary>
        public readonly long Volume;
        
        /// <summary>
        /// ID of the region that was hit.
        /// </summary>
        public readonly int Region;
        
        /// <summary>
        /// Whether the result point was on the edge of the region.
        /// </summary>
        public readonly bool IsOnEdge;
        
        /// <summary>
        /// Position of the hit.
        /// </summary>
        public readonly float4 Position;

        /// <summary>
        /// Initialize a new NativeNavHit with the given data.
        /// </summary>
        /// <param name="volume">ID of the volume that was hit.</param>
        /// <param name="region">ID of the region that was hit.</param>
        /// <param name="isOnEdge">Whether the result point was on the edge of the region.</param>
        /// <param name="position">Position of the hit.</param>
        public NativeNavHit(long volume, int region, bool isOnEdge, float4 position) {
            Volume = volume;
            Region = region;
            IsOnEdge = isOnEdge;
            Position = position;
        }
    }
    
    /// <summary>
    /// A native-friendly of a bounding box.
    /// </summary>
    public readonly struct NativeBounds {
        /// <summary>
        /// Center of the bounds.
        /// </summary>
        public readonly float4 Center;
        
        /// <summary>
        /// Extents of the bounds (half of its size).
        /// </summary>
        public readonly float4 Extents;
        
        /// <summary>
        /// Initialize a new NativeBounds with the given data.
        /// </summary>
        /// <param name="center">Center of the bounds.</param>
        /// <param name="extents">Extents of the bounds (half of its size).</param>
        public NativeBounds(float4 center, float4 extents) {
            Center = center;
            Extents = extents;
        }
    }

    /// <summary>
    /// The state of a pathfinding request.
    /// </summary>
    public enum NavPathState {
        /// <summary>
        /// Path is still processing.
        /// </summary>
        Pending,
        /// <summary>
        /// A valid path was found.
        /// </summary>
        Success,
        /// <summary>
        /// Request finished processing and no valid path was found.
        /// </summary>
        Failure,
    }

    /// <summary>
    /// A structure used by the navigation job to return the waypoints of a path.
    /// </summary>
    public readonly struct NativeNavWaypoint {
        /// <summary>
        /// Position of the waypoint in world space.
        /// </summary>
        public readonly float4 Position;
        
        /// <summary>
        /// Type of the waypoint in relation to the containing volume.
        /// </summary>
        public readonly NavWaypointType Type;

        /// <summary>
        /// Identifier of the NavVolume that contains this waypoint, or -1.
        /// </summary>
        public readonly long VolumeID;

        /// <summary>
        /// Initialize a new NativeNavWaypoint with the given data.
        /// </summary>
        /// <param name="position">Position of the waypoint in world space.</param>
        /// <param name="type">Type of the waypoint in relation to the containing volume.</param>
        /// <param name="volumeID">Identifier of the NavVolume that contains this waypoint, or -1.</param>
        public NativeNavWaypoint(float4 position, NavWaypointType type, long volumeID) {
            Position = position;
            Type = type;
            VolumeID = volumeID;
        }
    }
}