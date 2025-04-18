using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using Vapor;
using Vapor.Inspector;
using Object = UnityEngine.Object;

namespace VaporEditor.Inspector
{
    public class PaginatedList : VisualElement
    {
        public InspectorTreeElement ParentTreeElement { get; }
        public InspectorTreeProperty Property { get; }

        // Elements
        public Foldout Foldout { get; private set; }
        public VisualElement Header { get; private set; }
        public Label Label { get; private set; }
        public Label PageNumber { get; private set; }
        public IntegerField CountField { get; private set; }
        public VisualElement Container { get; private set; }
        public VisualElement Content { get; private set; }

        private int _visibleMaxCount = 14;
        private int _currentPage = 1;
        private List<InspectorTreeElement> _visibleContent = new();
        private event Action<PaginatedList, int, int> SizeChanged;
        private event Action<int, string, object, object> ValueChanged;

        public PaginatedList(InspectorTreeElement parentTreeElement, InspectorTreeProperty property, string header)
        {
            AddToClassList("unity-box"); // Style like a box
            ParentTreeElement = parentTreeElement;
            Property = property;

            StyleBackground();
            DrawFoldout(header);

            DrawContent();

            var type = Property.ParentType;
            if (Property.TryGetAttribute<ListDrawerAttribute>(out var listAtr))
            {
                if (!listAtr.SizeChangedMethodName.EmptyOrNull())
                {
                    var methodInfo = ReflectionUtility.GetMethod(type, listAtr.SizeChangedMethodName);
                    if (methodInfo != null)
                    {
                        SizeChanged += (sender, old, @new) =>
                        {
                            methodInfo.Invoke(Property.GetParentObject(), new object[] { old, @new });
                            GetFirstAncestorOfType<InspectorTreeElement>().Root.RebuildAndRedraw();
                        };
                    }
                    else
                    {
                        Debug.LogError($"Could Not Find Size Changed Method: {listAtr.SizeChangedMethodName}");
                    }
                }
                
                if (!listAtr.ElementChangedMethodName.EmptyOrNull())
                {
                    var methodInfo = ReflectionUtility.GetMethod(type, listAtr.ElementChangedMethodName);
                    if (methodInfo != null)
                    {
                        ValueChanged += (index, propertyName, old, @new) =>
                        {
                            methodInfo.Invoke(Property.GetParentObject(), new[] { index, propertyName, old, @new });
                        };
                    }
                    else
                    {
                        Debug.LogError($"Could Not Find Element Changed Method: {listAtr.ElementChangedMethodName}");
                    }
                }
            }
        }

        private void StyleBackground()
        {
            style.borderBottomColor = ContainerStyles.BorderColor;
            style.borderTopColor = ContainerStyles.BorderColor;
            style.borderRightColor = ContainerStyles.BorderColor;
            style.borderLeftColor = ContainerStyles.BorderColor;
            style.borderBottomLeftRadius = 3;
            style.borderBottomRightRadius = 3;
            style.borderTopLeftRadius = 3;
            style.borderTopRightRadius = 3;
            style.marginTop = 3;
            style.marginBottom = 3;
            style.marginLeft = 0;
            style.marginRight = 0;
            style.backgroundColor = ContainerStyles.BackgroundColor;
        }

        private void DrawFoldout(string header)
        {
            Foldout = new Foldout()
            {
                name = "styled-foldout-foldout",
                viewDataKey = $"styled-paginated-list__vdk_{header}"
            };

            var tog = Foldout.Q<Toggle>();
            tog.RegisterCallback<NavigationSubmitEvent>(evt => { evt.StopImmediatePropagation(); }, TrickleDown.TrickleDown);
            var togStyle = Foldout.Q<Toggle>().style;
            togStyle.marginTop = 0;
            togStyle.marginLeft = 0;
            togStyle.marginRight = 0;
            togStyle.marginBottom = 0;
            togStyle.backgroundColor = ContainerStyles.HeaderColor;

            var togContainerStyle = Foldout.Q<Toggle>().hierarchy[0].style;
            togContainerStyle.marginLeft = 3;
            togContainerStyle.marginTop = 3;
            togContainerStyle.marginBottom = 3;

            // Label
            //Foldout.Q<Toggle>().Q<Label>().RemoveFromHierarchy();
            DrawHeader(header);
            Foldout.Q<Toggle>().hierarchy[0].Add(Header);


            // Content
            Container = Foldout.Q<VisualElement>("unity-content");
            Container.style.marginTop = 0;
            Container.style.marginRight = 0;
            Container.style.marginBottom = 0;
            Container.style.marginLeft = 0;


            Foldout.value = false;
            Add(Foldout);
        }

        private void DrawHeader(string header)
        {
            bool editable = true;
            if (Property.TryGetAttribute<ListDrawerAttribute>(out var listAtr))
            {
                editable = listAtr.Editable;
            }
            
            Header = new VisualElement()
            {
                style =
                {
                    flexDirection = FlexDirection.Row, 
                    flexGrow = 1f,
                }
            };
            Label = new Label(header)
            {
                name = "styled-header-box-label",
                style =
                {
                    marginLeft = 0,
                    marginRight = 0,
                    marginBottom = 0,
                    marginTop = 0,
                    paddingTop = 0,
                    unityTextAlign = TextAnchor.MiddleLeft
                }
            };
            PageNumber = new Label($"{_currentPage}/{MaxPageCount()}")
            {
                style =
                {
                    minWidth = 31,
                    marginLeft = 0,
                    marginRight = 0,
                    marginBottom = 0,
                    marginTop = 0,
                    paddingTop = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            CountField = new IntegerField()
            {
                isReadOnly = !editable,
                isDelayed = true,
                style =
                {
                    minWidth = 31,
                    marginRight = 7,
                }
            };
            CountField.SetBinding("value", new DataBinding
            {
                dataSource = Property,
                dataSourcePath = new PropertyPath(nameof(Property.ArraySize)),
                bindingMode = BindingMode.ToTarget,
                updateTrigger = BindingUpdateTrigger.OnSourceChanged
            });
            //CountField.BindProperty(ArraySizeProperty);
            CountField.RegisterCallbackOnce<GeometryChangedEvent>(OnCountFieldBuilt);
            CountField.RegisterValueChangedCallback<int>(OnCountPropertyChanged);

            var pgLeft = new Button(DecrementPage)
            {
                text = "<",
                style =
                {
                    minWidth = 20,
                    minHeight = 20,
                    paddingRight = 0,
                    paddingLeft = 0,
                    paddingBottom = 0,
                    paddingTop = 0,
                    marginLeft = 2,
                    marginRight = 2,
                    marginTop = 0,
                    marginBottom = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            var pgRight = new Button(IncrementPage)
            {
                text = ">",
                style =
                {
                    minWidth = 20,
                    minHeight = 20,
                    paddingRight = 0,
                    paddingLeft = 0,
                    paddingBottom = 0,
                    paddingTop = 0,
                    marginLeft = 2,
                    marginRight = 2,
                    marginTop = 0,
                    marginBottom = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            var subtract = new Button(SubtractElement)
            {
                text = "-",
                style =
                {
                    minWidth = 20,
                    minHeight = 20,
                    paddingRight = 0,
                    paddingLeft = 0,
                    paddingBottom = 0,
                    paddingTop = 0,
                    marginLeft = 2,
                    marginRight = 2,
                    marginTop = 0,
                    marginBottom = 0,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            var add = new Button(AddElement)
            {
                text = "+",
                style =
                {
                    minWidth = 20,
                    minHeight = 20,
                    paddingRight = 0,
                    paddingLeft = 0,
                    paddingBottom = 0,
                    paddingTop = 0,
                    marginLeft = 2,
                    marginRight = 2,
                    marginTop = 0,
                    marginBottom = 0,

                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };

            Header.Add(Label);
            Header.Add(new VisualElement() { style = { flexGrow = 1f } });
            Header.Add(pgLeft);
            Header.Add(PageNumber);
            Header.Add(pgRight);
            if (editable)
            {
                Header.Add(subtract);
                Header.Add(add);
            }

            Header.Add(CountField);
        }        

        private void DrawContent()
        {
            Assert.IsTrue(Property.IsArray, $"Trying to draw a list for something that isn't an array {Property.PropertyPath}");

            Content = new VisualElement()
            {
                style =
                {

                }
            };
            _visibleContent.Clear();

            if (_currentPage > MaxPageCount())
            {
                _currentPage = MaxPageCount();
            }
            var indexStart = (_currentPage - 1) * _visibleMaxCount;
            var indexEnd = Mathf.Min(indexStart + _visibleMaxCount, Property.ArraySize);
            Property.RequireRedraw = Redraw;
            for (int i = indexStart; i < indexEnd; i++)
            {
                var prop = Property.ArrayData[i];
                var element = new InspectorTreeRootElement(ParentTreeElement, prop);
                var treeField = element.Q<InspectorTreeFieldElement>();
                //Get Children
                treeField.Query<TreePropertyField>().ForEach(field => field.ValueChanged += OnElementChanged);
                treeField.style.flexDirection = FlexDirection.Row;
                treeField.style.flexGrow = 1f;
                treeField.style.backgroundColor = (i % 2 == 0) ? ContainerStyles.DarkInspectorBackgroundColor : ContainerStyles.InspectorBackgroundColor;
                var contextBox = new Image
                {
                    image = EditorGUIUtility.IconContent("_Menu").image,
                    scaleMode = ScaleMode.ScaleToFit,
                    style =
                        {
                            maxWidth = 31,
                        }
                };
                DrawContextMenu(contextBox, prop);
                treeField.hierarchy[0].style.flexGrow = 1f;
                treeField.hierarchy.Insert(0, contextBox);
                treeField.hierarchy.Add(new Button(() => Property.RemoveAt(prop.ElementIndex))
                {
                    text = "x",
                    style =
                    {
                        maxHeight = 31,
                        alignSelf = Align.Center,
                    }
                });
                
                {
                    // Need to rebind property here for bugs with some IMGUIContainer objects.
                    // This triggers the Reset method to call which causes the IMGUI container to refresh.
                    var property = element.Q<VaporPropertyField>();
                    property?.BindProperty(prop.InspectorObject.FindSerializedProperty(prop.PropertyPath));
                }

                _visibleContent.Add(element);
                Content.Add(element);
            }
            Container.Add(Content);
        }

        private void OnElementChanged(TreePropertyField sender, object previous, object current)
        {
            var element = sender.Property.GetParentObject();
            
            ValueChanged?.Invoke(Property.IndexOf(element), sender.Property.PropertyName, previous, current);
        }

        private void DrawContextMenu(VisualElement ve, InspectorTreeProperty property)
        {
            ve.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Reset", _ =>
                {
                    Debug.Log($"Resetting: {property} of type {property.PropertyType}");
                    if (property.PropertyType.IsSubclassOf(typeof(Object)))
                    {
                        property.SetValue<Object>(null);
                    }
                    else
                    {
                        try
                        {
                            var clonedTarget = Activator.CreateInstance(property.PropertyType);
                            property.SetValue(clonedTarget);
                        }
                        catch (MissingMethodException exp)
                        {
                            Debug.LogWarning(exp);
                        }
                    }
                    property.InspectorObject.ApplyModifiedProperties();
                });
                evt.menu.AppendAction("Copy", _ => { ClipboardUtility.WriteToBuffer(property.GetValue()); });
                evt.menu.AppendAction("Paste", _ => { ClipboardUtility.ReadFromBuffer(property); }, _ =>
                {
                    var read = ClipboardUtility.CanReadFromBuffer(property.PropertyType);
                    return read ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Duplicate Array Element", _ =>
                {
                    Property.DuplicateArrayProperty(property);
                });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Copy Property Path", _ => { EditorGUIUtility.systemCopyBuffer = property.PropertyPath; });
                evt.menu.AppendSeparator();
                evt.menu.AppendAction("Move Up", _ =>
                {
                    Property.Swap(property.ElementIndex, Mathf.Max(0, property.ElementIndex - 1));
                });
                evt.menu.AppendAction("Move Down", _ =>
                {
                    Property.Swap(property.ElementIndex, Mathf.Min(Property.ArraySize - 1, property.ElementIndex + 1));
                });
                evt.menu.AppendAction("Move To/First", _ =>
                {
                    Property.Swap(property.ElementIndex, 0);
                });
                evt.menu.AppendAction("Move To/Last", _ =>
                {
                    Property.Swap(property.ElementIndex, Property.ArraySize - 1);
                });
                evt.menu.AppendAction("Move To/Previous Page", _ =>
                {
                    var last = (property.ElementIndex / _visibleMaxCount * _visibleMaxCount) - 1;

                    Property.Swap(property.ElementIndex, Mathf.Max(0, last));

                }, property.ElementIndex > _visibleMaxCount - 1 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("Move To/Next Page", _ =>
                {
                    var next = (property.ElementIndex + _visibleMaxCount) / _visibleMaxCount * _visibleMaxCount;
                    Property.Swap(property.ElementIndex, Mathf.Min(Property.ArraySize - 1, next));
                }, _ =>
                {
                    return MaxPageCount() == 1 + property.ElementIndex / _visibleMaxCount ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
                });

            }));
        }

        public void Redraw()
        {
            Debug.Log("Redraw Called!");
            Content.RemoveFromHierarchy();
            DrawContent();
        }

        #region - Callbacks -
        private void OnCountFieldBuilt(GeometryChangedEvent evt)
        {
            //CountField.Q<Label>().style.display = DisplayStyle.None;
            CountField.Q<VisualElement>("unity-text-input").Q<TextElement>().style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        private void IncrementPage()
        {
            if (_currentPage < MaxPageCount())
            {
                _currentPage++;
                UpdatePageCount();
                Redraw();
            }
        }

        private void DecrementPage()
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                UpdatePageCount();
                Redraw();
            }
        }

        private void AddElement()
        {
            Property.Add();
        }

        private void SubtractElement()
        {
            if (Property.ArraySize > 0)
            {
                Property.RemoveLast();
            }
        }

        private void OnCountPropertyChanged(ChangeEvent<int> evt)
        {
            if (evt.previousValue == evt.newValue)
            {
                return;
            }

            //Debug.Log($"Array Count Changed {evt.previousValue} -> {evt.newValue}");
            if (_currentPage > MaxPageCount())
            {
                _currentPage = MaxPageCount();
            }
            UpdatePageCount();

            Property.ResizeArray(evt.newValue);
            SizeChanged?.Invoke(this, evt.previousValue, evt.newValue);
        }
        #endregion

        #region - Helpers -
        private int MaxPageCount() => Mathf.Max(1, Mathf.CeilToInt(Property.ArraySize * 1f / _visibleMaxCount));

        private void UpdatePageCount()
        {
            PageNumber.text = $"{_currentPage}/{MaxPageCount()}";
        }
        #endregion
    }
}
