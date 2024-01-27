// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Infohazard.HyperNav;
using UnityEditor;
using UnityEngine;

namespace Infohazard.HyperNav.Editor {
    [CustomEditor(typeof(NavPathfinder))]
    public class NavPathfinderEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            serializedObject.Update();
            
            EditorGUILayout.LabelField("Instance Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(NavPathfinder.PropNames.IsMainInstance));
            SerializedProperty modeProp = serializedObject.FindProperty(NavPathfinder.PropNames.Mode);
            EditorGUILayout.PropertyField(modeProp);
            NavPathfindingMode mode = (NavPathfindingMode)modeProp.enumValueIndex;

            // Draw help box explaining the mode.
            if (mode == NavPathfindingMode.MainThreadEndOfFrame) {
                EditorGUILayout.HelpBox(
                    "Pathfinding will be completed and the provided callback invoked in the next LateUpdate call.",
                    MessageType.Info);
            } else if (mode == NavPathfindingMode.MainThreadAsynchronous) {
                EditorGUILayout.HelpBox(
                    "Pathfinding will be completed over several frames, with a max number of pathfinding steps per frame shared between all requests.",
                    MessageType.Info);
            } else {
                EditorGUILayout.HelpBox(
                    "Pathfinding will be completed on another thread using Unity's C# Job System and Burst Compiler if it is enabled.",
                    MessageType.Info);
            }
            
            // Only have any algorithm settings to draw in MainThreadAsynchronous and JobThread.
            if (mode == NavPathfindingMode.MainThreadAsynchronous || mode == NavPathfindingMode.JobThread) {
                EditorGUILayout.Space(NavEditorUtility.NarrowVerticalSpacing);
                EditorGUILayout.LabelField("Algorithm Settings", EditorStyles.boldLabel);
            }

            // Draw different properties depending on pathfinding mode.
            if (mode == NavPathfindingMode.MainThreadAsynchronous) {
                SerializedProperty maxRequestsProp =
                    serializedObject.FindProperty(NavPathfinder.PropNames.MaxExecutingRequests);
                EditorGUILayout.PropertyField(maxRequestsProp);
                maxRequestsProp.intValue = Mathf.Max(0, maxRequestsProp.intValue);
                
                SerializedProperty maxOpsProp = serializedObject.FindProperty(NavPathfinder.PropNames.MaxPathOpsPerFrame);
                EditorGUILayout.PropertyField(maxOpsProp);
                maxOpsProp.intValue = Mathf.Max(1, maxOpsProp.intValue);
            }

            if (mode == NavPathfindingMode.JobThread) {
                SerializedProperty concurrentProp =
                    serializedObject.FindProperty(NavPathfinder.PropNames.MaxConcurrentJobs);
                EditorGUILayout.PropertyField(concurrentProp);
                concurrentProp.intValue = Mathf.Max(1, concurrentProp.intValue);
                
                SerializedProperty framesProp = serializedObject.FindProperty(NavPathfinder.PropNames.MaxCompletionFrames);
                EditorGUILayout.PropertyField(framesProp);
                framesProp.intValue = Mathf.Max(1, framesProp.intValue);
                if (framesProp.intValue > 3) {
                    EditorGUILayout.HelpBox(
                        "A value greater than 3 frames will reduce memory performance due to the internals of Unity's Job System.",
                        MessageType.Warning);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}