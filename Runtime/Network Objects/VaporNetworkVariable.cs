using System;
using JetBrains.Annotations;
using Unity.Netcode;
using UnityEngine;

namespace Vapor.NetworkObjects
{
    [GenerateSerializationForGenericParameter(0)]
    public class VaporNetworkVariable<T> : VaporNetworkVariableBase where T : IEquatable<T>
    {
        public delegate void NetworkVariableChanged(VaporNetworkVariableBase sender, T lastValue, T currentValue);

        public T LastValue { get; protected set; }

        protected T InternalValue;

        public T Value
        {
            get => InternalValue;
            set
            {
                if (!Owner.IsServer)
                {
                    Debug.LogError($"Write Permission: Attempted to write to a network variable on a client");
                    return;
                }

                if (InternalValue.Equals(value))
                {
                    return;
                }

                LastValue = InternalValue;
                InternalValue = value;
                OnValueChanged?.Invoke(this, LastValue, InternalValue);

                if (!IsDirty)
                {
                    IsDirty = true;
                    Owner.MarkNetworkVariableDirty(this);
                }
            }
        }

        public event NetworkVariableChanged OnValueChanged;

        public VaporNetworkVariable(T defaultValue, [NotNull] VaporNetworkObject owner)
        {
            InternalValue = defaultValue;
            Owner = owner;
            NetworkVariableId = Owner.GetNextNetworkVariableId();
            Owner.RegisterNetworkVariable(this);
        }

        protected void InvokeOnValueChanged()
        {
            OnValueChanged?.Invoke(this, LastValue, Value);
        }

        public override void Write(FastBufferWriter writer)
        {
            BytePacker.WriteValueBitPacked(writer, NetworkVariableId);
            NetworkVariableSerialization<T>.Write(writer, ref InternalValue);
            IsDirty = false;
        }

        public override void Read(FastBufferReader reader)
        {
            LastValue = InternalValue;
            NetworkVariableSerialization<T>.Read(reader, ref InternalValue);
            IsDirty = false;
            
            OnValueChanged?.Invoke(this, LastValue, InternalValue);
        }
    }
}