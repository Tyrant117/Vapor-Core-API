using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using Vapor;
using Vapor.Keys;

namespace VaporEditor
{
    public static class DataRegistryMenu
    {
        [MenuItem("Vapor/Keys/Generate Data Keys", priority = -9100)]
        private static void GenerateDataKeys()
        {
            SetupDataKeys();
            RuntimeEditorUtility.SaveAndRefresh();
        }

        public static void SetupDataKeys()
        {
            GlobalDataRegistry.Initialize();

            var types = GlobalDataRegistry.GetAllTypes().ToList();
            var baseTypes = types
                .Select(t => t.BaseType)
                .Where(t => t != null && t != typeof(object) && typeof(IData).IsAssignableFrom(t))
                .ToList();
            types.AddRange(baseTypes);
            // Filter out types that don't implement IData
            types = types.Distinct().ToList();
            
            
            foreach (var type in types)
            {
                // Make generic DataRegistry<type>
                var keyOptions = type.GetCustomAttribute<KeyOptionsAttribute>();
                var genericType = typeof(DataRegistry<>).MakeGenericType(type);

                // Get the GetAll method via reflection
                var getAllMethod = genericType.GetMethod("GetAll");
                var allData = (IEnumerable<IData>)getAllMethod!.Invoke(null, null);
                var keys = allData.Select(d => KeyGenerator.StringToKeyValuePair(d.Name)).ToList();
                var scriptName = type.Name;
                scriptName = scriptName.Replace("Scriptable", "").Replace("Data", "").Replace("Key", "");
                scriptName = scriptName.EndsWith("SO") ? scriptName[..^2] : scriptName;
                scriptName = scriptName.EndsWith("So") ? scriptName[..^2] : scriptName;
                scriptName = scriptName.EndsWith("s") ? scriptName[..^1] : scriptName;
                
                var category = keyOptions?.Category ?? $"{scriptName}s";
                scriptName = $"{scriptName}Keys";
                KeyGenerator.FormatKeyFiles(KeyGenerator.RELATIVE_KEY_PATH, KeyGenerator.NAMESPACE_NAME, scriptName, category, keys);
            }
        }
    }
}
