using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static partial class InspectorMenu
    {
        private const string InspectorSessionStateInitialized = "_inspectorSessionStateInitialized";

        /// <remarks>
        /// <para>
        /// Deferred a tick: <see cref="OnCodeInitializingAttribute"/> runs earlier than the
        /// <c>[InitializeOnLoadMethod]</c> it replaces, before the asset database is ready for the
        /// refresh this ends in.
        /// </para>
        /// <para>
        /// This used to also maintain a <c>Vapor/Installation/Inspectors Enabled</c> toggle over a
        /// separate <c>VAPOR_INSPECTOR</c> define. The inspector is part of the ecosystem rather than an
        /// option within it, so it now lives and dies with <c>VAPOR</c> and there is nothing left to
        /// toggle.
        /// </para>
        /// </remarks>
        [OnCodeInitializing]
        private static void InitializeSession()
        {
            if (SessionState.GetBool(InspectorSessionStateInitialized, false))
            {
                return;
            }

            SessionState.SetBool(InspectorSessionStateInitialized, true);

            EditorApplication.delayCall += () =>
            {
                Debug.Log("Running one-time editor session initialization...");
                InitializeOncePerSession();
            };
        }

        private static void InitializeOncePerSession()
        {
            DataRegistryMenu.SetupDataKeys();
            AssetDatabase.Refresh();
        }

        // [MenuItem("Vapor/Inspector/Create Inspectors From Selection", false, 1)]
        // private static void CreateInspectorsFromSelection()
        // {
        //     try
        //     {
        //         AssetDatabase.StartAssetEditing();
        //         var items = Selection.objects;
        //         foreach (var item in items)
        //         {
        //             if (item is not MonoScript script) continue;
        //
        //             var type = script.GetClass();
        //             if (type == null && script.text.Contains(script.name))
        //             {
        //                 // Check for generics.
        //                 int genericStart = script.text.IndexOf('<') + 1;
        //                 int genericEnd = script.text.IndexOf('>');
        //                 var span = script.text[genericStart..genericEnd];
        //                 var paramCount = span.Split(',').Length;
        //                 Debug.Log($"{span} - {paramCount}");
        //             }
        //             if (type == null) continue;
        //             Debug.Log($"Generating Inspector Script: {script.name} - {type}");
        //             if (type.IsSubclassOf(typeof(Object)))
        //             {
        //                 _CreateEditorClassFile(type.Name, type.Namespace);
        //             }
        //             else
        //             {
        //                 _CreatePropertyDrawerClassFile(type.Name, type.Namespace);
        //             }
        //         }
        //     }
        //     finally
        //     {
        //         AssetDatabase.StopAssetEditing();
        //         AssetDatabase.SaveAssets();
        //         AssetDatabase.Refresh();
        //     }
        //
        //     return;
        //
        //     static void _CreateEditorClassFile(string className, string namespaceName)
        //     {
        //         StringBuilder sb = new();
        //
        //         sb.Append("//\t* THIS SCRIPT IS AUTO-GENERATED *\n");
        //         sb.Append("using UnityEditor;\n");
        //         sb.Append($"using {FolderSetupUtility.EDITOR_NAMESPACE};\n");
        //         sb.Append($"using {namespaceName};\n");
        //
        //         sb.Append($"namespace {FolderSetupUtility.EDITOR_NAMESPACE}\n");
        //         sb.Append("{\n");
        //         sb.Append("#if VAPOR\n");
        //         sb.Append("\t[CanEditMultipleObjects]\n" +
        //                   $"\t[CustomEditor(typeof({className}), true)]\n");
        //         sb.Append($"\tpublic class {className}Editor : {nameof(InspectorBaseEditor)}\n");
        //         sb.Append("\t{\n");
        //
        //         sb.Append("\t}\n");
        //         sb.Append("#endif\n");
        //         sb.Append("}");
        //
        //         System.IO.File.WriteAllText($"{Application.dataPath}/{FolderSetupUtility.EDITOR_RELATIVE_PATH}/{className}Editor.cs", sb.ToString());
        //     }
        //
        //     static void _CreatePropertyDrawerClassFile(string className, string namespaceName)
        //     {
        //         StringBuilder sb = new();
        //
        //         sb.Append("//\t* THIS SCRIPT IS AUTO-GENERATED *\n");
        //         sb.Append("using UnityEditor;\n");
        //         sb.Append($"using {FolderSetupUtility.EDITOR_NAMESPACE};\n");
        //         sb.Append($"using {namespaceName};\n");
        //
        //         sb.Append($"namespace {FolderSetupUtility.EDITOR_NAMESPACE}\n");
        //         sb.Append("{\n");
        //         sb.Append("#if VAPOR\n");
        //         sb.Append($"\t[CustomPropertyDrawer(typeof({className}), true)]\n");
        //         sb.Append($"\tpublic class {className}Drawer : PropertyDrawer\n");
        //         sb.Append("\t{\n");
        //
        //         sb.Append("\t}\n");
        //         sb.Append("#endif\n");
        //         sb.Append("}");
        //
        //         System.IO.File.WriteAllText($"{Application.dataPath}/{FolderSetupUtility.PROPERTY_DRAWER_RELATIVE_PATH}/{className}Drawer.cs", sb.ToString());
        //     }
        // }
    }
}
