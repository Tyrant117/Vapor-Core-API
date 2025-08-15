using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using Vapor;

namespace VaporEditor.Inspector
{
    public class ComboBox<T> : VisualElement
    {
        private readonly VisualElement _arrowElement;
        public const string ARROW_USS_CLASS_NAME = "unity-base-popup-field" + "__arrow";
        
        public Label Label { get; protected set; }
        public Button Dropdown { get; protected set; }
        public Label DropdownLabel { get; protected set; }

        public List<string> Choices { get; private set; }
        public List<T> Values { get; private set; }
        public List<int> CurrentSelectedIndices { get; set; }

        private string _categorySplitCharacter;
        private GenericSearchProvider _searchProvider;
        private readonly List<string> _pendingSelection;
        private readonly StringBuilder _stringBuilder = new();
        private readonly bool _flattenCategories;

        public event Action<ComboBox<T>, List<int>> SelectionChanged;

        public ComboBox()
        {
            style.flexDirection = FlexDirection.Row;
            style.flexGrow = 1f;
            style.marginLeft = 3;
            Choices = new List<string>();
            Values = new List<T>();

            Choices.Add("Invalid Choices");
            Values.Add(default);
            var searchModels = new List<GenericSearchModel>()
            {
                new("Invalid Choices", string.Empty, "Invalid Choices")
                {
                    Tooltip = string.Empty,
                }
            };
            _searchProvider = new GenericSearchProvider(OnSelect, searchModels, false);

            _pendingSelection = new List<string>(1);
            CurrentSelectedIndices = new List<int>(1);
            Label = new Label(string.Empty)
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
            DropdownLabel = new Label(string.Empty)
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
            _arrowElement = new VisualElement()
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    maxWidth = 31,
                }
            };
            _arrowElement.AddToClassList(ARROW_USS_CLASS_NAME);

            Dropdown.Add(DropdownLabel);
            Dropdown.Add(_arrowElement);
            Add(Label);
            Add(Dropdown);
            Select(Choices[0]);
        }

        public ComboBox(string label, int selectedIndex, List<string> choices, List<T> values, List<string> tooltips, bool multiSelect, bool noCopy = false, string categorySplitCharacter = null,
            bool flattenCategories = false)
        {
            Assert.IsTrue(choices.Count == values.Count, "Choices and Values length must match");
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
            _categorySplitCharacter = categorySplitCharacter;
            _flattenCategories = flattenCategories;
            

            if (Choices.Count == 0)
            {
                Choices.Add("Invalid Choices");
                Values.Add(default);
                selectedIndex = 0;
            }

            int count = multiSelect ? Choices.Count : 1;
            _pendingSelection = new List<string>(count);
            CurrentSelectedIndices = new List<int>(count);

            var searchModels = new List<GenericSearchModel>();
            int idx = 0;
            foreach (var choice in choices)
            {
                string c = _categorySplitCharacter.EmptyOrNull() ? choice : choice.Replace(_categorySplitCharacter[0], '/');
                int lastIdx = c.LastIndexOf('/');
                string category = string.Empty;
                string cName = c;
                if (lastIdx != -1)
                {
                    category = c[..lastIdx];
                    cName = c[(lastIdx + 1)..];
                }

                var sm = new GenericSearchModel(category.EmptyOrNull() ? cName : $"{category}/{cName}", _flattenCategories ? string.Empty : category, cName)
                {
                    Tooltip = (tooltips?.IsValidIndex(idx) ?? false) ? tooltips[idx] : string.Empty, 
                };
                searchModels.Add(sm);
                idx++;
            }

            _searchProvider = new GenericSearchProvider(OnSelect, searchModels, multiSelect);
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
            DropdownLabel = new Label(string.Empty)
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
            _arrowElement = new VisualElement()
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    maxWidth = 31,
                }
            };
            _arrowElement.AddToClassList(ARROW_USS_CLASS_NAME);

            Dropdown.Add(DropdownLabel);
            Dropdown.Add(_arrowElement);
            Add(Label);
            Add(Dropdown);

            if (!multiSelect)
            {
                Select(Choices[selectedIndex]);
            }
        }

        private void OnSelect(GenericSearchModel[] searchModels)
        {
            if (searchModels != null)
            {
                Select(searchModels.Select(sm => sm.UniqueName /*sm.Category.EmptyOrNull() ? sm.Name : $"{sm.Category}/{sm.Name}"*/));
            }
        }

        public void SetChoices(List<string> choices, List<T> values, List<string> tooltips, string categorySplitCharacter = null)
        {
            Assert.IsTrue(choices.Count == values.Count, "Choices and Values length must match");
            Choices = new List<string>(choices);
            Values = new List<T>(values);
            var searchModels = new List<GenericSearchModel>();
            _categorySplitCharacter = categorySplitCharacter.EmptyOrNull() ? _categorySplitCharacter : categorySplitCharacter;
            
            int idx = 0;
            foreach (var choice in choices)
            {
                string c = _categorySplitCharacter.EmptyOrNull() ? choice : choice.Replace(_categorySplitCharacter[0], '/');
                int lastIdx = c.LastIndexOf('/');
                string category = string.Empty;
                string cName = c;
                if (lastIdx != -1)
                {
                    category = c[..lastIdx];
                    cName = c[(lastIdx + 1)..];
                }

                var sm = new GenericSearchModel(category.EmptyOrNull() ? cName : $"{category}/{cName}", _flattenCategories ? string.Empty : category, cName)
                {
                    Tooltip = (tooltips?.IsValidIndex(idx) ?? false) ? tooltips[idx] : string.Empty,
                };
                searchModels.Add(sm);
                idx++;
            }

            var multiSelect = _searchProvider.AllowMultiSelect;
            _searchProvider = new GenericSearchProvider(OnSelect, searchModels, multiSelect);
            _pendingSelection.Clear();
            CurrentSelectedIndices.Clear();
            DropdownLabel.text = string.Empty;
        }

        public void Select(string choice)
        {
            _pendingSelection.Add(choice);
            if (_pendingSelection.Count == 1)
            {
                schedule.Execute(SelectionComplete);
            }
        }

        public void DeselectAll()
        {
            if (!_searchProvider.AllowMultiSelect)
            {
                return;
            }

            _pendingSelection.Clear();
            schedule.Execute(SelectionComplete);
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
            GenericSearchWindow.Show(pos, pos, _searchProvider, false);

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
                    _searchProvider.SetModelToggled(ps);
                    ps = _categorySplitCharacter.EmptyOrNull() ? ps : ps.Replace('/', _categorySplitCharacter[0]);
                    _stringBuilder.Append(ps);
                    _stringBuilder.Append(",");
                    CurrentSelectedIndices.Add(Choices.IndexOf(ps));
                }

                string psl = _pendingSelection[^1];
                _searchProvider.SetModelToggled(psl);
                psl = _categorySplitCharacter.EmptyOrNull() ? psl : psl.Replace('/', _categorySplitCharacter[0]);
                _stringBuilder.Append(psl);
                CurrentSelectedIndices.Add(Choices.IndexOf(psl));

                _pendingSelection.Clear();
            }
            else
            {
                _stringBuilder.Append("None");
            }

            DropdownLabel.text = _stringBuilder.ToString();
            Dropdown.tooltip = _stringBuilder.ToString().Replace(',', '\n');
            SelectionChanged?.Invoke(this, CurrentSelectedIndices);
        }

        protected void SetMultiSelect(bool multiSelect)
        {
            _searchProvider.AllowMultiSelect = multiSelect;
        }
    }
}
