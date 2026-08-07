using System;
using Unity.Netcode;

namespace Vapor.NetworkObjects
{
    public abstract class VaporNetworkVariableBase : IDisposable
    {
        protected VaporNetworkObject Owner;
        public uint NetworkVariableId { get; protected set; }
        public bool IsDirty { get; protected set; }
        /// <summary>
        /// Writes whatever the peers need to catch up with this variable. Every implementation must
        /// lead with <see cref="NetworkVariableId"/> bit-packed — <see cref="VaporNetworkObject"/>
        /// reads it back to decide which variable a payload belongs to, so a variable that omits it
        /// desyncs every entry after it in the packet.
        /// </summary>
        public abstract void Write(FastBufferWriter writer);

        /// <summary>
        /// Writes the variable's entire state, for the full packet that brings a peer up to date on
        /// spawn. Only collections need to tell this apart from <see cref="Write"/>: a scalar's delta
        /// is its whole value either way.
        /// </summary>
        public virtual void WriteFull(FastBufferWriter writer) => Write(writer);

        public abstract void Read(FastBufferReader reader);
        
        public virtual void Dispose()
        {
            NetworkVariableId = 0;
            Owner = null;
        }
    }
}