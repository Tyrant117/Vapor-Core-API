using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Vapor;
using Attribute = System.Attribute;

namespace VaporEditor.Inspector
{
    public interface ISearchProvider<T> where T : SearchModelBase
    {
        IEnumerable<T> GetDescriptors();
        Vector2 Position { get; set; }
        bool AllowMultiSelect { get; set; }

        bool Select(T searchModel);
        bool SelectMany(T[] searchModels);
    }

    internal class TypeCollectionSearchProvider : ISearchProvider<TypeSearchModel>
    {
        public Vector2 Position { get; set; }
        public bool AllowMultiSelect { get; set; }

        private readonly Action<TypeSearchModel> _onSelect;
        private readonly List<TypeSearchModel> _sortedDescriptors;
        
        public TypeCollectionSearchProvider(Action<TypeSearchModel> onSelect, HashSet<Assembly> validAssemblies, Func<Type,bool> filter, bool includeAbstract, bool flattenCategories, params Type[] types)
        {
            _onSelect = onSelect;
            var filterFunc = filter ?? (t => t.IsPublic || t.IsNestedPublic);
            var allTypes = new List<Type>(16000);
            foreach (var type in types)
            {
                var derivedTypes = type.IsSubclassOf(typeof(Attribute)) 
                    ? TypeCache.GetTypesWithAttribute(type).Where(t => filterFunc(t) && (includeAbstract || !t.IsAbstract) && (validAssemblies == null || validAssemblies.Contains(t.Assembly))) 
                    : TypeCache.GetTypesDerivedFrom(type).Where(t => filterFunc(t) && (includeAbstract || !t.IsAbstract) && (validAssemblies == null || validAssemblies.Contains(t.Assembly)));
                // Get all types from the assembly
                allTypes.AddRange(derivedTypes);
            }
            allTypes.AddRange(types.Where(t => filterFunc(t) && (includeAbstract || !t.IsAbstract) && (validAssemblies == null || validAssemblies.Contains(t.Assembly))));
            
            
            _sortedDescriptors = new List<TypeSearchModel>(allTypes.Count);
            foreach (var t in allTypes.Distinct())
            {
                var typeName = t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>" : t.Name;
                var model = new TypeSearchModel(flattenCategories ? string.Empty : t.Namespace?.Replace('.', '/'), typeName, true, t).WithSynonyms($"{t.Namespace}.{typeName}");
                _sortedDescriptors.Add(model as TypeSearchModel);
            }
        }
        
        public IEnumerable<TypeSearchModel> GetDescriptors()
        {
            return _sortedDescriptors;
        }
        public bool Select(TypeSearchModel searchModel)
        {
            _onSelect?.Invoke(searchModel);
            return true;
        }

        public bool SelectMany(TypeSearchModel[] searchModels)
        {
            return true;
        }
    }
    
    public class GenericSearchProvider : ISearchProvider<GenericSearchModel>
    {
        public Vector2 Position { get; set; }
        public bool AllowMultiSelect { get; set; }

        private readonly Action<GenericSearchModel[]> _onSelect;
        private readonly List<GenericSearchModel> _cachedDescriptors;

        public GenericSearchProvider(Action<GenericSearchModel[]> onSelect, List<GenericSearchModel> descriptors, bool allowMultiSelect)
        {
            AllowMultiSelect = allowMultiSelect;
            _onSelect = onSelect;
            _cachedDescriptors = descriptors;
        }

        public IEnumerable<GenericSearchModel> GetDescriptors()
        {
            return _cachedDescriptors;
        }

        public void SetModelToggled(string uniqueName)
        {
            var model = _cachedDescriptors.FirstOrDefault(sm => sm.Category.EmptyOrNull() ? sm.Name == uniqueName : sm.GetFullName() == uniqueName);
            if (model != null)
            {
                model.IsToggled = true;
            }
        }

        public bool Select(GenericSearchModel searchModel)
        {
            _onSelect?.Invoke(new[] { searchModel });
            return true;
        }

        public bool SelectMany(GenericSearchModel[] searchModels)
        {
            _onSelect?.Invoke(searchModels);
            return true;
        }
    }
}
