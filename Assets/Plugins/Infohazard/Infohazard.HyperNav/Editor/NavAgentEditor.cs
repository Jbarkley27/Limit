// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Infohazard.HyperNav.Editor {
    [CanEditMultipleObjects]
    [CustomEditor(typeof(NavAgent), true)]
    public class NavAgentEditor : UnityEditor.Editor{
        public override void OnInspectorGUI() {
            using (new LocalizationGroup(target))
            {
                EditorGUI.BeginChangeCheck();
                serializedObject.Update();
                SerializedProperty iterator = serializedObject.GetIterator();
                for (bool enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false) {
                    using var scope = new EditorGUI.DisabledScope(iterator.propertyPath == "m_Script");
                    EditorGUILayout.PropertyField(iterator, true);

                    if (iterator.propertyPath == NavAgent.PropNames.AvoidanceAgent &&
                        targets.OfType<NavAgent>().Any(agent => agent.AvoidanceAgent == null)) {

                        if (GUILayout.Button("Add Avoidance Agent")) {
                            foreach (NavAgent agent in targets.OfType<NavAgent>()) {
                                Undo.RecordObject(agent, "Add Avoidance Agent");
                                AvoidanceAgent avoidanceAgent = Undo.AddComponent<AvoidanceAgent>(agent.gameObject);
                                agent.AvoidanceAgent = avoidanceAgent;
                                PrefabUtility.RecordPrefabInstancePropertyModifications(agent);
                            }
                        }
                    }
                }
                serializedObject.ApplyModifiedProperties();
                EditorGUI.EndChangeCheck();
            }
        }
    }
}