using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace VaporEditor
{
    public static class PackageDependencyResolver
    {
        private static AddRequest s_Request;
        private static int s_RequestIndex;

        private static readonly string[] s_Dependencies = new string[]
        {
            "com.unity.editorcoroutines",
            "com.unity.nuget.newtonsoft-json",
            "com.unity.addressables",
        };
        
        [MenuItem("Vapor/Installation/Resolve Dependencies", priority = -10000)]
        private static void ResolveDependencies()
        {
            s_RequestIndex = 0;
            s_Request = Client.Add(s_Dependencies[s_RequestIndex]);
            EditorApplication.update += Progress;
        }

        private static void Progress()
        {
            if (s_Request.IsCompleted)
            {
                if (s_Request.Status == StatusCode.Success)
                {
                    Debug.Log("Installed: " + s_Request.Result.packageId);
                }
                else if (s_Request.Status >= StatusCode.Failure)
                {
                    Debug.Log(s_Request.Error.message);
                }
                s_RequestIndex++;
                if (s_RequestIndex >= s_Dependencies.Length)
                {
                    s_Request = Client.Add(s_Dependencies[s_RequestIndex]);
                }
                else
                {
                    EditorApplication.update -= Progress;
                }
            }
        }
    }
}
