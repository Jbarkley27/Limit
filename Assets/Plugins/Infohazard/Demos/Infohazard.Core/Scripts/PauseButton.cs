using System.Collections;
using System.Collections.Generic;
using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class PauseButton : MonoBehaviour {
        public void TogglePaused() {
            Pause.Paused = !Pause.Paused;
        }
    }
}