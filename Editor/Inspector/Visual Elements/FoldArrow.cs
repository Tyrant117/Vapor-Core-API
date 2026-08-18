using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// The little triangle in front of a foldable box header, drawn from borders rather than from a
    /// glyph or a texture so it cannot come out as a missing character on any editor skin.
    /// </summary>
    public static class FoldArrow
    {
        private static readonly Color ArrowColor = new Color(1f, 1f, 1f, 0.7f);

        /// <summary>A new arrow, pointing right when collapsed and down when not.</summary>
        public static VisualElement Create(bool collapsed)
        {
            var wrapper = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style = { width = 14, height = 14, alignItems = Align.Center, justifyContent = Justify.Center, flexShrink = 0f, marginRight = 2 },
            };

            wrapper.Add(new VisualElement { name = "Arrow", pickingMode = PickingMode.Ignore, style = { width = 0, height = 0 } });
            Set(wrapper, collapsed);
            return wrapper;
        }

        /// <summary>Points an existing arrow the other way.</summary>
        public static void Set(VisualElement wrapper, bool collapsed)
        {
            var arrow = wrapper?.Q("Arrow");
            if (arrow == null)
            {
                return;
            }

            arrow.style.borderLeftWidth = 0;
            arrow.style.borderRightWidth = 0;
            arrow.style.borderTopWidth = 0;
            arrow.style.borderBottomWidth = 0;

            if (collapsed)
            {
                arrow.style.borderTopWidth = 4;
                arrow.style.borderBottomWidth = 4;
                arrow.style.borderLeftWidth = 6;
                arrow.style.borderTopColor = Color.clear;
                arrow.style.borderBottomColor = Color.clear;
                arrow.style.borderLeftColor = ArrowColor;
            }
            else
            {
                arrow.style.borderLeftWidth = 4;
                arrow.style.borderRightWidth = 4;
                arrow.style.borderTopWidth = 6;
                arrow.style.borderLeftColor = Color.clear;
                arrow.style.borderRightColor = Color.clear;
                arrow.style.borderTopColor = ArrowColor;
            }
        }
    }
}
