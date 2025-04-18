using System;
using System.Reflection;
using UnityEngine;

namespace VaporEditor.Inspector
{
    #region - Resolvers -
    public abstract class SerializedResolverContainer
    {
        public abstract void Resolve();

        //protected object NearestTarget(SerializedWrapperObject parent)
        //{
        //    while (parent != null)
        //    {
        //        if(parent.Member == null)
        //        {
        //            return parent is SerializedInspectorRootNode root ? root.Source.Object : null;
        //        }

        //        var target = parent.Member.GetValue();
        //        if (target != null)
        //        {
        //            return target;
        //        }
        //        parent = parent.Parent;
        //    }
        //    return null;
        //}
    }

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
            if (_currentValue.Equals(val))
            {
                return;
            }

            _currentValue = val;
            _onValueChanged.Invoke(_currentValue);
        }
    }

    public class SerializedResolverContainerType<T> : SerializedResolverContainer
    {
        private readonly InspectorTreeProperty _property;
        private readonly MemberInfo _memberInfo;
        private readonly Action<T> _onValueChanged;

        private T _currentValue;

        public SerializedResolverContainerType(InspectorTreeProperty property, MemberInfo memberInfo, Action<T> onValueChanged)
        {
            _property = property;
            _memberInfo = memberInfo;
            _onValueChanged = onValueChanged;

            if (ReflectionUtility.TryResolveMemberValue(_property.GetParentObject(), _memberInfo, null, out _currentValue))
            {
                //Debug.Log($"Resolved Value: {_parent.Object} | {_currentValue}");
                _onValueChanged.Invoke(_currentValue);
            }
            else
            {
                //Debug.Log($"Couldnt Resolve Value: {_parent.Object}");
            }
        }

        public override void Resolve()
        {
            if (ReflectionUtility.TryResolveMemberValue<T>(_property.GetParentObject(), _memberInfo, null, out var val))
            {
                if (_currentValue != null && _currentValue.Equals(val))
                {
                    return;
                }

                if(_currentValue == null && val == null)
                {
                    return;
                }

                _currentValue = val;
                _onValueChanged.Invoke(_currentValue);
            }
        }
    }

    public class SerializedResolverContainerObject<T> : SerializedResolverContainer
    {
        private readonly object _target;
        private readonly MemberInfo _memberInfo;
        private readonly Action<T> _onValueChanged;

        private T _currentValue;

        public SerializedResolverContainerObject(object target, MemberInfo memberInfo, Action<T> onValueChanged)
        {
            _target = target;
            _memberInfo = memberInfo;
            _onValueChanged = onValueChanged;

            if (ReflectionUtility.TryResolveMemberValue(_target, _memberInfo, null, out _currentValue))
            {
                //Debug.Log($"Resolved Value: {_parent.Object} | {_currentValue}");
                _onValueChanged.Invoke(_currentValue);
            }
            else
            {
                Debug.LogError($"Couldnt Resolve Value: {_target} - {_memberInfo.Name}");
            }
        }

        public override void Resolve()
        {
            if (ReflectionUtility.TryResolveMemberValue<T>(_target, _memberInfo, null, out var val))
            {
                if (_currentValue.Equals(val))
                {
                    return;
                }

                _currentValue = val;
                _onValueChanged.Invoke(_currentValue);
            }
        }
    }
    #endregion
}
