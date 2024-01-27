using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Infohazard.Demos {
    [Serializable]
    public struct DemoStruct {
        [SerializeField] private Vector3 _vector3Field;
        [SerializeField] private LayerMask _layerMaskField;
    }
    
    public class DemoScriptableObject : ScriptableObject {
        [SerializeField] private int _intField;
        [SerializeField] private string _stringField;
        [SerializeField] private bool _boolField;
        [SerializeField] private DemoStruct _structField;
        [SerializeField] private float[] _arrayField;
    }
}