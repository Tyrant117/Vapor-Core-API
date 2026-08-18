using System;
using System.Collections.Generic;
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
            if (IsDefinedEverywhere())
            {
                return;
            }

            PackageDependencyResolver.InstallDependencies(SetDefine);
        }

        private static void SetDefine(bool success)
        {
            if (success)
            {
                foreach (var target in AllBuildTargets())
                {
                    PlayerSettings.GetScriptingDefineSymbols(target, out var defines);
                    if (defines.Contains(SymbolName))
                    {
                        continue;
                    }

                    ArrayUtility.Add(ref defines, SymbolName);
                    PlayerSettings.SetScriptingDefineSymbols(target, defines);
                }
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// True only when every build target already carries the define.
        /// </summary>
        /// <remarks>
        /// Checking just the active target is what let the define go missing everywhere else. Every Vapor
        /// assembly is gated on <c>VAPOR</c> through <c>defineConstraints</c>, and an assembly whose
        /// constraint is unmet is not an error — it simply is not built. Switching to a platform the
        /// define never reached therefore made the entire framework quietly cease to exist, with no
        /// compile error naming the cause.
        /// </remarks>
        private static bool IsDefinedEverywhere()
        {
            foreach (var target in AllBuildTargets())
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out var defines);
                if (!defines.Contains(SymbolName))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Every build target group Unity will accept defines for, skipping the obsolete and the
        /// unknown ones it rejects.
        /// </summary>
        private static IEnumerable<NamedBuildTarget> AllBuildTargets()
        {
            foreach (var group in (BuildTargetGroup[])Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown
                    || typeof(BuildTargetGroup).GetField(group.ToString())?.IsDefined(typeof(ObsoleteAttribute), false) == true)
                {
                    continue;
                }

                NamedBuildTarget target;
                try
                {
                    target = NamedBuildTarget.FromBuildTargetGroup(group);
                }
                catch (ArgumentException)
                {
                    // Groups with no named equivalent in this editor version.
                    continue;
                }

                yield return target;
            }
        }
    }
}

