using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace VaporEditor.Inspector
{
    [AutoStaticsCleanup]
    public partial class TypeSearchProvider : ISearchProvider<TypeSearchModel>
    {
        private static List<TypeSearchModel> s_CachedDescriptors;
        
        public Vector2 Position { get; set; }
        public bool AllowMultiSelect { get; set; }

        private readonly IEnumerable<TypeSearchModel> _filteredDescriptors;
        private readonly HashSet<Assembly> _validAssemblies;
        private readonly Action<TypeSearchModel> _onSelect;

        /// <summary>
        /// A picker over a set of candidates that is already known.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The other constructor answers "which type in this project", and builds a static cache of all
        /// sixteen thousand of them filed by namespace to do it. This answers "which of these", where
        /// the caller has the list in hand — the implementations of one interface, usually a handful.
        /// </para>
        /// <para>
        /// So it builds its own models rather than filtering that cache, which is what lets them be
        /// flat, named for reading, and carry a description. It also sidesteps the cache being static:
        /// the shared list is built once with whatever the first caller asked for, so a per-call
        /// preference about categories could never have survived in it.
        /// </para>
        /// </remarks>
        public static TypeSearchProvider ForTypes(Action<TypeSearchModel> onSelect, IEnumerable<Type> candidates)
        {
            var types = candidates == null
                ? new List<Type>()
                : candidates.Where(t => t != null).ToList();

            var sharedSuffix = TypeDisplayNames.FindSharedSuffix(types);
            var models = new List<TypeSearchModel>(types.Count);

            foreach (var type in types)
            {
                var model = new TypeSearchModel(type.FullName ?? type.Name, string.Empty, TypeDisplayNames.GetDisplayName(type, sharedSuffix), true, type)
                {
                    Tooltip = TypeDisplayNames.Describe(type),
                };

                // The real name still finds it: the display name drops the suffix every sibling shares,
                // and that is exactly what someone who knows the class would type.
                model.WithSynonyms(type.Name, type.FullName);
                models.Add(model);
            }

            models.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return new TypeSearchProvider(onSelect, models);
        }

        private TypeSearchProvider(Action<TypeSearchModel> onSelect, List<TypeSearchModel> descriptors)
        {
            _onSelect = onSelect;
            _filteredDescriptors = descriptors;
        }

        public TypeSearchProvider(Action<TypeSearchModel> onSelect, HashSet<Assembly> validAssemblies, Func<Type, bool> filter = null, bool flattenCategories = false)
        {
            _onSelect = onSelect;
            var filterFunc = filter ?? (t => t.IsPublic || t.IsNestedPublic);
            if (s_CachedDescriptors != null)
            {
                _filteredDescriptors = s_CachedDescriptors.Where(tsm => filterFunc(tsm.Type) && (validAssemblies == null || validAssemblies.Contains(tsm.Type.Assembly)));
                return;
            }

            var compiledAssembly = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            var allTypes = new List<Type>(16000);
            var asmSet = new HashSet<Assembly>();
            Func<Type,bool> defaultFilter = t => t.IsPublic || t.IsNestedPublic;
            foreach (var assembly in compiledAssembly)
            {
                if (assembly == null)
                {
                    continue;
                }

                if (assembly.flags.HasFlag(AssemblyFlags.EditorAssembly))
                {
                    continue;
                }

                // Load the assembly
                var asm = Assembly.Load(assembly.name);
                if (asm == null)
                {
                    continue;
                }
                asmSet.Add(asm);
            }
            
            foreach (var asmPath in compiledAssembly[0].compiledAssemblyReferences)
            {
                var asmName = Path.GetFileNameWithoutExtension(asmPath);
                if (!asmName.Contains("UnityEngine"))
                {
                    continue;
                }

                var asm = Assembly.Load(asmName);
                if (asm == null)
                {
                    continue;
                }
                
                if(asm.IsDefined(typeof(AssemblyIsEditorAssembly), true))
                {
                    continue;
                }

                if (!asmSet.Contains(asm))
                {
                    // Get all types from the assembly
                    allTypes.AddRange(asm.GetTypes().Where(t => defaultFilter(t) && t.Namespace != null && !t.Namespace.Contains("UnityEditor")));
                }
            }

            foreach (var asm in asmSet)
            {
                // Get all types from the assembly
                allTypes.AddRange(asm.GetTypes().Where(defaultFilter));
            }
            
            allTypes.AddRange(typeof(string).Assembly.GetTypes().Where(defaultFilter));
            
            s_CachedDescriptors = new List<TypeSearchModel>(allTypes.Count);
            foreach (var t in allTypes.Distinct())
            {
                var typeName = t.IsGenericType ? $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(a => a.Name))}>" : t.Name;
                var model = new TypeSearchModel(flattenCategories ? string.Empty : t.Namespace?.Replace('.', '/'), typeName, true, t).WithSynonyms($"{t.Namespace}.{typeName}");
                s_CachedDescriptors.Add(model as TypeSearchModel);
            }
            
            _filteredDescriptors = s_CachedDescriptors.Where(tsm => filterFunc(tsm.Type) && (validAssemblies == null || validAssemblies.Contains(tsm.Type.Assembly)));
        }

        public IEnumerable<TypeSearchModel> GetDescriptors()
        {
            return _filteredDescriptors;
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
}