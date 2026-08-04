using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public static class ShowObjectPickerUtility
    {
	    public enum ObjectPickerSources
	    {
		    Assets,
		    AssetsAndScene,
		    OnlyMonobehaviours,
	    }

	    /// <summary>
        /// Map that caches the search filter of a field and interface type pair.
        /// </summary>
        private static readonly Dictionary<Type, Dictionary<Type, string>> s_FilterMapByFieldType = new();

        /// <summary>
        /// Reusable string builder to create search filters.
        /// </summary>
        private static readonly StringBuilder s_SearchFilterBuilder = new();

        /// <summary>
        /// Reusable list used to store the minimum assignable field types that implement the given interface.
        /// </summary>
        private static readonly List<Type> s_MinimumAssignableImplementations = new();

        private static bool IsDirectImplementation(Type type, Type interfaceType)
        {
	        var directImplementedInterfaces = type.BaseType == null ? type.GetInterfaces() : type.GetInterfaces().Except(type.BaseType.GetInterfaces());
	        return directImplementedInterfaces.Contains(interfaceType);
        }

        private static void GetDirectImplementations(Type fieldType, Type interfaceType, List<Type> resultList)
        {
	        if (!interfaceType.IsInterface)
		        return;

	        ReflectionUtility.ForEachType(t =>
	        {
		        if (!t.IsInterface && fieldType.IsAssignableFrom(t) && interfaceType.IsAssignableFrom(t) && IsDirectImplementation(t, interfaceType))
			        resultList.Add(t);
	        });
        }
        
        public static string GetSearchFilter(Type fieldType, Type interfaceType)
        {
	        if (!s_FilterMapByFieldType.TryGetValue(fieldType, out var filterByInterfaceType))
	        {
		        filterByInterfaceType = new Dictionary<Type, string>();
		        s_FilterMapByFieldType.Add(fieldType, filterByInterfaceType);
	        }
	        else if (filterByInterfaceType.TryGetValue(interfaceType, out var cachedSearchFilter))
	        {
		        return cachedSearchFilter;
	        }

	        s_MinimumAssignableImplementations.Clear();
	        GetDirectImplementations(fieldType, interfaceType, s_MinimumAssignableImplementations);

	        s_SearchFilterBuilder.Clear();
	        foreach (var type in s_MinimumAssignableImplementations)
	        {
		        s_SearchFilterBuilder.Append("t:");
		        s_SearchFilterBuilder.Append(type.Name);
		        s_SearchFilterBuilder.Append(" ");
	        }
	        var searchFilter = s_SearchFilterBuilder.ToString();

	        filterByInterfaceType.Add(interfaceType, searchFilter);
	        return searchFilter;
        }

        private static MethodInfo s_CachedShowMethod;
        private static bool s_ShowMethodLookupFailed;

        /// <summary>
        /// Finds ObjectSelector.Show by shape rather than by an exact signature. Unity has changed this
        /// parameter list more than once - the allowed-id list went from List&lt;int&gt; to
        /// List&lt;EntityId&gt; in 6.x - and matching the exact types meant every change broke the picker.
        /// Only the leading parameters, which have been stable, are matched.
        /// </summary>
        private static MethodInfo _InternalFetchMethod__ObjectSelector_Show()
        {
            if (s_CachedShowMethod != null || s_ShowMethodLookupFailed)
            {
                return s_CachedShowMethod;
            }

            var objectSelectorType = typeof(Editor).Assembly.GetType("UnityEditor.ObjectSelector");
            foreach (var candidate in objectSelectorType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (candidate.Name != "Show")
                {
                    continue;
                }

                // (obj, requiredType, objectBeingEdited, allowSceneObjects, allowedIds, onClosed, onUpdated, [showNoneItem])
                var parameters = candidate.GetParameters();
                if (parameters.Length < 7
                    || parameters[0].ParameterType != typeof(Object)
                    || parameters[1].ParameterType != typeof(Type)
                    || parameters[2].ParameterType != typeof(Object)
                    || parameters[3].ParameterType != typeof(bool)
                    || parameters[5].ParameterType != typeof(Action<Object>)
                    || parameters[6].ParameterType != typeof(Action<Object>))
                {
                    continue;
                }

                s_CachedShowMethod = candidate;
                return candidate;
            }

            s_ShowMethodLookupFailed = true;
            Debug.LogError("UNITY CHANGED THE API. No matching \"Show\" overload on UnityEditor.ObjectSelector. Found: \n"
                           + string.Join(",\n", objectSelectorType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                               .Where(info => info.Name == "Show").Select(info =>
                                   "  " + info.Name + " {" + string.Join(",\n      ", info.GetParameters().Select(p => p.Name + ":" + p.ParameterType)) + "\n}\n")));
            return null;
        }

        /// <summary>
        /// Builds the argument array to match whatever overload was found, so an added trailing parameter
        /// doesn't break the call.
        /// </summary>
        private static object[] BuildShowArguments(MethodInfo show, Object initialValueOrNull, Type requiredType, bool allowSceneObjects,
            Action<Object> selectorClosed, Action<Object> selectedUpdated)
        {
            var arguments = new object[show.GetParameters().Length];
            arguments[0] = initialValueOrNull;
            arguments[1] = requiredType;
            arguments[2] = null; // objectBeingEdited
            arguments[3] = allowSceneObjects;
            arguments[4] = null; // allowed instance/entity ids - unfiltered
            arguments[5] = selectorClosed;
            arguments[6] = selectedUpdated;
            if (arguments.Length > 7)
            {
                arguments[7] = true; // showNoneItem
            }

            return arguments;
        }

        private static void InvokeShow(MethodInfo show, Object initialValueOrNull, Type requiredType, bool allowSceneObjects,
            Action<Object> selectorClosed, Action<Object> selectedUpdated, string searchFilter)
        {
            var objectSelectorType = typeof(Editor).Assembly.GetType("UnityEditor.ObjectSelector");
            var piGet = objectSelectorType.GetProperty("get", BindingFlags.Public | BindingFlags.Static);
            var os = piGet?.GetValue(null);
            if (os == null)
            {
                Debug.LogError("Could not reach UnityEditor.ObjectSelector.get - the object picker cannot be shown.");
                return;
            }

            show.Invoke(os, BuildShowArguments(show, initialValueOrNull, requiredType, allowSceneObjects, selectorClosed, selectedUpdated));

            if (string.IsNullOrEmpty(searchFilter))
            {
                return;
            }

            var piSearchFilter = objectSelectorType.GetProperty("searchFilter", BindingFlags.NonPublic | BindingFlags.Instance);
            piSearchFilter?.SetValue(os, searchFilter);
        }

        public static void ShowObjectPicker<T>(Action<T> onSelectorClosed, Action<T> onSelectionChanged, T initialValueOrNull = null, ObjectPickerSources sources = ObjectPickerSources.Assets,
	        string searchFilter = null)
	        where T : Object
        {
	        var miShow = _InternalFetchMethod__ObjectSelector_Show();
	        if (miShow == null)
	        {
		        return;
	        }

	        Action<Object> selectorClosed;
	        Action<Object> selectedUpdated;
	        switch (sources)
	        {
		        case ObjectPickerSources.Assets:
		        case ObjectPickerSources.AssetsAndScene:
			        selectedUpdated = o => onSelectionChanged?.Invoke(o as T);
			        selectorClosed = o => onSelectorClosed?.Invoke(o as T);
			        break;
		        case ObjectPickerSources.OnlyMonobehaviours:
			        selectedUpdated = o => onSelectionChanged?.Invoke(o is GameObject go ? go.GetComponent<T>() : null);
			        selectorClosed = o => onSelectorClosed?.Invoke(o is GameObject go ? go.GetComponent<T>() : null);
			        break;
		        default:
			        throw new Exception("Impossible value of sources parameter");
	        }

	        InvokeShow(miShow, initialValueOrNull, typeof(T),
		        sources is ObjectPickerSources.AssetsAndScene or ObjectPickerSources.OnlyMonobehaviours,
		        selectorClosed, selectedUpdated, searchFilter);
        }

        public static void ShowObjectPicker(Type type, Action<Object> onSelectorClosed, Action<Object> onSelectionChanged, Object initialValueOrNull = null,
	        ObjectPickerSources sources = ObjectPickerSources.Assets, string searchFilter = null)
        {
	        var miShow = _InternalFetchMethod__ObjectSelector_Show();
	        if (miShow == null)
	        {
		        return;
	        }

	        Action<Object> selectorClosed;
	        Action<Object> selectedUpdated;
	        switch (sources)
	        {
		        case ObjectPickerSources.Assets:
		        case ObjectPickerSources.AssetsAndScene:
			        selectedUpdated = o => onSelectionChanged?.Invoke(o);
			        selectorClosed = o => onSelectorClosed?.Invoke(o);
			        break;
		        case ObjectPickerSources.OnlyMonobehaviours:
			        selectedUpdated = o => onSelectionChanged?.Invoke(o is GameObject go ? go.GetComponent(type) : null);
			        selectorClosed = o => onSelectorClosed?.Invoke(o is GameObject go ? go.GetComponent(type) : null);
			        break;
		        default:
			        throw new Exception("Impossible value of sources parameter");
	        }

	        InvokeShow(miShow, initialValueOrNull, type,
		        sources is ObjectPickerSources.AssetsAndScene or ObjectPickerSources.OnlyMonobehaviours,
		        selectorClosed, selectedUpdated, searchFilter);
        }
    }
}
