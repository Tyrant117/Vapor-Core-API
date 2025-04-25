using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    public static class ReflectionUtility
    {
        public static readonly Func<FieldInfo, bool> FieldSearchPredicate = f => !f.IsDefined(typeof(HideInInspector))
                                                                                 && !f.IsDefined(typeof(NonSerializedAttribute))
                                                                                 && (f.IsPublic || f.IsDefined(typeof(SerializeField)));
        public static readonly Func<MethodInfo, bool> MethodSearchPredicate = f => f.IsDefined(typeof(ButtonAttribute));
        public static readonly Func<PropertyInfo, bool> PropertySearchPredicate = f => f.IsDefined(typeof(ShowInInspectorAttribute));

        private static readonly Dictionary<Type, List<Type>> s_TypeCache = new();
        private static readonly Dictionary<Type, List<FieldInfo>> s_FieldCache = new();
        private static readonly Dictionary<Type, List<PropertyInfo>> s_PropertyCache = new();
        private static readonly Dictionary<Type, List<MethodInfo>> s_MethodCache = new();
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> s_TypeNameMap = new(256);

        private static readonly List<FieldInfo> s_MatchingFields = new(16);
        private static readonly List<PropertyInfo> s_MatchingProperties = new(16);
        private static readonly List<MethodInfo> s_MatchingMethods = new(16);

        private static Assembly[] s_Assemblies;
        private static List<Type[]> s_TypesPerAssembly;
        private static List<Dictionary<string, Type>> s_AssemblyTypeMaps;
        private static readonly Dictionary<Type, List<Type>> s_AssignableTypes = new();

        private const BindingFlags k_SearchFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            s_TypeCache.Clear();
            s_FieldCache.Clear();
            s_PropertyCache.Clear();
            s_MethodCache.Clear();
            s_TypeNameMap.Clear();

            s_Assemblies = null;
            s_TypesPerAssembly = null;
            s_AssemblyTypeMaps = null;
            s_AssignableTypes.Clear();
        }

        public static List<FieldInfo> GetAllFields(Type type)
        {
            if (type == null)
            {
                return new List<FieldInfo>();
            }
            
            if (s_FieldCache.TryGetValue(type, out var fields))
            {
                return fields;
            }

            fields = new List<FieldInfo>(64);
            s_FieldCache.Add(type, fields);

            var types = GetSelfAndBaseTypes(type);
            for (var i = types.Count - 1; i >= 0; i--)
            {
                foreach (var fieldInfo in types[i].GetFields(k_SearchFlags))
                {
                    fields.Add(fieldInfo);
                }
            }
            return fields;
        }

        public static List<PropertyInfo> GetAllProperties(Type type)
        {
            if (type == null)
            {
                return new List<PropertyInfo>();
            }
            
            if (s_PropertyCache.TryGetValue(type, out var properties))
            {
                return properties;
            }

            properties = new List<PropertyInfo>(64);
            s_PropertyCache.Add(type, properties);

            var types = GetSelfAndBaseTypes(type);
            for (var i = types.Count - 1; i >= 0; i--)
            {
                foreach (var propertyInfo in types[i].GetProperties(k_SearchFlags))
                {
                    properties.Add(propertyInfo);
                }
            }
            return properties;
        }

        public static List<MethodInfo> GetAllMethods(Type type)
        {
            if (type == null)
            {
                return new List<MethodInfo>();
            }
            
            if (s_MethodCache.TryGetValue(type, out var methods))
            {
                return methods;
            }

            methods = new List<MethodInfo>(64);
            s_MethodCache.Add(type, methods);

            var types = GetSelfAndBaseTypes(type);
            for (var i = types.Count - 1; i >= 0; i--)
            {
                foreach (var methodInfo in types[i].GetMethods(k_SearchFlags))
                {
                    methods.Add(methodInfo);
                }
            }
            return methods;
        }

        public static List<FieldInfo> GetAllFieldsThatMatch(Type type, Func<FieldInfo, bool> predicate, bool exitOnFirstMatch, bool declaredOnly = false)
        {
            s_MatchingFields.Clear();
            var fields = GetAllFields(type);

            if (exitOnFirstMatch)
            {
                var fieldInfo = fields.FirstOrDefault(predicate);
                if (fieldInfo != null)
                {
                    s_MatchingFields.Add(fieldInfo);
                }

                return s_MatchingFields;
            }

            foreach (var fieldInfo in fields.Where(predicate))
            {
                if (declaredOnly)
                {
                    if(fieldInfo.DeclaringType == type)
                    {
                        s_MatchingFields.Add(fieldInfo);
                    }
                }
                else
                {
                    s_MatchingFields.Add(fieldInfo);
                }
            }
            return s_MatchingFields;
        }

        public static List<PropertyInfo> GetAllPropertiesThatMatch(Type type, Func<PropertyInfo, bool> predicate, bool exitOnFirstMatch, bool declaredOnly = false)
        {
            s_MatchingProperties.Clear();
            var properties = GetAllProperties(type);

            if (exitOnFirstMatch)
            {
                var propertyInfo = properties.FirstOrDefault(predicate);
                if (propertyInfo != null)
                {
                    s_MatchingProperties.Add(propertyInfo);
                }
                return s_MatchingProperties;
            }

            foreach (var propertyInfo in properties.Where(predicate))
            {
                if (declaredOnly)
                {
                    if(propertyInfo.DeclaringType == type)
                    {
                        s_MatchingProperties.Add(propertyInfo);
                    }
                }
                else
                {
                    s_MatchingProperties.Add(propertyInfo);
                }
            }
            return s_MatchingProperties;
        }

        public static List<MethodInfo> GetAllMethodsThatMatch(Type type, Func<MethodInfo, bool> predicate, bool exitOnFirstMatch, bool declaredOnly = false)
        {
            s_MatchingMethods.Clear();
            var methods = GetAllMethods(type);

            if (exitOnFirstMatch)
            {
                var methodInfo = methods.FirstOrDefault(predicate);
                if (methodInfo != null)
                {
                    s_MatchingMethods.Add(methodInfo);
                }
                return s_MatchingMethods;
            }

            foreach (var methodInfo in methods.Where(predicate))
            {
                if (declaredOnly)
                {
                    if(methodInfo.DeclaringType == type)
                    {
                        s_MatchingMethods.Add(methodInfo);
                    }
                }
                else
                {
                    s_MatchingMethods.Add(methodInfo);
                }
            }
            return s_MatchingMethods;
        }
        
        public static FieldInfo GetField(Type type, string fieldName)
        {
            if (type == null)
            {
                Debug.LogError("The type is null. Check for missing scripts.");
                return null;
            }

            return GetAllFieldsThatMatch(type, f => f.Name.Equals(fieldName, StringComparison.Ordinal), true).FirstOrDefault();
        }

        public static PropertyInfo GetProperty(Type type, string propertyName)
        {
            if (type == null)
            {
                Debug.LogError("The type is null. Check for missing scripts.");
                return null;
            }

            return GetAllPropertiesThatMatch(type, p => p.Name.Equals(propertyName, StringComparison.Ordinal), true).FirstOrDefault();
        }

        public static MethodInfo GetMethod(Type type, string methodName)
        {
            if (type == null)
            {
                Debug.LogError("The type is null. Check for missing scripts.");
                return null;
            }

            return GetAllMethodsThatMatch(type, m => m.Name.Equals(methodName, StringComparison.Ordinal), true).FirstOrDefault();
        }

        public static MemberInfo GetMember(Type type, string memberName)
        {
            //Debug.Log($"{type} | {memberName}");

            if (s_TypeNameMap.TryGetValue(type, out var map) && map.TryGetValue(memberName, out var member))
            {
                return member;
            }
            else
            {
                map = new Dictionary<string, MemberInfo>(256);
                s_TypeNameMap[type] = map;
            }

            var hasProperty = GetProperty(type, memberName);
            if (hasProperty != null)
            {
                map.Add(memberName, hasProperty);
                return hasProperty;
            }

            var hasMethod = GetMethod(type, memberName);
            if (hasMethod != null)
            {
                map.Add(memberName, hasMethod);
                return hasMethod;
            }

            var hasField = GetField(type, memberName);
            if (hasField != null)
            {
                map.Add(memberName, hasField);
                return hasField;
            }

            return null;
        }

        public static bool TryGetMemberValue<T>(object target, MemberInfo member, out T value)
        {
            if (member is FieldInfo fieldInfo)
            {
                value = (T)fieldInfo.GetValue(target);
                return true;

            }
            else if (member is PropertyInfo propertyInfo)
            {
                value = (T)propertyInfo.GetValue(target);
                return true;
            }
            else
            {
                value = default;
                return false;
            }
        }

        public static bool TrySetMemberValue(object target, MemberInfo member, object value)
        {
            if (member is FieldInfo fieldInfo)
            {

                fieldInfo.SetValue(target, value);
                return true;

            }
            else if (member is PropertyInfo propertyInfo)
            {
                propertyInfo.SetValue(target, value);
                return true;
            }
            else
            {
                return false;
            }
        }

        public static bool TryInvokeMember<T>(object target, MemberInfo member, object[] arguments, out T returnValue)
        {
            if (member is MethodInfo methodInfo)
            {
                var val = methodInfo.Invoke(target, arguments);
                returnValue = val is T rVal ? rVal : default;
                return true;
            }
            else
            {
                returnValue = default;
                return false;
            }
        }

        public static bool TryResolveMemberValue<T>(object target, MemberInfo memberInfo, object[] arguments, out T value)
        {
            return TryGetMemberValue(target, memberInfo, out value) || TryInvokeMember(target, memberInfo, arguments, out value);
        }

        /// <summary>
        ///		Get type and all base types of target, sorted as following:
        ///		<para />[target's type, base type, base's base type, ...]
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static List<Type> GetSelfAndBaseTypes(object target)
        {
            return GetSelfAndBaseTypes(target.GetType());
        }

        /// <summary>
        ///		Get type and all base types of target, sorted as following:
        ///		<para />[target's type, base type, base's base type, ...]
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public static List<Type> GetSelfAndBaseTypes(Type target)
        {
            if (s_TypeCache.TryGetValue(target, out var types))
            {
                return types;
            }
            else
            {
                types = new List<Type>(16);
                s_TypeCache.Add(target, types);

                types.Add(target);
                while (types[^1].BaseType != null)
                {
                    types.Add(types[^1].BaseType);
                }

                return types;
            }
        }

        public static bool IsPublicStatic(FieldInfo arg)
        {
            return arg.IsStatic && arg.IsPublic;
        }

        public static bool IsPublicStaticGet(PropertyInfo arg)
        {
            return arg.GetMethod.IsStatic && arg.GetMethod.IsPublic;
        }

        public static bool IsPublicStaticSet(PropertyInfo arg)
        {
            return arg.SetMethod.IsStatic && arg.SetMethod.IsPublic;
        }

        public static bool IsPublicStatic(MethodInfo arg)
        {
            return arg.IsStatic && arg.IsPublic;
        }

        #region - Assemblies -
        private static Assembly[] GetCachedAssemblies() { return s_Assemblies ??= AppDomain.CurrentDomain.GetAssemblies(); }

        private static List<Type[]> GetCachedTypesPerAssembly()
        {
            if (s_TypesPerAssembly != null)
                return s_TypesPerAssembly;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            s_TypesPerAssembly = new List<Type[]>(assemblies.Length);
            foreach (var assembly in assemblies)
            {
                try
                {
                    s_TypesPerAssembly.Add(assembly.GetTypes());
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip any assemblies that don't load properly -- suppress errors
                }
            }

            return s_TypesPerAssembly;
        }

        private static List<Dictionary<string, Type>> GetCachedAssemblyTypeMaps()
        {
            if (s_AssemblyTypeMaps != null)
                return s_AssemblyTypeMaps;

            var typesPerAssembly = GetCachedTypesPerAssembly();
            s_AssemblyTypeMaps = new List<Dictionary<string, Type>>(typesPerAssembly.Count);
            foreach (var types in typesPerAssembly)
            {
                try
                {
                    var typeMap = new Dictionary<string, Type>();
                    foreach (var type in types)
                    {
                        if (type.FullName != null) typeMap[type.FullName] = type;
                    }

                    s_AssemblyTypeMaps.Add(typeMap);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip any assemblies that don't load properly -- suppress errors
                }
            }

            return s_AssemblyTypeMaps;
        }

        /// <summary>
        /// Caches type information from all currently loaded assemblies.
        /// </summary>
        public static void PreWarmTypeCache() { GetCachedAssemblyTypeMaps(); }

        /// <summary>
        /// Executes a delegate function for every assembly that can be loaded.
        /// </summary>
        /// <remarks>
        /// `ForEachAssembly` iterates through all assemblies and executes a method on each one.
        /// If an <see cref="ReflectionTypeLoadException"/> is thrown, it is caught and ignored.
        /// </remarks>
        /// <param name="callback">The callback method to execute for each assembly.</param>
        public static void ForEachAssembly(Action<Assembly> callback)
        {
            var assemblies = GetCachedAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    callback(assembly);
                }
                catch (ReflectionTypeLoadException)
                {
                    // Skip any assemblies that don't load properly -- suppress errors
                }
            }
        }

        /// <summary>
        /// Executes a delegate function for each type in every assembly.
        /// </summary>
        /// <param name="callback">The callback to execute.</param>
        public static void ForEachType(Action<Type> callback)
        {
            var typesPerAssembly = GetCachedTypesPerAssembly();
            foreach (var types in typesPerAssembly)
            {
                foreach (var type in types)
                {
                    callback(type);
                }
            }
        }

        /// <summary>
        /// Search all assemblies for a type that matches a given predicate delegate.
        /// </summary>
        /// <param name="predicate">The predicate function. Must return <see langword="true"/> for the type that matches the search.</param>
        /// <returns>The first type for which <paramref name="predicate"/> returns <see langword="true"/>, or `null` if no matching type exists.</returns>
        public static Type FindType(Func<Type, bool> predicate)
        {
            var typesPerAssembly = GetCachedTypesPerAssembly();
            foreach (var types in typesPerAssembly)
            {
                foreach (var type in types)
                {
                    if (predicate(type))
                        return type;
                }
            }

            return null;
        }

        public static void FindTypesByPredicate(Func<Type, bool> predicate, List<Type> resultList)
        {
            var typesPerAssembly = GetCachedTypesPerAssembly();
            foreach (var types in typesPerAssembly)
            {
                resultList.AddRange(types.Where(predicate));
            }
        }

        /// <summary>
        /// Find a type in any assembly by its full name.
        /// </summary>
        /// <param name="fullName">The name of the type as returned by <see cref="Type.FullName"/>.</param>
        /// <returns>The type found, or null if no matching type exists.</returns>
        public static Type FindTypeByFullName(string fullName)
        {
            var typesPerAssembly = GetCachedAssemblyTypeMaps();
            foreach (var assemblyTypes in typesPerAssembly)
            {
                if (assemblyTypes.TryGetValue(fullName, out var type))
                    return type;
            }

            return null;
        }

        /// <summary>
        /// Search all assemblies for a set of types that matches any one of a set of predicates.
        /// </summary>
        /// <remarks>
        /// This function tests each predicate against each type in each assembly. If the predicate returns
        /// <see langword="true"/> for a type, then that <see cref="Type"/> object is assigned to the corresponding index of
        /// the <paramref name="resultList"/>. If a predicate returns <see langword="true"/> for more than one type, then the
        /// last matching result is used. If no type matches the predicate, then that index of <paramref name="resultList"/>
        /// is left unchanged.
        /// </remarks>
        /// <param name="predicates">The predicate functions. A predicate function must return <see langword="true"/>
        /// for the type that matches the search and should only match one type.</param>
        /// <param name="resultList">The list to which found types will be added. The list must have
        /// the same number of elements as the <paramref name="predicates"/> list.</param>
        public static void FindTypesBatch(List<Func<Type, bool>> predicates, List<Type> resultList)
        {
            var typesPerAssembly = GetCachedTypesPerAssembly();
            for (var i = 0; i < predicates.Count; i++)
            {
                var predicate = predicates[i];
                foreach (var assemblyTypes in typesPerAssembly)
                {
                    foreach (var type in assemblyTypes)
                    {
                        if (predicate(type))
                            resultList[i] = type;
                    }
                }
            }
        }

        /// <summary>
        /// Searches all assemblies for a set of types by their <see cref="Type.FullName"/> strings.
        /// </summary>
        /// <remarks>
        /// If a type name in <paramref name="typeNames"/> is not found, then the corresponding index of <paramref name="resultList"/>
        /// is set to `null`.
        /// </remarks>
        /// <param name="typeNames">A list containing the <see cref="Type.FullName"/> strings of the types to find.</param>
        /// <param name="resultList">An empty list to which any matching <see cref="Type"/> objects are added. A
        /// result in <paramref name="resultList"/> has the same index as corresponding name in <paramref name="typeNames"/>.</param>
        public static void FindTypesByFullNameBatch(List<string> typeNames, List<Type> resultList)
        {
            var assemblyTypeMap = GetCachedAssemblyTypeMaps();
            foreach (var typeName in typeNames)
            {
                var found = false;
                foreach (var typeMap in assemblyTypeMap)
                {
                    if (typeMap.TryGetValue(typeName, out var type))
                    {
                        resultList.Add(type);
                        found = true;
                        break;
                    }
                }

                // If a type can't be found, add a null entry to the list to ensure indexes match
                if (!found)
                    resultList.Add(null);
            }
        }

        /// <summary>
        /// Searches for a type by assembly simple name and its <see cref="Type.FullName"/>.
        /// an assembly with the given simple name and returns the type with the given full name in that assembly
        /// </summary>
        /// <param name="assemblyName">Simple name of the assembly (<see cref="Assembly.GetName()"/>).</param>
        /// <param name="typeName">Full name of the type to find (<see cref="Type.FullName"/>).</param>
        /// <returns>The type if found, otherwise null</returns>
        public static Type FindTypeInAssemblyByFullName(string assemblyName, string typeName)
        {
            var assemblies = GetCachedAssemblies();
            var assemblyTypeMaps = GetCachedAssemblyTypeMaps();
            for (var i = 0; i < assemblies.Length; i++)
            {
                if (assemblies[i].GetName().Name != assemblyName)
                    continue;

                return assemblyTypeMaps[i].TryGetValue(typeName, out var type) ? type : null;
            }

            return null;
        }

        public static IEnumerable<Type> FindAllTypes()
        {
            var typesPerAssembly = GetCachedTypesPerAssembly();
            foreach (var types in typesPerAssembly)
            {
                foreach (var type in types)
                {
                    yield return type;
                }
            }
        }

        public static List<Type> GetAssignableTypesOf(Type baseType)
        {
            if(!s_AssignableTypes.TryGetValue(baseType, out var types))
            {
                //Debug.Log($"Finding Inherited Types Of: {baseType}");
                types = new List<Type>();
                s_AssignableTypes.Add(baseType, types);

                ForEachType(t =>
                {
                    if (baseType.IsAssignableFrom(t) && t != baseType && !t.IsInterface)
                    {
                        //Debug.Log($"Found: {t}");
                        types.Add(t);
                    }
                });
            }
            return types;            
        }
        #endregion
    }
}
