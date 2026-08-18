using System;
using Unity.Scripting.LifecycleManagement;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    [NoAutoStaticsCleanup]
    public class NativeClassExtensionUtilities
    {
        private static Type _extensionOfNativeClassAttribute;

        public static bool ExtendsANativeType(Type type)
        {
            _extensionOfNativeClassAttribute ??= Type.GetType("UnityEngine.ExtensionOfNativeClassAttribute, UnityEngine");
            return type.GetCustomAttributes(_extensionOfNativeClassAttribute, inherit: true).Length != 0;
        }

        public static bool ExtendsANativeType(Object obj)
        {
            return (object)obj != null && ExtendsANativeType(obj.GetType());
        }
    }
}
