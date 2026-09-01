using System;
using System.Collections.Generic;
using System.Text;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// Point at any control in any editor window and get a written description of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same gesture as the UI Toolkit Debugger's element picker, for the same reason: when the
    /// thing you want to complain about is a control rather than a file, pointing at it is the only
    /// precise way to say which one. "The gear is not centred" needs a screenshot; <c>ScratchpadWindow
    /// ▸ Toolbar ▸ ToolbarButton ▸ VisualElement</c> needs nothing.
    /// </para>
    /// <para>
    /// Built entirely on public API. An overlay goes into every open window's root, and
    /// <see cref="IPanel.Pick"/> answers what is underneath it — the same hit test the event system
    /// uses, so what gets picked is what a click would have hit. The overlay switches its own
    /// <see cref="VisualElement.pickingMode"/> off for the duration of that call, because otherwise
    /// the only thing it can ever find is itself.
    /// </para>
    /// <para>
    /// Every window gets an overlay rather than only the one under the cursor, because there is no
    /// public way to poll the mouse between windows: the pointer events themselves are what tells us
    /// where the cursor is, and only a window with an overlay sends them.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// Exempt from statics cleanup, and this one is not a formality. Wiping
    /// <see cref="Overlays"/> would drop the references without taking the overlays out of the
    /// windows they are in — a full-window element that swallows every click, with the only thing
    /// that knew how to remove it now pointing at nothing. Pick mode has to end through
    /// <see cref="Cancel"/> or not at all; a domain reload takes the overlays with the UI it rebuilds.
    /// </remarks>
    [NoAutoStaticsCleanup]
    internal static class ScratchpadElementPicker
    {
        /// <summary>How many ancestors to name. Past this the path is scaffolding, not identity.</summary>
        private const int MaxDepth = 6;

        private static readonly List<VisualElement> Overlays = new();
        private static Action<string> _onPicked;

        public static bool IsPicking => _onPicked != null;

        /// <summary>Raised whenever pick mode starts or stops, so a toggle can show which it is.</summary>
        public static event Action PickingChanged;

        /// <summary>
        /// Enters pick mode and stays in it.
        /// </summary>
        /// <remarks>
        /// The callback fires once per click and pick mode remains active, so several controls can be
        /// collected in one go. Ending it is deliberate — the toggle, Escape, or a right-click —
        /// which is what makes the button a toggle rather than a one-shot, and what gives it something
        /// honest to highlight.
        /// </remarks>
        public static void Begin(Action<string> onPicked)
        {
            if (onPicked == null)
            {
                return;
            }

            Cancel();
            _onPicked = onPicked;

            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                // A window that has never been shown has no panel to pick into, and asking for its
                // root would build one.
                if (window == null || !window.hasFocus && window.rootVisualElement?.panel == null)
                {
                    continue;
                }

                Install(window);
            }

            if (Overlays.Count == 0)
            {
                _onPicked = null;
            }

            PickingChanged?.Invoke();
        }

        public static void Cancel()
        {
            var wasPicking = _onPicked != null;

            foreach (var overlay in Overlays)
            {
                overlay?.RemoveFromHierarchy();
            }

            Overlays.Clear();
            _onPicked = null;

            if (wasPicking)
            {
                PickingChanged?.Invoke();
            }
        }

        /// <summary>Turns pick mode off if it is on, and on if it is off.</summary>
        public static void Toggle(Action<string> onPicked)
        {
            if (IsPicking)
            {
                Cancel();
                return;
            }

            Begin(onPicked);
        }

        private static void Install(EditorWindow window)
        {
            var root = window.rootVisualElement;
            if (root == null)
            {
                return;
            }

            var overlay = new VisualElement
            {
                name = "scratchpad-picker-overlay",
                pickingMode = PickingMode.Position,
                focusable = true,
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0,
                },
            };

            var highlight = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    backgroundColor = new Color(0.35f, 0.65f, 1f, 0.18f),
                    borderTopWidth = 1,
                    borderBottomWidth = 1,
                    borderLeftWidth = 1,
                    borderRightWidth = 1,
                    display = DisplayStyle.None,
                },
            };

            ScratchpadStyles.SetBorderColor(highlight, new Color(0.35f, 0.65f, 1f, 0.9f));
            overlay.Add(highlight);

            var caption = new Label
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.9f),
                    color = Color.white,
                    fontSize = 10,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 1,
                    paddingBottom = 1,
                    display = DisplayStyle.None,
                    maxWidth = 460,
                    whiteSpace = WhiteSpace.Normal,
                },
            };

            overlay.Add(caption);

            overlay.RegisterCallback<PointerMoveEvent>(evt =>
                OnMove(window, overlay, highlight, caption, evt.position));

            overlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                evt.StopImmediatePropagation();

                // Right-click and middle-click cancel rather than pick, so a mis-entered pick mode
                // can be escaped without committing a note to some arbitrary control.
                if (evt.button != 0)
                {
                    Cancel();
                    return;
                }

                Finish(window, overlay, evt.position);
            });

            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                {
                    return;
                }

                evt.StopPropagation();
                Cancel();
            });

            root.Add(overlay);
            overlay.BringToFront();
            overlay.Focus();
            Overlays.Add(overlay);
        }

        private static void OnMove(EditorWindow window, VisualElement overlay, VisualElement highlight,
            Label caption, Vector2 position)
        {
            var picked = PickUnder(overlay, position);
            if (picked == null)
            {
                highlight.style.display = DisplayStyle.None;
                caption.style.display = DisplayStyle.None;
                return;
            }

            var bounds = picked.worldBound;
            if (bounds.width <= 0 || bounds.height <= 0 || float.IsNaN(bounds.width))
            {
                return;
            }

            var local = overlay.WorldToLocal(new Vector2(bounds.xMin, bounds.yMin));

            highlight.style.display = DisplayStyle.Flex;
            highlight.style.left = local.x;
            highlight.style.top = local.y;
            highlight.style.width = bounds.width;
            highlight.style.height = bounds.height;

            caption.style.display = DisplayStyle.Flex;
            caption.text = Describe(window, picked);

            // Above the element normally, below it when there is no room, so the caption never sits
            // off the top of the window where it cannot be read.
            caption.style.left = local.x;
            caption.style.top = local.y > 18f ? local.y - 18f : local.y + bounds.height + 2f;
        }

        /// <summary>
        /// Reports one pick without leaving pick mode.
        /// </summary>
        /// <remarks>
        /// Staying in is what makes collecting several controls possible. A click that lands on
        /// nothing pickable is ignored rather than treated as a cancel, since the likeliest reason
        /// for one is a slightly-missed target.
        /// </remarks>
        private static void Finish(EditorWindow window, VisualElement overlay, Vector2 position)
        {
            var picked = PickUnder(overlay, position);
            if (picked == null)
            {
                return;
            }

            _onPicked?.Invoke(Describe(window, picked));
        }

        /// <summary>
        /// What the click would have hit, had the overlay not been in the way.
        /// </summary>
        private static VisualElement PickUnder(VisualElement overlay, Vector2 position)
        {
            var panel = overlay.panel;
            if (panel == null)
            {
                return null;
            }

            var mode = overlay.pickingMode;
            overlay.pickingMode = PickingMode.Ignore;

            try
            {
                var picked = panel.Pick(position);

                // Belt and braces: if the hit test still lands inside the overlay, treat it as a miss
                // rather than describing our own furniture back to the user.
                return IsOurs(picked) ? null : picked;
            }
            finally
            {
                overlay.pickingMode = mode;
            }
        }

        private static bool IsOurs(VisualElement element)
        {
            for (var e = element; e != null; e = e.parent)
            {
                if (e.name == "scratchpad-picker-overlay")
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Names the element and its ancestors, nearest last.
        /// </summary>
        /// <remarks>
        /// The path is written root-first because that is the order it is read in — window, then the
        /// region, then the control. Text is included wherever an element has any, since a button's
        /// label identifies it far better than its type does.
        /// </remarks>
        private static string Describe(EditorWindow window, VisualElement element)
        {
            var parts = new List<string>();

            for (var e = element; e != null && parts.Count < MaxDepth; e = e.parent)
            {
                parts.Add(Name(e));
            }

            parts.Reverse();

            var builder = new StringBuilder();
            builder.Append(window.GetType().Name);
            builder.Append(": ");
            builder.Append(string.Join(" > ", parts));
            return builder.ToString();
        }

        private static string Name(VisualElement element)
        {
            var type = element.GetType().Name;

            if (!string.IsNullOrEmpty(element.name) && !element.name.StartsWith("unity-", StringComparison.Ordinal))
            {
                return $"{type}#{element.name}";
            }

            if (element is TextElement text && !string.IsNullOrWhiteSpace(text.text))
            {
                var line = text.text.Trim().Replace('\n', ' ');
                if (line.Length > 32)
                {
                    line = line[..29] + "...";
                }

                return $"{type} \"{line}\"";
            }

            return type;
        }
    }
}
