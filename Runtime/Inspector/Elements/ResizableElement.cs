using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    //[UxmlElement("ResizableElement")]
    [UxmlElement]
    public partial class ResizableElement : VisualElement
    {
        #region Types
        public enum Resizer
        {
            None = 0,
            Top = 1 << 0,
            Bottom = 1 << 1,
            Left = 1 << 2,
            Right = 1 << 3,
        }
        #endregion

        private Dictionary<Resizer, VisualElement> m_Resizers = new();
        private List<Manipulator> m_Manipulators = new();

        public ResizableElement() : this("UXML/Resizable")
        {
            
        }

        public ResizableElement(string uxml)
        {
            pickingMode = PickingMode.Ignore;

            var tpl = Resources.Load<VisualTreeAsset>(uxml);
            var sheet = Resources.Load<StyleSheet>("Resizable");
            styleSheets.Add(sheet);

            tpl.CloneTree(this);

            foreach (Resizer direction in new[] { Resizer.Top, Resizer.Bottom, Resizer.Left, Resizer.Right })
            {
                VisualElement resizer = this.Q(direction.ToString().ToLower() + "-resize");
                if (resizer != null)
                {
                    var manipulator = new ElementResizer(this, direction);
                    resizer.AddManipulator(manipulator);
                    m_Manipulators.Add(manipulator);
                }
                m_Resizers[direction] = resizer;
            }

            foreach (Resizer vertical in new[] { Resizer.Top, Resizer.Bottom })
            {
                foreach (Resizer horizontal in new[] { Resizer.Left, Resizer.Right })
                {
                    VisualElement resizer = this.Q(vertical.ToString().ToLower() + "-" + horizontal.ToString().ToLower() + "-resize");
                    if (resizer != null)
                    {
                        var manipulator = new ElementResizer(this, vertical | horizontal);
                        resizer.AddManipulator(manipulator);
                        m_Manipulators.Add(manipulator);
                    }
                    m_Resizers[vertical | horizontal] = resizer;
                }
            }
        }

        public void SetResizeRules(Resizer allowedResizeDirections)
        {
            foreach (var manipulator in m_Manipulators)
            {
                if (manipulator == null)
                {
                    return;
                }

                var resizeElement = manipulator as ElementResizer;
                // If resizer direction is not in list of allowed directions, disable the callbacks on it
                if ((resizeElement.direction & allowedResizeDirections) == 0)
                {
                    resizeElement.isEnabled = false;
                }
                else if ((resizeElement.direction & allowedResizeDirections) != 0)
                {
                    resizeElement.isEnabled = true;
                }
            }
        }

        

        // Lets visual element owners bind a callback to when any resize operation is completed
        public void BindOnResizeCallback(EventCallback<MouseUpEvent> mouseUpEvent)
        {
            foreach (var manipulator in m_Manipulators)
            {
                if (manipulator == null)
                {
                    return;
                }

                manipulator.target.RegisterCallback(mouseUpEvent);
            }
        }
    }
}
