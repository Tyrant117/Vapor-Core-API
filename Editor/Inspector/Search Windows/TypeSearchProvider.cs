using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace VaporEditor.Inspector
{
    internal class TypeSearchProvider : ISearchProvider<TypeSearchModel>
    {
        private static List<TypeSearchModel> s_CachedDescriptors;
        
        public Vector2 Position { get; set; }
        public bool AllowMultiSelect { get; set; }

        private readonly IEnumerable<TypeSearchModel> _filteredDescriptors;
        private readonly HashSet<Assembly> _validAssemblies;
        private readonly Action<TypeSearchModel> _onSelect;

        public TypeSearchProvider(Action<TypeSearchModel> onSelect, HashSet<Assembly> validAssemblies, Func<Type,bool> filter = null)
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
                var asmName = System.IO.Path.GetFileNameWithoutExtension(asmPath);
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
                var model = new TypeSearchModel(t.Namespace?.Replace('.', '/'), typeName, true, t).WithSynonyms($"{t.Namespace}.{typeName}");
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