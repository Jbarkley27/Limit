using System;
using System.Collections;
using System.Collections.Generic;
using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class DemoMakeSphereBounce : MonoBehaviour {
        [SerializeField] private UniqueNameListEntry _objectName;

        private GameObject _object;

        private void OnEnable() {
            _object = null;
        }

        private void Update() {
            if (_objectName != null && _object == null) {
                UniqueNamedObject.TryGetObject(_objectName, out _object);
            }
            
            if (_object) {
                _object.transform.position = _object.transform.position.WithY(Mathf.Sin(Time.time * 3));
            }
        }

        private void OnValidate() {
            _object = null;
        }
    }
}