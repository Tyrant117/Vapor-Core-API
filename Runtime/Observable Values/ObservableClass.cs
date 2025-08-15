using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Vapor.NewtonsoftConverters;

namespace Vapor.Observables
{
    /// <summary>
    /// Container for a collection of saved fields.
    /// </summary>
    [Serializable]
    public struct SavedObservableClass
    {
        public string Name;
        public Type ClassType;
        public SavedObservable[] SavedFields;

        public SavedObservableClass(string name, Type type, List<SavedObservable> fields)
        {
            Name = name;
            ClassType = type;
            SavedFields = fields.ToArray();
        }
    }

    public interface IObservedClass
    {
        void SetupFields(ObservableClass @class);
    }

    /// <summary>
    /// An abstract implementation of an observable class that can keep track of a collection of Observables.
    /// When a value is changed inside the monitored collection the entire class will be marked dirty.
    /// This class also facillitates serializing and deserializing the class into a json format.
    /// The one requirement of this class is it must implement a constructor that only implements the default string named arguments.
    /// <code>
    /// public class ChildObservableClass
    /// {
    ///     public ChildObservableClass(string className) : base(className) { }
    ///     
    ///     protected override SetupFields()
    ///     {
    ///         // Field initialization should be done here, not in the constructor.
    ///     }
    /// }
    /// </code>
    /// </summary>
    public abstract class ObservableClass
    {
        /// <summary>
        /// A unique name for this instance of the class.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A unique id for this instance of the class.
        /// </summary>
        public ushort Key { get; }

        /// <summary>
        /// Gets a field based on the ID and casts it to a type that inherits from <see cref="Observable"/>. There is no checking, will throw errors on invalid id.
        /// </summary>
        /// <param name="fieldName">The id of the field to retrieve</param>
        /// <typeparam name="T">The type to cast the field to</typeparam>
        /// <returns>The <see cref="Observable"/> of type T</returns>
        public T GetField<T>(string fieldName) where T : Observable => (T)Fields[fieldName.GetStableHashU16()];
        public T GetField<T>(ushort fieldId) where T : Observable => (T)Fields[fieldId];

        public T GetFieldValue<T>(string fieldName) where T : struct, IEquatable<T> => GetField<Observable<T>>(fieldName).Value;
        public T GetFieldValue<T>(ushort fieldId) where T : struct, IEquatable<T> => GetField<Observable<T>>(fieldId).Value;

        public void SetFieldValue<T>(string fieldName, T value) where T : struct, IEquatable<T> => GetField<Observable<T>>(fieldName).Value = value;
        public void SetFieldValue<T>(ushort fieldId, T value) where T : struct, IEquatable<T> => GetField<Observable<T>>(fieldId).Value = value;

        protected readonly Dictionary<ushort, Observable> Fields = new();
        protected bool IsLoaded;

        /// <summary>
        /// This event is fired when the <see cref="Observable"/>s of the class change.
        /// </summary>
        public event Action<ObservableClass, Observable> Dirtied;

        protected ObservableClass(ushort key)
        {
            Name = key.ToString();
            Key = key;
        }

        protected ObservableClass(string className)
        {
            Name = className;
            Key = Name.GetStableHashU16();
        }

        #region - Fields -
        public Observable<T> GetOrAddField<T>(ushort fieldId, bool saveValue, T value, Action<Observable<T>, T> callback = null) where T : struct, IEquatable<T>
        {
            if (!Fields.ContainsKey(fieldId))
            {
                return AddField(fieldId, saveValue, value, callback);
            }
            else
            {
                var field = (Observable<T>)Fields[fieldId];
                field.WithChanged(callback);
                return field;
            }
        }

        public Observable<T> GetOrAddField<T>(string fieldName, bool saveValue, T value, Action<Observable<T>, T> callback = null) where T : struct, IEquatable<T>
        {
            var id = fieldName.GetStableHashU16();
            if (!Fields.ContainsKey(id))
            {
                return AddField(fieldName, saveValue, value, callback);
            }
            else
            {
                var field = (Observable<T>)Fields[id];
                field.WithChanged(callback);
                return field;
            }
        }

        public Observable<T> AddField<T>(ushort fieldId, bool saveValue, T value, Action<Observable<T>, T> callback = null) where T : struct, IEquatable<T>
        {
            if (!Fields.ContainsKey(fieldId))
            {
                var field = new Observable<T>(fieldId, saveValue, value).WithChanged(callback);
                field.WithDirtied(MarkDirty);
                Fields.Add(fieldId, field);
                MarkDirty(Fields[fieldId]);
                return field;
            }
            else
            {
                Debug.LogError($"Field [{fieldId}] already added to class {Name}");
                return (Observable<T>)Fields[fieldId];
            }
        }

        public Observable<T> AddField<T>(string fieldName, bool saveValue, T value, Action<Observable<T>, T> callback = null) where T : struct, IEquatable<T>
        {
            var id = fieldName.GetStableHashU16();
            if (!Fields.ContainsKey(id))
            {
                var field = new Observable<T>(fieldName, saveValue, value).WithChanged(callback);
                field.WithDirtied(MarkDirty);
                Fields.Add(id, field);
                MarkDirty(Fields[id]);
                return field;
            }
            else
            {
                Debug.LogError($"Field [{fieldName}] already added to class {Name}");
                return (Observable<T>)Fields[id];
            }
        }

        public void AddField(Observable field)
        {
            if (Fields.TryAdd(field.Id, field))
            {
                field.WithDirtied(MarkDirty);
                MarkDirty(field);
            }
            else
            {
                Debug.LogError($"Field [{field.Name}] already added to class {Name}");
            }
        }

        public void RemoveField(string fieldName)
        {
            RemoveField(fieldName.GetStableHashU16());
        }
        public void RemoveField(ushort fieldId)
        {
            if (Fields.TryGetValue(fieldId, out var field))
            {
                field.ClearCallbacks();
                Fields.Remove(fieldId);
            }
        }

        public virtual void MarkDirty(Observable field)
        {
            Dirtied?.Invoke(this, field);
        }
        #endregion

        #region - Saving and Loading -
        public string SaveAsJson()
        {
            var save = Save();
            return JsonConvert.SerializeObject(save, NewtonsoftUtility.SerializerSettings);
        }

        public SavedObservableClass Save()
        {
            List<SavedObservable> holder = new();
            foreach (var field in Fields.Values)
            {
                if (!field.SaveValue)
                {
                    continue;
                }

                holder.Add(field.Save());
            }

            return new SavedObservableClass(Name, GetType(), holder);
        }

        public static SavedObservableClass Load(string json)
        {
            return JsonConvert.DeserializeObject<SavedObservableClass>(json, NewtonsoftUtility.SerializerSettings);
        }

        public void Load(SavedObservableClass load)
        {
            if (!IsLoaded)
            {
                if (load.SavedFields != null)
                {
                    foreach (var field in load.SavedFields)
                    {
                        var obs = Observable.Load(field);
                        var id = obs.Id;
                        if (Fields.TryGetValue(id, out var obsField))
                        {
                            obsField.SetValueBoxed(obs.GetValueBoxed());
                        }
                        else
                        {
                            AddField(obs);
                        }
                    }
                }
                IsLoaded = true;
            }
        }
        #endregion
    }

    public class ObservableClass<T> : ObservableClass where T : IObservedClass
    {
        public T ObservedClass { get; }

        public ObservableClass(string className, T observedClass) : base(className)
        {
            ObservedClass = observedClass;
            ObservedClass.SetupFields(this);
        }
    }
}
