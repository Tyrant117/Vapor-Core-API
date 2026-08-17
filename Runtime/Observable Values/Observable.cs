using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Assertions;
using Vapor.Networking;
using Vapor.Serialization;
using Vapor.Unsafe;

namespace Vapor.Observables
{
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
        public uint Id { get; }

        /// <summary>
        /// If true, this value will be saved.
        /// </summary>
        public bool SaveValue { get; set; }

        // ***** Events ******
        public event Action<Observable> Dirtied;

        protected Observable(uint id, bool saveValue)
        {
            Name = id.ToString();
            Id = id;
            SaveValue = saveValue;
        }

        protected Observable(string name, bool saveValue)
        {
            Name = name;
            Id = Name.Hash32();
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

        public abstract string SaveAsVsl();
        public abstract SavedObservable Save();

        public static Observable Load(string vsl)
        {
            var saveData = Vsl.Deserialize<SavedObservable>(vsl);
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
    public class Observable<T> : Observable, IEquatable<Observable<T>>, ICloneable where T : struct, IEquatable<T>
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
                if (_linkedNetworkVariable != null && _linkedNetworkVariable.CanWrite)
                {
                    _linkedNetworkVariable.Value = _value;
                }
            }
        }

        private VaporNetworkVariable<T> _linkedNetworkVariable;

        public event Action<Observable<T>, T> ValueChanged; // Value and Old Value

        public Observable(uint id, bool saveValue) : base(id, saveValue)
        {
            Value = default;
        }

        public Observable(uint id, bool saveValue, T value) : base(id, saveValue)
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

        public override string SaveAsVsl()
        {
            return Vsl.Serialize(Save());
        }

        public override SavedObservable Save()
        {
            return new SavedObservable(Name, typeof(T), Value);
        }

        #endregion

        #region - Networking -

        /// <summary>Follows a replicated variable: whenever it changes, this observable takes the value (the reading side).</summary>
        public void LinkClientNetworkedVariable(VaporNetworkVariable<T> networkVariable)
        {
            networkVariable.OnValueChanged += OnClientNetworkValueChanged;
        }

        private void OnClientNetworkValueChanged(T previous, T current)
        {
            Value = current;
        }

        /// <summary>Drives a replicated variable: whenever this observable changes where the variable may be written, the variable takes the value.</summary>
        public Observable<T> WithLinkedServerNetworkedVariable(VaporNetworkVariable<T> networkVariable)
        {
            _linkedNetworkVariable = networkVariable;
            return this;
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

    /// <summary>
    /// Stores the observable's declared value type beside its value so the value can be read without
    /// relying on JSON's untyped token model.
    /// </summary>
    [NoAutoStaticsCleanup]
    public sealed class SavedObservableFormatter : VslFormatter<SavedObservable>
    {
        public static readonly SavedObservableFormatter Instance = new SavedObservableFormatter();

        public override void Write(ref VslWriter writer, in SavedObservable value, VslContext context)
        {
            writer.BeginTuple();
            writer.WriteString(value.Name);
            SystemTypeFormatter.Instance.Write(ref writer, value.Type, context);
            if (value.Type == null)
            {
                writer.WriteNull();
            }
            else
            {
                VslFormatterRegistry.Get(value.Type).WriteObject(ref writer, value.Value, context);
            }
            writer.EndTuple();
        }

        public override SavedObservable Read(ref VslReader reader, VslContext context)
        {
            reader.ReadTupleStart();
            var name = reader.ReadString();
            var type = SystemTypeFormatter.Instance.Read(ref reader, context);
            var value = type == null
                ? ReadNullValue(ref reader)
                : VslFormatterRegistry.Get(type).ReadObject(ref reader, context);
            reader.ReadTupleEnd();
            return new SavedObservable(name, type, value);
        }

        private static object ReadNullValue(ref VslReader reader)
        {
            if (reader.TryReadNull())
            {
                return null;
            }

            throw new VslException("A saved observable with no runtime type must have a null value.");
        }
    }

    internal static partial class ObservableVslFormatters
    {
        [OnCodeInitializing]
        private static void Register()
        {
            VslFormatterRegistry.Register(SavedObservableFormatter.Instance);
        }
    }
}
