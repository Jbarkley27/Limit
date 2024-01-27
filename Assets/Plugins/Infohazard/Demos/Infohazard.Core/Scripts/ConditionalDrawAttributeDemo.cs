using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class ConditionalDrawAttributeDemo : MonoBehaviour {
        [SerializeField] private bool _showField1;

        [SerializeField, ConditionalDraw(nameof(_showField1))]
        private string _field1;

        [SerializeField] private Mode _mode;

        [SerializeField, ConditionalDraw(nameof(_mode), Mode.Float)]
        private float _floatValue;

        [SerializeField, ConditionalDraw(nameof(_mode), Mode.Vector2)]
        private Vector2 _vector2Value;

        [SerializeField, ConditionalDraw(nameof(_mode), Mode.Vector3)]
        private Vector3 _vector3Value;
        
        public enum Mode {
            Float, Vector2, Vector3
        }
    }
}