// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Infohazard.Core;
using Infohazard.HyperNav;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

namespace Infohazard.HyperNav.Editor {
    /// <summary>
    /// A struct used to store mesh data of an in-progress volume mesh.
    /// </summary>
    /// <remarks>
    /// Contains cached info about connections to make mesh operations simpler.
    /// </remarks>
    public struct MultiRegionMeshInfo {
        /// <summary>
        /// All vertices of the mesh.
        /// </summary>
        public List<Vector3> Vertices { get; private set; }
        
        /// <summary>
        /// For each vertex index, which other vertex indices it is connected to via edges.
        /// </summary>
        public List<List<int>> VertexConnections { get; private set; }
        
        /// <summary>
        /// For each vertex index, which regions it is a part of.
        /// </summary>
        public List<List<int>> VertexRegionMembership { get; private set; }
        
        /// <summary>
        /// For each region, the indices of all the triangles in that region.
        /// </summary>
        public List<List<int>> RegionTriangleLists { get; private set; }
        
        /// <summary>
        /// For each triangle, for each region, what index that triangle's vertices start in that region.
        /// </summary>
        public Dictionary<Triangle, Dictionary<int, int>> TriangleIndicesPerRegion { get; private set; }

        /// <summary>
        /// Create a new empty MultiRegionMeshInfo with all data structures allocated.
        /// </summary>
        /// <returns>The created MultiRegionMeshInfo.</returns>
        public static MultiRegionMeshInfo CreateEmptyInfo() {
            return new MultiRegionMeshInfo {
                Vertices = new List<Vector3>(),
                VertexConnections = new List<List<int>>(),
                VertexRegionMembership = new List<List<int>>(),
                RegionTriangleLists = new List<List<int>>(),
                TriangleIndicesPerRegion = new Dictionary<Triangle, Dictionary<int, int>>(),
            };
        }
    }

    /// <summary>
    /// A value that can be incremented, decremented, or added to in a thread safe way.
    /// </summary>
    /// <remarks>
    /// The operations are pre-increments (equivalent to ++i),
    /// meaning the return value is the value after the increment.
    /// </remarks>
    public class ThreadSafeIncrementor {
        private int _value;

        /// <summary>
        /// Current value of the incrementor.
        /// </summary>
        /// <remarks>
        /// This can be used to set the value in a non-thread-safe manor.
        /// </remarks>
        public int Value {
            get => _value;
            set => _value = value;
        } 

        /// <summary>
        /// Create a new ThreadSafeIncrementor with the given initial value.
        /// </summary>
        /// <param name="value">The initial value.</param>
        public ThreadSafeIncrementor(int value = 0) {
            _value = value;
        }

        /// <summary>
        /// Add one to the value, then return the new value, as an atomic operation.
        /// </summary>
        /// <returns>The new value with one added.</returns>
        public int Increment() {
            return Interlocked.Increment(ref _value);
        }
        
        /// <summary>
        /// Subtract one from the value, then return the new value, as an atomic operation.
        /// </summary>
        /// <returns>The new value with one subtracted.</returns>
        public int Decrement() {
            return Interlocked.Decrement(ref _value);
        }

        /// <summary>
        /// Add the given value to the value, then return the new value, as an atomic operation.
        /// </summary>
        /// <param name="value">The value to add.</param>
        /// <returns>The new value with value added.</returns>
        public int Add(int value) {
            return Interlocked.Add(ref _value, value);
        }
    }
    
    /// <summary>
    /// Represents current bake state of a volume, including progress fraction and current operation display name.
    /// </summary>
    public struct NavVolumeBakeProgress {
        public float Progress;
        public string Operation;
    }
    
    /// <summary>
    /// Contains all the code needed to generate the data for a NavVolume.
    /// </summary>
    public static class NavVolumeBakingUtil {
        // Materials used for preview meshes.
        private static Material _voxelPreviewMaterial;
        private static Material _voxelDistancePreviewMaterial;
        private static Material _regionIDPreviewMaterial;
        private static Material _voxelOutlinePreviewMaterial;
        private static Material _triangulationPreviewMaterial;
        private static Material _triangulationOutlinePreviewMaterial;

        // Progress messages.
        private const string OpNameCalculatingBlocked = "Calculating Blocked Voxels [Step 1/6]";
        private const string OpNameCalculatingRegions = "Calculating Regions [Step 2/6]";
        private const string OpNameCombiningRegions = "Combining Regions [Step 3/6]";
        private const string OpNameTriangulatingRegions = "Triangulating Regions [Step 4/6]";
        private const string OpNameSimplifyingRegions = "Simplifying Region Meshes [Step 5/6]";
        private const string OpNameGeneratingData = "Generating Serialized Data [Step 6/6]";

        /// <summary>
        /// The bake progress for each volume currently being baked.
        /// </summary>
        public static readonly Dictionary<NavVolume, NavVolumeBakeProgress> BakeProgress =
            new Dictionary<NavVolume, NavVolumeBakeProgress>();

        /// <summary>
        /// The coroutine for each volume currently being baked.
        /// </summary>
        public static readonly Dictionary<NavVolume, EditorCoroutine> BakeCoroutines =
            new Dictionary<NavVolume, EditorCoroutine>();

        /// <summary>
        /// Invoked when the bake progress for a NavVolume changes.
        /// </summary>
        public static event Action<NavVolume> BakeProgressUpdated;

        // Used to loop through directions.
        private static readonly Vector3Int[] NeighborDirections = {
            Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back, Vector3Int.right, Vector3Int.left,
        };

        #region Public Methods

        /// <summary>
        /// Get the NavVolumeData for a given volume, or create and save the object if it doesn't exist yet.
        /// </summary>
        /// <param name="volume">The NavVolume component.</param>
        public static void GetOrCreateData(NavVolume volume) {
            // If volume already has a valid data object just return it.
            if (volume.Data != null) {
                return;
            }
            
            string name = $"HyperNavVolume_{volume.InstanceID}";
            
            // Create the new data object.
            NavVolumeData data = ScriptableObject.CreateInstance<NavVolumeData>();
            data.name = name;
            
            // Set the volume's Data reference.
            Undo.RecordObject(volume, "Create NavVolumeData");
            volume.Data = data;
            PrefabUtility.RecordPrefabInstancePropertyModifications(volume);
            
            // Save it in a folder relative to the GameObject path.
            // If in prefab at path Assets/Prefabs/Prefab1, volume data path is Assets/Prefabs/HyperNavVolume_XXXXX.
            // If in scene at path Assets/Scenes/Level1, volume data path is Assets/Scenes/Level1/HyperNavVolume_XXXXX.
            string saveFolder = GetFolderForVolumeData(volume.gameObject);
            if (string.IsNullOrEmpty(saveFolder)) {
                Debug.LogError($"Could not find folder to save volume for object {volume.gameObject}.");
                return;
            }

            // Ensure folder exists.
            if (!AssetDatabase.IsValidFolder(saveFolder)) {
                int lastSlash = saveFolder.LastIndexOf('/');
                string parentFolder = saveFolder.Substring(0, lastSlash);
                string folderName = saveFolder.Substring(lastSlash + 1);
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }

            // Save the asset.
            string assetPath = $"{saveFolder}/{name}.asset";
            AssetDatabase.CreateAsset(data, assetPath);
        }

        private static string GetFolderForVolumeData(GameObject gameObject) {
            // First check for prefab instance.
            string path = null;
            GameObject prefab = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
            if (prefab != null) {
                path = AssetDatabase.GetAssetPath(prefab);
            }
            
            // If not a prefab instance, check if a prefab stage.
            if (string.IsNullOrEmpty(path)) {
                PrefabStage stage = PrefabStageUtility.GetPrefabStage(gameObject);
                path = stage ? stage.assetPath : null;
            }

            // If a prefab or stage, get the containing folder.
            if (!string.IsNullOrEmpty(path)) {
                return path.Substring(0, path.LastIndexOf('/'));
            }
            
            // Lastly, check if in a scene.
            path = gameObject.scene.path;
            if (!string.IsNullOrEmpty(path)) {
                return path.Substring(0, path.LastIndexOf('.'));
            }

            return null;
        }

        /// <summary>
        /// Bake the NavVolumeData for a given volume.
        /// </summary>
        /// <param name="volume">The volume to bake.</param>
        public static void BakeData(NavVolume volume) {
            BakeCoroutines[volume] = EditorCoroutineUtility.StartCoroutine(CRT_BakeData(volume), volume);
        }

        // Coroutine to bake a volume asynchronously.
        private static IEnumerator CRT_BakeData(NavVolume volume) {
            Vector3 fVoxelCounts = volume.Bounds.size / volume.VoxelSize;
            Vector3Int voxelCounts = new Vector3Int {
                x = Mathf.FloorToInt(fVoxelCounts.x),
                y = Mathf.FloorToInt(fVoxelCounts.y),
                z = Mathf.FloorToInt(fVoxelCounts.z),
            };

            // Use a stopwatch to measure the time taken by each step of the bake.
            // This is mostly useful to me so I can find which parts are most critical to optimize.
            Stopwatch stopwatch = new Stopwatch();
            StringBuilder sb = new StringBuilder();
            void LogStopwatch(string label) {
                sb.Append($"{label}: {stopwatch.ElapsedMilliseconds} ms{Environment.NewLine}");
            }
            
            // Calculate 3D array of which voxels are blocked or open.
            stopwatch.Restart();
            Fast3DArray voxels = new Fast3DArray(voxelCounts.x, voxelCounts.y, voxelCounts.z);
            IEnumerator enum1 = CalculateBlockedVoxels(volume, voxelCounts, voxels);
            while (enum1.MoveNext()) yield return null;
            LogStopwatch("Calculate blocked voxels");

            // Build preview mesh that shows blocked voxels as red.
            if (volume.VisualizationMode == NavVolumeVisualizationMode.Voxels) {
                stopwatch.Restart();
                BuildVoxelPreviewMesh(volume, voxels);
                LogStopwatch("Build voxel preview mesh");
            }

            // Separate open voxels into regions.
            // At this point a region is a contiguous set of voxels,
            // and only non-contiguous islands become separate regions.
            stopwatch.Restart();
            Fast3DArray regions = CalculateRegions(voxelCounts, voxels, out int regionCount);
            LogStopwatch("Calculate initial regions");

            // Build preview mesh that shows the voxels of each initial region as a different color.
            if (volume.VisualizationMode == NavVolumeVisualizationMode.InitialRegions) {
                stopwatch.Restart();
                BuildRegionIDPreviewMesh(volume, regions, regionCount);
                LogStopwatch("Build initial region preview mesh");
            }

            // Regions will be split in multiple threads,
            // so keep an incrementor to keep track of how many regions there are.
            ThreadSafeIncrementor regionCountIncr = new ThreadSafeIncrementor(regionCount);
            
            // Split regions on concavities so that all regions are convex.
            stopwatch.Restart();
            IEnumerator enum2 = ConvexifyAllRegions(voxelCounts, regions, regionCountIncr, volume);
            while (enum2.MoveNext()) yield return null;
            regionCount = regionCountIncr.Value;
            
            // Sanity check to make sure stuff's working right.
            EnsureAllRegionsAreContiguous(voxelCounts, regions);
            LogStopwatch("Calculate convex regions");

            // Build preview mesh that shows the voxels of each convex region as a different color.
            if (volume.VisualizationMode == NavVolumeVisualizationMode.ConvexRegions) {
                stopwatch.Restart();
                BuildRegionIDPreviewMesh(volume, regions, regionCount);
                LogStopwatch("Build convex region preview mesh");
            }

            // After splitting regions, see if some can be recombined in different ways to reduce total region count.
            stopwatch.Restart();
            IEnumerator enum3 = CombineRegionsWherePossible(voxelCounts, regions, regionCountIncr, volume);
            while (enum3.MoveNext()) yield return null;
            regionCount = regionCountIncr.Value;
            
            // Sanity check to make sure stuff's working right.
            EnsureAllRegionsAreContiguous(voxelCounts, regions);
            LogStopwatch("Combine regions");

            // Build preview mesh that shows the voxels of each combined region as a different color.
            if (volume.VisualizationMode == NavVolumeVisualizationMode.CombinedRegions) {
                stopwatch.Restart();
                BuildRegionIDPreviewMesh(volume, regions, regionCount);
                LogStopwatch("Build combined region preview mesh");
            }

            // Use Marching Cubes to convert voxel regions into meshes.
            stopwatch.Restart();
            MultiRegionMeshInfo meshInfo = MultiRegionMeshInfo.CreateEmptyInfo();
            IEnumerator enum4 = TriangulateRegions(voxelCounts, regions, regionCount, meshInfo, volume);
            while (enum4.MoveNext()) yield return null;
            LogStopwatch("Triangulate regions (marching cubes)");

            // Build preview mesh that shows region meshes as triangles and edges.
            if (volume.VisualizationMode == NavVolumeVisualizationMode.RegionTriangulation) {
                stopwatch.Restart();
                BuildTriangulationPreviewMesh(volume, meshInfo.Vertices, meshInfo.RegionTriangleLists);
                LogStopwatch("Build triangulated preview mesh");
            }

            // Simplify region meshes by removing vertices that don't add any detail.
            stopwatch.Restart();
            IEnumerator enum5 = DecimateRegions(meshInfo, volume);
            while (enum5.MoveNext()) yield return null;
            foreach (var triangleList in meshInfo.RegionTriangleLists) {
                triangleList.RemoveAll(i => i < 0);
            }
            LogStopwatch("Decimate regions");

            // Build preview mesh that shows simplified region meshes.
            if (volume.VisualizationMode == NavVolumeVisualizationMode.Decimation) {
                stopwatch.Restart();
                BuildTriangulationPreviewMesh(volume, meshInfo.Vertices, meshInfo.RegionTriangleLists);
                LogStopwatch("Build decimated preview mesh");
            }

            // Convert data in temporary MultiRegionMeshInfo format to the actual NavVolumeData.
            stopwatch.Restart();
            IEnumerator enum6 = PopulateData(meshInfo, volume);
            while (enum6.MoveNext()) yield return null;
            LogStopwatch("Update serialized data");

            // Clear out the preview mesh - it will be generated from NavVolumeEditor.
            if (volume.VisualizationMode == NavVolumeVisualizationMode.Final ||
                volume.VisualizationMode == NavVolumeVisualizationMode.Blocking) {
                volume.EditorOnlyPreviewMesh = null;
            }

            // Remove bake progress and coroutine tracker.
            BakeProgress.Remove(volume);
            BakeCoroutines.Remove(volume);
            BakeProgressUpdated?.Invoke(volume);

            // Log time taken for each step for optimization purposes.
            Debug.Log($"Time taken to bake volume:{Environment.NewLine}{sb}");
        }

        /// <summary>
        /// Cancel an actively baking volume and clear out its data.
        /// </summary>
        /// <param name="volume">The volume being baked.</param>
        public static void CancelBake(NavVolume volume) {
            if (!BakeCoroutines.TryGetValue(volume, out EditorCoroutine crt)) return;
            
            EditorCoroutineUtility.StopCoroutine(crt);
            BakeCoroutines.Remove(volume);
            BakeProgress.Remove(volume);
            BakeProgressUpdated?.Invoke(volume);
            volume.EditorOnlyPreviewMesh = null;
        }

        /// <summary>
        /// Clear out the baked data of a volume (does not destroy or un-assign the actual data object).
        /// </summary>
        /// <param name="volume"></param>
        public static void ClearData(NavVolume volume) {
            Undo.RecordObject(volume.Data, "Clear HyperNav Data");
            volume.Data.Clear();
            volume.EditorOnlyPreviewMesh = null;
            EditorUtility.SetDirty(volume.Data);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region General Helper Methods
        
        // Used to run an action as a task on a new thread if multithreading is enabled;
        // otherwise will just run the action and return.
        private static Task RunTask(NavVolume volume, Action task) {
            if (volume.UseMultithreading) {
                return Task.Run(task);
            } else {
                task();
                return Task.CompletedTask;
            }
        }

        // Used to run an action as a task on a new thread if multithreading is enabled;
        // otherwise will just run the action and return.
        private static Task<T> RunTask<T>(NavVolume volume, Func<T> task) {
            if (volume.UseMultithreading) {
                return Task.Run(task);
            } else {
                return Task.FromResult(task());
            }
        }

        #endregion

        #region Serialized Data Population
        
        // Populate NavVolumeData from MultiRegionMeshInfo.
        private static IEnumerator PopulateData(MultiRegionMeshInfo meshInfo, NavVolume volume) {
            // Create data structures for NavVolumeData.
            List<int> blockingIndices = new List<int>();
            List<List<int>> triLists = new List<List<int>>();
            List<Vector3> vertices = new List<Vector3>();
            Dictionary<Vector3, int> vertexIndices = new Dictionary<Vector3, int>();

            BakeProgress[volume] = new NavVolumeBakeProgress {
                Operation = OpNameGeneratingData,
                Progress = 0,
            };
            BakeProgressUpdated?.Invoke(volume);
            yield return null;

            // Subtract one from region count, as region 0 in the MultiRegionMeshInfo is the blocking triangles.
            int regionCount = meshInfo.RegionTriangleLists.Count - 1;

            // Go through all vertices and rebuild index lists.
            // This ensures that any vertices that are no longer used are not included in the data.
            for (int i = -1; i < regionCount; i++) {
                List<int> oldTriList = meshInfo.RegionTriangleLists[i + 1];
                List<int> newTriList;

                if (i == -1) {
                    newTriList = blockingIndices;
                } else {
                    newTriList = new List<int>();
                    triLists.Add(newTriList);
                }
                
                // Only add vertices that are still used by triangles.
                for (int j = 0; j < oldTriList.Count; j++) {
                    Vector3 vertex = meshInfo.Vertices[oldTriList[j]];
                    if (!vertexIndices.TryGetValue(vertex, out int vertexIndex)) {
                        vertexIndex = vertices.Count;
                        vertices.Add(vertex);
                        vertexIndices[vertex] = vertexIndex;
                    }
                    
                    newTriList.Add(vertexIndex);
                }
            }

            NavRegionData[] regions = new NavRegionData[regionCount];
            
            // calculate the bounds for each region.
            Bounds[] regionBounds = new Bounds[regionCount];
            for (int regionIndex = 0; regionIndex < regionCount; regionIndex++) {
                List<int> regionIndices = triLists[regionIndex];
                
                bool hasSetBounds = false;
                
                for (int i = 0; i < regionIndices.Count; i++) {
                    // First vertex of each region creates bounds, remaining vertices expand them.
                    Vector3 pos = vertices[regionIndices[i]];
                    if (hasSetBounds) {
                        regionBounds[regionIndex].Encapsulate(pos);
                    } else {
                        regionBounds[regionIndex] = new Bounds(pos, Vector3.zero);
                        hasSetBounds = true;
                    }
                }
            }

            // Need to determine which regions each vertex, edge, and triangle belongs to.
            // When one feature is shared by multiple regions, a connection can be created.
            Dictionary<int, HashSet<int>> vertexRegionMembership = new Dictionary<int, HashSet<int>>();
            Dictionary<Edge, HashSet<int>> edgeRegionMembership = new Dictionary<Edge, HashSet<int>>();
            Dictionary<Triangle, HashSet<int>> triangleRegionMembership =
                new Dictionary<Triangle, HashSet<int>>();

            // Helper function to record that a feature is used by a region.
            void AddFeatureToRegion<T>(in T feature, int region, Dictionary<T, HashSet<int>> dict) {
                if (!dict.TryGetValue(feature, out HashSet<int> regionSet)) {
                    regionSet = new HashSet<int>();
                    dict[feature] = regionSet;
                }

                regionSet.Add(region);
            }

            // Loop through all features of all regions and add them to the lists.
            for (int i = 0; i < regionCount; i++) {
                List<int> regionIndices = triLists[i];
                int triCount = regionIndices.Count / 3;
                for (int triIndex = 0; triIndex < triCount; triIndex++) {
                    int triStart = triIndex * 3;

                    int v1 = regionIndices[triStart + 0];
                    int v2 = regionIndices[triStart + 1];
                    int v3 = regionIndices[triStart + 2];

                    Edge e1 = new Edge(v1, v2);
                    Edge e2 = new Edge(v2, v3);
                    Edge e3 = new Edge(v3, v1);

                    Triangle tri = new Triangle(v1, v2, v3);

                    AddFeatureToRegion(v1, i, vertexRegionMembership);
                    AddFeatureToRegion(v2, i, vertexRegionMembership);
                    AddFeatureToRegion(v3, i, vertexRegionMembership);
                    
                    AddFeatureToRegion(e1, i, edgeRegionMembership);
                    AddFeatureToRegion(e2, i, edgeRegionMembership);
                    AddFeatureToRegion(e3, i, edgeRegionMembership);
                    
                    AddFeatureToRegion(tri, i, triangleRegionMembership);
                }
                BakeProgress[volume] = new NavVolumeBakeProgress {
                    Operation = OpNameGeneratingData,
                    Progress = (i / (float)regionCount) * 0.33f,
                };
                BakeProgressUpdated?.Invoke(volume);
                yield return null;
            }

            // Now that the regions that each feature belongs to are recorded,
            // it is possible to determine the internal connections of each region.
            for (int regionIndex = 0; regionIndex < regionCount; regionIndex++) {
                // Create a dictionary that maps the index of another region to the features shared with that region.
                // This is necessary so that only one external connection is created per neighboring region.
                var sharedFeatures =
                    new Dictionary<int, (List<int> sharedVertices, List<Edge> sharedEdges, List<Triangle>
                        sharedTriangles)>();
                
                // Will also calculate the bound planes in this loop.
                List<NavRegionBoundPlane> boundPlanes = new List<NavRegionBoundPlane>();

                // Helper function to add the regions that share a vertex to the sharedFeatures dict.
                void AddVertexSharedRegions(int vertex) {
                    // Loop through all the regions that share the vertex.
                    foreach (int otherRegion in vertexRegionMembership[vertex]) {
                        if (otherRegion == regionIndex) continue;

                        // If no shared features with this region have been found yet,
                        // or no shared vertices have been found, create vertices list and update sharedFeatures.
                        sharedFeatures.TryGetValue(otherRegion, out var features);
                        if (features.sharedVertices == null) {
                            features.sharedVertices = new List<int>();
                            sharedFeatures[otherRegion] = features;
                        }

                        // If this vertex hasn't been found to be shared, add it.
                        if (!features.sharedVertices.Contains(vertex)) {
                            features.sharedVertices.Add(vertex);
                        }
                    }
                }

                // Helper function to add the regions that share an edge to the sharedFeatures dict.
                void AddEdgeSharedRegions(Edge edge) {
                    // Loop through all the regions that share the edge.
                    foreach (int otherRegion in edgeRegionMembership[edge]) {
                        if (otherRegion == regionIndex) continue;
                        
                        // If no shared features with this region have been found yet,
                        // or no shared edges have been found, create edges list and update sharedFeatures.
                        sharedFeatures.TryGetValue(otherRegion, out var features);
                        if (features.sharedEdges == null) {
                            features.sharedEdges = new List<Edge>();
                            sharedFeatures[otherRegion] = features;
                        }

                        // If this edge hasn't been found to be shared, add it.
                        if (!features.sharedEdges.Contains(edge)) {
                            features.sharedEdges.Add(edge);
                        }
                    }
                }

                // Helper function to add the regions that share a triangle to the sharedFeatures dict.
                void AddTriangleSharedRegions(Triangle triangle) {
                    // Loop through all the regions that share the triangle.
                    foreach (int otherRegion in triangleRegionMembership[triangle]) {
                        if (otherRegion == regionIndex) continue;

                        // If no shared features with this region have been found yet,
                        // or no shared triangles have been found, create triangles list and update sharedFeatures.
                        sharedFeatures.TryGetValue(otherRegion, out var features);
                        if (features.sharedTriangles == null) {
                            features.sharedTriangles = new List<Triangle>();
                            sharedFeatures[otherRegion] = features;
                        }

                        // If this triangle hasn't been found to be shared, add it.
                        if (!features.sharedTriangles.Contains(triangle)) {
                            features.sharedTriangles.Add(triangle);
                        }
                    }
                }

                List<int> regionIndices = triLists[regionIndex];

                Vector3 center = regionBounds[regionIndex].center;

                // Loop through the triangles of the region.
                int triCount = regionIndices.Count / 3;
                for (int triIndex = 0; triIndex < triCount; triIndex++) {
                    int triStart = triIndex * 3;

                    int v1 = regionIndices[triStart + 0];
                    int v2 = regionIndices[triStart + 1];
                    int v3 = regionIndices[triStart + 2];

                    Edge e1 = new Edge(v1, v2);
                    Edge e2 = new Edge(v2, v3);
                    Edge e3 = new Edge(v3, v1);

                    Triangle tri = new Triangle(v1, v2, v3);
                    
                    // Add shared features.
                    AddVertexSharedRegions(v1);
                    AddVertexSharedRegions(v2);
                    AddVertexSharedRegions(v3);
                    AddEdgeSharedRegions(e1);
                    AddEdgeSharedRegions(e2);
                    AddEdgeSharedRegions(e3);
                    AddTriangleSharedRegions(tri);

                    // Calculate bound plane for the triangle.
                    Vector3 v2Pos = vertices[v2];
                    Vector3 normal = Vector3.Cross(vertices[v1] - v2Pos, vertices[v3] - v2Pos).normalized;
                    Vector3 toCenter = center - v2Pos;

                    // Triangle vertices are in no particular order, so ensure normal is pointing correct direction.
                    if (Vector3.Dot(normal, toCenter) > 0) {
                        normal *= -1;
                    }

                    // If no bound plane with this normal has been added, add it.
                    // Only one plane per normal is needed in a convex shape, which regions are.
                    if (!boundPlanes.Any(plane => Vector3.Dot(plane.Normal, normal) > 0.99999)) {
                        boundPlanes.Add(NavRegionBoundPlane.Create(normal, v2));
                    }
                }

                // Create connections based on sharedFeatures.
                NavInternalLinkData[] connections =
                    new NavInternalLinkData[sharedFeatures.Count];

                int index = 0;
                foreach (var pair in sharedFeatures) {
                    connections[index++] = NavInternalLinkData.Create(
                        pair.Key,
                        pair.Value.sharedVertices?.ToArray() ?? Array.Empty<int>(),
                        pair.Value.sharedEdges?.ToArray() ?? Array.Empty<Edge>(),
                        pair.Value.sharedTriangles?.ToArray() ?? Array.Empty<Triangle>());
                }
                
                // Sort connections based on the ID of the connected region.
                // I don't remember why this was needed.
                Array.Sort(connections, (data1, data2) => data1.ConnectedRegionID - data2.ConnectedRegionID);

                // Create region data.
                regions[regionIndex] =
                    NavRegionData.Create(regionIndex, triLists[regionIndex].ToArray(), regionBounds[regionIndex],
                                                    connections, boundPlanes.ToArray());
                
                BakeProgress[volume] = new NavVolumeBakeProgress {
                    Operation = OpNameGeneratingData,
                    Progress = 0.33f + (regionIndex / (float)regionCount) * 0.66f,
                };
                BakeProgressUpdated?.Invoke(volume);
                yield return null;
            }
            
            Undo.RecordObject(volume.Data, "Bake HyperNav Data");
            volume.Data.Populate(vertices.ToArray(), regions, blockingIndices.ToArray());
            EditorUtility.SetDirty(volume.Data);
            AssetDatabase.SaveAssets();
        }

        #endregion

        #region Mesh Generation And Processing

        // Remove vertices that don't contribute to the actual shape of the regions.
        // If a vertex contributes to the shape of one region but not another, it must not be removed from either,
        // because it will form a shared vertex on the resulting connection.
        private static IEnumerator DecimateRegions(MultiRegionMeshInfo meshInfo, NavVolume volume) {
            const int chunkSize = 500;
            
            // For each vertex of the mesh, determine if it is needed or not.
            for (int i = 0; i < meshInfo.Vertices.Count; i++) {
                int sharpEdgeCount = 0;
                List<int> connections = meshInfo.VertexConnections[i];
                int firstSharpEdge = -1;
                int secondSharpEdge = -1;
                
                // First, determine if the vertex lies on at least one sharp edge.
                // If there is one sharp edge, there should be at least one more.
                // When that is the case, both sharp edges will need to be known later on.
                for (int j = 0; j < connections.Count; j++) {
                    int vertex2Index = connections[j];

                    if (IsEdgeAngled(meshInfo, i, vertex2Index, out _, out _)) {

                        if (sharpEdgeCount == 0) {
                            firstSharpEdge = vertex2Index;
                        } else if (sharpEdgeCount == 1) {
                            secondSharpEdge = vertex2Index;
                        }
                        
                        sharpEdgeCount++;
                    }
                }
                
                // A vertex can be removed if it has no sharp edges or exactly two.
                // Any other number of sharp edges, and it is on a corner and is needed.
                if (sharpEdgeCount != 0 && sharpEdgeCount != 2) continue;

                foreach (int region in meshInfo.VertexRegionMembership[i]) {
                    if (sharpEdgeCount == 0 ||
                        !AreVerticesConnectedInRegion(meshInfo, i, firstSharpEdge, region) ||
                        !AreVerticesConnectedInRegion(meshInfo, i, secondSharpEdge, region)) {
                        // Case 1 - no sharp edges: just remove all triangles that connect to the vertex.
                        // Build an edge loop that contains all the newly loose edges.
                        // Then fill in triangles using the ear clipping algorithm.
                        int firstConnectedVertex = meshInfo.VertexConnections[i]
                                                           .First(v2 => AreVerticesConnectedInRegion(
                                                                      meshInfo, i, v2, region));
                    
                        if (!RemoveVertexTriangles(volume.transform, meshInfo, i, 
                                                   firstConnectedVertex, -1, region)) yield break;
                    } else {
                        // Case 2 - 2 sharp edges: same as case 1, but ensure that the two sharp edges
                        // remain as edges in the newly created triangles. We do this by running ear clipping
                        // twice, once for each side of these edges.
                        
                        if (!RemoveVertexTriangles(volume.transform, meshInfo, i, 
                                                   firstSharpEdge, secondSharpEdge, region)) yield break;
                        if (!RemoveVertexTriangles(volume.transform, meshInfo, i, 
                                                   secondSharpEdge, firstSharpEdge, region)) yield break;
                    }
                }

                // Remove vertex connection data for removed vertex.
                foreach (int connectedVertex in meshInfo.VertexConnections[i]) {
                    meshInfo.VertexConnections[connectedVertex].Remove(i);
                }

                meshInfo.VertexConnections[i] = null;
                meshInfo.VertexRegionMembership[i] = null;

                // Every X vertices, update progress and yield until the next frame.
                if (i % chunkSize == 0) {
                    BakeProgress[volume] = new NavVolumeBakeProgress {
                        Operation = OpNameSimplifyingRegions,
                        Progress = i / (float)meshInfo.Vertices.Count,
                    };
                    BakeProgressUpdated?.Invoke(volume);
                    yield return null;
                }
            }
        }

        // Check whether the given region contains a triangle containing the two given vertices.
        private static bool AreVerticesConnectedInRegion(MultiRegionMeshInfo meshInfo, int vertex1, int vertex2, int region) {
            // Only eligible third vertices of the triangle are those that are connected to both vertex1 and vertex2.
            foreach (int vertex3 in meshInfo.VertexConnections[vertex1].Intersect(meshInfo.VertexConnections[vertex2])) {
                Triangle triangle = new Triangle(vertex1, vertex2, vertex3);
                if (meshInfo.TriangleIndicesPerRegion.TryGetValue(triangle, out Dictionary<int, int> triIndices) &&
                    triIndices.ContainsKey(region)) return true;
            }

            return false;
        }

        // Remove the triangles in the given region that include the given vertex.
        // This creates a hole, which is then filled using the Ear Clipping algorithm.
        // The result is that the mesh no longer has any triangles containing that vertex,
        // but the overall shape is unchanged.
        private static bool RemoveVertexTriangles(Transform transform, MultiRegionMeshInfo meshInfo, int vertexIndex,
                                                  int firstVertex, int lastVertex, int region) {
            
            // Delete the triangles and get a list of the hole vertices.
            List<int> vertexOrder = RemoveTrianglesAndGetEdgeRing(transform, meshInfo, vertexIndex, firstVertex, 
                                                                  lastVertex, region);
            if (vertexOrder == null) return false;
            if (vertexOrder.Count < 3) {
                DrawDebugVertexOrder(meshInfo, transform, vertexIndex, vertexOrder);
                Debug.LogError($"RemoveTrianglesAndGetEdgeRing returned vertex order with count < 3 when removing {vertexIndex}.");
                return false;
            }

            Vector3 v = meshInfo.Vertices[vertexIndex];
            Vector3 v1 = meshInfo.Vertices[vertexOrder[0]];
            Vector3 v2 = meshInfo.Vertices[vertexOrder[1]];

            // Get the normal of the new triangles we are creating.
            Vector3 normal = Vector3.Cross(v1 - v, v2 - v).normalized;

            // Ear clipping algorithm:
            // While there are enough points to make a triangle, choose the sharpest point and make a triangle
            // with that point and its neighbors, then remove that point from the list and repeat.
            // Avoid getting into a situation where the only triangles we can create are slivers.
            HashSet<int> flatVertices = new HashSet<int>();
            HashSet<int> concaveVertices = new HashSet<int>();
            while (vertexOrder.Count >= 3) {

                // Find any vertices which, if clipped, would create a sliver triangle.
                // These are any vertices with an angle close to 180 degrees.
                flatVertices.Clear();
                concaveVertices.Clear();
                for (int j = 0; j < vertexOrder.Count; j++) {
                    GetCurrentAndNeighboringVertexIndices(vertexOrder, j, out int curVertexIndex,
                                                          out int prevVertexIndex, out int nextVertexIndex);

                    float dot = GetDotAndCrossProduct(meshInfo, curVertexIndex, prevVertexIndex, nextVertexIndex, out Vector3 cross);
                    if (dot < -0.99999) {
                        flatVertices.Add(curVertexIndex);
                        continue;
                    }

                    float crossDot = Vector3.Dot(cross, normal);
                    if (crossDot < 0) {
                        concaveVertices.Add(curVertexIndex);
                    }
                }
                
                // Give priority to vertices with a flat neighbor.
                // Only check vertices without a flat neighbor if none of those can be clipped.
                // This ensures that we get rid of vertices with flat neighbors as soon as possible,
                // to prevent being left with sliver triangles.
                int bestVertex = FindVertexToClip(meshInfo, vertexOrder, flatVertices, true,
                                                  concaveVertices, normal);

                if (bestVertex == -1) {
                    bestVertex = FindVertexToClip(meshInfo, vertexOrder, flatVertices, false,
                                                  concaveVertices, normal);
                }

                if (bestVertex == -1) {
                    Debug.LogError($"Did not find vertex to clip while removing {vertexIndex}.");
                    Debug.LogError($"Edge Ring: [{string.Join(", ", vertexOrder)}]");
                    DrawDebugVertexOrder(meshInfo, transform, vertexIndex, vertexOrder);
                    return false;
                }

                // Convert index in vertex order list to index in all vertices.
                GetCurrentAndNeighboringVertexIndices(vertexOrder, bestVertex, out int newV1,
                                                      out int newV2, out int newV3);

                // Create new triangle and initialize it in the mesh data.
                Triangle newTriangle = new Triangle(newV1, newV2, newV3);
                if (!meshInfo.TriangleIndicesPerRegion.TryGetValue(
                        newTriangle, out Dictionary<int, int> triangleIndices)) {
                    triangleIndices = new Dictionary<int, int>();
                    meshInfo.TriangleIndicesPerRegion.Add(newTriangle, triangleIndices);
                }

                // Add triangle's index to cached values.
                triangleIndices[region] = meshInfo.RegionTriangleLists[region].Count;

                // Add indices so triangle is part of mesh.
                meshInfo.RegionTriangleLists[region].Add(newV1);
                meshInfo.RegionTriangleLists[region].Add(newV2);
                meshInfo.RegionTriangleLists[region].Add(newV3);

                // Add new vertex connections.
                ConnectVertices(meshInfo, newV1, newV2);
                ConnectVertices(meshInfo, newV2, newV3);
                ConnectVertices(meshInfo, newV3, newV1);

                vertexOrder.RemoveAt(bestVertex);
            }

            return true;
        }

        private static int FindVertexToClip(in MultiRegionMeshInfo meshInfo, List<int> vertexOrder,
                                            HashSet<int> flatVertices, bool checkFlatNeighbors,
                                            HashSet<int> concaveVertices, Vector3 normal) {
            // Find the angle with the sharpest point.
            // Do not consider any nearly-flat angles.
            int bestVertex = -1;
            float bestDot = -1;
            for (int j = 0; j < vertexOrder.Count; j++) {
                GetCurrentAndNeighboringVertexIndices(vertexOrder, j, out int curVertexIndex,
                                                      out int prevVertexIndex, out int nextVertexIndex);

                if (VertexHasFlatNeighbor(flatVertices, prevVertexIndex, nextVertexIndex) != checkFlatNeighbors) {
                    continue;
                }

                if (!CanClipVertex(meshInfo, flatVertices, concaveVertices, normal,
                                   prevVertexIndex, curVertexIndex, nextVertexIndex)) {
                    continue;
                }

                // Want to clip the vertex with the highest dot product (the sharpest angle).
                float dot = GetDotAndCrossProduct(meshInfo, curVertexIndex, prevVertexIndex, nextVertexIndex, out _);
                if (dot > bestDot) {
                    bestVertex = j;
                    bestDot = dot;
                }
            }

            return bestVertex;
        }

        private static bool VertexHasFlatNeighbor(HashSet<int> flatVertices, int prevVertexIndex, int nextVertexIndex) {
            return flatVertices.Contains(prevVertexIndex) || flatVertices.Contains(nextVertexIndex);
        }

        private static bool CanClipVertex(in MultiRegionMeshInfo meshInfo, HashSet<int> flatVertices, 
                                          HashSet<int> concaveVertices, Vector3 normal, int prevVertexIndex, 
                                          int curVertexIndex,int nextVertexIndex) {

            Vector3 vCur = meshInfo.Vertices[curVertexIndex];
            Vector3 vNext = meshInfo.Vertices[nextVertexIndex];
            Vector3 vPrev = meshInfo.Vertices[prevVertexIndex];

            if (flatVertices.Contains(curVertexIndex)) return false;

            // Concave vertices may be inside the clipped triangle, making it invalid
            if (concaveVertices.Count > 0) {
                if (concaveVertices.Contains(curVertexIndex)) return false;

                foreach (int concaveVertexIndex in concaveVertices) {
                    if (concaveVertexIndex == prevVertexIndex || concaveVertexIndex == nextVertexIndex) continue;
                    Vector3 concaveVertex = meshInfo.Vertices[concaveVertexIndex];

                    if (MathUtility.IsPointInsideBound(vNext, vPrev, normal, concaveVertex) &&
                        MathUtility.IsPointInsideBound(vPrev, vCur, normal, concaveVertex) &&
                        MathUtility.IsPointInsideBound(vCur, vNext, normal, concaveVertex)) {
                        return false;
                    }
                }
            }

            return true;
        }

        // Remove the triangles connecting to a given vertex and return a list of vertices that make up the new hole.
        private static List<int> RemoveTrianglesAndGetEdgeRing(Transform transform, MultiRegionMeshInfo meshInfo,
                                                               int vertexIndex, int firstVertex, int lastVertex, int region) {
            int currentConnectedVertex = firstVertex;
            List<int> vertexOrder = new List<int>();
            while (true) {
                vertexOrder.Add(currentConnectedVertex);
                if (currentConnectedVertex == lastVertex) break;
                
                // Search for a triangle that contains both the current edge ring vertex and the removing vertex.
                // Use the intersection of both vertices' connections to find candidates for the third vertex
                // in this triangle.
                List<int> sharedVertices = meshInfo.VertexConnections[vertexIndex]
                                                   .Intersect(meshInfo.VertexConnections[currentConnectedVertex])
                                                   .ToList();

                bool foundNext = false;
                
                // Loop through all candidates to find one that will work to make the next triangle.
                foreach (int sharedVertex in sharedVertices) {
                    // Cannot consider any vertices already added to the vertex order.
                    if (sharedVertex != firstVertex && vertexOrder.Contains(sharedVertex)) continue;
                    Triangle triangle = new Triangle(vertexIndex, currentConnectedVertex, sharedVertex);
                    // Vertex must be part of a triangle in the current region.
                    if (!meshInfo.TriangleIndicesPerRegion.TryGetValue(
                            triangle, out Dictionary<int, int> triangleRegions) ||
                        !triangleRegions.TryGetValue(region, out int triangleStartIndex)) continue;

                    // Found a valid triangle. Remove it and finish.
                    triangleRegions.Remove(region);
                    
                    // Deleting elements here would invalidate other indices, so just set them to -1 and delete later.
                    meshInfo.RegionTriangleLists[region][triangleStartIndex + 0] = -1;
                    meshInfo.RegionTriangleLists[region][triangleStartIndex + 1] = -1;
                    meshInfo.RegionTriangleLists[region][triangleStartIndex + 2] = -1;
                    
                    currentConnectedVertex = sharedVertex;
                    foundNext = true;
                    break;
                }

                if (!foundNext) {
                    DrawDebugVertexOrder(meshInfo, transform, vertexIndex, vertexOrder);
                    Debug.LogError($"Error finding next edge, vertex = {vertexIndex}, region = {region}, vertexOrder = [{string.Join(", ", vertexOrder)}]");
                    return null;
                }

                if (currentConnectedVertex == firstVertex) break;
            }

            return vertexOrder;
        }

        // Used for debugging to draw an edge ring when removing a vertex fails.
        private static void DrawDebugVertexOrder(MultiRegionMeshInfo meshInfo, Transform transform, int vertexIndex, List<int> vertexOrder) {
            if (vertexOrder.Count <= 0) return;
            Debug.DrawLine(transform.TransformPoint(meshInfo.Vertices[vertexIndex]),
                           transform.TransformPoint(meshInfo.Vertices[vertexOrder[0]]),
                           Color.green, 50);
            for (int i = 0; i < vertexOrder.Count - 1; i++) {
                Debug.DrawLine(transform.TransformPoint(meshInfo.Vertices[vertexOrder[i]]),
                               transform.TransformPoint(meshInfo.Vertices[vertexOrder[i + 1]]),
                               Color.green, 50);
            }
        }

        // Helper to get dot and cross product of a vertex in an edge ring.
        // Specifically, gets those products of the two edges the vertex is part of.
        private static float GetDotAndCrossProduct(MultiRegionMeshInfo meshInfo, int curVertexIndex, int prevVertexIndex,
                                           int nextVertexIndex, out Vector3 crossProduct) {
            Vector3 curVertex = meshInfo.Vertices[curVertexIndex];
            Vector3 prevVertex = meshInfo.Vertices[prevVertexIndex];
            Vector3 nextVertex = meshInfo.Vertices[nextVertexIndex];

            Vector3 v1 = Vector3.Normalize(nextVertex - curVertex);
            Vector3 v2 = Vector3.Normalize(prevVertex - curVertex);

            float dot = Vector3.Dot(v1, v2);
            crossProduct = Vector3.Cross(v1, v2);
            return dot;
        }

        // Get the indices of the current, previous, and next vertices in the vertex order.
        private static void GetCurrentAndNeighboringVertexIndices(List<int> vertexOrder, int index, out int curVertexIndex, 
                                                                  out int prevVertexIndex, out int nextVertexIndex) {
            curVertexIndex = vertexOrder[index];
            prevVertexIndex = vertexOrder[(index + vertexOrder.Count - 1) % vertexOrder.Count];
            nextVertexIndex = vertexOrder[(index + 1) % vertexOrder.Count];
        }

        // Mark the two vertices as connected if they are not already.
        private static void ConnectVertices(MultiRegionMeshInfo meshInfo, int vertex1Index, int vertex2Index) {
            if (!meshInfo.VertexConnections[vertex1Index].Contains(vertex2Index)) {
                meshInfo.VertexConnections[vertex1Index].Add(vertex2Index);
            }
            
            if (!meshInfo.VertexConnections[vertex2Index].Contains(vertex1Index)) {
                meshInfo.VertexConnections[vertex2Index].Add(vertex1Index);
            }
        }

        // Check if the given edge connects two triangles at different angles
        private static bool IsEdgeAngled(in MultiRegionMeshInfo meshInfo, int vertex1Index, int vertex2Index,
                                         out Vector3 o1, out Vector3 o2) {
            Vector3 vertex1 = meshInfo.Vertices[vertex1Index];
            Vector3 vertex2 = meshInfo.Vertices[vertex2Index];

            List<int> connections1 = meshInfo.VertexConnections[vertex1Index];
            List<int> connections2 = meshInfo.VertexConnections[vertex2Index];

            List<int> sharedVertices = connections1.Intersect(connections2).ToList();

            // Search for first triangle.
            for (int k = 0; k < sharedVertices.Count; k++) {
                int otherIndex1 = sharedVertices[k];
                if (!meshInfo.TriangleIndicesPerRegion.ContainsKey(new Triangle(vertex1Index, vertex2Index, otherIndex1))) {
                    continue;
                }
                
                // Search for second triangle.
                for (int l = k + 1; l < sharedVertices.Count; l++) {
                    int otherIndex2 = sharedVertices[l];
                    if (!meshInfo.TriangleIndicesPerRegion.ContainsKey(new Triangle(vertex1Index, vertex2Index, otherIndex2))) {
                        continue;
                    }

                    if (otherIndex2 == otherIndex1 || 
                        otherIndex2 == vertex1Index || 
                        otherIndex2 == vertex2Index || 
                        otherIndex1 == vertex1Index || 
                        otherIndex1 == vertex2Index || 
                        vertex1Index == vertex2Index) {
                        
                        Debug.LogError("Duplicate vertex found.");
                    }
                    
                    Vector3 otherVertex1 = meshInfo.Vertices[otherIndex1];
                    Vector3 otherVertex2 = meshInfo.Vertices[otherIndex2];

                    // Calculate normals and compare dot product to determine if the edge is angled.
                    Vector3 normal1 = Vector3.Cross(vertex2 - vertex1, otherVertex1 - vertex1).normalized;
                    Vector3 normal2 = Vector3.Cross(otherVertex2 - vertex1, vertex2 - vertex1).normalized;

                    float dot = Mathf.Abs(Vector3.Dot(normal1, normal2));
                    if (dot < 0.95f) {
                        o1 = otherVertex1;
                        o2 = otherVertex2;
                        return true;
                    }
                }
            }

            o1 = default;
            o2 = default;
            return false;
        }

        // Convert voxel representations of regions to triangle meshes using the Marching Cubes algorithm.
        private static IEnumerator TriangulateRegions(Vector3Int voxelCounts, Fast3DArray regions, int regionCount,
                                                      MultiRegionMeshInfo mesh, NavVolume volume) {
            Dictionary<Vector3, int> vertexIndices = new Dictionary<Vector3, int>();
            Dictionary<Vector3, int> regionZeroVertexIndices = new Dictionary<Vector3, int>();
            
            // Regions are mostly independent, but they can share vertices.
            for (int regionIndex = 0; regionIndex < regionCount; regionIndex++) {
                List<int> triList = null;

                Dictionary<Vector3, int> regionVertexIndices =
                    regionIndex == 0 ? regionZeroVertexIndices : vertexIndices;

                // Loop through all voxels and add the marching cube sections.
                for (int x = -1; x < voxelCounts.x; x++) {
                    for (int y = -1; y < voxelCounts.y; y++) {
                        for (int z = -1; z < voxelCounts.z; z++) {
                            Vector3Int current = new Vector3Int(x, y, z);
                            Vector3 currentV = current + Vector3.one * 0.5f;
                            
                            // Find index in marching cubes tables.
                            byte caseIndex = GetMarchingCubesIndex(voxelCounts, regions, regionIndex, x, y, z);

                            if (triList == null) {
                                triList = new List<int>();
                                mesh.RegionTriangleLists.Add(triList);
                            }

                            // Use Marching Cubes tables to determine edge indices to connect.
                            byte[] edgeIndices = MarchingCubesTables.TriTable[caseIndex];
                            int triCount = edgeIndices.Length / 3;

                            // Add all triangles to mesh.
                            for (int triIndex = 0; triIndex < triCount; triIndex++) {
                                int triStart = triIndex * 3;

                                int crossPoint1 = -1;
                                int crossPoint2 = -1;
                                int nonCrossPoint = -1;
                                
                                // Loop through edges of triangle to create.
                                // If an edge passes through the center, split it and create two triangles.
                                // This avoids most cases where opposing quads from bordering regions have the opposite
                                // triangulation, and thus have no shared faces.
                                for (int i = 0; i < 3; i++) {
                                    int n = (i + 1) % 3;
                                    int o = (i + 2) % 3;

                                    if (MarchingCubesTables.AcrossCenterMidpoints[edgeIndices[triStart + i]] != edgeIndices[triStart + n])
                                        continue;
                                    
                                    crossPoint1 = triStart + i;
                                    crossPoint2 = triStart + n;
                                    nonCrossPoint = triStart + o;
                                    break;
                                }
                                
                                if (crossPoint1 == -1) {
                                    // Get vertices based on the edge index.
                                    Vector3 vertex1 = currentV + GetMarchingCubesVertex(mesh, edgeIndices, triStart + 0);
                                    Vector3 vertex2 = currentV + GetMarchingCubesVertex(mesh, edgeIndices, triStart + 1);
                                    Vector3 vertex3 = currentV + GetMarchingCubesVertex(mesh, edgeIndices, triStart + 2);

                                    // Add vertices to the mesh.
                                    int vertex1Index = AddVertex(mesh, vertex1, volume, regionVertexIndices);
                                    int vertex2Index = AddVertex(mesh, vertex2, volume, regionVertexIndices);
                                    int vertex3Index = AddVertex(mesh, vertex3, volume, regionVertexIndices);
                                    
                                    // Create triangle.
                                    AddTriangle(mesh, triList, regionIndex, vertex1Index, vertex2Index, vertex3Index);
                                } else {
                                    // Get vertices based on the edge index, plus center point.
                                    Vector3 vertexCross1 = currentV + GetMarchingCubesVertex(mesh, edgeIndices, crossPoint1);
                                    Vector3 vertexCross2 = currentV + GetMarchingCubesVertex(mesh, edgeIndices, crossPoint2);
                                    Vector3 vertexNonCross = currentV + GetMarchingCubesVertex(mesh, edgeIndices, nonCrossPoint);
                                    Vector3 vertexCenter = currentV + Vector3.one * 0.5f;

                                    // Add vertices to the mesh.
                                    int vertexCross1Index = AddVertex(mesh, vertexCross1, volume, regionVertexIndices);
                                    int vertexCross2Index = AddVertex(mesh, vertexCross2, volume, regionVertexIndices);
                                    int vertexNonCrossIndex = AddVertex(mesh, vertexNonCross, volume, regionVertexIndices);
                                    int vertexCenterIndex = AddVertex(mesh, vertexCenter, volume, regionVertexIndices);

                                    // Create two triangles.
                                    AddTriangle(mesh, triList, regionIndex, vertexCross1Index,
                                                vertexCenterIndex, vertexNonCrossIndex);
                                    AddTriangle(mesh, triList, regionIndex, vertexCenterIndex,
                                        vertexCross2Index, vertexNonCrossIndex);
                                }
                            }
                        }
                    }
                }

                BakeProgress[volume] = new NavVolumeBakeProgress {
                    Operation = OpNameTriangulatingRegions,
                    Progress = (regionIndex + 1) / (float) regionCount,
                };
                BakeProgressUpdated?.Invoke(volume);
                yield return null;
            }
        }
        
        // Add a vertex to the mesh and initialize needed data structures.
        private static int AddVertex(MultiRegionMeshInfo mesh, Vector3 vertex, NavVolume volume,
                                     Dictionary<Vector3, int> vertexIndices) {
            if (!vertexIndices.TryGetValue(vertex, out int vertexIndex)) {
                vertexIndex = mesh.Vertices.Count;
                mesh.Vertices.Add(volume.Bounds.min + (vertex * volume.VoxelSize));
                mesh.VertexConnections.Add(new List<int>());
                mesh.VertexRegionMembership.Add(new List<int>());
                vertexIndices.Add(vertex, vertexIndex);
            }

            return vertexIndex;
        }

        // Add a triangle to a region, set up connected vertices
        private static void AddTriangle(MultiRegionMeshInfo mesh, List<int> triList, int region,
                                        int vertex1Index, int vertex2Index, int vertex3Index) {
            int firstIndex = triList.Count;
            
            triList.Add(vertex1Index);
            triList.Add(vertex2Index);
            triList.Add(vertex3Index);

            AddVertexToRegion(mesh, vertex1Index, region);
            AddVertexToRegion(mesh, vertex2Index, region);
            AddVertexToRegion(mesh, vertex3Index, region);
            
            ConnectVertices(mesh, vertex1Index, vertex2Index);
            ConnectVertices(mesh, vertex2Index, vertex3Index);
            ConnectVertices(mesh, vertex3Index, vertex1Index);
            
            Triangle triangle = new Triangle(vertex1Index, vertex2Index, vertex3Index);
            if (!mesh.TriangleIndicesPerRegion.TryGetValue(triangle, out Dictionary<int, int> triangleMeshes)) {
                triangleMeshes = new Dictionary<int, int>();
                mesh.TriangleIndicesPerRegion.Add(triangle, triangleMeshes);
            }
            
            triangleMeshes[region] = firstIndex;
        }

        // Register the given vertex as being used by the given region.
        public static void AddVertexToRegion(MultiRegionMeshInfo mesh, int vertex, int region) {
            if (!mesh.VertexRegionMembership[vertex].Contains(region)) {
                mesh.VertexRegionMembership[vertex].Add(region);
            }
        }

        #endregion

        #region Marching Cubes
        
        // Get index in MarchingCubesTables.TriTable for a given cube (8 voxels).
        // A voxel is considered "on" if it belongs to the given region.
        // x, y, z is the position of the min position of the cube.
        private static byte GetMarchingCubesIndex(Vector3Int voxelCounts, Fast3DArray regions, int regionID,
                                                  int x, int y, int z) {
            
            // Note: this code is ugly and fast, it was much slower as a loop using Vector math.
            // Read it and weep.
            
            byte caseIndex = 0;

            int sx = voxelCounts.x - 1;
            int sy = voxelCounts.y - 1;
            int sz = voxelCounts.z - 1;

            int nx = x + 1;
            int ny = y + 1;
            int nz = z + 1;

            bool xv = x >= 0;
            bool yv = y >= 0;
            bool zv = z >= 0;

            bool nxv = x < sx;
            bool nyv = y < sy;
            bool zbv = z < sz;

            if (yv) {
                if (xv && zv && regions[x, y, z] == regionID) caseIndex |= 1;
                if (nxv && zv && regions[nx, y, z] == regionID) caseIndex |= 2;
                if (nxv && zbv && regions[nx, y, nz] == regionID) caseIndex |= 4;
                if (xv && zbv && regions[x, y, nz] == regionID) caseIndex |= 8;
            }

            if (nyv) {
                if (xv && zv && regions[x, ny, z] == regionID) caseIndex |= 16;
                if (nxv && zv &&  regions[nx, ny, z] == regionID) caseIndex |= 32;
                if (nxv && zbv && regions[nx, ny, nz] == regionID) caseIndex |= 64;
                if (xv && zbv && regions[x, ny, nz] == regionID) caseIndex |= 128;
            }

            return caseIndex;
        }

        // Get index in MarchingCubesTables.TriTable for a given cube (8 voxels).
        // A voxel is considered "on" if it belongs to either of the given regions.
        // x, y, z is the position of the min position of the cube.
        private static byte GetMarchingCubesIndex(Vector3Int voxelCounts, Fast3DArray regions, int regionID1, int regionID2,
                                                  int x, int y, int z) {
            // Note: this code is ugly and fast, it was much slower as a loop
            
            byte caseIndex = 0;

            int sx = voxelCounts.x - 1;
            int sy = voxelCounts.y - 1;
            int sz = voxelCounts.z - 1;

            int nx = x + 1;
            int ny = y + 1;
            int nz = z + 1;

            bool xv = x >= 0;
            bool yv = y >= 0;
            bool zv = z >= 0;

            bool nxv = x < sx;
            bool nyv = y < sy;
            bool zbv = z < sz;

            if (yv) {
                if (xv && zv && regions.IsOneOf(x, y, z, regionID1, regionID2)) caseIndex |= 1;
                if (nxv && zv && regions.IsOneOf(nx, y, z, regionID1, regionID2)) caseIndex |= 2;
                if (nxv && zbv && regions.IsOneOf(nx, y, nz, regionID1, regionID2)) caseIndex |= 4;
                if (xv && zbv && regions.IsOneOf(x, y, nz, regionID1, regionID2)) caseIndex |= 8;
            }

            if (nyv) {
                if (xv && zv && regions.IsOneOf(x, ny, z, regionID1, regionID2)) caseIndex |= 16;
                if (nxv && zv &&  regions.IsOneOf(nx, ny, z, regionID1, regionID2)) caseIndex |= 32;
                if (nxv && zbv && regions.IsOneOf(nx, ny, nz, regionID1, regionID2)) caseIndex |= 64;
                if (xv && zbv && regions.IsOneOf(x, ny, nz, regionID1, regionID2)) caseIndex |= 128;
            }

            return caseIndex;
        }

        // Convert an edge index to a vertex position.
        private static Vector3 GetMarchingCubesVertex(MultiRegionMeshInfo mesh, byte[] edgeIndices, int edge) {
            byte edgeIndex = edgeIndices[edge];

            byte vertex1Index = MarchingCubesTables.EdgeToVertexIndices[edgeIndex, 0];
            byte vertex2Index = MarchingCubesTables.EdgeToVertexIndices[edgeIndex, 1];

            Vector3Int vertex1 = MarchingCubesTables.Vertices[vertex1Index];
            Vector3Int vertex2 = MarchingCubesTables.Vertices[vertex2Index];

            return (Vector3) (vertex1 + vertex2) / 2.0f;
        }

        #endregion

        #region Voxel Region Calculation

        // Tracks a task that is determining if two regions can be combined.
        private struct PossibleRegionCombo {
            public Task<bool> Task;
            public int Region1;
            public int Region2;
        }
        
        // Look at all pairs of regions and combine any paris where the two regions together would be convex.
        // This helps to reduce the total region count significantly, especially reducing the tiny regions.
        private static IEnumerator CombineRegionsWherePossible(Vector3Int voxelCounts, Fast3DArray regions, ThreadSafeIncrementor regionCount,
                                                               NavVolume volume) {

            bool combinedAnyRegion;
            HashSet<int> goneRegions = new HashSet<int>();
            HashSet<Edge> checkedPairs = new HashSet<Edge>();

            float expectedMaxProgress = regionCount.Value * 0.2f;
            List<PossibleRegionCombo> tasks = new List<PossibleRegionCombo>();
            HashSet<int> usedRegions = new HashSet<int>();

            int curRegionCount = regionCount.Value;

            // Get map of all adjacent regions.
            // This only needs to be calculated once, after that we can just update it.
            Dictionary<int, HashSet<int>> regionsAdjacency = GetRegionAdjacencyMap(voxelCounts, regions);

            do {
                tasks.Clear();
                usedRegions.Clear();
                combinedAnyRegion = false;

                // Look at all regions except 0 (because zero is the blocking indices).
                for (int regionId = 1; regionId < curRegionCount; regionId++) {
                    if (goneRegions.Contains(regionId) ||
                        !regionsAdjacency.TryGetValue(regionId, out HashSet<int> adjacent)) continue;
                    
                    foreach (int otherRegion in adjacent) {
                        if (otherRegion == regionId) {
                            Debug.LogError($"Region {regionId} reported as adjacent to itself.");
                            continue;
                        }
                        
                        // We only need to check each pair of regions once.
                        if (goneRegions.Contains(otherRegion) ||
                            !checkedPairs.Add(new Edge(regionId, otherRegion))) continue;

                        // Start async task checking if the two regions can be combined.
                        int curRegionId = regionId;
                        Task<bool> task = RunTask(volume,
                            () => CanCombineRegions(voxelCounts, regions, curRegionId, otherRegion));
                        tasks.Add(new PossibleRegionCombo {
                            Task = task,
                            Region1 = regionId,
                            Region2 = otherRegion,
                        });
                    }
                }

                // Wait for all tasks to complete.
                foreach (PossibleRegionCombo task in tasks) {
                    if (!task.Task.IsCompleted) {
                        bool finished = task.Task.Wait(10_000);
                        if (!finished) {
                            Debug.LogError("Task failed to complete in 10s.");
                        }
                    }
                }

                // For each task, if it determined the regions can be merged, do so.
                foreach (PossibleRegionCombo task in tasks) {
                    if (task.Task.Status != TaskStatus.RanToCompletion) {
                        break;
                    }
                    
                    // For each time we run the tasks, we can only merge each region once.
                    // After that the merge-ability must be recalculated.
                    if (!task.Task.Result ||
                        usedRegions.Contains(task.Region1) ||
                        usedRegions.Contains(task.Region2)) continue;

                    usedRegions.Add(task.Region1);
                    usedRegions.Add(task.Region2);
                    combinedAnyRegion = true;

                    // Mark removed region as no longer existing.
                    goneRegions.Add(task.Region1);

                    // Update adjacency of both regions.
                    HashSet<int> adjacent = regionsAdjacency[task.Region1];
                    HashSet<int> otherAdjacency = regionsAdjacency[task.Region2];
                    otherAdjacency.UnionWith(adjacent);
                    otherAdjacency.Remove(task.Region1);
                    otherAdjacency.Remove(task.Region2);
                    foreach (int otherOtherRegion in otherAdjacency) {
                        HashSet<int> otherOtherAdjacency = regionsAdjacency[otherOtherRegion];
                        otherOtherAdjacency.Remove(task.Region1);
                        otherOtherAdjacency.Add(task.Region2);
                    }
                    adjacent.Clear();
                    
                    // Set the voxels of region 1 to be of region 2.
                    CombineRegions(voxelCounts, regions, task.Region1, task.Region2);
                            
                    // Region has changed so we need to recheck pairs containing it.
                    checkedPairs.RemoveWhere(pair => pair.Vertex1 == task.Region2 || pair.Vertex2 == task.Region2);
                }

                BakeProgress[volume] = new NavVolumeBakeProgress {
                    Operation = OpNameCombiningRegions,
                    Progress = Mathf.Clamp01(goneRegions.Count / expectedMaxProgress),
                };
                BakeProgressUpdated?.Invoke(volume);
                yield return regionCount;
            } while (combinedAnyRegion);

            int newRegionCount = regionCount.Value - goneRegions.Count;

            // Shift all region indices down by number of lower regions that were removed.
            Dictionary<int, int> regionMap = new Dictionary<int, int>();
            Dictionary<int, int> counts = new Dictionary<int, int>();
            for (int x = 0; x < voxelCounts.x; x++) {
                for (int y = 0; y < voxelCounts.y; y++) {
                    for (int z = 0; z < voxelCounts.z; z++) {
                        int region = regions[x, y, z];
                        if (region < 0) continue;
                        if (goneRegions.Contains(region)) {
                            Debug.LogError($"Found voxel in region {region} which is supposed to be gone");
                        }
                        if (!regionMap.TryGetValue(region, out int newRegion)) {
                            newRegion = region - goneRegions.Count(r => r < region);

                            if (newRegion >= newRegionCount) {
                                Debug.LogError($"Region {newRegion} greater than region count");
                            }

                            regionMap[region] = newRegion;
                        }

                        counts.TryGetValue(newRegion, out int count);
                        count++;
                        counts[newRegion] = count;

                        regions[x, y, z] = newRegion;
                    }
                }
            }

            Debug.Log($"Reduced region count by {goneRegions.Count}, from {regionCount.Value} to {newRegionCount}.");

            regionCount.Value = newRegionCount;
            yield return regionCount;
        }

        // Set the voxels of region 1 to be of region 2.
        private static void CombineRegions(Vector3Int voxelCounts, Fast3DArray regions, int region1, int region2) {
            for (int x = 0; x < voxelCounts.x; x++) {
                for (int y = 0; y < voxelCounts.y; y++) {
                    for (int z = 0; z < voxelCounts.z; z++) {
                        if (regions[x, y, z] == region1) {
                            regions[x, y, z] = region2;
                        }
                    }
                }
            }
        }

        // Check if the region made by combining two regions would be convex.
        private static bool CanCombineRegions(Vector3Int voxelCounts, Fast3DArray regions, int region1, int region2) {
            // First check for any internal concavities.
            for (int x = 0; x < voxelCounts.x - 1; x++) {
                for (int y = 0; y < voxelCounts.y - 1; y++) {
                    for (int z = 0; z < voxelCounts.z - 1; z++) {
                        byte cube = GetMarchingCubesIndex(voxelCounts, regions, region1, region2, x, y, z);
                        if (MarchingCubesCavityTables.CubesWithInternalCavities[cube]) {
                            return false;
                        }
                    }
                }
            }
            
            // Next check for concavities created by neighbor relationships.
            for (int dir = 0; dir < MarchingCubesTables.PositiveDirections.Length; dir++) {
                Vector3Int dirVector = MarchingCubesTables.PositiveDirections[dir];
                int dx = dirVector.x;
                int dy = dirVector.y;
                int dz = dirVector.z;
                
                for (int x = 0; x < voxelCounts.x - 1; x++) {
                    for (int y = 0; y < voxelCounts.y - 1; y++) {
                        for (int z = 0; z < voxelCounts.z - 1; z++) {
                            byte selfCube = GetMarchingCubesIndex(voxelCounts, regions, region1, region2, x, y, z);
                            if (selfCube == 0 || selfCube == 255) continue;

                            int[] concaveNeighbors = MarchingCubesCavityTables.CubeConcaveNeighbors[selfCube][dir];
                            if (concaveNeighbors.Length == 0) continue;

                            int nx = x + dx;
                            int ny = y + dy;
                            int nz = z + dz;

                            if (IsOutOfBounds(voxelCounts, nx + 1, ny + 1, nz + 1)) continue;
                            
                            byte neighborCube = GetMarchingCubesIndex(voxelCounts, regions, region1, region2, nx, ny, nz);
                            if (Array.IndexOf(concaveNeighbors, neighborCube) >= 0) {
                                return false;
                            }
                        }
                    }
                }
            }
            
            return true;
        }

        // Get a map of which regions each region is adjacent to.
        private static Dictionary<int, HashSet<int>> GetRegionAdjacencyMap(Vector3Int voxelCounts, Fast3DArray regions) {
            Dictionary<int, HashSet<int>> regionsAdjacency = new Dictionary<int, HashSet<int>>();
            for (int x = 0; x < voxelCounts.x; x++) {
                for (int y = 0; y < voxelCounts.y; y++) {
                    for (int z = 0; z < voxelCounts.z; z++) {
                        int curRegion = regions[x, y, z];
                        if (curRegion <= 0) continue;

                        if (!regionsAdjacency.TryGetValue(curRegion, out HashSet<int> curAdjacent)) {
                            curAdjacent = new HashSet<int>();
                            regionsAdjacency[curRegion] = curAdjacent;
                        }

                        Vector3Int pos = new Vector3Int(x, y, z);
                        foreach (Vector3Int dir in NeighborDirections) {
                            Vector3Int n = pos + dir;
                            if (IsOutOfBounds(voxelCounts, n)) continue;
                            int nRegion = regions[n.x, n.y, n.z];
                            if (nRegion != curRegion && nRegion > 0) {
                                curAdjacent.Add(nRegion);
                            }
                        }
                    }
                }
            }

            return regionsAdjacency;
        }

        // Used for sanity checking to ensure regions don't have any isolated islands.
        private static bool EnsureAllRegionsAreContiguous(Vector3Int voxelCounts, Fast3DArray regions) {
            Dictionary<int, HashSet<Vector3Int>> regionDict = new Dictionary<int, HashSet<Vector3Int>>();
            HashSet<int> errorRegions = new HashSet<int>();
            for (int x = 0; x < voxelCounts.x; x++) {
                for (int y = 0; y < voxelCounts.y; y++) {
                    for (int z = 0; z < voxelCounts.z; z++) {
                        Vector3Int pos = new Vector3Int(x, y, z);
                        int region = regions[x, y, z];
                        if (region == 0) continue;
                        if (errorRegions.Contains(region)) continue;

                        if (regionDict.TryGetValue(region, out HashSet<Vector3Int> regionCont)) {
                            // When a voxel is found from an existing region but it's not in the filled set,
                            // the region must not be contiguous.
                            if (!regionCont.Contains(pos)) {
                                Debug.LogError($"Region {region} is not contiguous at {pos}!");
                                errorRegions.Add(region);
                            }
                        } else {
                            // When a new region is found, flood fill the HashSet with all of the contiguous voxels.
                            regionCont = new HashSet<Vector3Int>();
                            regionDict[region] = regionCont;

                            Queue<Vector3Int> queue = new Queue<Vector3Int>();
                            queue.Enqueue(pos);
                            while (queue.Count > 0) {
                                Vector3Int cur = queue.Dequeue();
                                if (IsOutOfBounds(voxelCounts, cur) ||
                                    regions[cur.x, cur.y, cur.z] != region ||
                                    !regionCont.Add(cur)) continue;

                                foreach (Vector3Int dir in NeighborDirections) {
                                    queue.Enqueue(cur + dir);
                                }
                            }
                        }
                    }
                }
            }

            return errorRegions.Count == 0;
        }

        // Ensure that all regions are convex by splitting them at any concavities.
        private static IEnumerator ConvexifyAllRegions(Vector3Int voxelCounts, Fast3DArray regions, ThreadSafeIncrementor regionCount, NavVolume volume) {
            ConcurrentQueue<int> regionsToProcess = new ConcurrentQueue<int>(Enumerable.Range(1, regionCount.Value - 1));

            ThreadSafeIncrementor finishedTaskCount = new ThreadSafeIncrementor();
            ThreadSafeIncrementor activeTaskCount = new ThreadSafeIncrementor();
            
            // Search all regions. When a region is split, search the two created regions.
            // Repeat this until no tasks remain.
            while (true) {
                while (regionsToProcess.TryDequeue(out int nextRegion)) {
                    activeTaskCount.Increment();
                    Task newTask = RunTask(volume, () => {
                        try {
                            ConvexifyRegion(voxelCounts, regions, nextRegion, regionCount, regionsToProcess, volume);
                        } catch (Exception ex) {
                            Debug.LogException(ex);
                        }
                        
                        finishedTaskCount.Increment();
                        activeTaskCount.Decrement();
                    });
                }

                if (activeTaskCount.Value == 0 && regionsToProcess.IsEmpty) break;
                
                BakeProgress[volume] = new NavVolumeBakeProgress {
                    Operation = OpNameCalculatingRegions,
                    Progress = finishedTaskCount.Value / (float) regionCount.Value,
                };
                BakeProgressUpdated?.Invoke(volume);
                yield return null;
            }
        }

        // Search through a region, and if a concavity is found, split the region.
        private static void ConvexifyRegion(Vector3Int voxelCounts, Fast3DArray regions, int regionId,
                                            ThreadSafeIncrementor regionCount, ConcurrentQueue<int> regionsToProcess,
                                            NavVolume volume) {
            
            // Look for cubes with internal concavities and split at those points.
            for (int x = 0; x < voxelCounts.x - 1; x++) {
                for (int y = 0; y < voxelCounts.y - 1; y++) {
                    for (int z = 0; z < voxelCounts.z - 1; z++) {
                        Vector3Int pos = new Vector3Int(x, y, z);
                        byte cube = GetMarchingCubesIndex(voxelCounts, regions, regionId, x, y, z);
                        if (MarchingCubesCavityTables.CubesWithInternalCavities[cube]) {
                            SplitRegionForInternalConcavity(voxelCounts, regions, regionId, regionCount, pos, 
                                                            regionsToProcess, volume);
                        }
                    }
                }
            }

            // Look for cubes with neighbor concavities and split between them.
            for (int dir = 0; dir < MarchingCubesTables.PositiveDirections.Length; dir++) {
                Vector3Int dirVector = MarchingCubesTables.PositiveDirections[dir];
                int dx = dirVector.x;
                int dy = dirVector.y;
                int dz = dirVector.z;

                for (int x = 0; x < voxelCounts.x - 1; x++) {
                    for (int y = 0; y < voxelCounts.y - 1; y++) {
                        for (int z = 0; z < voxelCounts.z - 1; z++) {
                            Vector3Int selfPos = new Vector3Int(x, y, z);

                            byte selfCube = GetMarchingCubesIndex(voxelCounts, regions, regionId, x, y, z);
                            if (selfCube == 0 || selfCube == 255) continue;

                            int[] concaveNeighbors = MarchingCubesCavityTables.CubeConcaveNeighbors[selfCube][dir];
                            if (concaveNeighbors.Length == 0) continue;

                            int nx = x + dx;
                            int ny = y + dy;
                            int nz = z + dz;
                            if (IsOutOfBounds(voxelCounts, nx + 1, ny + 1, nz + 1)) continue;

                            byte neighborCube = GetMarchingCubesIndex(voxelCounts, regions, regionId, nx, ny, nz);
                            if (Array.IndexOf(concaveNeighbors, neighborCube) >= 0) {
                                SplitRegionForNeighborConcavity(voxelCounts, regions, regionId, regionCount, selfPos,
                                                                dir, regionsToProcess, volume);
                            }
                        }
                    }
                }
            }
        }

        // Split a region between two neighbor marching cubes.
        private static void SplitRegionForNeighborConcavity(Vector3Int voxelCounts, Fast3DArray regions, int regionId,
                                                            ThreadSafeIncrementor regionCount, Vector3Int pos, 
                                                            int dirIndex, ConcurrentQueue<int> regionsToProcess,
                                                            NavVolume volume) {
            
            Vector3Int[] dirs = MarchingCubesTables.PositiveDirections;
            Vector3Int zAxis = dirs[dirIndex];
            Vector3Int xAxis = dirs[(dirIndex + 1) % dirs.Length];
            Vector3Int yAxis = dirs[(dirIndex + 2) % dirs.Length];

            int vz = MathUtility.Dot(pos, zAxis);

            int voxelsVx = MathUtility.Dot(voxelCounts, xAxis);
            int voxelsVy = MathUtility.Dot(voxelCounts, yAxis);
            int voxelsVz = MathUtility.Dot(voxelCounts, zAxis);

            // Two marching cubes means there are three voxels,
            // meaning two possible split locations.
            // Determine which one breaks fewer convex cubes and use that.
            int split1 = GetBrokenCubeCount(regions, regionId, vz + 1, xAxis, yAxis, zAxis,
                                            new Vector2Int(voxelsVx, voxelsVy));
            int split2 = GetBrokenCubeCount(regions, regionId, vz + 2, xAxis, yAxis, zAxis,
                                            new Vector2Int(voxelsVx, voxelsVy));

            if (split1 <= split2) {
                SplitRegion(regions, regionId, vz + 1, xAxis, yAxis, zAxis,
                            new Vector3Int(voxelsVx, voxelsVy, voxelsVz),
                            regionCount, pos + Vector3Int.one, regionsToProcess, volume);
            } else {
                SplitRegion(regions, regionId, vz + 2, xAxis, yAxis, zAxis,
                            new Vector3Int(voxelsVx, voxelsVy, voxelsVz),
                            regionCount, pos + Vector3Int.one + zAxis, regionsToProcess, volume);
            }
        }

        // Split a region to break up a single concave cube.
        private static void SplitRegionForInternalConcavity(Vector3Int voxelCounts, Fast3DArray regions, int regionId, 
                                                           ThreadSafeIncrementor regionCount, Vector3Int pos,
                                                           ConcurrentQueue<int> regionsToProcess, NavVolume volume) {
            int cube = GetMarchingCubesIndex(voxelCounts, regions, regionId, pos.x, pos.y, pos.z);

            // Find best split axis - must actually divide the cube.
            int xSplit = IsCubeBrokenOnAxis(cube, 0)
                ? GetBrokenCubeCount(regions, regionId, pos.x + 1,
                                     Vector3Int.forward, Vector3Int.up, Vector3Int.right,
                                     new Vector2Int(voxelCounts.z, voxelCounts.y))
                : int.MaxValue;
            
            int ySplit = IsCubeBrokenOnAxis(cube, 1)
                ? GetBrokenCubeCount(regions, regionId, pos.y + 1,
                                     Vector3Int.right, Vector3Int.forward, Vector3Int.up,
                                     new Vector2Int(voxelCounts.x, voxelCounts.z))
                : int.MaxValue;

            int zSplit = IsCubeBrokenOnAxis(cube, 2)
                ? GetBrokenCubeCount(regions, regionId, pos.z + 1,
                                     Vector3Int.right, Vector3Int.up, Vector3Int.forward,
                                     new Vector2Int(voxelCounts.x, voxelCounts.y))
                : int.MaxValue;

            // Split on best axis.
            if (xSplit <= ySplit && xSplit <= zSplit) {
                SplitRegion(regions, regionId, pos.x + 1,
                            Vector3Int.forward, Vector3Int.up, Vector3Int.right,
                            new Vector3Int(voxelCounts.z, voxelCounts.y, voxelCounts.x),
                            regionCount, pos + Vector3Int.one, regionsToProcess, volume);
            } else if (ySplit <= xSplit && ySplit <= zSplit) {
                SplitRegion(regions, regionId, pos.y + 1,
                            Vector3Int.right, Vector3Int.forward, Vector3Int.up,
                            new Vector3Int(voxelCounts.x, voxelCounts.z, voxelCounts.y),
                            regionCount, pos + Vector3Int.one, regionsToProcess, volume);
            } else {
                SplitRegion(regions, regionId, pos.z + 1,
                            Vector3Int.right, Vector3Int.up, Vector3Int.forward,
                            new Vector3Int(voxelCounts.x, voxelCounts.y, voxelCounts.z),
                            regionCount, pos + Vector3Int.one, regionsToProcess, volume);
            }
        }

        // Get the voxels that would be contiguous with the original region if it was split at the given position.
        // Use axis abstraction to enable this to work on x, y, or z axis.
        private static HashSet<Vector3Int> GetContiguousIfSplit(Vector3Int voxelCounts, Fast3DArray regions, int regionId,
                                                                Vector3Int start,
                                                                Vector3Int xAxis, Vector3Int yAxis, Vector3Int zAxis) {

            HashSet<Vector3Int> result = new HashSet<Vector3Int>();
            Queue<Vector3Int> queue = new Queue<Vector3Int>();

            // Flood fill to find contiguous cubes.
            int z = MathUtility.Dot(start, zAxis);
            queue.Enqueue(start);
            queue.Enqueue(start + xAxis);
            queue.Enqueue(start + yAxis);
            queue.Enqueue(start + xAxis + yAxis);
            
            while (queue.Count > 0) {
                Vector3Int cur = queue.Dequeue();
                int cz = MathUtility.Dot(cur, zAxis);
                // Don't cross the split axis.
                if (result.Contains(cur) || IsOutOfBounds(voxelCounts, cur) || cz > z ||
                    regions[cur.x, cur.y, cur.z] != regionId) {
                    continue;
                }

                // Enqueue neighbors.
                result.Add(cur);
                foreach (Vector3Int dir in NeighborDirections) {
                    Vector3Int n = cur + dir;
                    queue.Enqueue(n);
                }
            }

            return result;
        }

        // Expand contiguous voxels to cross the split axis,
        // but not into any that were contiguous with the actual split region.
        private static void ExpandContiguousExcluding(Vector3Int voxelCounts, Fast3DArray regions, int regionId,
                                                      HashSet<Vector3Int> toExpand, HashSet<Vector3Int> toExclude) {

            Queue<Vector3Int> queue = new Queue<Vector3Int>(toExpand);
            
            while (queue.Count > 0) {
                Vector3Int cur = queue.Dequeue();
                foreach (Vector3Int dir in NeighborDirections) {
                    Vector3Int n = cur + dir;

                    // Add all in-bounds voxels of this region that are not in toExclude.
                    if (IsOutOfBounds(voxelCounts, n) ||
                        regions[n.x, n.y, n.z] != regionId ||
                        toExclude.Contains(n) ||
                        !toExpand.Add(n)) {
                        
                        continue;
                    }
                    
                    queue.Enqueue(n);
                }
            }
        }

        // Split a region into two at the given point along the given axis.
        // Voxels further along the split axis than the split point will go into the new region.
        private static void SplitRegion(Fast3DArray regions, int regionId, int startZ,
                                        Vector3Int xAxis, Vector3Int yAxis, Vector3Int zAxis,
                                        Vector3Int axisLimits, ThreadSafeIncrementor regionCount, Vector3Int startPos,
                                        ConcurrentQueue<int> regionsToProcess, NavVolume volume) {

            Vector3Int voxelCounts = new Vector3Int(
                regions.SizeX, 
                regions.SizeY, 
                regions.SizeZ);

            // Get initial contiguous regions staying on one side of the split axis.
            Vector3Int startMinus1 = startPos - Vector3Int.one;
            HashSet<Vector3Int> contiguousWithCurrent =
                GetContiguousIfSplit(voxelCounts, regions, regionId, startMinus1, xAxis, yAxis, zAxis);
            HashSet<Vector3Int> contiguousWithNew =
                GetContiguousIfSplit(voxelCounts, regions, regionId, startMinus1 + zAxis, xAxis, yAxis, -zAxis);
            
            // Voxels on side B of the axis but not contiguous to the current region B are added to region A.
            ExpandContiguousExcluding(voxelCounts, regions, regionId, contiguousWithCurrent, contiguousWithNew);
            
            // Voxels on side A of the axis but not contiguous to the current region A are added to region B.
            ExpandContiguousExcluding(voxelCounts, regions, regionId, contiguousWithNew, contiguousWithCurrent);

            int newRegion = regionCount.Increment() - 1;
            
            // Loop through all voxels and assign new region if should split.
            for (int vx = 0; vx < axisLimits.x; vx++) {
                for (int vy = 0; vy < axisLimits.y; vy++) {
                    for (int vz = 0; vz < axisLimits.z; vz++) {
                        Vector3Int pos = xAxis * vx + yAxis * vy + zAxis * vz;
                        if (regions[pos.x, pos.y, pos.z] != regionId) continue;

                        // Only split a voxel into the new region if it is part of contiguousWithNew.
                        bool contiguousNew = contiguousWithNew.Contains(pos);
                        if (contiguousNew) {
                            regions[pos.x, pos.y, pos.z] = newRegion;
                        }
                    }
                }
            }
            
            // Add new region to queue because it might still have concavities.
            regionsToProcess.Enqueue(newRegion);
        }

        // Get the number of convex cubes that would be broken
        // if the region was split on the given axis at the given point.
        // This approximation is useful to find the least disruptive place to split.
        private static int GetBrokenCubeCount(Fast3DArray regions, int regionId, int vz,
                                              Vector3Int xAxis, Vector3Int yAxis, Vector3Int zAxis,
                                              Vector2Int axisLimits) {
            int count = 0;
            Vector3Int zOffset = vz * zAxis;
            Vector3Int[] sideASamples = new[] {
                -zAxis - xAxis - yAxis,
                -zAxis - xAxis,
                -zAxis - yAxis,
                -zAxis
            };

            Vector3Int[] sideBSamples = new[] {
                -xAxis - yAxis,
                -xAxis,
                -yAxis,
                Vector3Int.zero,
            };

            // Define virtual x and y (both orthogonal to the split axis).
            // Loop through all x and y coordinates to find any cubes that would be split.
            Vector3Int voxelCounts = new Vector3Int(regions.SizeX, regions.SizeY, regions.SizeZ);
            for (int vx = 1; vx < axisLimits.x; vx++) {
                for (int vy = 1; vy < axisLimits.y; vy++) {
                    Vector3Int pos = zOffset + vx * xAxis + vy * yAxis;

                    // For each side, check if there are "on" voxels on that side of the cube.
                    bool hasSideA = sideASamples.Any(v => regions[pos.x + v.x, pos.y + v.y, pos.z + v.z] == regionId);
                    bool hasSideB = sideBSamples.Any(v => regions[pos.x + v.x, pos.y + v.y, pos.z + v.z] == regionId);

                    if (hasSideA && hasSideB) {
                        // If has both sides, the cube will be split.
                        // If the cube is concave, this is good, otherwise it is bad.
                        byte cubeIndex = GetMarchingCubesIndex(voxelCounts, regions, regionId, pos.x - 1, pos.y - 1, pos.z - 1);
                        if (MarchingCubesCavityTables.CubesWithInternalCavities[cubeIndex]) {
                            count--;
                        } else {
                            count++;
                        }
                    } else if (hasSideA || hasSideB) {
                        // If has one side, that means it would be splitting along an existing wall of faces,
                        // which is good as it is more likely to reduce concavities.
                        count--;
                    }
                }
            }
            
            return count;
        }

        // Check whether the given cube index has "on" voxels on both sides of the given axis.
        private static bool IsCubeBrokenOnAxis(int cube, int axis) {
            byte[] sideAVerts = MarchingCubesTables.VerticesOnSideAPerDirection[axis];
            byte[] sideBVerts = MarchingCubesTables.VerticesOnSideBPerDirection[axis];
            
            bool hasSideA = sideAVerts.Any(v => (cube & (1 << v)) != 0);
            bool hasSideB = sideBVerts.Any(v => (cube & (1 << v)) != 0);

            return hasSideA && hasSideB;
        }

        // Calculate the initial regions by finding all contiguous islands of voxels.
        private static Fast3DArray CalculateRegions(Vector3Int voxelCounts, Fast3DArray voxels, out int regionCount) {
            Fast3DArray regions = new Fast3DArray(voxelCounts.x, voxelCounts.y, voxelCounts.z);
            for (int x = 0; x < voxelCounts.x; x++) {
                for (int y = 0; y < voxelCounts.y; y++) {
                    for (int z = 0; z < voxelCounts.z; z++) {
                        // If voxel is blocked, set its region to zero.
                        // Else, set it to -1 to mark that it needs a region.
                        if (voxels[x, y, z] == 0) {
                            regions[x, y, z] = 0;
                        } else {
                            regions[x, y, z] = -1;
                        }
                    }
                }
            }

            regionCount = 1;
            
            for (int x = 0; x < voxelCounts.x; x++) {
                for (int y = 0; y < voxelCounts.y; y++) {
                    for (int z = 0; z < voxelCounts.z; z++) {
                        
                        if (regions[x, y, z] >= 0) continue; // Region is already set 

                        // Create a new region and flood fill all contiguous voxels.
                        regions[x, y, z] = regionCount++;
                        ExpandVoxelRegion(voxelCounts, new Vector3Int(x, y, z), voxels, regions);
                    }
                }
            }
            
            return regions;
        }

        // Flood fill all contiguous voxels.
        private static void ExpandVoxelRegion(Vector3Int voxelCounts, Vector3Int startPos, Fast3DArray voxels, Fast3DArray regions) {
            Queue<Vector3Int> toExplore = new Queue<Vector3Int>();
            toExplore.Enqueue(startPos);

            ExpandVoxelRegions(voxelCounts, voxels, regions, toExplore);
        }

        // Flood fill all contiguous voxels.
        private static void ExpandVoxelRegions(Vector3Int voxelCounts, Fast3DArray voxels, Fast3DArray regions, Queue<Vector3Int> toExplore) {
            while (toExplore.Count > 0) {
                Vector3Int current = toExplore.Dequeue();
                int region = regions[current.x, current.y, current.z];
                for (int i = 0; i < NeighborDirections.Length; i++) {
                    Vector3Int n = current + NeighborDirections[i];

                    if (IsOutOfBounds(voxelCounts, n)) {
                        continue;
                    }

                    if (regions[n.x, n.y, n.z] >= 0) continue;

                    regions[n.x, n.y, n.z] = region;
                    toExplore.Enqueue(n);
                }
            }
        }

        // Return whether given position is out of bounds.
        private static bool IsOutOfBounds(in Vector3Int voxelCounts, in Vector3Int n) {
            return n.x < 0 || n.x >= voxelCounts.x ||
                   n.y < 0 || n.y >= voxelCounts.y ||
                   n.z < 0 || n.z >= voxelCounts.z;
        }

        // Return whether given position is out of bounds.
        private static bool IsOutOfBounds(in Vector3Int voxelCounts, int x, int y, int z) {
            return x < 0 || x >= voxelCounts.x ||
                   y < 0 || y >= voxelCounts.y ||
                   z < 0 || z >= voxelCounts.z;
        }

        // Perform samples at every voxel to determine of an agent with radius agentRadius can fit in that position.
        private static IEnumerator CalculateBlockedVoxels(NavVolume volume, Vector3Int voxelCounts, Fast3DArray voxels) {

            bool usePhysicsScene = false;
            PhysicsScene physicsScene = default;
            
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(volume.gameObject);
            if (stage != null) {
                usePhysicsScene = true;
                physicsScene = stage.scene.GetPhysicsScene();
            }
            
            
            int queryNum = Mathf.CeilToInt(volume.VoxelSize / volume.MaxAgentRadius) + 1;
            float distancePer = volume.VoxelSize / (queryNum - 1);
            for (int x = 0; x < voxelCounts.x; x++) {
                for (int y = 0; y < voxelCounts.y; y++) {
                    for (int z = 0; z < voxelCounts.z; z++) {
                        Vector3 voxelCorner = volume.Bounds.min + new Vector3(x, y, z) * volume.VoxelSize;
                        Vector3 voxelCenter = voxelCorner + Vector3.one * (0.5f * volume.VoxelSize);

                        bool debug = x == 0 && y == 0 && z == 0 && volume.VisualizeVoxelQueries;
                        if (debug) {
                            DebugUtility.DrawDebugBounds(new Bounds(volume.transform.TransformPoint(voxelCenter),
                                                                    Vector3.one * volume.VoxelSize), 
                                                         Color.red, 10);
                        }
                        
                        bool validHit = false;

                        if (volume.EnableMultiQuery) {
                            for (int xInner = 0; xInner < queryNum; xInner++) {
                                for (int yInner = 0; yInner < queryNum; yInner++) {
                                    for (int zInner = 0; zInner < queryNum; zInner++) {
                                        Vector3 innerPos =
                                            voxelCorner + new Vector3(xInner, yInner, zInner) * distancePer;
                                        validHit |= SphereTest(volume, innerPos, ref physicsScene, usePhysicsScene, debug);
                                        if (validHit) break;
                                    }
                                    if (validHit) break;
                                }
                                if (validHit) break;
                            }
                        } else {
                            validHit = SphereTest(volume, voxelCenter, ref physicsScene, usePhysicsScene, debug);
                        }
                        
                        if (validHit) {
                            voxels[x, y, z] = 0;
                        } else {
                            voxels[x, y, z] = -1;
                        }
                    }
                }
                
                BakeProgress[volume] = new NavVolumeBakeProgress {
                    Operation = OpNameCalculatingBlocked,
                    Progress = (x + 1) / (float)voxelCounts.x,
                };
                BakeProgressUpdated?.Invoke(volume);
                yield return null;
            }

            // If using start locations, set all positions that are not contiguous with a start location as invalid.
            if (volume.UseStartLocations) {
                HashSet<Vector3Int> reachable = new HashSet<Vector3Int>();
                Queue<Vector3Int> queue = new Queue<Vector3Int>();
                foreach (Vector3 pos in volume.StartLocations) {
                    Vector3 posInBounds = pos - volume.Bounds.min;
                    Vector3Int voxelPos = Vector3Int.RoundToInt(posInBounds / volume.VoxelSize);
                    queue.Enqueue(voxelPos);
                }

                // Flood fill all voxels reachable from StartLocations.
                while (queue.Count > 0) {
                    Vector3Int pos = queue.Dequeue();
                    if (IsOutOfBounds(voxelCounts, pos) ||
                        voxels[pos.x, pos.y, pos.z] == 0 ||
                        !reachable.Add(pos)) continue;
                    
                    foreach (Vector3Int dir in NeighborDirections) {
                        queue.Enqueue(pos + dir);
                    }
                }

                // Mark non-reachable voxels as blocked.
                for (int x = 0; x < voxelCounts.x; x++) {
                    for (int y = 0; y < voxelCounts.y; y++) {
                        for (int z = 0; z < voxelCounts.z; z++) {
                            if (!reachable.Contains(new Vector3Int(x, y, z))) {
                                voxels[x, y, z] = 0;
                            }
                        }
                    }
                }
            }
        }

        private static Collider[] _raycastHits = new Collider[1024];
        private static bool SphereTest(NavVolume volume, Vector3 localPos, ref PhysicsScene scene, bool usePhysicsScene, bool debug) {
            Vector3 worldPoint = volume.transform.TransformPoint(localPos);

            if (debug) {
                DebugUtility.DrawDebugCircle(worldPoint, Vector3.up, volume.MaxAgentRadius, Color.green, 10);
                DebugUtility.DrawDebugCircle(worldPoint, Vector3.right, volume.MaxAgentRadius, Color.green, 10);
                DebugUtility.DrawDebugCircle(worldPoint, Vector3.forward, volume.MaxAgentRadius, Color.green, 10);
            }

            // Sample using physics query.
            int hitCount;
            if (usePhysicsScene) {
                hitCount = scene.OverlapSphere(worldPoint, volume.MaxAgentRadius, _raycastHits,
                                               volume.BlockingLayers, QueryTriggerInteraction.Ignore);
            } else {
                hitCount = Physics.OverlapSphereNonAlloc(worldPoint, volume.MaxAgentRadius, _raycastHits,
                                                         volume.BlockingLayers, QueryTriggerInteraction.Ignore);
            }

            if (!volume.StaticOnly) return hitCount > 0;
            
            for (int i = 0; i < hitCount; i++) {
                if (_raycastHits[i].gameObject.isStatic) return true;
            }

            return false;

        }

        #endregion

        #region Preview Mesh Building

        /// <summary>
        /// Build a preview mesh for the given region based on the given list of vertices and lists of triangles.
        /// </summary>
        /// <param name="volume">The volume to build for.</param>
        /// <param name="vertices">The list of all vertices.</param>
        /// <param name="triLists">The list of all triangle lists.</param>
        public static void BuildTriangulationPreviewMesh(NavVolume volume, IReadOnlyList<Vector3> vertices,
                                                         IReadOnlyList<IReadOnlyList<int>> triLists) {
            Mesh mesh = new Mesh();
            
            mesh.indexFormat = IndexFormat.UInt32; // 32 bit to support large meshes.
            mesh.subMeshCount = triLists.Count * 2; // 1 mesh per region for triangles, 1 for edges.
            
            // Set vertices without modification.
            mesh.SetVertices(vertices as List<Vector3> ?? vertices.ToList());
            
            // Loop through triangles and add indices.
            for (int i = 0; i < triLists.Count; i++) {
                // Set triangle submesh indices directly.
                IReadOnlyList<int> triList = triLists[i];
                mesh.SetIndices(triList as List<int> ?? triList.ToList(), MeshTopology.Triangles, i);
                
                // Calculate and set indices for line submesh.
                List<int> lineIndices = new List<int>();
                int triCount = triList.Count / 3;
                for (int j = 0; j < triCount; j++) {
                    int triBase = j * 3;
                    for (int k = 0; k < 3; k++) {
                        int index1 = triBase + k;
                        int index2 = triBase + ((k + 1) % 3);
                        
                        lineIndices.Add(triList[index1]);
                        lineIndices.Add(triList[index2]);
                    }
                }
                mesh.SetIndices(lineIndices, MeshTopology.Lines, triLists.Count + i);
            }
            
            volume.EditorOnlyPreviewMesh = mesh;
            
            // Load and assign materials.
            if (!_triangulationPreviewMaterial)
                _triangulationPreviewMaterial = Resources.Load<Material>("HyperNav/TriangulationPreviewMaterial");
            
            if (!_triangulationOutlinePreviewMaterial)
                _triangulationOutlinePreviewMaterial = Resources.Load<Material>("HyperNav/TriangulationOutlinePreviewMaterial");

            volume.EditorOnlyPreviewMaterials =
                Enumerable.Repeat(_triangulationPreviewMaterial, triLists.Count)
                          .Concat(Enumerable.Repeat(_triangulationOutlinePreviewMaterial, triLists.Count))
                          .ToArray();
        }

        // Build preview mesh out of voxels that gives a different color to each voxel region.
        private static void BuildRegionIDPreviewMesh(NavVolume volume, Fast3DArray regions, int regionCount) {
            Mesh mesh = new Mesh();

            int sizeX = regions.SizeX;
            int sizeY = regions.SizeY;
            int sizeZ = regions.SizeZ;

            List<Vector3> positions = new List<Vector3>();
            List<Color> colors = new List<Color>();
            
            List<int>[] quadIndices = new List<int>[regionCount];
            
            Vector3 boxSize = volume.VoxelSize * Vector3.one * 0.2f;

            Dictionary<int, Color> regionColors = new Dictionary<int, Color>();

            // Add all cubes to the mesh as both triangles and lines.
            for (int x = 0; x < sizeX; x++) {
                for (int y = 0; y < sizeY; y++) {
                    for (int z = 0; z < sizeZ; z++) {
                        int region = regions[x, y, z];
                        if (region < 0) continue;
                        Vector3 voxelPos = volume.Bounds.min +
                                           new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * volume.VoxelSize;

                        int firstIndex = positions.Count;
                        AddBoxPositions(positions, voxelPos, boxSize);

                        List<int> indices = quadIndices[region];
                        if (indices == null) {
                            indices = new List<int>();
                            quadIndices[region] = indices;
                        }
                        
                        AddBoxIndices(indices, firstIndex);

                        // Vertex color by region.
                        if (!regionColors.TryGetValue(region, out Color color)) {
                            color = new Color(Random.value, Random.value, Random.value);
                            regionColors[region] = color;
                        }

                        AddRepeatingData(colors, color, 8);
                    }
                }
            }

            // Set all vertices and indices.
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.subMeshCount = regionCount;
            mesh.SetVertices(positions);
            mesh.SetColors(colors);

            for (int i = 0; i < regionCount; i++) {
                mesh.SetIndices(quadIndices[i], MeshTopology.Quads, i);
            }
            
            volume.EditorOnlyPreviewMesh = mesh;
            
            // Load and assign materials.
            if (!_regionIDPreviewMaterial)
                _regionIDPreviewMaterial = Resources.Load<Material>("HyperNav/RegionIDPreviewMaterial");

            volume.EditorOnlyPreviewMaterials = Enumerable.Repeat(_regionIDPreviewMaterial, regionCount).ToArray();
        }

        // Build preview mesh that shows blocked voxels as red.
        private static void BuildVoxelPreviewMesh(NavVolume volume, Fast3DArray voxels) {
            Mesh mesh = new Mesh();

            int sizeX = voxels.SizeX;
            int sizeY = voxels.SizeY;
            int sizeZ = voxels.SizeZ;

            List<Vector3> positions = new List<Vector3>();
            List<int> quadIndices = new List<int>();
            List<int> lineIndices = new List<int>();
            Vector3 boxSize = volume.VoxelSize * Vector3.one;

            // Loop through all voxels, and if blocked, add box positions and indices for quads and lines.
            for (int x = 0; x < sizeX; x++) {
                for (int y = 0; y < sizeY; y++) {
                    for (int z = 0; z < sizeZ; z++) {
                        if (voxels[x, y, z] != 0) continue;
                        Vector3 voxelPos = volume.Bounds.min +
                                           new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * volume.VoxelSize;

                        int firstIndex = positions.Count;
                        AddBoxPositions(positions, voxelPos, boxSize);
                        AddBoxIndices(quadIndices, firstIndex);
                        AddBoxLineIndices(lineIndices, firstIndex);
                    }
                }
            }

            // Build mesh.
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.subMeshCount = 2;
            mesh.SetVertices(positions);
            mesh.SetIndices(quadIndices, MeshTopology.Quads, 0);
            mesh.SetIndices(lineIndices, MeshTopology.Lines, 1);

            volume.EditorOnlyPreviewMesh = mesh;
            
            // Load and assign materials.
            if (!_voxelPreviewMaterial)
                _voxelPreviewMaterial = Resources.Load<Material>("HyperNav/VoxelPreviewMaterial");
            if (!_voxelOutlinePreviewMaterial)
                _voxelOutlinePreviewMaterial = Resources.Load<Material>("HyperNav/VoxelOutlinePreviewMaterial");

            volume.EditorOnlyPreviewMaterials = new[] { _voxelPreviewMaterial, _voxelOutlinePreviewMaterial };
        }

        // Add a value to a list a given number of times.
        private static void AddRepeatingData<T>(List<T> values, T value, int count) {
            for (int i = 0; i < count; i++) {
                values.Add(value);
            }
        }

        // Add the corner positions of a box to a list of positions.
        private static void AddBoxPositions(List<Vector3> positions, Vector3 center, Vector3 size) {
            Vector3 ext = size * 0.5f;

            positions.Add(center + new Vector3(-ext.x, -ext.y, -ext.z));
            positions.Add(center + new Vector3(-ext.x, -ext.y, +ext.z));
            positions.Add(center + new Vector3(-ext.x, +ext.y, -ext.z));
            positions.Add(center + new Vector3(-ext.x, +ext.y, +ext.z));
            positions.Add(center + new Vector3(+ext.x, -ext.y, -ext.z));
            positions.Add(center + new Vector3(+ext.x, -ext.y, +ext.z));
            positions.Add(center + new Vector3(+ext.x, +ext.y, -ext.z));
            positions.Add(center + new Vector3(+ext.x, +ext.y, +ext.z));
        }

        // Add the indices for the lines at the edges of a box to a list of indices.
        private static void AddBoxLineIndices(List<int> indices, int firstVertex) {
            int nnn = firstVertex + 0;
            int nnp = firstVertex + 1;
            int npn = firstVertex + 2;
            int npp = firstVertex + 3;
            int pnn = firstVertex + 4;
            int pnp = firstVertex + 5;
            int ppn = firstVertex + 6;
            int ppp = firstVertex + 7;
            
            AddLineIndices(indices, nnn, pnn, pnp, nnp); // bottom
            AddLineIndices(indices, npp, ppp, ppn, npn); // top
            AddLineIndices(indices, nnn, npn); // back left
            AddLineIndices(indices, pnn, ppn); // back right
            AddLineIndices(indices, nnp, npp); // front left
            AddLineIndices(indices, pnp, ppp); // front right
        }

        // Add the indices for the quads of a box to a list of indices.
        private static void AddBoxIndices(List<int> indices, int firstVertex) {
            int nnn = firstVertex + 0;
            int nnp = firstVertex + 1;
            int npn = firstVertex + 2;
            int npp = firstVertex + 3;
            int pnn = firstVertex + 4;
            int pnp = firstVertex + 5;
            int ppn = firstVertex + 6;
            int ppp = firstVertex + 7;
            
            AddQuadIndices(indices, nnn, pnn, pnp, nnp); // bottom
            AddQuadIndices(indices, npp, ppp, ppn, npn); // top
            AddQuadIndices(indices, npn, ppn, pnn, nnn); // back
            AddQuadIndices(indices, nnp, pnp, ppp, npp); // front
            AddQuadIndices(indices, npp, npn, nnn, nnp); // left
            AddQuadIndices(indices, ppn, ppp, pnp, pnn); // right
        }

        // Add the indices of a non-closed line sequence to a list of indices.
        private static void AddLineIndices(List<int> indices, params int[] toAdd) {
            for (int i = 0; i < toAdd.Length - 1; i++) {
                indices.Add(toAdd[i]);
                indices.Add(toAdd[i + 1]);
            }
        }

        // Add the indices of a quad to a list of indices.
        private static void AddQuadIndices(List<int> indices, int v1, int v2, int v3, int v4) {
            indices.Add(v1);
            indices.Add(v2);
            indices.Add(v3);
            indices.Add(v4);
        }

        #endregion
    }
}