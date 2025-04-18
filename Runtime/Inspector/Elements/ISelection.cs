using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Inspector
{
    public interface ISelection
    {
        //
        // Summary:
        //     Get the selection.
        List<ISelectable> selection { get; }

        //
        // Summary:
        //     Add element to selection.
        //
        // Parameters:
        //   selectable:
        //     Selectable element to add.
        void AddToSelection(ISelectable selectable);

        //
        // Summary:
        //     Remove element from selection.
        //
        // Parameters:
        //   selectable:
        //     Selectable element to remove.
        void RemoveFromSelection(ISelectable selectable);

        //
        // Summary:
        //     Clear selection.
        void ClearSelection();
    }
}
