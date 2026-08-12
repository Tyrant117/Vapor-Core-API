using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using Vapor.Unsafe;

namespace Vapor.GameplayTags
{
    /// <summary>
    /// A hierarchical tree over dotted tag names ("Ability.Fire.Burn"), keyed by the same
    /// <c>uint = Hash32(name)</c> space as <c>GameplayTag</c> and <c>IData</c>. Every segment of a dotted path
    /// gets its own node, so "Ability.Fire.Burn" also resolves "Ability.Fire" and "Ability".
    ///
    /// <para>The tree builds lazily from <see cref="Initialize"/> on first access and rebuilds whenever the
    /// data registry is rebuilt, so callers never need to initialize it. Building lazily rather than from a
    /// load callback is what keeps it from racing the registry's own startup initialization.</para>
    ///
    /// <para>Nodes are keyed by the hash of the dotted path, so two paths that collide resolve to the same
    /// node. That is the same 32-bit exposure <c>GlobalDataRegistry</c> records collisions for, and it is not
    /// re-checked here.</para>
    /// </summary>
    [AutoStaticsCleanup]
    public static partial class GameplayTagTree
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
                // Unsubscribe first: the statics here are reset on entering play mode while the event on
                // the registry is not, so a bare += would stack another handler every play session.
                GlobalDataRegistry.OnRegistriesBuilt -= Invalidate;
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
            if (s_Initializing)
            {
                return;
            }

            s_Initializing = true;
            try
            {
                // GetAllKeys() reads the data registry, which can log or resolve a tag name and re-enter
                // here. Build into locals and swap at the end so a re-entrant read sees the previous tree,
                // never a torn one.
                var rootTags = new List<GameplayTagTreeNode>();
                var tagMap = new Dictionary<uint, GameplayTagTreeNode>();

                foreach (var tag in GameplayTagUtility.GetAllKeys())
                {
                    AddPath(tag.Name, tagMap, rootTags);
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
            AddPath(tag, s_TagMap, s_RootTags);
        }

        /// <summary>
        /// Walks a dotted path, adding any segment that is not already present, and returns the deepest node.
        /// </summary>
        /// <remarks>
        /// Shared by the full build and by <see cref="InsertTag"/>. They were separate copies that had
        /// drifted: one keyed its lookup by path and the other by hash, so inserting "None" never found the
        /// node it had just stored under the reserved key 0 and re-added it on every call; and the two
        /// disagreed on what <c>Root</c> meant below the second level.
        /// </remarks>
        private static GameplayTagTreeNode AddPath(string tag, Dictionary<uint, GameplayTagTreeNode> tagMap, List<GameplayTagTreeNode> rootTags)
        {
            string[] parts = tag.Split('.', StringSplitOptions.RemoveEmptyEntries);
            string currentPath = string.Empty;
            GameplayTagTreeNode node = null;

            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = i == 0 ? parts[0] : $"{currentPath}.{parts[i]}";
                node = GetOrAddNode(currentPath, node, tagMap, rootTags);
            }

            return node;
        }

        private static GameplayTagTreeNode GetOrAddNode(string path, GameplayTagTreeNode parent, Dictionary<uint, GameplayTagTreeNode> tagMap, List<GameplayTagTreeNode> rootTags)
        {
            uint key = KeyFor(path);
            if (tagMap.TryGetValue(key, out var node))
            {
                return node;
            }

            node = new GameplayTagTreeNode
            {
                Name = path,
                Key = key,
                Children = new List<GameplayTagTreeNode>(),
                Parent = parent,
            };

            tagMap[key] = node;

            if (parent == null)
            {
                rootTags.Add(node);
            }
            else
            {
                // Root is the top-level ancestor. A root's own Root stays null, so the second level takes
                // the parent itself and everything below inherits it.
                node.Root = parent.Root ?? parent;
                parent.Children.Add(node);
            }

            return node;
        }

        /// <summary>"None" is the reserved zero key; every other path is the hash of its dotted name.</summary>
        private static uint KeyFor(string path) => path == "None" ? 0u : path.Hash32();

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
