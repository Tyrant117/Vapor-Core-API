using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    internal interface IStyledElement
    {
        // The attribute goes on the field: an interface is not one of the declarations it accepts.
        // The sheet is a loaded asset, which outlives any code reload, so it is long-living on purpose.
        [NoAutoStaticsCleanup]
        readonly static StyleSheet s_Style = Resources.Load<StyleSheet>("StyledVisualElements");

        void Style();
    }
}
