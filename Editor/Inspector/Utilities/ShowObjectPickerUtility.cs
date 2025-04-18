using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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

        private static MethodInfo _InternalFetchMethod__ObjectSelector_Show(Type typeToShowInSelector)
        {
	        var objectSelectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ObjectSelector");


	        // var getter = objectSelectorType.GetProperty("get", BindingFlags.Static | BindingFlags.Public);
	        /*** Type 3: Unity - 2022 onwards */
	        var miShow = objectSelectorType.GetMethod("Show", BindingFlags.NonPublic | BindingFlags.Instance, null, new[]
	        {
		        typeToShowInSelector,
		        typeof(Type),
		        typeof(Object),
		        typeof(bool),
		        typeof(List<int>),
		        typeof(Action<Object>),
		        typeof(Action<Object>),
		        typeof(bool) // new optional param added in Unity 2022
	        }, new ParameterModifier[0]);

	        if (miShow != null) return miShow;
	        
	        const string methodName = "Show";
	        Debug.LogError("UNITY CHANGED THE API. NEW API: Found \"" + methodName + "\"methods: \n" + string.Join(",\n", objectSelectorType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
		        .Where(info => info.Name == methodName).Select(info =>
			        "  " + info.Name + " {" + string.Join(",\n      ", info.GetParameters().Select(parameterInfo => parameterInfo.Name + ":" + parameterInfo.ParameterType)) + "\n}\n")));
	        return null;
        }

        public static void ShowObjectPicker<T>(Action<T> onSelectorClosed, Action<T> onSelectionChanged, T initialValueOrNull = null, ObjectPickerSources sources = ObjectPickerSources.Assets,
	        string searchFilter = null)
	        where T : Object
        {
	        var miShow = _InternalFetchMethod__ObjectSelector_Show(typeof(T));

	        Action<Object> selectorClosed;
	        Action<Object> selectedUpdated;
	        switch (sources)
	        {
		        case ObjectPickerSources.Assets:
		        case ObjectPickerSources.AssetsAndScene:
			        selectedUpdated = o => { onSelectionChanged(o as T); };
			        selectorClosed = o => onSelectorClosed.Invoke(o as T);
			        break;
		        case ObjectPickerSources.OnlyMonobehaviours:
			        selectedUpdated = o =>
			        {
				        if (o is GameObject go)
				        {
					        onSelectionChanged(go.GetComponent<T>());
				        }

				        onSelectionChanged(null);
			        };
			        selectorClosed = o =>
			        {
				        if (o is GameObject go)
				        {
					        onSelectorClosed?.Invoke(go.GetComponent<T>());
				        }

				        onSelectorClosed?.Invoke(null);
			        };
			        break;
		        default:
			        throw new Exception("Impossible value of sources parameter");
	        }

	        var objectSelectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ObjectSelector");
	        var piGet = objectSelectorType.GetProperty("get", BindingFlags.Public | BindingFlags.Static);
	        // ReSharper disable once PossibleNullReferenceException
	        var os = piGet.GetValue(null);
	        miShow.Invoke(os, new object[]
		        {
			        initialValueOrNull,
			        typeof(T),
			        null,
			        sources is ObjectPickerSources.AssetsAndScene or ObjectPickerSources.OnlyMonobehaviours,
			        null,
			        selectorClosed,
			        selectedUpdated,
			        true
		        }
	        );
	        if (!string.IsNullOrEmpty(searchFilter))
	        {
		        var piSearchFilter = objectSelectorType.GetProperty("searchFilter", BindingFlags.NonPublic | BindingFlags.Instance);
		        piSearchFilter?.SetValue(os, searchFilter);
	        }
        }

        public static void ShowObjectPicker(Type type, Action<Object> onSelectorClosed, Action<Object> onSelectionChanged, Object initialValueOrNull = null,
	        ObjectPickerSources sources = ObjectPickerSources.Assets, string searchFilter = null)
        {
	        var miShow = _InternalFetchMethod__ObjectSelector_Show(typeof(Object));

	        Action<Object> selectorClosed;
	        Action<Object> selectedUpdated;
	        switch (sources)
	        {
		        case ObjectPickerSources.Assets:
		        case ObjectPickerSources.AssetsAndScene:
			        selectedUpdated = onSelectionChanged;
			        selectorClosed = o => onSelectorClosed?.Invoke(o);
			        break;
		        case ObjectPickerSources.OnlyMonobehaviours:
			        selectedUpdated = o =>
			        {
				        if (o is GameObject go)
				        {
					        onSelectionChanged(go.GetComponent(type));
				        }

				        onSelectionChanged(null);
			        };
			        selectorClosed = o =>
			        {
				        if (o is GameObject go)
				        {
					        onSelectorClosed?.Invoke(go.GetComponent(type));
				        }

				        onSelectorClosed?.Invoke(null);
			        };
			        break;
		        default:
			        throw new Exception("Impossible value of sources parameter");
	        }

	        var objectSelectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ObjectSelector");
	        var piGet = objectSelectorType.GetProperty("get", BindingFlags.Public | BindingFlags.Static);
	        // ReSharper disable once PossibleNullReferenceException
	        var os = piGet.GetValue(null);
	        miShow.Invoke(os, new object[]
		        {
			        initialValueOrNull,
			        type,
			        null,
			        sources is ObjectPickerSources.AssetsAndScene or ObjectPickerSources.OnlyMonobehaviours,
			        null,
			        selectorClosed,
			        selectedUpdated,
			        true
		        }
	        );
	        if (!string.IsNullOrEmpty(searchFilter))
	        {
		        var piSearchFilter = objectSelectorType.GetProperty("searchFilter", BindingFlags.NonPublic | BindingFlags.Instance);
		        piSearchFilter?.SetValue(os, searchFilter);
	        }
        }
    }
}
