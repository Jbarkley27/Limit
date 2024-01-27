using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class EditNameOnlyAttributeDemo : MonoBehaviour {
        [SerializeField, EditNameOnly] private DemoScriptableObject _asset;
    }
}