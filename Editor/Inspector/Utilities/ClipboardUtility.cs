using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;

namespace VaporEditor.Inspector
{
    public static class ClipboardUtility
    {
        public static object CopyBuffer { get; set; }

        // public static void WriteToBuffer(InspectorFieldNode node)
        // {
        //     CopyBuffer = node.Property.boxedValue;
        // }

        public static void WriteToBuffer(object copyTarget)
        {
            CopyBuffer = CopyObject(copyTarget);
        }

        public static object CopyObject(object objSource)
        {
            using MemoryStream stream = new();
            BinaryFormatter formatter = new();
            formatter.Serialize(stream, objSource);
            stream.Position = 0;
            return formatter.Deserialize(stream);
        }

        // public static bool CanReadFromBuffer(InspectorFieldNode node)
        // {
        //     return CopyBuffer != null && (CopyBuffer.GetType() == node.FieldInfo.FieldType || CopyBuffer.GetType().IsSubclassOf(node.FieldInfo.FieldType));
        // }
        
        public static bool CanReadFromBuffer(Type type)
        {
            return CopyBuffer != null && (CopyBuffer.GetType() == type || CopyBuffer.GetType().IsSubclassOf(type));
        }
        
        // public static void ReadFromBuffer(InspectorFieldNode node)
        // {
        //     // Debug.Log($"Read: {CopyBuffer.GetType()} - {drawerInfo.FieldInfo.FieldType}");
        //     var isSubclassOrType = (CopyBuffer.GetType() == node.FieldInfo.FieldType || CopyBuffer.GetType().IsSubclassOf(node.FieldInfo.FieldType));
        //     if (!isSubclassOrType)
        //     {
        //         return;
        //     }
        //     node.Property.boxedValue = CopyBuffer;
        //     node.Property.serializedObject.ApplyModifiedProperties();
        // }
        
        public static void ReadFromBuffer(SerializedProperty property, Type type)
        {
            // Debug.Log($"Read: {CopyBuffer.GetType()} - {drawerInfo.FieldInfo.FieldType}");
            var isSubclassOrType = CopyBuffer.GetType() == type || CopyBuffer.GetType().IsSubclassOf(type);
            if (!isSubclassOrType)
            {
                return;
            }
            property.boxedValue = CopyBuffer;
            property.serializedObject.ApplyModifiedProperties();
        }

        public static void ReadFromBuffer(InspectorTreeProperty property)
        {
            // Debug.Log($"Read: {CopyBuffer.GetType()} - {drawerInfo.FieldInfo.FieldType}");
            var isSubclassOrType = CopyBuffer.GetType() == property.PropertyType || CopyBuffer.GetType().IsSubclassOf(property.PropertyType);
            if (!isSubclassOrType)
            {
                return;
            }
            property.SetValue(CopyBuffer);
            property.InspectorObject.ApplyModifiedProperties();
        }
    }
}
