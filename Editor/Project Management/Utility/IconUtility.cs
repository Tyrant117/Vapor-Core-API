using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.ProjectManagement
{
    public static class IconUtility
    {
        public static StyleBackground GetTaskBackground(TaskType type)
        {
            return type switch
            {
                TaskType.Script => new StyleBackground(GetScriptIcon()),
                TaskType.Texture => new StyleBackground(GetTextureIcon()),
                TaskType.Material => new StyleBackground(GetMaterialIcon()),
                TaskType.Audio => new StyleBackground(GetAudioIcon()),
                TaskType.Visual => new StyleBackground(GetVisualIcon()),
                TaskType.Prefab => new StyleBackground(GetPrefabIcon()),
                TaskType.Data => new StyleBackground(GetDataIcon()),
                TaskType.Design => new StyleBackground(GetDesignIcon()),
                _ => null
            };
        }
        
        public static Texture2D GetScriptIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_cs Script Icon").image : EditorGUIUtility.IconContent("AudioClip Icon").image);
        }
        
        public static Texture2D GetTextureIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_Texture Icon").image : EditorGUIUtility.IconContent("Texture Icon").image);
        }
        
        public static Texture2D GetMaterialIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_Material Icon").image : EditorGUIUtility.IconContent("Material Icon").image);
        }
        
        public static Texture2D GetAudioIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_AudioClip Icon").image : EditorGUIUtility.IconContent("AudioClip Icon").image);
        }
        
        public static Texture2D GetVisualIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_ParticleSystem Icon").image : EditorGUIUtility.IconContent("ParticleSystem Icon").image);
        }
        
        public static Texture2D GetPrefabIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_Prefab Icon").image : EditorGUIUtility.IconContent("Prefab Icon").image);
        }
        
        public static Texture2D GetDataIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_ScriptableObject Icon").image : EditorGUIUtility.IconContent("ScriptableObject Icon").image);
        }
        
        public static Texture2D GetDesignIcon()
        {
            return (Texture2D)(EditorGUIUtility.isProSkin ? EditorGUIUtility.IconContent("d_CollabEdit Icon").image : EditorGUIUtility.IconContent("CollabEdit Icon").image);
        }
    }
}
