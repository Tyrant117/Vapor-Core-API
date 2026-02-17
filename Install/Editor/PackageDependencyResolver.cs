using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace VaporEditorInstaller
{
    public static class PackageDependencyResolver
    {
        private static AddAndRemoveRequest s_Request;
        private static AddAndRemoveRequest s_RequestAddAndRemove;

        private static Action<bool> DependenciesLoaded;

        private static readonly string[] s_Dependencies = {
            "com.unity.editorcoroutines",
            "com.unity.nuget.newtonsoft-json",
            "com.unity.addressables",
            "com.unity.inputsystem",
            "com.unity.cinemachine",
            "com.unity.localization",
            "com.unity.multiplayer.playmode",
            "com.unity.netcode.gameobjects",
        };
        
        [MenuItem("Vapor/Installation/Resolve Dependencies", priority = -10000)]
        public static void ResolveDependencies()
        {
            // s_RequestIndex = 0;
            DependenciesLoaded = null;
            s_Request = Client.AddAndRemove(s_Dependencies);
            EditorApplication.update += Progress;
        }
        
        public static void InstallDependencies(Action<bool> callback = null)
        {
            // s_RequestIndex = 0;
            DependenciesLoaded = callback;
            s_Request = Client.AddAndRemove(s_Dependencies);
            EditorApplication.update += Progress;
        }

        private static void Progress()
        {
            if (s_Request.IsCompleted)
            {
                if (s_Request.Status == StatusCode.Success)
                {
                    Debug.Log("Installed Dependencies");
                    DependenciesLoaded?.Invoke(true);
                }
                else if (s_Request.Status >= StatusCode.Failure)
                {
                    Debug.Log(s_Request.Error.message);
                    DependenciesLoaded?.Invoke(false);
                }
                
                EditorApplication.update -= Progress;
            }
        }
    }
}
