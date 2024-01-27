using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class AssetDropdownDemo : MonoBehaviour {
        [SerializeField, AssetDropdown] private DemoScriptableObject _assetReference;
    }
}