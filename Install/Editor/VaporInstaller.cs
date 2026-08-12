using System.Linq;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace VaporEditorInstaller
{
    public static partial class VaporInstaller
    {
        private const string SessionStateInitialized = "_vaporSessionStateInitialized";

        private const string SymbolName = "VAPOR";

        /// <summary>
        /// Installs package dependencies and the VAPOR define, once per editor session.
        /// </summary>
        /// <remarks>
        /// The work is deferred a tick because <see cref="OnCodeInitializingAttribute"/> runs earlier
        /// than the <c>[InitializeOnLoadMethod]</c> it replaces — before assets are fully loaded — and
        /// package resolution ends in an <see cref="AssetDatabase.Refresh"/>.
        /// </remarks>
        [OnCodeInitializing]
        private static void InitializeSession()
        {
            if (SessionState.GetBool(SessionStateInitialized, false))
            {
                return;
            }

            SessionState.SetBool(SessionStateInitialized, true);

            EditorApplication.delayCall += () =>
            {
                Debug.Log("[Vapor Installer] Running one-time editor session initialization...");
                InitializeOncePerSession();
            };
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

