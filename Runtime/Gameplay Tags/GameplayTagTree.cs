using System;
using System.Collections.Generic;
using Vapor.Unsafe;

namespace Vapor.GameplayTags
{
    /// <summary>
    /// A hierarchical tree over dotted tag names ("Ability.Fire.Burn"), keyed by the same
    /// <c>uint = Hash32(name)</c> space as <c>GameplayTag</c> and <c>IData</c>. Every segment of a dotted path
    /// gets its own node, so "Ability.Fire.Burn" also resolves "Ability.Fire" and "Ability".
    ///
    /// <para>The tree builds lazily from <see cref="GameplayTagTree.Initialize"/> on first access and
    /// rebuilds whenever the data registry is rebuilt, so callers never need to initialize it. Building lazily
    /// is also what lets this type stand alone: Unity does not invoke <c>[RuntimeInitializeOnLoadMethod]</c> /
    /// <c>[InitializeOnLoadMethod]</c> on generic types, and it avoids racing the registry's own startup
    /// initialization.</para>
    /// </summary>
    public static class GameplayTagTree
    {
        private static bool s_Initialized;
        private static bool s_Initializing;
        private static bool s_Subscribed;
        private static List<GameplayTagTreeNode> s_RootTags = new();
        private static Dictionary<uint, GameplayTagTreeNode> s_TagMap = new();

        public static List<GameplayTagTreeNode> RootTags
        {
            get
            {
                EnsureInitialized();
                return s_RootTags;
            }
        }

        public static Dictionary<uint, GameplayTagTreeNode> TagMap
        {
            get
            {
                EnsureInitialized();
                return s_TagMap;
            }
        }

        private static void Invalidate() => s_Initialized = false;

        private static void EnsureInitialized()
        {
            if (!s_Subscribed)
            {
                // Rebuilding the registry (e.g. after regenerating data keys) must rebuild the tree.
                GlobalDataRegistry.OnRegistriesBuilt += Invalidate;
                s_Subscribed = true;
            }

            if (s_Initialized || s_Initializing)
            {
                return;
            }

            Initialize();
        }

        /// <summary>
        /// Forces an immediate rebuild. Normally unnecessary - the tree builds on first access and rebuilds
        /// whenever the registry does.
        /// </summary>
        public static void Initialize()
        {
            // GetAllTags() reads the data registry, which can log or resolve a tag name and re-enter here.
            // Build into locals and swap at the end so a re-entrant read sees the previous tree, never a torn one.
            if (s_Initializing)
            {
                return;
            }

            s_Initializing = true;
            try
            {
                var rootTags = new List<GameplayTagTreeNode>();
                var tagMap = new Dictionary<uint, GameplayTagTreeNode>();
                var tempTag = Activator.CreateInstance<GameplayTagTreeNode>();
                var tags = tempTag.GetAllTags();
                var nodeLookup = new Dictionary<string, GameplayTagTreeNode>();

                foreach (var tag in tags)
                {
                    string[] parts = tag.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    string currentPath = "";
                    GameplayTagTreeNode parent = null;

                    for (int i = 0; i < parts.Length; i++)
                    {
                        currentPath = i == 0 ? parts[0] : $"{currentPath}.{parts[i]}";

                        if (!nodeLookup.TryGetValue(currentPath, out var currentNode))
                        {
                            currentNode = new GameplayTagTreeNode
                            {
                                Name = currentPath,
                                Key = currentPath == "None" ? 0 : currentPath.Hash32(),
                                Children = new List<GameplayTagTreeNode>(),
                                Parent = parent,
                            };

                            nodeLookup[currentPath] = currentNode;
                            tagMap[currentNode.Key] = currentNode;

                            if (parent != null)
                            {
                                currentNode.Root = nodeLookup[parts[0]];
                                parent.Children.Add(currentNode);
                            }
                            else
                            {
                                rootTags.Add(currentNode);
                            }
                        }

                        parent = currentNode;
                    }
                }

                s_RootTags = rootTags;
                s_TagMap = tagMap;
                s_Initialized = true;
            }
            finally
            {
                s_Initializing = false;
            }
        }

        /// <summary>
        /// Adds a tag and every dotted ancestor it implies. Null or empty names are ignored.
        /// </summary>
        public static void InsertTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return;
            }

            EnsureInitialized();

            string[] parts = tag.Split('.', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = "";
            GameplayTagTreeNode parent = null;

            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = i == 0 ? parts[0] : $"{currentPath}.{parts[i]}";

                if (!s_TagMap.TryGetValue(currentPath.Hash32(), out var currentNode))
                {
                    currentNode = new GameplayTagTreeNode
                    {
                        Name = currentPath,
                        Key = currentPath == "None" ? 0 : currentPath.Hash32(),
                        Children = new List<GameplayTagTreeNode>(),
                        Parent = parent,
                    };

                    s_TagMap[currentNode.Key] = currentNode;

                    if (parent != null)
                    {
                        currentNode.Root = parent.Root;
                        parent.Children.Add(currentNode);
                    }
                    else
                    {
                        s_RootTags.Add(currentNode);
                    }
                }

                parent = currentNode;
            }
        }

        public static void Traverse(Action<GameplayTagTreeNode> visitor, bool preOrderSearch = true)
        {
            if(preOrderSearch)
            {
                foreach (var rootTag in RootTags)
                {
                    VisitPreOrder(rootTag, visitor);
                }
            }
            else
            {
                foreach (var rootTag in RootTags)
                {
                    VisitPostOrder(rootTag, visitor);
                }
            }
        }

        public static void TraverseFrom(GameplayTagTreeNode root, Action<GameplayTagTreeNode> visitor, bool preOrderSearch = true)
        {
            if (preOrderSearch)
            {
                VisitPreOrder(root, visitor);
            }
            else
            {
                VisitPostOrder(root, visitor);
            }
        }

        private static void VisitPreOrder(GameplayTagTreeNode parent, Action<GameplayTagTreeNode> visitor)
        {
            visitor(parent);
            foreach (var child in parent.Children)
            {
                VisitPreOrder(child, visitor);
            }
        }

        private static void VisitPostOrder(GameplayTagTreeNode parent, Action<GameplayTagTreeNode> visitor)
        {
            foreach (var child in parent.Children)
            {
                VisitPostOrder(child, visitor);
            }

            visitor(parent);
        }

        /// <summary>
        /// True when <paramref name="tagId"/> is <paramref name="searchId"/> itself or has it as an ancestor -
        /// e.g. "Ability.Fire.Burn" matches a search for "Ability".
        /// </summary>
        public static bool HasParentTag(uint tagId, uint searchId)
        {
            if (tagId == searchId)
            {
                return true;
            }

            if (!TagMap.TryGetValue(tagId, out var node))
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

        /// <summary>
        /// The dotted name registered for a key, or "None" when the key is not in the tree.
        /// </summary>
        public static string GetName(uint key)
        {
            TagMap.TryGetValue(key, out var node);
            return node?.Name ?? "None";
        }
    }
}
