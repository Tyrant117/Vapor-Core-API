using System.Collections.Generic;
using Vapor.Inspector;

namespace Vapor.GameplayTags
{
    public class GameplayTagTreeNode : TagTreeNode<GameplayTagTreeNode>
    {
        public override List<DropdownModel> GetAllTags() => GameplayTagUtility.GetAllKeys();
    }
}