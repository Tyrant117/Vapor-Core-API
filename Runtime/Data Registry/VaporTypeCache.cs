using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor
{
    public static class VaporTypeCache
    {
        private static readonly HashSet<Type> s_CachedTypes = new HashSet<Type>();
        private static bool s_IsInitialized;
        
        /// <summary>
        /// Ensures the cache is initialized before any operations are performed.
        /// </summary>
        private static void EnsureInitialized()
        {
            if (s_IsInitialized)
            {
                return;
            }
            
            InitializeCache();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        public static void EditorInitialize()
        {
            InitializeCache();
        }
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        public static void RuntimeInitialize()
        {
            s_IsInitialized = false;
            InitializeCache();
        }

        /// <summary>
        /// Initializes the type cache by scanning for types inheriting from MyBaseType
        /// and marked with TypeCacheAttribute.
        /// </summary>
        public static void InitializeCache()
        {
            if (s_IsInitialized)
            {
                return;
            }

            s_CachedTypes.Clear(); // Clear any existing cache

            try
            {
                var main = Assembly.Load("Assembly-CSharp");
                if (main != null)
                {
                    ScanAssembly(main);
                }
            }
            catch (Exception)
            {
                // Debug.LogException(e);
            }

            // Get all assemblies in the current application domain
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                if (!assembly.IsDefined(typeof(TypeCacheAttribute), true))
                {
                    continue;
                }

                ScanAssembly(assembly);
            }

            s_IsInitialized = true;
            Debug.Log($"{TooltipMarkup.Class(nameof(VaporTypeCache))} initialized with {s_CachedTypes.Count} types.");
        }

        private static void ScanAssembly(Assembly assembly)
        {
            // Get all types defined in the assembly
            var types = assembly.GetTypes();
            foreach (Type type in types)
            {
                // *** THIS IS THE CRUCIAL CHANGE ***
                // Check if *any* of this type's base classes (up to System.Object) have the TypeCacheAttribute.
                // If the type itself has the attribute, it counts as its own "base type" in this context.
                Type currentBaseType = type;
                bool inheritsFromAttributedBase = false;
                while (currentBaseType != null && currentBaseType != typeof(object))
                {
                    if (currentBaseType.IsDefined(typeof(TypeCacheAttribute), true)) // 'false' for direct definition
                    {
                        inheritsFromAttributedBase = true;
                        break; // Found an attributed base type
                    }

                    currentBaseType = currentBaseType.BaseType;
                }

                // 2. If not found in class inheritance, check implemented interfaces
                if (!inheritsFromAttributedBase)
                {
                    if (type.GetInterfaces().Any(implementedInterface => implementedInterface.IsDefined(typeof(TypeCacheAttribute), true)))
                    {
                        inheritsFromAttributedBase = true;
                    }
                }

                if (inheritsFromAttributedBase)
                {
                    s_CachedTypes.Add(type);
                    // Debug.Log($"{TooltipMarkup.Class(nameof(VaporTypeCache))} Cached {type.AssemblyQualifiedName}");
                }
            }
        }

        /// <summary>
        /// Gets all cached types.
        /// </summary>
        public static IReadOnlyCollection<Type> GetAllCachedTypes()
        {
            EnsureInitialized();
            return s_CachedTypes;
        }

        /// <summary>
        /// Filters the cached types to include only those that are assignable from a specified base type or interface.
        /// </summary>
        /// <typeparam name="TBaseType">The base type or interface to derive from.</typeparam>
        /// <returns>An IEnumerable of types derived from TBaseType.</returns>
        public static IEnumerable<Type> GetTypesDerivedFrom<TBaseType>()
        {
            EnsureInitialized();
            Type baseType = typeof(TBaseType);
            return s_CachedTypes.Where(type => baseType.IsAssignableFrom(type));
        }

        /// <summary>
        /// Filters a collection of types to include only those marked with a specific attribute.
        /// This is an extension method, allowing it to be chained.
        /// </summary>
        /// <typeparam name="TAttribute">The attribute type to check for.</typeparam>
        /// <param name="types">The collection of types to filter.</param>
        /// <param name="inherit">Whether to search the inheritance chain for the attribute.</param>
        /// <returns>An IEnumerable of types marked with the specified attribute.</returns>
        public static IEnumerable<Type> GetTypesWithAttribute<TAttribute>(this IEnumerable<Type> types, bool inherit = true)
            where TAttribute : Attribute
        {
            return types.Where(type => type.IsDefined(typeof(TAttribute), inherit));
        }

        /// <summary>
        /// Convenience method to get all cached types marked with a specific attribute directly from the cache.
        /// </summary>
        /// <typeparam name="TAttribute">The attribute type to check for.</typeparam>
        /// <param name="inherit">Whether to search the inheritance chain for the attribute.</param>
        /// <returns>An IEnumerable of all cached types marked with the specified attribute.</returns>
        public static IEnumerable<Type> GetTypesWithAttribute<TAttribute>(bool inherit = true)
            where TAttribute : Attribute
        {
            EnsureInitialized();
            return s_CachedTypes.Where(type => type.IsDefined(typeof(TAttribute), inherit));
        }

    }
}