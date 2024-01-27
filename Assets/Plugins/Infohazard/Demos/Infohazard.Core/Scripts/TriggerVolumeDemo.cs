using System;
using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class TriggerVolumeDemo : MonoBehaviour {
        [SerializeField] private TriggerVolume _volume;

        private void Awake() {
            _volume.TriggerEntered += VolumeOnTriggerEntered;
            _volume.TriggerExited += VolumeOnTriggerExited;
            _volume.AllExited += VolumeOnAllExited;
        }

        private void OnDestroy() {
            _volume.TriggerEntered -= VolumeOnTriggerEntered;
            _volume.TriggerExited -= VolumeOnTriggerExited;
            _volume.AllExited -= VolumeOnAllExited;
        }

        private void VolumeOnTriggerEntered(GameObject obj) {
            Debug.Log($"Object {obj.name} entered trigger.");
        }

        private void VolumeOnTriggerExited(GameObject obj) {
            Debug.Log($"Object {obj.name} exited trigger.");
        }

        private void VolumeOnAllExited(GameObject obj) {
            Debug.Log("All objects have exited trigger.");
        }
    }
}