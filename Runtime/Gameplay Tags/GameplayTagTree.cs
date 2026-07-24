using UnityEngine;
using Vapor.Inspector;

namespace Vapor.GameplayTags
{
    public static class GameplayTagTree
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeOnLoad()
        {
            TagTree<GameplayTagTreeNode>.Initialize();
        }
#endif
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void InitializeOnStartup()
        {
            TagTree<GameplayTagTreeNode>.Initialize();
        }

        public static bool HasParentTag(uint tagId, uint searchId)
        {
            if (tagId == searchId)
            {
                return true;
            }
            
            if (!TagTree<GameplayTagTreeNode>.TagMap.TryGetValue(tagId, out var node))
            {
                return false;
            }

            while (node != null)
            {
                if (node.Key == searchId)
                {
                    return true;
                }
                node = node.Parent;
            }

            return false;
        }

        public static void AddTag(string tagName)
        {
            if (tagName.EmptyOrNull())
            {
                return;
            }
            
            TagTree<GameplayTagTreeNode>.InsertTag(tagName);
        }

        public static string GetName(in GameplayTag gameplayTag)
        {
            TagTree<GameplayTagTreeNode>.TagMap.TryGetValue(gameplayTag.Key, out var node);
            return node?.Name ?? "None";
        }
    }
}
