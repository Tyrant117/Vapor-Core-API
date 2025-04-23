using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.ProjectManagement
{
    [UxmlElement]
    public partial class TextEditorControlsView : VisualElement
    {
        private TextField _textField;

        private Button _bold;
        public Button Bold
        {
            get
            {
                _bold ??= this.Q<Button>("Bold");
                return _bold;
            }
        }
        private Button _italics;
        public Button Italics
        {
            get
            {
                _italics ??= this.Q<Button>("Italics");
                return _italics;
            }
        }
        private Button _underline;
        public Button Underline
        {
            get
            {
                _underline ??= this.Q<Button>("Underline");
                return _underline;
            }
        }
        
        private Button _h1;
        public Button H1
        {
            get
            {
                _h1 ??= this.Q<Button>("H1");
                return _h1;
            }
        }
        private Button _h2;
        public Button H2
        {
            get
            {
                _h2 ??= this.Q<Button>("H2");
                return _h2;
            }
        }
        private Button _h3;
        public Button H3
        {
            get
            {
                _h3 ??= this.Q<Button>("H3");
                return _h3;
            }
        }
        
        private Button _left;
        public Button Left
        {
            get
            {
                _left ??= this.Q<Button>("Left");
                return _left;
            }
        }
        private Button _center;
        public Button Center
        {
            get
            {
                _center ??= this.Q<Button>("Center");
                return _center;
            }
        }
        private Button _right;
        public Button Right
        {
            get
            {
                _right ??= this.Q<Button>("Right");
                return _right;
            }
        }
        
        private Button _list;
        public Button List
        {
            get
            {
                _list ??= this.Q<Button>("List");
                return _list;
            }
        }
        
        private Button _raw;
        public Button Raw
        {
            get
            {
                _raw ??= this.Q<Button>("Raw");
                return _raw;
            }
        }

        private TextElement _textElement;
        private Vector2Int _lastRange;
        private bool _isRawView;
        

        public TextEditorControlsView()
        {
            this.ConstructFromResourcePath("Styles/TextEditorControlsView");
        }

        public void Init(TextField textField)
        {
            _textField = textField;
            _textElement = _textField.Q<TextElement>();
            _textElement.RegisterCallback<PointerUpEvent>(OnFieldLostFocus);
            _textElement.RegisterCallback<NavigationMoveEvent>(OnNavigationMove);

            Bold.clicked += OnBoldSelection;
            Italics.clicked += OnItalicsSelection;
            Underline.clicked += OnUnderlineSelection;

            H1.clicked += OnHeading1Selection;
            H2.clicked += OnHeading2Selection;
            H3.clicked += OnHeading3Selection;

            Left.Q<VisualElement>("Icon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_align_horizontally_left").image);
            Center.Q<VisualElement>("Icon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_align_horizontally_center").image);
            Right.Q<VisualElement>("Icon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_align_horizontally_right").image);
            List.Q<VisualElement>("Icon").style.backgroundImage = new StyleBackground((Texture2D)EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow@2x").image);
            
            Left.clicked += OnLeftAlignSelection;
            Center.clicked += OnCenterAlignSelection;
            Right.clicked += OnRightAlignSelection;

            List.clicked += OnListSelection;
            Raw.clicked += OnToggleRawDisplay;
        }

        public void Show()
        {
            if (_textField.text.StartsWith("<noparse>"))
            {
                _isRawView = true;
                Raw.AddToClassList("raw-button");
            }
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            int start = Mathf.Min(_textField.cursorIndex, _textField.selectIndex);
            int end = Mathf.Max(_textField.cursorIndex, _textField.selectIndex);
            _lastRange = new Vector2Int(start, end);
            evt.StopPropagation();
        }

        private void OnFieldLostFocus(PointerUpEvent evt)
        {
            int start = Mathf.Min(_textField.cursorIndex, _textField.selectIndex);
            int end = Mathf.Max(_textField.cursorIndex, _textField.selectIndex);
            _lastRange = new Vector2Int(start, end);
            evt.StopPropagation();
        }

        private void OnBoldSelection()
        {
            ApplyTag("b", string.Empty);
        }

        private void OnItalicsSelection()
        {
            ApplyTag("i", string.Empty);
        }

        private void OnUnderlineSelection()
        {
            ApplyTag("u", string.Empty);
        }

        private void OnHeading1Selection()
        {
            ApplyTag("size", "=+8");
        }

        private void OnHeading2Selection()
        {
            ApplyTag("size", "=+5");
        }

        private void OnHeading3Selection()
        {
            ApplyTag("size", "=+3");
        }

        private void OnLeftAlignSelection()
        {
            ApplyTag("align", "=left");
        }

        private void OnCenterAlignSelection()
        {
            ApplyTag("align", "=center");
        }

        private void OnRightAlignSelection()
        {
            ApplyTag("align", "=right");
        }
        
        private void OnListSelection()
        {
            ApplyTag("indent", "=12px");
        }

        private void OnToggleRawDisplay()
        {
            _isRawView = !_isRawView;
            if (_isRawView)
            {
                var text = _textElement.text;
                text = $"<noparse>{text}</noparse>";
                _textElement.text = text;
                Raw.AddToClassList("raw-button");
            }
            else
            {
                var text = _textElement.text;
                if (text.StartsWith("<noparse>"))
                {
                    text = text.Substring(9, text.Length - 9);
                }

                if (text.EndsWith("</noparse>"))
                {
                    text = text[..^10];
                }

                _textElement.text = text;
                Raw.RemoveFromClassList("raw-button");
            }
        }

        private void ApplyTag(string tag, string tagValue)
        {
            if (_lastRange.x == _lastRange.y) return; // no selection

            string originalText = _textField.text.Replace("<br>", "\n");
            var innerStart = MapStrippedToRich(originalText, _lastRange.x);
            var outerStart = innerStart;
            var outerEnd = MapStrippedToRich(originalText, _lastRange.y);
            var innerEnd = outerEnd;
            string innerSelected = originalText.Substring(innerStart, innerEnd - innerStart);
            if (TryGetTagLeft(originalText, innerStart, out var lts, out var fidx))
            {
                outerStart = fidx;
            }

            if (TryGetTagLeft(originalText, outerEnd, out var rts, out var lidx))
            {
                innerEnd = lidx;
                innerSelected = originalText.Substring(innerStart, innerEnd - innerStart);
            }
            rts.Reverse();

            var removeIdxs = new List<int>();
            if (lts.Count == 0 && rts.Count == 0)
            {
                innerSelected = $"<{tag}{tagValue}>{innerSelected}</{tag}>";
            }
            else
            {
                for (int i = 0; i < Mathf.Min(lts.Count, rts.Count); i++)
                {
                    var tagL = lts[i];
                    var tagR = rts[i];
                    if (tagL.StartsWith($"<{tag}{tagValue}>") && tagR == $"</{tag}>")
                    {
                        // Remove
                        removeIdxs.Add(i);
                    }
                    else if (tagL.StartsWith($"<{tag}") && tagR == $"</{tag}>")
                    {
                        // Switch
                        removeIdxs.Add(i);
                        innerSelected = $"<{tag}{tagValue}>{innerSelected}</{tag}>";
                    }
                }

                if (removeIdxs.Count == 0)
                {
                    innerSelected = $"<{tag}{tagValue}>{innerSelected}</{tag}>";
                }
                else
                {
                    removeIdxs.Reverse();
                    foreach (var i in removeIdxs)
                    {
                        lts.RemoveAt(i);
                        rts.RemoveAt(i);
                    }
                }
            }

            lts.Reverse();
            var prefix = string.Join(string.Empty, lts);
            var suffix = string.Join(string.Empty, rts);
            

            // Split the text into before, selected, and after
            string before = originalText[..outerStart];
            string selected = $"{prefix}{innerSelected}{suffix}";
            string after = originalText[outerEnd..];

            // Replace the text in the field
            string newText = $"{before}{selected}{after}";
            _textField.value = newText;
            _textField.Focus();
            _textField.SelectRange(_lastRange.x, _lastRange.y);
        }

        #region - Helpers -

        private bool TryGetTagLeft(string text, int start, out List<string> tags, out int startOfTagsIndex)
        {
            StringBuilder sb = new StringBuilder();
            tags = new List<string>();
            startOfTagsIndex = -1;
            if (start > 2)
            {
                bool insideTag = false;
                for (int i = start - 1; i >= 0; i--)
                {
                    var c = text[i];
                    if (c == '>')
                    {
                        insideTag = true;
                        sb.Append(c);
                        continue;
                    }

                    if (!insideTag)
                    {
                        break;
                    }
                    startOfTagsIndex = i;

                    if (c == '<')
                    {
                        insideTag = false;
                        sb.Append(c);
                        var revTag = sb.ToString();
                        sb.Clear();
                        var tag = new string(revTag.Reverse().ToArray());
                        tags.Add(tag);
                        continue;
                    }

                    sb.Append(c);
                }

                return tags.Count > 0;
            }
            
            return false;
        }

        private bool TryGetTagRight(string text, int end, out List<string> tags, out int endOfTagsIndex)
        {
            StringBuilder sb = new StringBuilder();
            tags = new List<string>();
            bool insideTag = false;
            endOfTagsIndex = -1;
            for (int i = end; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '<')
                {
                    insideTag = true;
                    sb.Append(c);
                    continue;
                }

                if (!insideTag)
                {
                    break;
                }
                endOfTagsIndex = i;

                if (c == '>')
                {
                    insideTag = false;
                    sb.Append(c);
                    tags.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            return tags.Count > 0;
        }

        private int MapStrippedToRich(string text, int strippedIndex)
        {
            int visibleCount = 0;
            bool insideTag = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    insideTag = true;
                    continue;
                }

                if (c == '>')
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag)
                {
                    if (visibleCount == strippedIndex)
                    {
                        return i;
                    }

                    visibleCount++;
                }
            }

            // If the index goes beyond visible text length, return full length
            return text.Length;
        }

        private static string StripRichTextTags(string text)
        {
            Regex rich = new Regex("<[^>]*>");
            if (rich.IsMatch (text))
            {
                text = rich.Replace(text, string.Empty);
            }
            return text;
        }
        #endregion
    }
}
