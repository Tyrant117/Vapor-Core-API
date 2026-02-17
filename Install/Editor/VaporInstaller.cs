using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VaporEditorInstaller
{
    public static class VaporInstaller
    {
        private const string SessionStateInitialized = "_vaporSessionStateInitialized";

        private const string SymbolName = "VAPOR";

        [InitializeOnLoadMethod]
        private static void InitializeSession()
        {
            if (SessionState.GetBool(SessionStateInitialized, false))
            {
                return;
            }

            SessionState.SetBool(SessionStateInitialized, true);

            Debug.Log("[Vapor Installer] Running one-time editor session initialization...");
            InitializeOncePerSession();
        }

        private static void InitializeOncePerSession()
        {
            // Toggle the checkmark
            PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), out var defines);
            if (defines.Contains(SymbolName))
            {
                return;
            }

            PackageDependencyResolver.InstallDependencies(SetDefine);
        }

        private static void SetDefine(bool success)
        {
            if (success)
            {
                PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), out var defines);
                ArrayUtility.Add(ref defines, SymbolName);
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup), defines);
                
                
            }
            AssetDatabase.Refresh();
        }
    }
}

