using Infohazard.Core;
using UnityEngine;

namespace Infohazard.Demos {
    public class TagDemo : MonoBehaviour {
        [SerializeField]
        private TagMask _tagValue = new TagMask(TagMask.PlayerMask | TagMask.GameControllerMask);
    }
}