using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Infohazard.Demos {
    public class DemoSineMovement : MonoBehaviour {
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private float _timeScale;
        [SerializeField] private float _distance;

        private void FixedUpdate() {
            float sine = Mathf.Sin(Time.time * _timeScale) * _distance;
            _rigidbody.MovePosition(_rigidbody.position + transform.forward * Time.fixedDeltaTime * sine);
        }
    }
}