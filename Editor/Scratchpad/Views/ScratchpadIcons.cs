using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VaporEditor.Scratchpad
{
    /// <summary>Which glyph an icon element draws.</summary>
    internal enum ScratchpadIcon
    {
        Refresh,
        CopyPrompt,
        CopyContract,
        AddFeature,
        AddSession,
        Archive,
        Unarchive,
        Console,
        Settings,
        Link,
        Rename,
        AddNote,
        Pick,
    }

    /// <summary>
    /// The window's icons, drawn rather than loaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Same reasoning as the review dots in <see cref="ScratchpadStyles"/>, and it is worth repeating
    /// because it is the whole argument: <c>EditorGUIUtility.IconContent</c> names are undocumented,
    /// they change between editor versions, and a name that stops resolving renders as nothing at all.
    /// A toolbar that silently loses its icons on an editor upgrade is a bad trade for the few minutes
    /// drawing them costs.
    /// </para>
    /// <para>
    /// Every glyph is authored in a 16×16 space and scaled to whatever the element is given, so the
    /// same path serves a 12px inline button and a 20px toolbar one. They are stroked rather than
    /// filled: a stroke keeps its weight when the editor's font scaling changes the element size,
    /// where a filled shape would turn into a blob.
    /// </para>
    /// </remarks>
    internal static class ScratchpadIcons
    {
        private const float Canvas = 16f;

        /// <summary>An icon element that repaints itself when <paramref name="tint"/> changes.</summary>
        public static VisualElement Create(ScratchpadIcon icon, Color tint, float size = 14f)
        {
            var element = new VisualElement
            {
                style =
                {
                    width = size,
                    height = size,
                    flexShrink = 0,
                    alignSelf = Align.Center,
                },
                pickingMode = PickingMode.Ignore,
            };

            element.generateVisualContent += context => Paint(context, icon, tint);
            return element;
        }

        /// <summary>An icon with a label beside it, for a button's content.</summary>
        public static VisualElement WithLabel(ScratchpadIcon icon, string text, Color tint, float size = 14f)
        {
            var row = ScratchpadStyles.Row();
            row.pickingMode = PickingMode.Ignore;
            row.Add(Create(icon, tint, size));

            var label = new Label(text)
            {
                style = { marginLeft = 4, color = tint },
                pickingMode = PickingMode.Ignore,
            };

            row.Add(label);
            return row;
        }

        private static void Paint(MeshGenerationContext context, ScratchpadIcon icon, Color tint)
        {
            var rect = context.visualElement.contentRect;
            if (rect.width <= 0 || rect.height <= 0 || float.IsNaN(rect.width))
            {
                return;
            }

            var scale = Mathf.Min(rect.width, rect.height) / Canvas;
            var offsetX = (rect.width - Canvas * scale) * 0.5f;
            var offsetY = (rect.height - Canvas * scale) * 0.5f;

            Vector2 P(float x, float y) => new(offsetX + x * scale, offsetY + y * scale);

            var painter = context.painter2D;
            painter.lineWidth = Mathf.Max(1f, 1.4f * scale);
            painter.strokeColor = tint;
            painter.fillColor = tint;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            switch (icon)
            {
                case ScratchpadIcon.Refresh:
                    PaintRefresh(painter, P, scale, offsetX, offsetY);
                    break;

                case ScratchpadIcon.CopyPrompt:
                    PaintCopyPrompt(painter, P);
                    break;

                case ScratchpadIcon.CopyContract:
                    PaintCopyContract(painter, P);
                    break;

                case ScratchpadIcon.AddFeature:
                    PaintFolder(painter, P);
                    PaintPlus(painter, P, 11.5f, 10.5f);
                    break;

                case ScratchpadIcon.AddSession:
                    PaintPage(painter, P);
                    PaintPlus(painter, P, 11.5f, 11f);
                    break;

                case ScratchpadIcon.Archive:
                    PaintBox(painter, P);
                    PaintArrow(painter, P, down: true);
                    break;

                case ScratchpadIcon.Unarchive:
                    PaintBox(painter, P);
                    PaintArrow(painter, P, down: false);
                    break;

                case ScratchpadIcon.Console:
                    PaintConsole(painter, P);
                    break;

                case ScratchpadIcon.Settings:
                    PaintSettings(painter, P, scale, offsetX, offsetY);
                    break;

                case ScratchpadIcon.Link:
                    PaintLink(painter, P);
                    break;

                case ScratchpadIcon.Rename:
                    PaintRename(painter, P);
                    break;

                case ScratchpadIcon.AddNote:
                    PaintAddNote(painter, P);
                    break;

                case ScratchpadIcon.Pick:
                    PaintPick(painter, P, scale, offsetX, offsetY);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(icon), icon, null);
            }
        }

        #region Glyphs

        /// <summary>Three quarters of a circle with a head on the end.</summary>
        private static void PaintRefresh(Painter2D painter, Func<float, float, Vector2> p, float scale,
            float offsetX, float offsetY)
        {
            var center = new Vector2(offsetX + 8f * scale, offsetY + 8f * scale);

            painter.BeginPath();
            painter.Arc(center, 5.5f * scale, Angle.Degrees(40), Angle.Degrees(320));
            painter.Stroke();

            // The head sits at the open end of the arc, pointing along the sweep.
            painter.BeginPath();
            painter.MoveTo(p(11.5f, 2.5f));
            painter.LineTo(p(12.6f, 5.6f));
            painter.LineTo(p(9.4f, 5.2f));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>A speech bubble: the notes going back into a conversation.</summary>
        private static void PaintCopyPrompt(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(2.5f, 3f));
            painter.LineTo(p(13.5f, 3f));
            painter.LineTo(p(13.5f, 10.5f));
            painter.LineTo(p(6.5f, 10.5f));
            painter.LineTo(p(4f, 13.5f));
            painter.LineTo(p(4f, 10.5f));
            painter.LineTo(p(2.5f, 10.5f));
            painter.ClosePath();
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(5f, 6f));
            painter.LineTo(p(11f, 6f));
            painter.MoveTo(p(5f, 8f));
            painter.LineTo(p(9f, 8f));
            painter.Stroke();
        }

        /// <summary>A document with a folded corner and ruled lines.</summary>
        private static void PaintCopyContract(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(3.5f, 2f));
            painter.LineTo(p(9.5f, 2f));
            painter.LineTo(p(12.5f, 5f));
            painter.LineTo(p(12.5f, 14f));
            painter.LineTo(p(3.5f, 14f));
            painter.ClosePath();
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(9.5f, 2f));
            painter.LineTo(p(9.5f, 5f));
            painter.LineTo(p(12.5f, 5f));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(5.5f, 8f));
            painter.LineTo(p(10.5f, 8f));
            painter.MoveTo(p(5.5f, 10.5f));
            painter.LineTo(p(10.5f, 10.5f));
            painter.Stroke();
        }

        private static void PaintFolder(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(2f, 12.5f));
            painter.LineTo(p(2f, 3.5f));
            painter.LineTo(p(6f, 3.5f));
            painter.LineTo(p(7.5f, 5.5f));
            painter.LineTo(p(13f, 5.5f));
            painter.LineTo(p(13f, 12.5f));
            painter.ClosePath();
            painter.Stroke();
        }

        private static void PaintPage(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(3.5f, 2f));
            painter.LineTo(p(11f, 2f));
            painter.LineTo(p(11f, 14f));
            painter.LineTo(p(3.5f, 14f));
            painter.ClosePath();
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(5.5f, 5.5f));
            painter.LineTo(p(9f, 5.5f));
            painter.MoveTo(p(5.5f, 8f));
            painter.LineTo(p(9f, 8f));
            painter.Stroke();
        }

        /// <summary>A plus sitting over the lower-right of whatever it was drawn on.</summary>
        private static void PaintPlus(Painter2D painter, Func<float, float, Vector2> p, float x, float y)
        {
            const float arm = 2.6f;

            painter.BeginPath();
            painter.MoveTo(p(x - arm, y));
            painter.LineTo(p(x + arm, y));
            painter.MoveTo(p(x, y - arm));
            painter.LineTo(p(x, y + arm));
            painter.Stroke();
        }

        /// <summary>A lidded crate, which is what archiving looks like everywhere else.</summary>
        private static void PaintBox(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(2f, 3f));
            painter.LineTo(p(14f, 3f));
            painter.LineTo(p(14f, 6f));
            painter.LineTo(p(2f, 6f));
            painter.ClosePath();
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(3f, 6f));
            painter.LineTo(p(3f, 13.5f));
            painter.LineTo(p(13f, 13.5f));
            painter.LineTo(p(13f, 6f));
            painter.Stroke();
        }

        private static void PaintArrow(Painter2D painter, Func<float, float, Vector2> p, bool down)
        {
            var tip = down ? 11.8f : 8f;
            var tail = down ? 8f : 11.8f;
            var wing = down ? 9.6f : 10.2f;

            painter.BeginPath();
            painter.MoveTo(p(8f, tail));
            painter.LineTo(p(8f, tip));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(8f, tip));
            painter.LineTo(p(6f, wing));
            painter.MoveTo(p(8f, tip));
            painter.LineTo(p(10f, wing));
            painter.Stroke();
        }

        /// <summary>A prompt chevron and a caret rule: the console, in two strokes.</summary>
        private static void PaintConsole(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(2f, 2.5f));
            painter.LineTo(p(14f, 2.5f));
            painter.LineTo(p(14f, 13.5f));
            painter.LineTo(p(2f, 13.5f));
            painter.ClosePath();
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(4.5f, 6f));
            painter.LineTo(p(7f, 8f));
            painter.LineTo(p(4.5f, 10f));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(8.5f, 10.5f));
            painter.LineTo(p(11.5f, 10.5f));
            painter.Stroke();
        }

        /// <summary>A hub with six spokes. A literal cog reads as mud at this size.</summary>
        private static void PaintSettings(Painter2D painter, Func<float, float, Vector2> p, float scale,
            float offsetX, float offsetY)
        {
            var center = new Vector2(offsetX + 8f * scale, offsetY + 8f * scale);

            painter.BeginPath();
            painter.Arc(center, 3.2f * scale, Angle.Degrees(0), Angle.Degrees(360));
            painter.Stroke();

            for (var i = 0; i < 6; i++)
            {
                var radians = i * Mathf.PI / 3f;
                var dx = Mathf.Cos(radians);
                var dy = Mathf.Sin(radians);

                painter.BeginPath();
                painter.MoveTo(new Vector2(center.x + dx * 4.4f * scale, center.y + dy * 4.4f * scale));
                painter.LineTo(new Vector2(center.x + dx * 6.4f * scale, center.y + dy * 6.4f * scale));
                painter.Stroke();
            }
        }

        /// <summary>Two half-links, for attaching a note to a change.</summary>
        private static void PaintLink(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(6.5f, 4.5f));
            painter.LineTo(p(9.5f, 4.5f));
            painter.LineTo(p(11.5f, 6.5f));
            painter.LineTo(p(11.5f, 8f));
            painter.LineTo(p(9.5f, 10f));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(9.5f, 11.5f));
            painter.LineTo(p(6.5f, 11.5f));
            painter.LineTo(p(4.5f, 9.5f));
            painter.LineTo(p(4.5f, 8f));
            painter.LineTo(p(6.5f, 6f));
            painter.Stroke();
        }

        /// <summary>
        /// A note card with a plus: the thing the composer actually makes.
        /// </summary>
        /// <remarks>
        /// Drawn as the card it produces — a wide rectangle with the thick coloured left edge every
        /// note in the detail pane carries — rather than as a generic plus. The three near neighbours
        /// were all taken: the speech bubble means "prompt", the page-and-plus means "new session",
        /// and a pen would read as the rename pencil.
        /// </remarks>
        private static void PaintAddNote(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(2.5f, 4f));
            painter.LineTo(p(11.5f, 4f));
            painter.LineTo(p(11.5f, 12f));
            painter.LineTo(p(2.5f, 12f));
            painter.ClosePath();
            painter.Stroke();

            // The card's left edge, doubled in weight to match the note card in the detail pane.
            var width = painter.lineWidth;
            painter.lineWidth = width * 2f;
            painter.BeginPath();
            painter.MoveTo(p(2.5f, 4f));
            painter.LineTo(p(2.5f, 12f));
            painter.Stroke();
            painter.lineWidth = width;

            painter.BeginPath();
            painter.MoveTo(p(5f, 7f));
            painter.LineTo(p(9f, 7f));
            painter.MoveTo(p(5f, 9.5f));
            painter.LineTo(p(7.5f, 9.5f));
            painter.Stroke();

            PaintPlus(painter, p, 12.4f, 11.6f);
        }

        /// <summary>
        /// A crosshair with a gap at the centre — the target reticle, not a plus.
        /// </summary>
        /// <remarks>
        /// The gap is what separates it from <see cref="ScratchpadIcon.AddNote"/>'s plus at this size.
        /// Deliberately close to the shape the UI Toolkit Debugger uses for the same gesture, since
        /// that is where the user will have learnt it.
        /// </remarks>
        private static void PaintPick(Painter2D painter, Func<float, float, Vector2> p, float scale,
            float offsetX, float offsetY)
        {
            var center = new Vector2(offsetX + 8f * scale, offsetY + 8f * scale);

            painter.BeginPath();
            painter.Arc(center, 4.6f * scale, Angle.Degrees(0), Angle.Degrees(360));
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(8f, 1.4f));
            painter.LineTo(p(8f, 5.4f));
            painter.MoveTo(p(8f, 10.6f));
            painter.LineTo(p(8f, 14.6f));
            painter.MoveTo(p(1.4f, 8f));
            painter.LineTo(p(5.4f, 8f));
            painter.MoveTo(p(10.6f, 8f));
            painter.LineTo(p(14.6f, 8f));
            painter.Stroke();

            painter.BeginPath();
            painter.Arc(center, 1.1f * scale, Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill();
        }

        /// <summary>A pencil over a rule.</summary>
        private static void PaintRename(Painter2D painter, Func<float, float, Vector2> p)
        {
            painter.BeginPath();
            painter.MoveTo(p(3f, 10.5f));
            painter.LineTo(p(10f, 3.5f));
            painter.LineTo(p(12.5f, 6f));
            painter.LineTo(p(5.5f, 13f));
            painter.LineTo(p(2.5f, 13.5f));
            painter.ClosePath();
            painter.Stroke();

            painter.BeginPath();
            painter.MoveTo(p(8.5f, 5f));
            painter.LineTo(p(11f, 7.5f));
            painter.Stroke();
        }

        #endregion
    }
}
