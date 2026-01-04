using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace Vapor
{
    // An element that displays progress inside a partially filled circle
    [UxmlElement]
    public partial class RadialProgress : VisualElement
    {
        // These are USS class names for the control overall and the label.
        private const string k_USSClassName = "radial-progress";
        private const string k_USSTextElementClassName = "radial-progress__label";
        private const string k_USSIconClassName = "radial-progress__icon";

        // These objects allow C# code to access custom USS properties.
        private static readonly CustomStyleProperty<Color> s_TrackColor = new("--track-color");
        private static readonly CustomStyleProperty<Color> s_ProgressColor = new("--progress-color");
        private static readonly CustomStyleProperty<Color> s_BackgroundColor = new("--background-color");

        private Color _trackColor = Color.black;
        private Color _progressColor = Color.red;
        private Color _backgroundColor = Color.white;

        // This is the label that displays the percentage.
        private TextElement _textElement;
        private Image _icon;

        // This is the number that the Label displays as a percentage.
        private float _progress;

        // A value between 0 and 1
        [UxmlAttribute]
        public float Progress
        {
            // The progress property is exposed in C#.
            get => _progress;
            set
            {
                // Whenever the progress property changes, MarkDirtyRepaint() is named. This causes a call to the
                // generateVisualContents callback.
                _progress = Mathf.Clamp01(value);
                if(_textElement != null)
                {
                    _textElement.text = $"{_progress * 100:P1}";
                }
                MarkDirtyRepaint();
            }
        }

        private float _thickness = 12.0f;
        [UxmlAttribute]
        public float Thickness
        {
            get => _thickness;
            set
            {
                _thickness = value;
                MarkDirtyRepaint();
            }
        }

        // This default constructor is RadialProgress's only constructor.
        public RadialProgress()
        {
            // Add the USS class name for the overall control.
            AddToClassList(k_USSClassName);

            // Register a callback after custom style resolution.
            RegisterCallback<CustomStyleResolvedEvent>(CustomStylesResolved);

            // Register a callback to generate the visual content of the control.
            generateVisualContent += GenerateVisualContent;

            Progress = 0.0f;
        }

        public RadialProgress WithText()
        {
            // Create a Label, add a USS class name, and add it to this visual tree.
            _textElement = new TextElement { text = $"{Progress * 100:P1}" }.AddClasses(k_USSTextElementClassName);
            Add(_textElement);
            return this;
        }

        public RadialProgress WithIcon(Sprite icon)
        {
            _icon = new Image { sprite = icon }.AddClasses(k_USSIconClassName);
            Add(_icon);
            return this;
        }

        private static void CustomStylesResolved(CustomStyleResolvedEvent evt)
        {
            RadialProgress element = (RadialProgress)evt.currentTarget;
            element.UpdateCustomStyles();
        }

        // After the custom colors are resolved, this method uses them to color the meshes and (if necessary) repaint
        // the control.
        private void UpdateCustomStyles()
        {
            bool repaint = false;
            if (customStyle.TryGetValue(s_ProgressColor, out var progressColor))
            {
                _progressColor = progressColor;
                repaint = true;
            }
            
            if (customStyle.TryGetValue(s_TrackColor, out var trackColor))
            {
                _trackColor = trackColor;
                repaint = true;
            }
            
            if (customStyle.TryGetValue(s_BackgroundColor, out var backgroundColor))
            {
                _backgroundColor = backgroundColor;
                repaint = true;
            }

            if (repaint)
            {
                MarkDirtyRepaint();
            }
        }

        private void GenerateVisualContent(MeshGenerationContext context)
        {
            float width = contentRect.width;
            float height = contentRect.height;

            var painter = context.painter2D;
            painter.lineWidth = Thickness;
            painter.lineCap = LineCap.Butt;

            // Draw the background
            painter.fillColor = _backgroundColor;
            painter.BeginPath();
            painter.Arc(new Vector2(width * 0.5f, height * 0.5f), width * 0.5f, 0.0f, 360.0f);
            painter.Fill();


            // Draw the track
            painter.strokeColor = _trackColor;
            painter.BeginPath();
            painter.Arc(new Vector2(width * 0.5f, height * 0.5f), width * 0.5f, 0.0f, 360.0f);
            painter.Stroke();

            // Draw the progress
            painter.strokeColor = _progressColor;
            painter.BeginPath();
            painter.Arc(new Vector2(width * 0.5f, height * 0.5f), width * 0.5f, -90.0f, 360.0f * Progress - 90.0f);
            painter.Stroke();
        }
    }
}
