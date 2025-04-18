using UnityEngine;

namespace Vapor.Keys
{
    /// <summary>
    /// A scriptable object implementation of the IKey interface that derives its display name from the <see cref="ScriptableObject.name"/>
    /// </summary>
    //[CreateAssetMenu(menuName = "Vapor/Keys/Named Key",fileName = "NamedKey", order = VaporConfig.KeyPriority_LAST)]
    public class NamedKeySo : KeySo
    {
        public override string DisplayName => name;
    }
}
