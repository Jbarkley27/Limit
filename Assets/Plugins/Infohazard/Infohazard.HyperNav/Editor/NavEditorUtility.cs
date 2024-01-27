// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.IO;
using System.Text;
using Infohazard.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Infohazard.HyperNav.Editor {
    /// <summary>
    /// Utility functions used internally, but you can use them too, I mean I'm not your boss.
    /// </summary>
    public static class NavEditorUtility {
        public const int NarrowVerticalSpacing = 8;
        
        /// <summary>
        /// Export a mesh as an OBJ file.
        /// </summary>
        /// <remarks>
        /// This is the most basic export possible, and should not be used for actual art.
        /// It is only used to more closely inspect a preview mesh in a modeling application.
        /// The material names will be included in the OBJ, but the MTL file is not created.
        /// Normals are also not included.
        /// </remarks>
        /// <param name="mesh">The mesh to export.</param>
        public static void ExportPreviewMesh(Mesh mesh) {
            string path = EditorUtility.SaveFilePanel("Save Mesh", Application.dataPath, "Preview.obj", "obj");
            if (string.IsNullOrEmpty(path)) return;

            StringBuilder builder = new StringBuilder();

            // Export vertex positions.
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) {
                Vector3 v = vertices[i];
                builder.Append($"v {v.x} {v.y} {v.z}").Append(Environment.NewLine);
            }

            // Export triangle indices.
            int smCount = mesh.subMeshCount;
            for (int i = 0; i < smCount; i++) {
                builder.Append($"usemtl mat{i}").Append(Environment.NewLine);
                int[] tris = mesh.GetIndices(i);
                int triCount = tris.Length / 3;
                for (int j = 0; j < triCount; j++) {
                    int t = j * 3;
                    if (tris[t] < 0) continue;
                    builder.Append($"f {tris[t + 0] + 1} {tris[t + 1] + 1} {tris[t + 2] + 1}").Append(Environment.NewLine);
                }
            }
            
            File.WriteAllText(path, builder.ToString());
        }

        /// <summary>
        /// Menu item to create a new NavVolume.
        /// </summary>
        [MenuItem("Tools/Infohazard/Create/Nav Volume", priority = 1)]
        public static void CreateVolume() {
            CoreEditorUtility.CreateGameObjectInSceneWithComponent<NavVolume>("NavVolume");
        }

        /// <summary>
        /// Menu item to create a new NavPathfinder.
        /// </summary>
        [MenuItem("Tools/Infohazard/Create/Nav Pathfinder", priority = 1)]
        public static void CreatePathfinder() {
            CoreEditorUtility.CreateGameObjectInSceneWithComponent<NavPathfinder>("NavPathfinder");
        }

        /// <summary>
        /// Menu item to create a new AvoidanceManager.
        /// </summary>
        [MenuItem("Tools/Infohazard/Create/Avoidance Manager", priority = 1)]
        public static void CreateAvoidanceManager() {
            CoreEditorUtility.CreateGameObjectInSceneWithComponent<AvoidanceManager>("AvoidanceManager");
        }
    }
}