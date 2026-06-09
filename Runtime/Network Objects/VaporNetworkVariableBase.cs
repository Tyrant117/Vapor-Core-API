using System;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    public abstract class VaporNetworkVariableBase : IDisposable
    {
        protected VaporNetworkObject Owner;
        public uint NetworkVariableId { get; protected set; }
        public bool IsDirty { get; protected set; }
        public abstract void Write(FastBufferWriter writer);
        public abstract void Read(FastBufferReader reader);
        
        public virtual void Dispose()
        {
            NetworkVariableId = 0;
            Owner = null;
        }
    }
}