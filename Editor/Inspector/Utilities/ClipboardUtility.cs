using System;
using System.Runtime.Serialization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public static class ClipboardUtility
    {
        public static object CopyBuffer { get; set; }

        public static void WriteToBuffer(object copyTarget)
        {
            CopyBuffer = CopyObject(copyTarget);
        }

        /// <summary>
        /// Deep copies a value through Unity's own serializer.
        /// <para>
        /// This used to run through BinaryFormatter, which needs [Serializable] on every type in the graph
        /// and cannot round-trip a UnityEngine.Object reference - so it threw for most inspector types.
        /// Unity's serializer follows the same rules the inspector already draws by, and
        /// <see cref="EditorJsonUtility"/> preserves object references.
        /// </para>
        /// </summary>
        public static object CopyObject(object objSource)
        {
            if (objSource == null)
            {
                return null;
            }

            // A Unity object is a reference. Copying one would make a different asset rather than another
            // handle to the same one, so the reference itself is what gets copied.
            if (objSource is Object unityObject)
            {
                return unityObject;
            }

            var type = objSource.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            {
                return objSource;
            }

            var json = EditorJsonUtility.ToJson(objSource);

            // A boxed struct cannot be written through in place, so it round-trips into a fresh instance.
            if (type.IsValueType)
            {
                return JsonUtility.FromJson(json, type);
            }

            // Uninitialized rather than constructed - the source's own constructor may have required
            // arguments, and every serialized member is about to be overwritten anyway.
            var clone = FormatterServices.GetUninitializedObject(type);
            EditorJsonUtility.FromJsonOverwrite(json, clone);
            return clone;
        }

        public static bool CanReadFromBuffer(Type type)
        {
            return CopyBuffer != null && (CopyBuffer.GetType() == type || CopyBuffer.GetType().IsSubclassOf(type));
        }

        public static void ReadFromBuffer(SerializedProperty property, Type type)
        {
            if (!CanReadFromBuffer(type))
            {
                return;
            }

            property.boxedValue = CopyBuffer;
            property.serializedObject.ApplyModifiedProperties();
        }

        public static void ReadFromBuffer(InspectorTreeProperty property)
        {
            if (!CanReadFromBuffer(property.PropertyType))
            {
                return;
            }

            // Copied again on paste, so pasting twice doesn't hand out the same instance both times.
            property.SetValue(CopyObject(CopyBuffer));
            property.InspectorObject.ApplyModifiedProperties();
        }
    }
}
