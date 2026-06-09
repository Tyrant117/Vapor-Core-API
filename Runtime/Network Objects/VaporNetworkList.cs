using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    [GenerateSerializationForGenericParameter(0)]
    public class VaporNetworkList<T> : VaporNetworkVariableBase where T : unmanaged, IEquatable<T>
    {
        private NativeList<T> m_List = new NativeList<T>(64, Allocator.Persistent);
        private NativeList<NetworkListEvent<T>> m_DirtyEvents = new NativeList<NetworkListEvent<T>>(64, Allocator.Persistent);

        /// <summary>
        /// Delegate type for list changed event
        /// </summary>
        /// <param name="changeEvent">Struct containing information about the change event</param>
        public delegate void OnListChangedDelegate(NetworkListEvent<T> changeEvent);
        
        /// <summary>
        /// Creates A NetworkList/>
        /// </summary>
        public event OnListChangedDelegate OnListChanged;

        private NetworkList<T> t;
        
        public VaporNetworkList(IEnumerable<T> defaultValues, [NotNull] VaporNetworkObject owner)
        {
            if (defaultValues != null)
            {
                foreach (var value in defaultValues)
                {
                    m_List.Add(value);
                }
            }
            Owner = owner;
            NetworkVariableId = Owner.GetNextNetworkVariableId();
            Owner.RegisterNetworkVariable(this);
        }
        
        /// <summary>
        /// Finalizer that ensures proper cleanup of network list resources
        /// </summary>
        ~VaporNetworkList()
        {
            Dispose();
        }
        
        public override void Dispose()
        {
            if (m_List.IsCreated)
            {
                m_List.Dispose();
            }

            if (m_DirtyEvents.IsCreated)
            {
                m_DirtyEvents.Dispose();
            }

            base.Dispose();
        }

        public override void Write(FastBufferWriter writer)
        {
            writer.WriteValueSafe((ushort)m_List.Length);
            for (int i = 0; i < m_List.Length; i++)
            {
                NetworkVariableSerialization<T>.Write(writer, ref m_List.ElementAt(i));
            }
        }
        public override void Read(FastBufferReader reader)
        {
            m_List.Clear();
            reader.ReadValueSafe(out ushort count);
            for (int i = 0; i < count; i++)
            {
                var value = new T();
                NetworkVariableSerialization<T>.Read(reader, ref value);
                m_List.Add(value);
            }
        }
    }
}