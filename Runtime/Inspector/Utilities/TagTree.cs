using System;
using System.Collections.Generic;
using Vapor.Keys;

namespace Vapor.Inspector
{
    public abstract class TagTreeNode
    {
        public TagTreeNode Root { get; set; }
        public TagTreeNode Parent { get; set; }
        public string Name { get; set; }
        public ushort Key { get; set; }
        public List<TagTreeNode> Children { get; set; }

        public abstract List<DropdownModel> GetAllTags();
    }
    
    public static class TagTree<TNode> where TNode : TagTreeNode, new()
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
                string[] parts = tag.Name.Split('.');
                string currentPath = "";
                TagTreeNode parent = null;

                for (int i = 0; i < parts.Length; i++)
                {
                    currentPath = i == 0 ? parts[0] : $"{currentPath}.{parts[i]}";

                    if (!nodeLookup.TryGetValue(currentPath, out var currentNode))
                    {
                        currentNode = new TNode()
                        {
                            Name = currentPath,
                            Key = currentPath == "None" ? KeyDropdownValue.None : currentPath.GetStableHashU16(),
                            Children = new List<TagTreeNode>(),
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

        public static void Traverse(Action<TagTreeNode> visitor)
        {
            foreach (var rootTag in RootTags)
            {
                visitor(rootTag);
                Visit(rootTag, visitor);
            }
        }

        public static void TraverseFrom(TagTreeNode root, Action<TagTreeNode> visitor)
        {
            Visit(root, visitor);
        }

        private static void Visit(TagTreeNode parent, Action<TagTreeNode> visitor)
        {
            foreach (var child in parent.Children)
            {
                visitor(child);
                Visit(child, visitor);
            }
        }
    }
}