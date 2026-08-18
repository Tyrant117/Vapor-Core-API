using System;
using System.Collections.Generic;

namespace VaporEditor.Inspector
{
    #region - Resolvers -
    public abstract class SerializedResolverContainer
    {
        public abstract void Resolve();
    }

    /// <summary>
    /// Polls one compiled resolver and pushes the value out whenever it changes.
    /// </summary>
    /// <remarks>
    /// The accessor arrives already compiled — see <see cref="ResolverBinding"/>, which is what every
    /// call site goes through — so a tick here is a delegate call and an equality check, with no
    /// reflection and no boxing of the <typeparamref name="T"/> on the way out.
    /// </remarks>
    public abstract class SerializedResolverContainerBase<T> : SerializedResolverContainer
    {
        private readonly Func<object, T> _accessor;
        private readonly Action<T> _onValueChanged;

        private T _currentValue;

        protected SerializedResolverContainerBase(Func<object, T> accessor, Action<T> onValueChanged)
        {
            _accessor = accessor;
            _onValueChanged = onValueChanged;
        }

        /// <summary>
        /// The object the expression reads from, re-fetched every tick because the tree hands out a fresh
        /// boxed copy for a struct and can be rebuilt underneath us between ticks.
        /// </summary>
        protected abstract object GetTarget();

        /// <summary>
        /// Publishes the starting value. Called at the end of the derived constructor rather than from
        /// this one, because <see cref="GetTarget"/> reads fields the derived constructor has not yet set.
        /// </summary>
        protected void Prime()
        {
            var target = GetTarget();
            if (target == null)
            {
                return;
            }

            _currentValue = _accessor(target);
            _onValueChanged.Invoke(_currentValue);
        }

        public override void Resolve()
        {
            var target = GetTarget();
            if (target == null)
            {
                // Matches what the reflection path did when it could not read a value: leave the last
                // published state alone. An unassigned nested reference is not a reason to hide a row.
                return;
            }

            var value = _accessor(target);
            if (EqualityComparer<T>.Default.Equals(_currentValue, value))
            {
                return;
            }

            _currentValue = value;
            _onValueChanged.Invoke(_currentValue);
        }
    }

    /// <summary>
    /// Watches a value that is not read off the inspected object at all, such as
    /// <c>EditorApplication.isPlaying</c>.
    /// </summary>
    public class SerializedResolverContainerAction<T> : SerializedResolverContainer
    {
        private readonly Func<T> _checkForChanged;
        private readonly Action<T> _onValueChanged;

        private T _currentValue;

        public SerializedResolverContainerAction(Func<T> checkForChanged, Action<T> onValueChanged)
        {
            _checkForChanged = checkForChanged;
            _onValueChanged = onValueChanged;

            _currentValue = _checkForChanged.Invoke();
            _onValueChanged.Invoke(_currentValue);
        }

        public override void Resolve()
        {
            var val = _checkForChanged.Invoke();
            if (EqualityComparer<T>.Default.Equals(_currentValue, val))
            {
                return;
            }

            _currentValue = val;
            _onValueChanged.Invoke(_currentValue);
        }
    }

    /// <summary>
    /// Reads a resolver off the object that declares a tree property.
    /// </summary>
    public class SerializedResolverContainerType<T> : SerializedResolverContainerBase<T>
    {
        private readonly InspectorTreeProperty _property;

        public SerializedResolverContainerType(InspectorTreeProperty property, Func<object, T> accessor, Action<T> onValueChanged)
            : base(accessor, onValueChanged)
        {
            _property = property;
            Prime();
        }

        protected override object GetTarget() => _property.GetParentObject();
    }

    /// <summary>
    /// Reads a resolver off an object held directly, for the group headers and other places that have no
    /// tree property to hang off.
    /// </summary>
    public class SerializedResolverContainerObject<T> : SerializedResolverContainerBase<T>
    {
        private readonly object _target;

        public SerializedResolverContainerObject(object target, Func<object, T> accessor, Action<T> onValueChanged)
            : base(accessor, onValueChanged)
        {
            _target = target;
            Prime();
        }

        protected override object GetTarget() => _target;
    }
    #endregion
}
