using System;
using UnityEngine;
using UnityEngine.UIElements;
using Vapor.Inspector;

namespace VaporEditor.Inspector
{
    /// <summary>
    /// The single way an inspector element turns an <c>@</c> resolver into something that ticks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call sites used to look up a <see cref="System.Reflection.MemberInfo"/> themselves and hand it to
    /// a container, which meant each one independently decided what to do when the lookup came back null.
    /// They all decided the same thing — nothing — so a misspelled resolver was invisible: the row simply
    /// never reacted, with no console entry and nothing on screen to say why.
    /// </para>
    /// <para>
    /// Routing every binding through here fixes that once. A resolver that will not compile writes one
    /// console warning and draws a help box against the row it belongs to, and a resolver that does
    /// compile never touches reflection again.
    /// </para>
    /// </remarks>
    public static class ResolverBinding
    {
        /// <summary>
        /// Binds <paramref name="expression"/> against the object that declares <paramref name="property"/>.
        /// </summary>
        /// <returns>True when the resolver compiled and is now ticking.</returns>
        public static bool Bind<T>(VisualElement owner, InspectorTreeProperty property, string expression, Action<T> onValueChanged)
        {
            if (property == null)
            {
                Report(owner, new ResolverCompileError($"Resolver '@{expression}' has no property to read from."));
                return false;
            }

            if (!ResolverExpression.TryCompile<T>(property.ParentType, expression, out var accessor, out var error))
            {
                Report(owner, error);
                return false;
            }

            InspectorResolverTicker.Register(owner, new SerializedResolverContainerType<T>(property, accessor, onValueChanged));
            return true;
        }

        /// <summary>
        /// Binds <paramref name="expression"/> against <paramref name="target"/> directly, for the group
        /// headers and other places with no tree property to hang off.
        /// </summary>
        public static bool Bind<T>(VisualElement owner, object target, Type targetType, string expression, Action<T> onValueChanged)
        {
            if (!ResolverExpression.TryCompile<T>(targetType, expression, out var accessor, out var error))
            {
                Report(owner, error);
                return false;
            }

            InspectorResolverTicker.Register(owner, new SerializedResolverContainerObject<T>(target, accessor, onValueChanged));
            return true;
        }

        /// <summary>
        /// Registers a resolver that watches something other than the inspected object.
        /// </summary>
        public static void BindExternal<T>(VisualElement owner, Func<T> read, Action<T> onValueChanged)
        {
            InspectorResolverTicker.Register(owner, new SerializedResolverContainerAction<T>(read, onValueChanged));
        }

        /// <summary>
        /// Puts a compile failure in front of whoever wrote it: once in the console, and on screen for as
        /// long as the inspector is open.
        /// </summary>
        public static void Report(VisualElement owner, ResolverCompileError error)
        {
            if (error == null)
            {
                return;
            }

            if (error.ClaimConsoleReport())
            {
                Debug.LogWarning(error.Message);
            }

            if (owner == null)
            {
                return;
            }

            // Placed as a sibling above the row rather than inside it, so that elements which index into
            // their own hierarchy — TreePropertyField reaches for hierarchy[0] to attach inline buttons —
            // do not find the help box where they expect their own content. Deferred to attach time
            // because the owner has no parent yet while its decorators are still being applied.
            owner.RegisterCallbackOnce<AttachToPanelEvent>(_ =>
            {
                var box = CreateErrorBox(error.Message);
                var parent = owner.hierarchy.parent;
                if (parent == null)
                {
                    owner.hierarchy.Add(box);
                    return;
                }

                parent.hierarchy.Insert(parent.hierarchy.IndexOf(owner), box);
            });
        }

        /// <summary>
        /// The shared look for a resolver that could not be compiled, so the row-level and inspector-level
        /// reports read as the same thing.
        /// </summary>
        public static HelpBox CreateErrorBox(string message)
        {
            var box = new HelpBox(message, HelpBoxMessageType.Error);
            box.style.marginTop = 2;
            box.style.marginBottom = 2;
            box.style.whiteSpace = WhiteSpace.Normal;
            return box;
        }
    }
}
