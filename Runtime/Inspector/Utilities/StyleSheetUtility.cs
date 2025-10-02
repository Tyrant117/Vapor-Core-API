using System;
using System.Collections.Generic;

namespace Vapor.Inspector
{
    [Flags]
    public enum PseudoState
    {
        None = -1,
        Hover = 1 << 0,
        Active = 1 << 1,
        Focus = 1 << 2,
        Checked = 1 << 3,
        Disabled = 1 << 4,
    }

    public static class StyleSheetUtility
    {
        public static IEnumerable<string> GetPseudoStates(PseudoState state)
        {
            if ((state & PseudoState.Hover) == PseudoState.Hover)
            {
                yield return PseudoStates.Hover;

            }
            else if ((state & PseudoState.Active) == PseudoState.Active)
            {
                yield return PseudoStates.Active;
            }
        }

        public static class PseudoStates
        {
            public const string Hover = "__hover";
            public const string Active = "__active";
            public const string Focus = "__focus";
            public const string Checked = "__checked";
            public const string Disabled = "__disabled";
        }

        public static class Tags
        {
            public const string DragReceiver = "___dragReceiver";
        }
    }
}
