using UnityEngine;
using Vapor;
using Vapor.Inspector;

namespace Vapor.Keys
{
    public class KeyChecker : MonoBehaviour
    {
        [InlineButton("Check", "Check")]
        public string Name;
        public ushort Key;

        private void Check()
        {
            Key = Name.GetStableHashU16();
        }
    }
}
