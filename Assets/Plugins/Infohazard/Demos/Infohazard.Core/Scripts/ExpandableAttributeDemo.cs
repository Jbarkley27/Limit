using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class ExpandableAttributeDemo : MonoBehaviour {
        [SerializeField] private int _intField;
        [SerializeField, Expandable] private DemoScriptableObject _objectReference;
    }
}