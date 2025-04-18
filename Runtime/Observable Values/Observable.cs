using System;
using Newtonsoft.Json;
using UnityEngine.Assertions;
using Vapor.NewtonsoftConverters;

namespace Vapor.Observables
{
    // public static class ObservableSerializerUtility
    // {
    //     public static readonly JsonSerializerSettings s_JsonSerializerSettings = new()
    //     {
    //         Formatting = Formatting.Indented,
    //         ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    //         TypeNameHandling = TypeNameHandling.Auto,
    //     };
    // }

    [Serializable]
    public struct SavedObservable
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public object Value { get; set; }

        public SavedObservable(string name, Type type, object value)
        {
            Name = name;
            Type = type;
            Value = value;
        }
    }

    [Serializable]
    public abstract class Observable
    {
        // ***** Properties ******
        /// <summary>
        /// The Id of the field.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The Id of the field.
        /// </summary>
        public ushort Id { get; }

        /// <summary>
        /// If true, this value will be saved.
        /// </summary>
        public bool SaveValue { get; set; }

        // ***** Events ******
        public event Action<Observable> Dirtied;

        protected Observable(ushort id, bool saveValue)
        {
            Name = id.ToString();
            Id = id;
            SaveValue = saveValue;
        }

        protected Observable(string name, bool saveValue)
        {
            Name = name;
            Id = Name.GetStableHashU16();
            SaveValue = saveValue;
        }

        #region - Value -
        public abstract object GetValueBoxed();
        public abstract void SetValueBoxed(object value);
        #endregion

        #region - Events -
        internal Observable WithDirtied(Action<Observable> callback)
        {
            if (callback != null)
            {
                Dirtied += callback;
            }
            return this;
        }

        protected void OnDirtied()
        {
            Dirtied?.Invoke(this);
        }

        internal virtual void ClearCallbacks()
        {
            Dirtied = null;
        }
        #endregion

        #region - Saving and Loading -
        public abstract string SaveAsJson();
        public abstract SavedObservable Save();

        public static Observable Load(string json)
        {
            var saveData = JsonConvert.DeserializeObject<SavedObservable>(json, NewtonsoftUtility.SerializerSettings);
            var convertedValue = TypeUtility.SafeCastToType(saveData.Value, saveData.Type);
            Type loadType = typeof(Observable<>).MakeGenericType(saveData.Type);
            var result = (Observable)Activator.CreateInstance(loadType, saveData.Name, true);
            result.SetValueBoxed(convertedValue);
            return result;
        }

        public static Observable Load(SavedObservable dataToLoad)
        {
            var convertedValue = TypeUtility.SafeCastToType(dataToLoad.Value, dataToLoad.Type);
            Type loadType = typeof(Observable<>).MakeGenericType(dataToLoad.Type);
            var result = (Observable)Activator.CreateInstance(loadType, dataToLoad.Name, true);
            result.SetValueBoxed(convertedValue);
            return result;
        }

        #endregion        
    }

    [Serializable]
    public class Observable<T> : Observable, IEquatable<Observable<T>>, ICloneable where T : struct
    {
        public static implicit operator T(Observable<T> f) => f.Value;
        public static bool operator ==(Observable<T> left, Observable<T> right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            return left.Equals(right);
        }
        public static bool operator !=(Observable<T> left, Observable<T> right)
        {
            return !(left == right);
        }

        private T _value;
        public T Value
        {
            get => _value;
            set
            {
                if (_value.Equals(value))
                {
                    return;
                }

                var oldValue = _value;
                _value = value;
                ValueChanged?.Invoke(this, oldValue);
                OnDirtied();
            }
        }

        public event Action<Observable<T>, T> ValueChanged; // Value and Old Value

        public Observable(ushort id, bool saveValue) : base(id, saveValue)
        {
            Value = default;
        }

        public Observable(ushort id, bool saveValue, T value) : base(id, saveValue)
        {
            Value = value;
        }

        public Observable(string name, bool saveValue) : base(name, saveValue)
        {
            Value = default;
        }

        public Observable(string name, bool saveValue, T value) : base(name, saveValue)
        {
            Value = value;
        }

        #region - Value -
        public void SetWithoutNotify(T value)
        {
            _value = value;               
        }

        public override object GetValueBoxed()
        {
            return Value;
        }

        public override void SetValueBoxed(object value)
        {
            Assert.IsTrue(value is T, $"Value [{value}] is not correct type: {value.GetType()} | Expecting: {typeof(T)}");
            Value = (T)value;
        }
        #endregion

        #region - Events -
        public Observable<T> WithChanged(Action<Observable<T>, T> callback)
        {
            if (callback != null)
            {
                ValueChanged += callback;
            }
            return this;
        }

        internal override void ClearCallbacks()
        {
            base.ClearCallbacks();
            ValueChanged = null;
        }
        #endregion

        #region - Saving and Loading -
        public override string SaveAsJson()
        {
            var save = new SavedObservable(Name, typeof(T), Value);
            return JsonConvert.SerializeObject(save, NewtonsoftUtility.SerializerSettings);
        }

        public override SavedObservable Save()
        {
            return new SavedObservable(Name, typeof(T), Value);
        }
        #endregion

        #region - Helpers -

        public object Clone()
        {
            return new Observable<T>(Name, SaveValue, Value);
        }

        public override bool Equals(object other)
        {
            return other is Observable<T> val && Equals(val);
        }

        public bool Equals(Observable<T> other)
        {
            return other != null && (ReferenceEquals(this, other) || Value.Equals(other.Value));
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id);
        }

        public override string ToString()
        {
            return $"{Name} [{Value}]";
        }               
        #endregion
    }
}
