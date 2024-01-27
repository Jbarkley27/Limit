// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System.Collections.Generic;
using System.Linq;
using Infohazard.HyperNav;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace Infohazard.HyperNav.Editor {
    /// <summary>
    /// Custom editor for <see cref="Infohazard.HyperNav.NavVolume"/>.
    /// </summary>
    [CustomEditor(typeof(NavVolume))]
    public class NavVolumeEditor : UnityEditor.Editor {
        private static Color _handleColor = new Color(127f, 214f, 244f, 100f) / 255;
        private static Color _handleColorSelected = new Color(127f, 214f, 244f, 210f) / 255;
        private static Color _handleColorDisabled = new Color(127f * 0.75f, 214f * 0.75f, 244f * 0.75f, 100f) / 255;

        private BoxBoundsHandle _boundsHandle = new BoxBoundsHandle();

        private static Vector3[] _previewVertices;
        private static Dictionary<int, string> _previewVertexRegions;
        private static Mesh _previewMesh;
        private NavVolume[] _allVolumes;

        private bool EditingCollider => EditMode.editMode == EditMode.SceneViewEditMode.Collider && EditMode.IsOwner(this);

        public override void OnInspectorGUI() {
            NavVolume volume = (NavVolume) target;
            serializedObject.Update();

            CheckPrefabInstanceWithoutInstanceID();
            
            EditorGUILayout.LabelField("Runtime Settings", EditorStyles.boldLabel);
            
            // No editing Data or InstanceID directly.
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.Data));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.InstanceID));
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("↻", GUILayout.Width(32)))
            {
                serializedObject.ApplyModifiedProperties();
                volume.UpdateUniqueID();
                serializedObject.Update();
            }
            EditorGUILayout.EndHorizontal();
            
            // Bounds field and button to edit in the scene view (like a box collider).
            EditMode.DoEditModeInspectorModeButton(EditMode.SceneViewEditMode.Collider, "Edit Volume",
                                                   EditorGUIUtility.IconContent("EditCollider"), GetBounds, this);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.Bounds));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.AutoDetectMovement));
            
            EditorGUILayout.Space(NavEditorUtility.NarrowVerticalSpacing);
            EditorGUILayout.LabelField("Baking Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.BlockingLayers));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.StaticOnly));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.MaxAgentRadius));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.EnableMultiQuery));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.VoxelSize));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.MaxExternalLinkDistance));

            SerializedProperty useStartLocationsProp =
                serializedObject.FindProperty(NavVolume.PropNames.UseStartLocations);
            EditorGUILayout.PropertyField(useStartLocationsProp);
            
            // Draw StartLocations only if UseStartLocations is true.
            if (useStartLocationsProp.boolValue) {
                EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.StartLocations));
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.UseMultithreading));

            EditorGUILayout.Space(NavEditorUtility.NarrowVerticalSpacing);
            SerializedProperty visProp = serializedObject.FindProperty(NavVolume.PropNames.VisualizationMode);
            
            // Allow visualization settings to be hidden to reduce clutter.
            // Can use isExpanded on the property even though it is not normally expandable.
            visProp.isExpanded = EditorGUILayout.Foldout(visProp.isExpanded, "Visualization Settings", EditorStyles.foldoutHeader);
            if (visProp.isExpanded) {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(visProp);
                
                // When changing visualization mode, delete current visualization mesh to force regen.
                if (EditorGUI.EndChangeCheck()) {
                    volume.EditorOnlyPreviewMesh = null;
                }

                SerializedProperty visNeighborsProp = serializedObject.FindProperty(NavVolume.PropNames.VisualizeNeighbors);
                EditorGUILayout.PropertyField(visNeighborsProp);
                
                // Draw VisualizeNeighborsRegion only if VisualizeNeighbors is true.
                if (visNeighborsProp.boolValue) {
                    EditorGUI.indentLevel++;
                    SerializedProperty visNeighborRegionProp =
                        serializedObject.FindProperty(NavVolume.PropNames.VisualizeNeighborsRegion);
                    EditorGUILayout.PropertyField(visNeighborRegionProp, new GUIContent("Region"));
                    
                    // Clamp visualize region to valid values.
                    if (volume.Data != null) {
                        visNeighborRegionProp.intValue =
                            Mathf.Clamp(visNeighborRegionProp.intValue, 0, volume.Data.Regions.Count);
                    }
                    EditorGUI.indentLevel--;
                }

                SerializedProperty showVertsProp = serializedObject.FindProperty(NavVolume.PropNames.ShowVertexNumbers);
                EditorGUILayout.PropertyField(showVertsProp);
                
                // Draw ShowVertexNumbersRange only if ShowVertexNumbers is true.
                if (showVertsProp.boolValue) {
                    EditorGUI.indentLevel++;
                    SerializedProperty showVertsRangeProp =
                        serializedObject.FindProperty(NavVolume.PropNames.ShowVertexNumbersRange);
                    EditorGUILayout.PropertyField(showVertsRangeProp, new GUIContent("Range"));
                    showVertsRangeProp.floatValue = Mathf.Max(0, showVertsRangeProp.floatValue);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(serializedObject.FindProperty(NavVolume.PropNames.VisualizeVoxelQueries));
                EditorGUI.indentLevel--;
            }

            // Buttons
            EditorGUILayout.Space(NavEditorUtility.NarrowVerticalSpacing);
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            
            // Draw progress bar if baking or bake button otherwise.
            bool isBaking = NavVolumeBakingUtil.BakeProgress.TryGetValue(volume, out NavVolumeBakeProgress value);
            if (isBaking) {
                // ProgressBar is not available in layout drawing mode,
                // so draw an empty space then use that rect to draw the progress bar.
                EditorGUILayout.Space(EditorGUIUtility.singleLineHeight, true);
                Rect rect = GUILayoutUtility.GetLastRect();
                EditorGUI.ProgressBar(rect, value.Progress, $"{value.Operation}: {Mathf.RoundToInt(value.Progress * 100)}%");
            } else {
                if (GUILayout.Button("Bake", GUILayout.ExpandWidth(true))) {
                    serializedObject.ApplyModifiedProperties();
                    volume.UpdateUniqueID();
                    NavVolumeBakingUtil.GetOrCreateData(volume);
                    NavVolumeBakingUtil.BakeData(volume);
                    serializedObject.Update();
                }
            }

            // Draw cancel button if baking or clear button otherwise.
            if (isBaking) {
                if (GUILayout.Button("Cancel", GUILayout.Width(75))) {
                    NavVolumeBakingUtil.CancelBake(volume);
                }
            } else {
                if (GUILayout.Button("Clear", GUILayout.Width(75))) {
                    volume.UpdateUniqueID();
                    serializedObject.Update();
                    NavVolumeBakingUtil.ClearData(volume);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(NavVolumeBakingUtil.BakeProgress.Count > 0);
            if (GUILayout.Button("Bake All", GUILayout.ExpandWidth(true))) {
                serializedObject.ApplyModifiedProperties();
                foreach (NavVolume navVolume in _allVolumes) {
                    if (NavVolumeBakingUtil.BakeProgress.TryGetValue(navVolume, out NavVolumeBakeProgress _)) continue;
                    navVolume.UpdateUniqueID();
                    NavVolumeBakingUtil.GetOrCreateData(navVolume);
                    NavVolumeBakingUtil.BakeData(navVolume);
                }
                serializedObject.Update();
            }
            if (GUILayout.Button("Clear All", GUILayout.Width(75))) {
                serializedObject.ApplyModifiedProperties();
                foreach (NavVolume navVolume in _allVolumes) {
                    navVolume.UpdateUniqueID();
                    NavVolumeBakingUtil.ClearData(navVolume);
                }
                serializedObject.Update();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            
            
            if (NavVolumeBakingUtil.BakeProgress.Count > 0) {
                foreach (NavVolume navVolume in _allVolumes) {
                    if (NavVolumeBakingUtil.BakeProgress.TryGetValue(navVolume, out NavVolumeBakeProgress progress)) {
                        EditorGUILayout.BeginHorizontal();
                        
                        EditorGUILayout.Space(EditorGUIUtility.singleLineHeight, true);
                        Rect rect = GUILayoutUtility.GetLastRect();
                        EditorGUI.ProgressBar(rect, progress.Progress, $"{progress.Operation}: {Mathf.RoundToInt(progress.Progress * 100)}%");
                        
                        if (GUILayout.Button("Cancel", GUILayout.Width(75))) {
                            NavVolumeBakingUtil.CancelBake(volume);
                        }
                        
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }

            EditorGUILayout.BeginHorizontal();
            
            EditorGUI.BeginDisabledGroup(volume.Data == null);
            if (GUILayout.Button("Generate External Links")) {
                NavVolumeExternalLinkUtil.GenerateExternalLinks(volume);
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Generate All External Links")) {
                NavVolumeExternalLinkUtil.GenerateAllExternalLinks();
            }
            
            EditorGUILayout.EndHorizontal();;

            EditorGUI.BeginDisabledGroup(volume.EditorOnlyPreviewMesh == null);
            if (GUILayout.Button("Export Preview")) {
                NavEditorUtility.ExportPreviewMesh(volume.EditorOnlyPreviewMesh);
            }
            EditorGUI.EndDisabledGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void CheckPrefabInstanceWithoutInstanceID() {
            NavVolume volume = (NavVolume)target;
            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(volume.gameObject);
            if (status != PrefabInstanceStatus.Connected) return;

            SerializedProperty property = serializedObject.FindProperty(NavVolume.PropNames.InstanceID);
            if (property.prefabOverride) return;
            volume.UpdateUniqueID();
            serializedObject.ApplyModifiedProperties();
        }

        private Bounds GetBounds() => ((NavVolume) target).Bounds;
        
        [DrawGizmo(GizmoType.Selected | GizmoType.Active | GizmoType.Pickable)]
        private static void RenderBoxGizmoSelected(NavVolume volume, GizmoType gizmoType)
        {
            // Draw the bounds editor gizmo.
            RenderBoxGizmo(volume, gizmoType, true);

            // Draw neighbor connections of a chosen region.
            if (volume.VisualizeNeighbors && volume.VisualizeNeighborsRegion >= 0 &&
                volume.Data != null && volume.VisualizeNeighborsRegion < volume.Data.Regions.Count) {
                DrawNeighborVisualization(volume, volume.VisualizeNeighborsRegion);
            }

            // Draw all external links.
            if (volume.Data != null) {
                DrawExternalLinkVisualization(volume);
            }
        }

        [DrawGizmo(GizmoType.NotInSelectionHierarchy | GizmoType.Pickable)]
        private static void RenderBoxGizmoNotSelected(NavVolume volume, GizmoType gizmoType)
        {
            RenderBoxGizmo(volume, gizmoType, false);
        }
        
        // Draw the bounds editor gizmo.
        private static void RenderBoxGizmo(NavVolume volume, GizmoType gizmoType, bool selected)
        {
            Color color = selected ? _handleColorSelected : _handleColor;
            if (!volume.enabled)
                color = _handleColorDisabled;

            Color oldColor = Gizmos.color;
            Matrix4x4 oldMatrix = Gizmos.matrix;

            // Use the unscaled matrix for the NavMeshSurface
            Matrix4x4 localToWorld = Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, Vector3.one);
            Gizmos.matrix = localToWorld;

            Bounds bounds = volume.Bounds;
            
            // Draw wireframe bounds.
            Gizmos.color = color;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            // If selected, draw filled bounds.
            if (selected && volume.enabled)
            {
                Color colorTrans = new Color(color.r * 0.75f, color.g * 0.75f, color.b * 0.75f, color.a * 0.15f);
                Gizmos.color = colorTrans;
                Gizmos.DrawCube(bounds.center, bounds.size);
            }
            
            Gizmos.matrix = oldMatrix;
            Gizmos.color = oldColor;
        }

        private void OnEnable() {
            // Camera.onPreCull used to render visualization mesh in built-in RP.
            Camera.onPreCull -= Camera_OnPreCull;
            Camera.onPreCull += Camera_OnPreCull;
            
            // RenderPipelineManager.beginCameraRendering used to render visualization mesh in SRPs.
            RenderPipelineManager.beginCameraRendering -= RenderPipelineManager_BeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += RenderPipelineManager_BeginCameraRendering;
            
            // NavVolumeBakingUtil.BakeProgressUpdated used to repaint the progress bar.
            NavVolumeBakingUtil.BakeProgressUpdated -= NavVolumeUtil_BakeProgressUpdated;
            NavVolumeBakingUtil.BakeProgressUpdated += NavVolumeUtil_BakeProgressUpdated;

            _allVolumes = FindObjectsOfType<NavVolume>();
        }

        private void OnDisable() {
            Camera.onPreCull -= Camera_OnPreCull;
            RenderPipelineManager.beginCameraRendering -= RenderPipelineManager_BeginCameraRendering;
            NavVolumeBakingUtil.BakeProgressUpdated -= NavVolumeUtil_BakeProgressUpdated;
        }

        private void RenderPipelineManager_BeginCameraRendering(ScriptableRenderContext ctx, Camera camera) {
            CameraWillRender(camera);
        }

        private void Camera_OnPreCull(Camera camera) {
            CameraWillRender(camera);
        }

        private void NavVolumeUtil_BakeProgressUpdated(NavVolume volume) {
            if (volume != target) return;
            Repaint();
        }

        private void CameraWillRender(Camera camera) {
            if (!target || !camera || camera.gameObject.scene.isLoaded) return;
            RenderVisualization((NavVolume) target, camera);
        }

        private static void RenderVisualization(NavVolume volume, Camera camera) {
            if (!volume.isActiveAndEnabled) return;
            
            // If volume is baked and mode is Blocking or Final, generate the mesh.
            // All other modes must be generated while baking.
            if (volume.Data != null &&
                volume.EditorOnlyPreviewMesh == null &&
                volume.Data.Vertices != null) {
                if (volume.VisualizationMode == NavVolumeVisualizationMode.Final) {
                    NavVolumeBakingUtil.BuildTriangulationPreviewMesh(
                        volume, volume.Data.Vertices, volume.Data.Regions.Select(r => r.Indices).ToList());
                } else if (volume.VisualizationMode == NavVolumeVisualizationMode.Blocking) {
                    NavVolumeBakingUtil.BuildTriangulationPreviewMesh(
                        volume, volume.Data.Vertices, new List<IReadOnlyList<int>> { volume.Data.BlockingTriangleIndices });
                }
            }
            
            // Draw the mesh.
            Mesh mesh = volume.EditorOnlyPreviewMesh;
            Material[] mats = volume.EditorOnlyPreviewMaterials;
            Matrix4x4 matrix = volume.transform.localToWorldMatrix;
            if (mesh != null && mats != null) {
                for (int i = 0; i < mesh.subMeshCount && i < mats.Length; i++) {
                    //if (i != 0 && i != mesh.subMeshCount / 2) continue;
                    Graphics.DrawMesh(mesh, matrix, mats[i], 0, camera, i);
                }
            }
        }

        private void OnSceneGUI() {
            NavVolume volume = (NavVolume) target;
            // Draw collider editing handle.
            if (EditingCollider) {
                Bounds bounds = volume.Bounds;
                Color color = volume.enabled ? _handleColor : _handleColorDisabled;
                Matrix4x4 localToWorld =
                    Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, Vector3.one);
                using (new Handles.DrawingScope(color, localToWorld)) {
                    _boundsHandle.center = bounds.center;
                    _boundsHandle.size = bounds.size;

                    EditorGUI.BeginChangeCheck();
                    _boundsHandle.DrawHandle();
                    if (EditorGUI.EndChangeCheck()) {
                        Undo.RecordObject(volume, "Modified HyperNavVolume");
                        Vector3 center = _boundsHandle.center;
                        Vector3 size = _boundsHandle.size;
                        bounds.center = center;
                        bounds.size = size;
                        volume.Bounds = bounds;
                        EditorUtility.SetDirty(target);
                    }
                }
            }

            // Draw handles for each start location.
            if (volume.UseStartLocations) {
                serializedObject.Update();
                SerializedProperty prop = serializedObject.FindProperty(NavVolume.PropNames.StartLocations);
                if (prop.isExpanded) {
                    for (int i = 0; i < prop.arraySize; i++) {
                        SerializedProperty element = prop.GetArrayElementAtIndex(i);
                        Vector3 worldPos = volume.transform.TransformPoint(element.vector3Value);
                        Vector3 movedPos = Handles.PositionHandle(worldPos, volume.transform.rotation);
                        if (movedPos != worldPos) {
                            movedPos.x = Mathf.Round(movedPos.x / volume.VoxelSize) * volume.VoxelSize;
                            movedPos.y = Mathf.Round(movedPos.y / volume.VoxelSize) * volume.VoxelSize;
                            movedPos.z = Mathf.Round(movedPos.z / volume.VoxelSize) * volume.VoxelSize;
                            
                            element.vector3Value = volume.transform.InverseTransformPoint(movedPos);
                        }
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }

            // Draw vertex numbers.
            if (volume.ShowVertexNumbers) {
                DrawVertexNumbers(volume);
            }
        }

        private static void DrawNeighborVisualization(NavVolume volume, int regionIndex) {
            NavRegionData region = volume.Data.Regions[regionIndex];

            Matrix4x4 mat = Gizmos.matrix;
            Color color = Gizmos.color;
            Matrix4x4 volumeTransform = volume.transform.localToWorldMatrix;
            Gizmos.matrix *= volumeTransform;
            Gizmos.color = Color.green;
            using Handles.DrawingScope _ = new Handles.DrawingScope(new Color(0, 1, 0, 0.3f), volumeTransform);

            foreach (NavInternalLinkData connection in region.InternalLinks) {
                // Draw triangle connections as polygons.
                foreach (Triangle triangle in connection.Triangles) {
                    Vector3 v1 = volume.Data.Vertices[triangle.Vertex1];
                    Vector3 v2 = volume.Data.Vertices[triangle.Vertex2];
                    Vector3 v3 = volume.Data.Vertices[triangle.Vertex3];
                    Handles.DrawAAConvexPolygon(v1, v2, v3);
                }

                // Draw edge connections as lines.
                foreach (Edge edge in connection.Edges) {
                    Vector3 v1 = volume.Data.Vertices[edge.Vertex1];
                    Vector3 v2 = volume.Data.Vertices[edge.Vertex2];
                    Gizmos.DrawLine(v1, v2);
                }
                
                // Draw vertex connections as small spheres.
                foreach (int vertex in connection.Vertices) {
                    Vector3 v = volume.Data.Vertices[vertex];
                    Gizmos.DrawSphere(v, 0.05f);
                }
            }

            Gizmos.matrix = mat;
            Gizmos.color = color;
        }

        private static void DrawExternalLinkVisualization(NavVolume volume) {
            if (volume.Data == null || volume.Data.Regions == null) return;
            Color color = Gizmos.color;
            Gizmos.color = Color.green;

            foreach (NavRegionData region in volume.Data.Regions) {
                if (region.ExternalLinks == null) continue;
                foreach (NavExternalLinkData link in region.ExternalLinks) {
                    // If link has nonzero length, draw it as a line, otherwise as a small sphere.
                    Vector3 linkPos = volume.Data.ExternalLinksAreLocalSpace
                        ? volume.transform.TransformPoint(link.FromPosition)
                        : link.FromPosition;
                    
                    if (Vector3.SqrMagnitude(link.FromPosition - link.ToPosition) < 0.0001f) {
                        Gizmos.DrawWireSphere(linkPos, 0.1f);
                    } else {
                        Vector3 toPos = volume.Data.ExternalLinksAreLocalSpace
                            ? volume.transform.TransformPoint(link.ToPosition)
                            : link.ToPosition;
                        Gizmos.DrawLine(linkPos, toPos);
                    }
                }
            }

            Gizmos.color = color;
        }

        // Cache info on vertex indices to make drawing them faster.
        private static void CacheVertexNumbers(NavVolume volume) {
            _previewVertices = volume.EditorOnlyPreviewMesh.vertices;
            _previewVertexRegions = new Dictionary<int, string>();
            _previewMesh = volume.EditorOnlyPreviewMesh;

            Dictionary<int, HashSet<int>> vertexRegionSets = new Dictionary<int, HashSet<int>>();

            for (int i = 0; i < volume.EditorOnlyPreviewMesh.subMeshCount; i++) {
                int[] indices = volume.EditorOnlyPreviewMesh.GetIndices(i);
                for (int j = 0; j < indices.Length; j++) {
                    int index = indices[j];
                    if (!vertexRegionSets.TryGetValue(index, out HashSet<int> regions)) {
                        regions = new HashSet<int>();
                        vertexRegionSets[index] = regions;
                    }

                    regions.Add(i);
                    _previewVertexRegions[index] = $"{index}: [{string.Join(", ", regions)}]";
                }
            }
        }

        // Draw cached vertex numbers.
        private static void DrawVertexNumbers(NavVolume volume) {
            Camera cam = Camera.current;

            if (volume.EditorOnlyPreviewMesh == null || cam == null) return;

            if (_previewVertices == null || _previewVertexRegions == null ||
                _previewMesh != volume.EditorOnlyPreviewMesh) {
                CacheVertexNumbers(volume);
            }

            float range2 = volume.ShowVertexNumbersRange * volume.ShowVertexNumbersRange;
            if (volume.EditorOnlyPreviewMesh != null && cam != null) {
                foreach (var pair in _previewVertexRegions) {
                    Vector3 v = volume.transform.TransformPoint(_previewVertices[pair.Key]);
                    if (Vector3.SqrMagnitude(v - cam.transform.position) < range2) {
                        Handles.Label(v, pair.Value);
                    }
                }
            }
        }
    }
}