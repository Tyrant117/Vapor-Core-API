using System;
using System.Collections.Generic;
using Vapor.Keys;

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
        public ushort Key { get; set; }
        public List<TNode> Children { get; set; }

        public abstract List<DropdownModel> GetAllTags();
    }
    
    public static class TagTree<TNode> where TNode : TagTreeNode<TNode>, new()
    {
        public static List<TNode> RootTags { get; private set; }
        public static Dictionary<ushort, TNode> TagMap { get; private set; }

        public static void Initialize()
        {
            RootTags = new List<TNode>();
            TagMap = new Dictionary<ushort, TNode>();
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
                            Key = currentPath == "None" ? KeyDropdownValue.None : currentPath.Replace(" ", "").GetStableHashU16(),
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

        public static void Traverse(Action<TagTreeNode<TNode>> visitor)
        {
            foreach (var rootTag in RootTags)
            {
                visitor(rootTag);
                Visit(rootTag, visitor);
            }
        }

        public static void TraverseFrom(TagTreeNode<TNode> root, Action<TagTreeNode<TNode>> visitor)
        {
            Visit(root, visitor);
        }

        private static void Visit(TagTreeNode<TNode> parent, Action<TagTreeNode<TNode>> visitor)
        {
            foreach (var child in parent.Children)
            {
                visitor(child);
                Visit(child, visitor);
            }
        }
        
        public static bool HasParentTag(ushort tagId, ushort searchId)
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