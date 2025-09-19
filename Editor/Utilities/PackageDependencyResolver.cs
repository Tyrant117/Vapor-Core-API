using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace VaporEditor
{
    public static class PackageDependencyResolver
    {
        private static AddAndRemoveRequest s_Request;
        private static AddAndRemoveRequest s_RequestAddAndRemove;
        // private static int s_RequestIndex;

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
        private static readonly string[] s_NetcodeDependencies = {
            "com.unity.netcode.gameobjects",
            "com.unity.multiplayer.playmode",
        };
        
        [MenuItem("Vapor/Installation/Resolve Dependencies", priority = -10000)]
        private static void ResolveDependencies()
        {
            // s_RequestIndex = 0;
            s_Request = Client.AddAndRemove(s_Dependencies);
            EditorApplication.update += Progress;
        }
        
        [MenuItem("Vapor/Installation/Resolve Netcode Dependencies", priority = -10000)]
        private static void ResolveNetcodeDependencies()
        {
            s_RequestAddAndRemove = Client.AddAndRemove(s_NetcodeDependencies);
            EditorApplication.update += ProgressNetcode;
        }

        private static void Progress()
        {
            if (s_Request.IsCompleted)
            {
                if (s_Request.Status == StatusCode.Success)
                {
                    Debug.Log("Installed Dependencies");
                }
                else if (s_Request.Status >= StatusCode.Failure)
                {
                    Debug.Log(s_Request.Error.message);
                }

                // s_RequestIndex++;
                // if (s_RequestIndex >= s_Dependencies.Length)
                // {
                //     s_Request = Client.Add(s_Dependencies[s_RequestIndex]);
                // }
                // else
                // {
                // }
                EditorApplication.update -= Progress;
            }
        }

        private static void ProgressNetcode()
        {
            if (s_RequestAddAndRemove.IsCompleted)
            {
                if (s_RequestAddAndRemove.Status == StatusCode.Success)
                {
                    Debug.Log("Installed Netcode Dependencies");
                }
                else if (s_RequestAddAndRemove.Status >= StatusCode.Failure)
                {
                    Debug.Log(s_RequestAddAndRemove.Error.message);
                }

                // s_RequestIndex++;
                // if (s_RequestIndex >= s_NetcodeDependencies.Length)
                // {
                //     s_Request = Client.Add(s_NetcodeDependencies[s_RequestIndex]);
                // }
                // else
                // {
                // }
                EditorApplication.update -= ProgressNetcode;
            }
        }
    }
}
