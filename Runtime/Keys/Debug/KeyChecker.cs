using UnityEngine;
using Vapor;
using Vapor.Inspector;
using Vapor.Unsafe;

namespace Vapor.Keys
{
    public class KeyChecker : MonoBehaviour
    {
        [InlineButton("Check", "Check")]
        public string Name;
        public uint Key;

        private void Check()
        {
            Key = Name.Hash32();
        }
    }
}
