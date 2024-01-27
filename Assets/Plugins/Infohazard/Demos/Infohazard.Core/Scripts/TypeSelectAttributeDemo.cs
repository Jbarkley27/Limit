using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class TypeSelectAttributeDemo : MonoBehaviour {
        [SerializeField, TypeSelect(typeof(MonoBehaviour))]
        private string _monoBehaviorType;
    }
}