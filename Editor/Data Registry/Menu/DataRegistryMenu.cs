using System.Collections.Generic;
using System.Linq;
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
            GlobalDataRegistry.Initialize();

            var types = GlobalDataRegistry.GetAllTypes();

            foreach (var type in types)
            {
                // Make generic EffectRegistry<T>
                var genericType = typeof(DataRegistry<>).MakeGenericType(type);

                // Get the GetAll method via reflection
                var getAllMethod = genericType.GetMethod("GetAll");
                var allData = (IEnumerable<IData>)getAllMethod.Invoke(null, null);
                var keys = allData.Select(d => KeyGenerator.StringToKeyValuePair(d.Name)).ToList();
                var category = type.Name.Replace("Data", "");
                var scriptName = $"{category}Keys";
                category = $"{category}s";
                KeyGenerator.FormatKeyFiles(KeyGenerator.RELATIVE_KEY_PATH, KeyGenerator.NAMESPACE_NAME, scriptName, category, keys);
            }

            RuntimeEditorUtility.SaveAndRefresh();
        }
    }
}
