using System;
using Unity.Scripting.LifecycleManagement;

namespace Vapor
{
    [NoAutoStaticsCleanup]
    public static class TypeUtility
    {
        public delegate bool TryCustomCast(object value, Type targetType, out object result);

        /// <summary>
        /// Optional package-level conversion hook for foreign token models. Core deliberately does
        /// not depend on any particular JSON implementation.
        /// </summary>
        public static TryCustomCast CustomCaster { get; set; }

        public static object CastToType(object obj, Type targetType)
        {
            if (obj == null)
            {
                if (targetType.IsClass || targetType.IsInterface || Nullable.GetUnderlyingType(targetType) != null)
                {
                    return null; // Null is a valid value for reference types and nullable types
                }

                throw new ArgumentNullException(nameof(obj), "Cannot cast null to a non-nullable value type.");
            }

            // Check if the object is already of the target type
            if (targetType.IsAssignableFrom(obj.GetType()))
            {
                return obj; // No casting needed
            }

            if (targetType == typeof(Type) && obj is string s)
            {
                // Assume that a proper assembly qualified type is being sent.
                return Type.GetType(s);
            }
            
            if (targetType.IsEnum && obj.GetType().IsPrimitive)
            {
                return Enum.ToObject(targetType, (long)obj);
            }

            if (CustomCaster != null && CustomCaster(obj, targetType, out var customResult))
            {
                return customResult;
            }

            // The else covers structs
            return Convert.ChangeType(obj, targetType);
        }

        public static object SafeCastToType(object obj, Type targetType)
        {
            if (obj == null)
            {
                return null;
            }

            // Check if the object is already of the target type
            if (targetType.IsAssignableFrom(obj.GetType()))
            {
                return obj; // No casting needed
            }

            if (targetType == typeof(Type) && obj is string s)
            {
                // Assume that a proper assembly qualified type is being sent.
                return Type.GetType(s);
            }
            
            if (targetType.IsEnum && obj.GetType().IsPrimitive)
            {
                return Enum.ToObject(targetType, (long)obj);
            }

            if (CustomCaster != null && CustomCaster(obj, targetType, out var customResult))
            {
                return customResult;
            }

            // The else covers structs
            return typeof(IConvertible).IsAssignableFrom(obj.GetType()) ? Convert.ChangeType(obj, targetType) : null;
        }
    }
}
