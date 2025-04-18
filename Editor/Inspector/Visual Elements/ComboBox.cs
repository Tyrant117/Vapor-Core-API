using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Codice.CM.Interfaces;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;

namespace VaporEditor.Inspector
{
    public class ComboBox<T> : VisualElement/*, ISearchEntrySelected*/
    {
        private VisualElement m_ArrowElement;

        public static readonly string arrowUssClassName = "unity-base-popup-field" + "__arrow";

        public Label Label { get; protected set; }
        public Button Dropdown { get; protected set; }
        public Label DropdownLabel { get; protected set; }

        public List<string> Choices { get; private set; }
        public List<T> Values { get; private set; }
        public List<int> CurrentSelectedIndices { get; set; }

        private readonly GenericSearchProvider _searchProvider;
        // private readonly DropdownSearchWindowProvider _searchWindowProvider;
        private readonly List<string> _pendingSelection = new();
        private readonly StringBuilder _stringBuilder = new();
        private readonly bool _multiSelect;

        public event Action<ComboBox<T>, List<int>> SelectionChanged = delegate { };

        public ComboBox(string label, int selectedIndex, List<string> choices, List<T> values, bool multiSelect, bool noCopy = false)
        {
            Assert.IsTrue(choices.Count == values.Count, "Choices and Values length must match");
            _multiSelect = multiSelect;
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1f;
            style.marginLeft = 3;
            if (noCopy)
            {
                Choices = choices;
                Values = values;
            }
            else
            {
                Choices = new List<string>(choices);
                Values = new List<T>(values);
            }
            

            if (Choices.Count == 0)
            {
                Choices.Add("Invalid Choices");
                Values.Add(default);
                selectedIndex = 0;
            }

            int count = _multiSelect ? Choices.Count : 1;
            _pendingSelection = new List<string>(count);
            CurrentSelectedIndices = new List<int>(count);

            var searchModels = new List<GenericSearchModel>();
            foreach (var c in choices)
            {
                var sm = new GenericSearchModel(string.Empty, c);
                searchModels.Add(sm);
            }

            _searchProvider = new GenericSearchProvider(OnSelect, searchModels, multiSelect);
            // _searchWindowProvider = new DropdownSearchWindowProvider();
            // _searchWindowProvider.Initialize(this, Choices);

            Label = new Label(label)
            {
                style =
                {
                    flexGrow = 1f,
                    flexShrink = 1f,
                    overflow = Overflow.Hidden,
                    textOverflow = TextOverflow.Ellipsis,
                    unityTextAlign = TextAnchor.MiddleLeft,
                    minWidth = new StyleLength(new Length(33, LengthUnit.Percent)),
                    maxWidth = new StyleLength(new Length(33, LengthUnit.Percent))
                }
            };
            Dropdown = new Button(ShowMenu)
            {
                style =
                {
                    unityTextAlign = TextAnchor.MiddleLeft,
                    flexDirection = FlexDirection.Row,
                    flexGrow = 1f,
                    flexShrink = 1f,
                    paddingTop = 2f,
                    paddingBottom = 2f,
                }
            };
            DropdownLabel = new Label("")
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                   unityTextAlign = TextAnchor.MiddleLeft,
                   overflow = Overflow.Hidden,
                   textOverflow = TextOverflow.Ellipsis,
                   flexGrow = 1f,
                   flexShrink = 1f,
                }
            };
            m_ArrowElement = new VisualElement()
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    maxWidth = 31,
                }
            };
            m_ArrowElement.AddToClassList(arrowUssClassName);

            Dropdown.Add(DropdownLabel);
            Dropdown.Add(m_ArrowElement);
            Add(Label);
            Add(Dropdown);

            if (!_multiSelect)
            {
                Select(Choices[selectedIndex]);
            }
        }

        private void OnSelect(GenericSearchModel[] searchModels)
        {
            if (searchModels != null)
            {
                Select(searchModels.Select(sm => sm.Name));
            }
        }

        public void SetChoices(List<string> choices, List<T> values)
        {
            Assert.IsTrue(choices.Count == values.Count, "Choices and Values length must match");
            Choices = new List<string>(choices);
            Values = new List<T>(values);
            // _searchWindowProvider.Entries = Choices;
            _pendingSelection.Clear();
            CurrentSelectedIndices.Clear();
            DropdownLabel.text = "";
        }

        public void Select(string choice)
        {
            _pendingSelection.Add(choice);
            //Debug.Log($"ComboBox - Select: {name}");
            if (_pendingSelection.Count == 1)
            {
                schedule.Execute(SelectionComplete);
            }
        }

        public void DeselectAll()
        {
            if (_multiSelect)
            {
                //Debug.Log($"ComboBox - DeselectAll");
                _pendingSelection.Clear();
                schedule.Execute(SelectionComplete);
            }
        }

        public void Select(IEnumerable<string> names)
        {
            _pendingSelection.Clear();
            _pendingSelection.AddRange(names);
            schedule.Execute(SelectionComplete);
        }        

        private void ShowMenu()
        {
            var worldRect = GUIUtility.GUIToScreenRect(Dropdown.worldBound);
            var pos = new Vector2(worldRect.position.x + 24, worldRect.position.y + Dropdown.worldBound.height + 16);

            // var windowRect = GUIUtility.GUIToScreenRect(Dropdown.panel.visualTree.worldBound);
            //Debug.Log(windowRect);
            //Debug.Log(pos.y);
            

            // var height = Mathf.Min(300f, windowRect.y + windowRect.height - pos.y);
            //Debug.Log(height);
            // var size = new Vector2(Dropdown.resolvedStyle.width, height);
            // Rect rect = new(pos, size);

            _pendingSelection.Clear();
            GenericSearchWindow.Show(pos, pos, _searchProvider, false, false);

            // SearcherWindow.Show(null, _searchWindowProvider.LoadSearchWindow(_multiSelect, out var searchers),
            //         item => _searchWindowProvider.OnSearcherSelectEntry(item),
            //         null,
            //         null,
            //         rect);
            // if (_multiSelect && CurrentSelectedIndices.Count > 0)
            // {
            //     var searcherWindow = EditorWindow.GetWindow<SearcherWindow>();
            //     var control = searcherWindow.rootVisualElement[0];
            //     if(control is SearcherControl sc)
            //     {
            //         var fullSearchList = sc.GetSearcherItems();
            //         foreach (var idx in CurrentSelectedIndices)
            //         {
            //             var searchItem = fullSearchList.Find(x => x.FullName == Choices[idx]);
            //             if (searchItem != null)
            //             {
            //                 sc.ToggleItemForMultiSelect(searchItem, true);
            //             }
            //         }                   
            //     }
            //
            //     //var mi = control.GetType().GetMethod("ToggleItemForMultiSelect", BindingFlags.Instance | BindingFlags.NonPublic);
            //
            //     //foreach (var idx in CurrentSelectedIndices)
            //     //{
            //     //    var searchItem = searchers.Find(x => x.Name == Choices[idx]);
            //     //    if (searchItem != null)
            //     //    {
            //     //        mi.Invoke(control, new object[] { searchItem, true });
            //     //    }
            //     //}
            // }

        }

        private void SelectionComplete()
        {
            _stringBuilder.Clear();
            CurrentSelectedIndices.Clear();
            if (_pendingSelection.Count > 0)
            {
                for (int i = 0; i < _pendingSelection.Count - 1; i++)
                {
                    string ps = _pendingSelection[i];
                    _stringBuilder.Append(ps);
                    _stringBuilder.Append(",");
                    CurrentSelectedIndices.Add(Choices.IndexOf(ps));
                }
                _stringBuilder.Append(_pendingSelection[^1]);
                CurrentSelectedIndices.Add(Choices.IndexOf(_pendingSelection[^1]));

                _pendingSelection.Clear();
            }
            else
            {
                _stringBuilder.Append("None");
            }

            DropdownLabel.text = _stringBuilder.ToString();
            SelectionChanged.Invoke(this, CurrentSelectedIndices);
        }

        
    }
}
