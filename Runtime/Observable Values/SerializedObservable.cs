using System;
using UnityEngine;
using Vapor.Inspector;

namespace Vapor.Observables
{
    [Serializable, DrawWithVapor(UIGroupType.Box)]
    public class SerializedObservable<T> : IEquatable<SerializedObservable<T>> where T : struct, IEquatable<T>
    {
        public static implicit operator T(SerializedObservable<T> f) => f.Observable.Value;

        public static bool operator ==(SerializedObservable<T> left, SerializedObservable<T> right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            return left.Observable.Equals(right.Observable);
        }

        public static bool operator !=(SerializedObservable<T> left, SerializedObservable<T> right)
        {
            return !(left == right);
        }
        
        [HorizontalGroup("H"), SerializeField]
        private T _editorValue;
        [HorizontalGroup("H"), SerializeField]
        private T _runtimeValue;
        [HorizontalGroup("H"), SerializeField]
        private bool _saveValue;
        
        public Observable<T> Observable { get; private set; }

        public void Convert(string fieldName)
        {
#if UNITY_EDITOR
            Observable = new Observable<T>(fieldName, _saveValue, _editorValue);
#else
            Observable = new Observable<T>(fieldName, _saveValue, _runtimeValue);
#endif
        }

        public override bool Equals(object other)
        {
            return other is SerializedObservable<T> val && Equals(val);
        }

        public bool Equals(SerializedObservable<T> other)
        {
            return other != null && (ReferenceEquals(this, other) || Observable.Value.Equals(other.Observable.Value));
        }

        public override int GetHashCode()
        {
            // ReSharper disable once NonReadonlyMemberInGetHashCode
            return HashCode.Combine(Observable.Id);
        }

        public override string ToString()
        {
            return $"{Observable.Name} [{Observable.Value}]";
        }
    }
    
    [Serializable]
    public class SerializedFloat : SerializedObservable<float>
    {
        
    }
}