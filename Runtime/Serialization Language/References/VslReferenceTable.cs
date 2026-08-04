using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace Vapor.Serialization
{
    /// <summary>
    /// A reference resolver backed by an explicit id-to-object table.
    /// </summary>
    /// <remarks>
    /// Use it when neither an <c>EntityId</c> nor an asset locator is the right key — a networked
    /// object graph, a save format with its own stable ids, or a test that needs deterministic
    /// rebinding. Ids registered here take precedence; anything else falls through to
    /// <see cref="Fallback"/>.
    /// </remarks>
    public sealed class VslReferenceTable : IVslReferenceResolver
    {
        private readonly Dictionary<ulong, Object> _byId = new Dictionary<ulong, Object>();
        private readonly Dictionary<Object, ulong> _byObject = new Dictionary<Object, ulong>();
        private ulong _nextAutoId = 1;

        /// <summary>
        /// Resolver consulted for objects this table does not know about. Defaults to
        /// <see cref="VslObjectReferenceResolver"/>; set to null to make the table authoritative.
        /// </summary>
        public IVslReferenceResolver Fallback { get; set; } = VslObjectReferenceResolver.Instance;

        public int Count => _byId.Count;

        /// <summary>Registers an object under an explicit id.</summary>
        public void Register(ulong id, Object obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            _byId[id] = obj;
            _byObject[obj] = id;
        }

        /// <summary>Registers an object under the next automatically assigned id, and returns it.</summary>
        public ulong Register(Object obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (_byObject.TryGetValue(obj, out var existing))
            {
                return existing;
            }

            var id = _nextAutoId++;
            Register(id, obj);
            return id;
        }

        public void Clear()
        {
            _byId.Clear();
            _byObject.Clear();
            _nextAutoId = 1;
        }

        public bool TryGetReference(Object obj, out VslObjectReference reference)
        {
            reference = VslObjectReference.Null;

            if (obj == null)
            {
                return false;
            }

            if (_byObject.TryGetValue(obj, out var id))
            {
                reference = new VslObjectReference(id);
                return true;
            }

            return Fallback != null && Fallback.TryGetReference(obj, out reference);
        }

        public bool TryResolve(in VslObjectReference reference, Type expectedType, out Object obj)
        {
            if (reference.HasEntityId && _byId.TryGetValue(reference.EntityId, out obj) && obj != null)
            {
                obj = VslAssetLocator.Narrow(obj, expectedType);
                if (obj != null)
                {
                    return true;
                }
            }

            obj = null;
            return Fallback != null && Fallback.TryResolve(reference, expectedType, out obj);
        }
    }
}
