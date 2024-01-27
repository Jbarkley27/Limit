using System;
using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class CubeSpawner : MonoBehaviour {
        [SerializeField] private SpawnRef _cubePrefab;
        [SerializeField] private PassiveTimer _dropTimer;
        [SerializeField] private PassiveTimer _inactivePeriodTimer;

        private void Awake() {
            _dropTimer.Initialize();
            _inactivePeriodTimer.Initialize();
        }

        private void OnEnable() {
            _cubePrefab.Retain();
        }

        private void OnDisable() {
            _cubePrefab.Release();
        }

        private void Update() {
            if (_inactivePeriodTimer.IsIntervalEnded) {
                _inactivePeriodTimer.StartInterval();
            }

            if (_inactivePeriodTimer.RatioSinceIntervalStart > 0.5f) {
                if (_dropTimer.TryConsume()) {
                    _cubePrefab.Spawn(SpawnParams.At(transform));
                }
            }
        }

        public void ToggleEnabled() {
            enabled = !enabled;
        }
    }
}