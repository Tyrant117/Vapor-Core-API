using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace Vapor.Serialization
{
    /// <summary>
    /// Implemented by the generated code for every <see cref="VslCloneableAttribute"/> type. Virtual
    /// through the hierarchy, so cloning through a base-typed reference yields the runtime type.
    /// </summary>
    public interface IVslCloneable
    {
        /// <summary>A deep copy of this instance over its VSL-serialized members.</summary>
        object VslCloneObject();

        /// <summary>
        /// Deep-copies <paramref name="source"/> (which must be this instance's type or a base of it)
        /// into this instance. Virtual through the hierarchy, so a base-typed call still copies the
        /// runtime type's members.
        /// </summary>
        void VslCopyFrom(object source);
    }

    /// <summary>
    /// Deep copies for anything VSL knows how to serialize. Generated <c>Clone()</c> methods call
    /// this for members whose types have no generated clone of their own.
    /// </summary>
    /// <remarks>
    /// The order of preference: a generated clone (<see cref="IVslCloneable"/>), value types and
    /// strings by value, <c>UnityEngine.Object</c> references by reference (assets are shared, never
    /// duplicated), collections rebuilt element by element, and finally a VSL text round trip —
    /// slow, but exactly as faithful as serialization itself, which is the definition of "deep copy"
    /// here.
    /// </remarks>
    [NoAutoStaticsCleanup]
    public static class VslClone
    {
        /// <summary>
        /// A deep copy through the text format, on the runtime type — the correct-but-slow path that
        /// generated abstract roots fall back to for subclasses without their own generated clone.
        /// </summary>
        public static object RoundTrip(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            return Vsl.Deserialize(Vsl.Serialize(value, type), type);
        }

        public static T DeepCopy<T>(T value)
        {
            if (value == null)
            {
                return default;
            }

            var copy = DeepCopy((object)value, typeof(T));
            return copy is T typed ? typed : default;
        }

        public static object DeepCopy(object value, Type declaredType)
        {
            if (value == null)
            {
                return null;
            }

            switch (value)
            {
                case IVslCloneable cloneable:
                    return cloneable.VslCloneObject();
                case string:
                    return value;
                case UnityEngine.Object:
                    return value;
            }

            var type = value.GetType();
            if (type.IsValueType && !ContainsReferences(type))
            {
                return value;   // boxed copy already
            }

            if (type.IsArray)
            {
                var source = (Array)value;
                var elementType = type.GetElementType();
                var result = Array.CreateInstance(elementType, source.Length);
                for (int i = 0; i < source.Length; i++)
                {
                    result.SetValue(DeepCopy(source.GetValue(i), elementType), i);
                }

                return result;
            }

            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>))
                {
                    var result = (IList)Activator.CreateInstance(type);
                    var elementType = type.GetGenericArguments()[0];
                    foreach (var item in (IEnumerable)value)
                    {
                        result.Add(DeepCopy(item, elementType));
                    }

                    return result;
                }

                if (definition == typeof(Dictionary<,>))
                {
                    var result = (IDictionary)Activator.CreateInstance(type);
                    var arguments = type.GetGenericArguments();
                    foreach (DictionaryEntry entry in (IDictionary)value)
                    {
                        result.Add(DeepCopy(entry.Key, arguments[0]), DeepCopy(entry.Value, arguments[1]));
                    }

                    return result;
                }
            }

            // Everything else: through the text format, with the runtime type so a derived instance
            // in a base-typed slot comes back as itself.
            var text = Vsl.Serialize(value, type);
            return Vsl.Deserialize(text, type);
        }

        private static readonly Dictionary<Type, bool> s_ContainsReferences = new();

        private static bool ContainsReferences(Type type)
        {
            if (type.IsPrimitive || type.IsEnum) return false;
            lock (s_ContainsReferences)
            {
                if (s_ContainsReferences.TryGetValue(type, out var known)) return known;
                bool result = false;
                foreach (var field in type.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    var fieldType = field.FieldType;
                    if (!fieldType.IsValueType || (fieldType != type && ContainsReferences(fieldType)))
                    {
                        result = true;
                        break;
                    }
                }

                s_ContainsReferences[type] = result;
                return result;
            }
        }
    }
}
