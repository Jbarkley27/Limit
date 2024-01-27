using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class DrawSingleChildPropertyAttributeDemo : MonoBehaviour {
        [Header("This field is a vector, but we are only drawing the x component.")]
        [SerializeField, DrawSingleChildProperty("x")]
        private Vector3 _vectorField;
    }
}