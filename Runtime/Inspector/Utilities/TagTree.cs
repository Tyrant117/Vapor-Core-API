using System;
using System.Collections.Generic;
using Vapor.Keys;
using Vapor.Unsafe;

namespace Vapor.Inspector
{
    public interface ITagTreeNode
    {
        
    }
    
    public abstract class TagTreeNode<TNode> : ITagTreeNode where TNode : ITagTreeNode, new()
    {
        public TNode Root { get; set; }
        public TNode Parent { get; set; }
        public string Name { get; set; }
        public uint Key { get; set; }
        public List<TNode> Children { get; set; }

        public abstract List<DropdownModel> GetAllTags();
    }
    
    public static class TagTree<TNode> where TNode : TagTreeNode<TNode>, new()
    {
        public static List<TNode> RootTags { get; private set; }
        public static Dictionary<uint, TNode> TagMap { get; private set; }

        public static void Initialize()
        {
            RootTags = new List<TNode>();
            TagMap = new Dictionary<uint, TNode>();
            var tempTag = Activator.CreateInstance<TNode>();
            var tags = tempTag.GetAllTags();
            var nodeLookup = new Dictionary<string, TNode>();

            foreach (var tag in tags)
            {
                string[] parts = tag.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                string currentPath = "";
                TNode parent = null;

                for (int i = 0; i < parts.Length; i++)
                {
                    currentPath = i == 0 ? parts[0] : $"{currentPath}.{parts[i]}";

                    if (!nodeLookup.TryGetValue(currentPath, out var currentNode))
                    {
                        currentNode = new TNode()
                        {
                            Name = currentPath,
                            Key = currentPath == "None" ? KeyDropdownValue.None : currentPath.Replace(" ", "").Hash32(),
                            Children = new List<TNode>(),
                            Parent = parent,
                        };

                        nodeLookup[currentPath] = currentNode;
                        TagMap[currentNode.Key] = currentNode;

                        if (parent != null)
                        {
                            currentNode.Root = nodeLookup[parts[0]];
                            parent.Children.Add(currentNode);
                        }
                        else
                        {
                            RootTags.Add(currentNode);
                        }
                    }

                    parent = currentNode;
                }
            }
        }

        public static void Traverse(Action<TagTreeNode<TNode>> visitor, bool preOrderSearch = true)
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

        public static void TraverseFrom(TagTreeNode<TNode> root, Action<TagTreeNode<TNode>> visitor, bool preOrderSearch = true)
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

        private static void VisitPreOrder(TagTreeNode<TNode> parent, Action<TagTreeNode<TNode>> visitor)
        {
            visitor(parent);
            foreach (var child in parent.Children)
            {
                VisitPreOrder(child, visitor);
            }
        }

        private static void VisitPostOrder(TagTreeNode<TNode> parent, Action<TagTreeNode<TNode>> visitor)
        {
            foreach (var child in parent.Children)
            {
                VisitPostOrder(child, visitor);
            }

            visitor(parent);
        }

        public static bool HasParentTag(uint tagId, uint searchId)
        {
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
    }
}