using System.Collections.Generic;
using Vapor.Inspector;

namespace Vapor.GameplayTags
{
    public class GameplayTagTreeNode
    {
        public GameplayTagTreeNode Root { get; set; }
        public GameplayTagTreeNode Parent { get; set; }
        public string Name { get; set; }
        public uint Key { get; set; }
        public List<GameplayTagTreeNode> Children { get; set; }

        public List<DropdownModel> GetAllTags() => GameplayTagUtility.GetAllKeys();
    }
}