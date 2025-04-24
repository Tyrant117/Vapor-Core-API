using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VaporEditor.Inspector
{
    public static class InspectorMenu
    {
        private const string EnableVaporInspectors = "_enableVaporInspectors";
        private const string InspectorSessionStateInitialized = "_inspectorSessionStateInitialized";

        public static bool VaporInspectorsEnabled
        {
            get => EditorPrefs.GetBool(PlayerSettings.productName + EnableVaporInspectors, false);
            set => EditorPrefs.SetBool(PlayerSettings.productName + EnableVaporInspectors, value);
        }

        private const string SymbolName = "VAPOR_INSPECTOR";

        [InitializeOnLoadMethod]
        private static void InitToggle()
        {
            if (SessionState.GetBool(InspectorSessionStateInitialized, false))
            {
                return;
            }

            SessionState.SetBool(InspectorSessionStateInitialized, true);

            Debug.Log("Running one-time editor session initialization...");
            InitializeOncePerSession();
        }

        private static void InitializeOncePerSession()
        {
            // Toggle the checkmark
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), out var defines);
            if (defines.Contains(SymbolName) && !VaporInspectorsEnabled)
            {
                ArrayUtility.Remove(ref defines, SymbolName);
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), defines);
            }

            if (!defines.Contains(SymbolName) && VaporInspectorsEnabled)
            {
                ArrayUtility.Add(ref defines, SymbolName);
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), defines);
            }
                
            Menu.SetChecked("Vapor/Installation/Inspectors Enabled", VaporInspectorsEnabled);
        }

        [MenuItem("Vapor/Installation/Inspectors Enabled")]
        private static void ToggleSymbol()
        {
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), out var defines);
            if (defines.Contains(SymbolName))
            {
                ArrayUtility.Remove(ref defines, SymbolName);
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), defines);
                VaporInspectorsEnabled = false;
            }
            else
            {
                ArrayUtility.Add(ref defines, SymbolName);
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), defines);
                VaporInspectorsEnabled = true;
            }

            // Toggle the checkmark
            Menu.SetChecked("Vapor/Installation/Inspectors Enabled", VaporInspectorsEnabled);
            AssetDatabase.Refresh();
        }

        [MenuItem("Vapor/Installation/Inspectors Enabled", true)]
        private static bool ToggleSymbolValidate()
        {
            // Always return true since the menu item is always valid
            return true;
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
        //         sb.Append("#if VAPOR_INSPECTOR\n");
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
        //         sb.Append("#if VAPOR_INSPECTOR\n");
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
