using UnityEngine;

namespace Vapor.Inspector
{
    public interface ISelectable
    {       
        SelectableManipulator Selectable { get; set; }
    }
}
