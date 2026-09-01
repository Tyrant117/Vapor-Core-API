using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>
    /// The scratchpad's visual vocabulary, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window builds its tree in C# rather than from UXML. Both conventions exist in this package
    /// — the project-management windows clone UXML out of <c>Resources</c>, the inspector's visual
    /// elements construct themselves — and the deciding factor here is that a UXML file is an asset:
    /// it can fail to import, go missing from a package, or silently load as null, and the failure
    /// shows up as an empty window rather than as a compiler error.
    /// </para>
    /// <para>
    /// Colours are resolved against the current skin rather than hard-coded, and the state colours
    /// are shared with the note pills so that a kind means the same colour everywhere it appears.
    /// </para>
    /// </remarks>
    internal static class ScratchpadStyles
    {
        public static bool Pro => EditorGUIUtility.isProSkin;

        public static Color Background => Pro ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.76f, 0.76f, 0.76f);
        public static Color Panel => Pro ? new Color(0.24f, 0.24f, 0.24f) : new Color(0.80f, 0.80f, 0.80f);
        public static Color Raised => Pro ? new Color(0.27f, 0.27f, 0.27f) : new Color(0.85f, 0.85f, 0.85f);
        public static Color Line => Pro ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.60f, 0.60f, 0.60f);
        public static Color Selected => Pro ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.24f, 0.48f, 0.90f);
        public static Color Hover => Pro ? new Color(0.31f, 0.31f, 0.31f) : new Color(0.88f, 0.88f, 0.88f);

        public static Color Text => Pro ? new Color(0.83f, 0.83f, 0.83f) : new Color(0.10f, 0.10f, 0.10f);
        public static Color Dim => Pro ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.38f, 0.38f, 0.38f);

        public static Color Issue => new(0.90f, 0.42f, 0.36f);
        public static Color Work => new(0.45f, 0.68f, 0.92f);
        public static Color Comment => new(0.62f, 0.62f, 0.66f);
        public static Color Ok => new(0.45f, 0.78f, 0.48f);
        public static Color CloserLook => new(0.94f, 0.72f, 0.30f);

        public static Color KindColor(NoteKind kind) => kind switch
        {
            NoteKind.Issue => Issue,
            NoteKind.Work => Work,
            _ => Comment,
        };

        /// <summary>Green for live, gold for in flight, grey for done.</summary>
        /// <remarks>
        /// The status word is the one piece of text on a note card that changes meaning without
        /// changing length, so it is the one worth colouring. The palette says what is owed rather
        /// than what happened: <see cref="NoteStatus.Open"/> and <see cref="NoteStatus.Sent"/> are
        /// both outstanding and both bright, and the two closed states recede together whether they
        /// were acted on or not.
        /// </remarks>
        public static Color StatusColor(NoteStatus status) => status switch
        {
            NoteStatus.Open => Live,
            NoteStatus.Sent => InFlight,
            NoteStatus.Resolved => Settled,
            _ => Dim,
        };

        /// <summary>A readable green — the light one, so it holds up on the pro skin's grey.</summary>
        public static Color Live => Pro ? new Color(0.48f, 0.85f, 0.53f) : new Color(0.13f, 0.52f, 0.20f);

        /// <summary>Handed over, waiting on a reply.</summary>
        public static Color InFlight => Pro ? new Color(0.96f, 0.78f, 0.36f) : new Color(0.60f, 0.44f, 0.05f);

        /// <summary>Closed and no longer asking for anything.</summary>
        public static Color Settled => Pro ? new Color(0.44f, 0.58f, 0.46f) : new Color(0.30f, 0.44f, 0.32f);

        public static Color ReviewColor(ReviewState state) => state switch
        {
            ReviewState.Ok => Ok,
            ReviewState.CloserLook => CloserLook,
            _ => Dim,
        };

        #region Builders

        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static VisualElement Column()
        {
            var column = new VisualElement();
            column.style.flexDirection = FlexDirection.Column;
            return column;
        }

        public static VisualElement Spacer()
        {
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            return spacer;
        }

        public static Label Header(string text)
        {
            var label = new Label(text);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = Text;
            label.style.marginBottom = 2;
            return label;
        }

        /// <summary>A section caption: small, upper case, and out of the way.</summary>
        public static Label Caption(string text)
        {
            var label = new Label(text.ToUpperInvariant());
            label.style.fontSize = 9;
            label.style.color = Dim;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 8;
            label.style.marginBottom = 2;
            return label;
        }

        /// <summary>Body copy. Wraps, selectable, and dimmed when it is standing in for missing text.</summary>
        public static Label Body(string text, bool dim = false)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = dim ? Dim : Text;
            label.selection.isSelectable = true;
            return label;
        }

        /// <summary>
        /// The small coloured tag that names a note's kind.
        /// </summary>
        public static VisualElement Pill(string text, Color color)
        {
            var pill = new Label(text)
            {
                style =
                {
                    fontSize = 9,
                    color = color,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 1,
                    paddingBottom = 1,
                    marginRight = 4,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                    flexShrink = 0,
                },
            };

            SetBorderColor(pill, color);
            SetBorderWidth(pill, 1);
            return pill;
        }

        /// <summary>
        /// The review-state dot: hollow for unread, filled once you have made up your mind.
        /// </summary>
        /// <remarks>
        /// Drawn rather than loaded from <c>EditorGUIUtility.IconContent</c>. Built-in icon names are
        /// undocumented and change between editor versions, and a missing one renders as nothing at
        /// all — which for this control would mean a change looking reviewed because its glyph failed
        /// to load.
        /// </remarks>
        public static VisualElement ReviewDot(ReviewState state)
        {
            var color = ReviewColor(state);
            var dot = new VisualElement
            {
                style =
                {
                    width = 9,
                    height = 9,
                    marginRight = 6,
                    marginLeft = 2,
                    flexShrink = 0,
                    borderTopLeftRadius = 5,
                    borderTopRightRadius = 5,
                    borderBottomLeftRadius = 5,
                    borderBottomRightRadius = 5,
                    backgroundColor = state == ReviewState.Unreviewed ? Color.clear : color,
                },
            };

            SetBorderColor(dot, color);
            SetBorderWidth(dot, state == ReviewState.Unreviewed ? 1.5f : 0);
            return dot;
        }

        /// <summary>A count badge, hidden entirely when the count is zero.</summary>
        public static VisualElement Badge(int count, Color color)
        {
            var badge = new Label(count.ToString())
            {
                style =
                {
                    fontSize = 9,
                    color = Color.white,
                    backgroundColor = color,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    minWidth = 15,
                    paddingLeft = 3,
                    paddingRight = 3,
                    marginLeft = 3,
                    flexShrink = 0,
                    borderTopLeftRadius = 7,
                    borderTopRightRadius = 7,
                    borderBottomLeftRadius = 7,
                    borderBottomRightRadius = 7,
                    display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None,
                },
            };

            return badge;
        }

        /// <summary>
        /// A button carrying a drawn icon and a label, with the tooltip it is required to have.
        /// </summary>
        /// <remarks>
        /// The tooltip is a constructor parameter rather than something to set afterwards so that a
        /// button without one does not compile. Nothing in this window is self-evident enough to skip
        /// it — half the controls act on files.
        /// </remarks>
        public static Button IconButton(ScratchpadIcon icon, string text, string tooltip, Action action,
            float size = 13f)
        {
            var button = new Button(action) { tooltip = tooltip };
            button.style.paddingLeft = 4;
            button.style.paddingRight = 6;
            CenterContent(button);
            button.Add(ScratchpadIcons.WithLabel(icon, text, Text, size));
            return button;
        }

        /// <summary>
        /// Makes an element centre whatever is put inside it, both ways.
        /// </summary>
        /// <remarks>
        /// A <see cref="VisualElement"/> defaults to <c>flex-direction: column</c>, so an icon set to
        /// <c>align-self: center</c> inside one is centred <em>horizontally</em> and left wherever the
        /// main axis puts it vertically — which is the top. That is subtle enough to be worth stating
        /// once here rather than rediscovering per control: any element hosting a bare icon has to say
        /// which way round its axes are, and the answer is always row.
        /// </remarks>
        public static void CenterContent(VisualElement element)
        {
            element.style.flexDirection = FlexDirection.Row;
            element.style.alignItems = Align.Center;
            element.style.justifyContent = Justify.Center;
        }

        /// <summary>An icon-only button, for a row too dense to carry a word.</summary>
        public static Button IconOnlyButton(ScratchpadIcon icon, string tooltip, Action action,
            Color? tint = null, float size = 13f)
        {
            var button = new Button(action) { tooltip = tooltip };
            SetPadding(button, 2);
            button.style.marginLeft = 1;
            button.style.marginRight = 1;
            button.style.flexShrink = 0;
            CenterContent(button);
            button.Add(ScratchpadIcons.Create(icon, tint ?? Text, size));
            return button;
        }

        /// <summary>A plain text button that still has to declare a tooltip.</summary>
        public static Button TextButton(string text, string tooltip, Action action) =>
            new(action) { text = text, tooltip = tooltip, style = { fontSize = 10 } };

        public static VisualElement Separator()
        {
            var line = new VisualElement();
            line.style.height = 1;
            line.style.backgroundColor = Line;
            line.style.marginTop = 4;
            line.style.marginBottom = 4;
            return line;
        }

        public static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        public static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }

        public static void SetPadding(VisualElement element, float value)
        {
            element.style.paddingTop = value;
            element.style.paddingBottom = value;
            element.style.paddingLeft = value;
            element.style.paddingRight = value;
        }

        /// <summary>
        /// Makes a row light up under the cursor and stay lit while it is the selected one.
        /// </summary>
        public static void MakeSelectable(VisualElement row, Func<bool> isSelected)
        {
            void Paint() => row.style.backgroundColor = isSelected() ? Selected : Color.clear;

            row.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!isSelected())
                {
                    row.style.backgroundColor = Hover;
                }
            });

            row.RegisterCallback<MouseLeaveEvent>(_ => Paint());
            Paint();
        }

        #endregion
    }
}
