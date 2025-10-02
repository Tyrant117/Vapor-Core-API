using UnityEngine.UIElements;

namespace Vapor.Inspector
{
    public interface IPseudoStateManipulator
    {
#pragma warning disable IDE1006 // Naming Styles
        public VisualElement PseudoStateTarget { get; }
#pragma warning restore IDE1006 // Naming Styles

        public string PseudoStateHover { get; }
        public string PseudoStateActive { get; }
        public string PseudoStateFocus { get; }
        public string PseudoStateChecked { get; }
        public string PseudoStateDisabled { get; }

        void EnablePseudoStateClass(PseudoState state)
        {
            switch (state)
            {
                case PseudoState.None:
                    break;
                case PseudoState.Hover:
                    PseudoStateTarget.EnableInClassList(PseudoStateHover, true);
                    break;
                case PseudoState.Active:
                    PseudoStateTarget.EnableInClassList(PseudoStateActive, true);
                    break;
                case PseudoState.Focus:
                    PseudoStateTarget.EnableInClassList(PseudoStateFocus, true);
                    break;
                case PseudoState.Checked:
                    PseudoStateTarget.EnableInClassList(PseudoStateChecked, true);
                    break;
                case PseudoState.Disabled:
                    PseudoStateTarget.EnableInClassList(PseudoStateDisabled, true);
                    break;
                default:
                    break;
            }
        }
        void DisablePseudoStateClass(PseudoState state)
        {
            switch (state)
            {
                case PseudoState.None:
                    break;
                case PseudoState.Hover:
                    PseudoStateTarget.EnableInClassList(PseudoStateHover, false);
                    break;
                case PseudoState.Active:
                    PseudoStateTarget.EnableInClassList(PseudoStateActive, false);
                    break;
                case PseudoState.Focus:
                    PseudoStateTarget.EnableInClassList(PseudoStateFocus, false);
                    break;
                case PseudoState.Checked:
                    PseudoStateTarget.EnableInClassList(PseudoStateChecked, false);
                    break;
                case PseudoState.Disabled:
                    PseudoStateTarget.EnableInClassList(PseudoStateDisabled, false);
                    break;
                default:
                    break;
            }
        }
    }
}
