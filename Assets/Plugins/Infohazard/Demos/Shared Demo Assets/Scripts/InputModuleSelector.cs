using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Infohazard.Demos {
    public class InputModuleSelector : MonoBehaviour {
        [SerializeField] private BaseInputModule _standaloneInputModule;
        [SerializeField] private BaseInputModule _inputSystemInputModule;

        private void Awake() {
#if ENABLE_INPUT_SYSTEM
            if (_standaloneInputModule) _standaloneInputModule.enabled = false;
            if (_inputSystemInputModule) _inputSystemInputModule.enabled = true;
#else
            if (_standaloneInputModule) _standaloneInputModule.enabled = true;
            if (_inputSystemInputModule) _inputSystemInputModule.enabled = false;
#endif
        }
    }
}