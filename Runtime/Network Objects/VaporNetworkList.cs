using System;
using System.Collections;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Vapor.NetworkObjects
{
    /// <summary>
    /// A replicated list. The server mutates it; every peer holding the object converges on the same
    /// contents and is told what changed through <see cref="OnListChanged"/>.
    /// </summary>
    /// <remarks>
    /// Changes replicate as deltas — one <see cref="NetworkListEvent{T}"/> per mutation — so adding a
    /// single element costs one entry rather than the whole list. A full snapshot goes out instead
    /// whenever the owning object serializes a full packet, which is what brings a late joiner up to
    /// date, and whenever the delta would be larger than the list it describes.
    /// </remarks>
    [GenerateSerializationForGenericParameter(0)]
    public class VaporNetworkList<T> : VaporNetworkVariableBase, IReadOnlyList<T> where T : unmanaged, IEquatable<T>
    {
        /// <param name="changeEvent">What changed, and where.</param>
        public delegate void OnListChangedDelegate(NetworkListEvent<T> changeEvent);

        /// <summary>
        /// Raised for every change on every peer — once on the server as it mutates, and once on each
        /// receiver as the change is applied.
        /// </summary>
        public event OnListChangedDelegate OnListChanged;

        // The payload is one of two shapes and says which, so the sender is free to fall back to a
        // snapshot at any time without the receiver having to guess.
        private const byte k_Delta = 0;
        private const byte k_Full = 1;

        private NativeList<T> _list = new(64, Allocator.Persistent);
        private NativeList<NetworkListEvent<T>> _dirtyEvents = new(64, Allocator.Persistent);

        public VaporNetworkList([NotNull] VaporNetworkObject owner) : this(null, owner) { }

        public VaporNetworkList(IEnumerable<T> defaultValues, [NotNull] VaporNetworkObject owner)
        {
            Owner = owner;
            NetworkVariableId = Owner.GetNextNetworkVariableId();
            Owner.RegisterNetworkVariable(this);

            if (defaultValues == null)
            {
                return;
            }

            foreach (var value in defaultValues)
            {
                _list.Add(value);
            }
        }

        #region - Reading -

        public int Count => _list.Length;

        public T this[int index]
        {
            get => _list[index];
            set => Set(index, value);
        }

        public bool Contains(T item) => IndexOf(item) >= 0;

        public int IndexOf(T item)
        {
            for (int i = 0; i < _list.Length; i++)
            {
                if (_list[i].Equals(item))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The backing storage, for read-only bulk access without copying.</summary>
        public NativeArray<T>.ReadOnly AsNativeArray() => _list.AsArray().AsReadOnly();

        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion

        #region - Mutation -

        public void Add(T item)
        {
            if (!CanWrite())
            {
                return;
            }

            _list.Add(item);
            Changed(new NetworkListEvent<T>
            {
                Type = NetworkListEvent<T>.EventType.Add,
                Index = _list.Length - 1,
                Value = item,
            });
        }

        public void Insert(int index, T item)
        {
            if (!CanWrite() || !InRange(index, _list.Length, nameof(Insert)))
            {
                return;
            }

            if (index == _list.Length)
            {
                _list.Add(item);
            }
            else
            {
                _list.InsertRangeWithBeginEnd(index, index + 1);
                _list[index] = item;
            }

            Changed(new NetworkListEvent<T>
            {
                Type = NetworkListEvent<T>.EventType.Insert,
                Index = index,
                Value = item,
            });
        }

        public bool Remove(T item)
        {
            if (!CanWrite())
            {
                return false;
            }

            int index = IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            _list.RemoveAt(index);
            Changed(new NetworkListEvent<T>
            {
                Type = NetworkListEvent<T>.EventType.Remove,
                Index = index,
                Value = item,
            });
            return true;
        }

        public void RemoveAt(int index)
        {
            if (!CanWrite() || !InRange(index, _list.Length - 1, nameof(RemoveAt)))
            {
                return;
            }

            var removed = _list[index];
            _list.RemoveAt(index);
            Changed(new NetworkListEvent<T>
            {
                Type = NetworkListEvent<T>.EventType.RemoveAt,
                Index = index,
                Value = removed,
            });
        }

        /// <param name="forceUpdate">
        /// Replicate even when the value compares equal. Only useful for a type whose equality ignores
        /// a field that still has to reach the other peers.
        /// </param>
        public void Set(int index, T value, bool forceUpdate = false)
        {
            if (!CanWrite() || !InRange(index, _list.Length - 1, nameof(Set)))
            {
                return;
            }

            var previous = _list[index];
            if (!forceUpdate && previous.Equals(value))
            {
                return;
            }

            _list[index] = value;
            Changed(new NetworkListEvent<T>
            {
                Type = NetworkListEvent<T>.EventType.Value,
                Index = index,
                Value = value,
                PreviousValue = previous,
            });
        }

        public void Clear()
        {
            if (!CanWrite() || _list.Length == 0)
            {
                return;
            }

            _list.Clear();

            // Nothing queued before a clear can change where the receiver ends up, so the queue is
            // dropped rather than grown. In practice the send that follows a clear goes out as a
            // snapshot anyway — an emptied list makes the delta the larger of the two — but this keeps
            // the queue from accumulating across repeated clears.
            _dirtyEvents.Clear();
            Changed(new NetworkListEvent<T> { Type = NetworkListEvent<T>.EventType.Clear });
        }

        #endregion

        #region - Serialization -

        /// <summary>
        /// Writes the whole list without consuming the queued changes. A snapshot goes to one joining
        /// client; the deltas it already contains may still be owed to everyone else.
        /// </summary>
        public override void WriteFull(FastBufferWriter writer)
        {
            BytePacker.WriteValueBitPacked(writer, NetworkVariableId);
            writer.WriteValueSafe(k_Full);
            WriteContents(writer);
        }

        public override void Write(FastBufferWriter writer)
        {
            BytePacker.WriteValueBitPacked(writer, NetworkVariableId);

            // A delta that restates more than the list holds is a waste of bandwidth, and an empty one
            // means the dirty flag outlived its events — a snapshot is correct in both cases.
            if (_dirtyEvents.Length == 0 || _dirtyEvents.Length > _list.Length)
            {
                writer.WriteValueSafe(k_Full);
                WriteContents(writer);
            }
            else
            {
                writer.WriteValueSafe(k_Delta);
                WriteDelta(writer);
            }

            _dirtyEvents.Clear();
            IsDirty = false;
        }

        private void WriteContents(FastBufferWriter writer)
        {
            BytePacker.WriteValueBitPacked(writer, _list.Length);
            for (int i = 0; i < _list.Length; i++)
            {
                NetworkVariableSerialization<T>.Write(writer, ref _list.ElementAt(i));
            }
        }

        private void WriteDelta(FastBufferWriter writer)
        {
            BytePacker.WriteValueBitPacked(writer, _dirtyEvents.Length);
            for (int i = 0; i < _dirtyEvents.Length; i++)
            {
                var change = _dirtyEvents[i];
                writer.WriteValueSafe(change.Type);

                switch (change.Type)
                {
                    case NetworkListEvent<T>.EventType.Add:
                    case NetworkListEvent<T>.EventType.Remove:
                        NetworkVariableSerialization<T>.Write(writer, ref change.Value);
                        break;

                    case NetworkListEvent<T>.EventType.Insert:
                    case NetworkListEvent<T>.EventType.Value:
                        BytePacker.WriteValueBitPacked(writer, change.Index);
                        NetworkVariableSerialization<T>.Write(writer, ref change.Value);
                        break;

                    case NetworkListEvent<T>.EventType.RemoveAt:
                        BytePacker.WriteValueBitPacked(writer, change.Index);
                        break;

                    case NetworkListEvent<T>.EventType.Clear:
                        break;
                }
            }
        }

        public override void Read(FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte shape);
            switch (shape)
            {
                case k_Full:
                    ReadContents(reader);
                    break;

                case k_Delta:
                    ReadDelta(reader);
                    break;

                default:
                    // The payload cannot be skipped without knowing its length, so the rest of this
                    // packet is already lost. Saying so beats reading garbage.
                    Debug.LogError($"{nameof(VaporNetworkList<T>)} [{NetworkVariableId}] received an unknown payload [{shape}]. The peers are running different builds.");
                    break;
            }

            IsDirty = false;
        }

        private void ReadContents(FastBufferReader reader)
        {
            _list.Clear();
            ByteUnpacker.ReadValueBitPacked(reader, out int count);
            for (int i = 0; i < count; i++)
            {
                var value = new T();
                NetworkVariableSerialization<T>.Read(reader, ref value);
                _list.Add(value);
            }

            OnListChanged?.Invoke(new NetworkListEvent<T> { Type = NetworkListEvent<T>.EventType.Full });
        }

        private void ReadDelta(FastBufferReader reader)
        {
            ByteUnpacker.ReadValueBitPacked(reader, out int changeCount);
            for (int i = 0; i < changeCount; i++)
            {
                reader.ReadValueSafe(out NetworkListEvent<T>.EventType type);
                switch (type)
                {
                    case NetworkListEvent<T>.EventType.Add:
                    {
                        var value = new T();
                        NetworkVariableSerialization<T>.Read(reader, ref value);
                        _list.Add(value);
                        OnListChanged?.Invoke(new NetworkListEvent<T>
                        {
                            Type = type,
                            Index = _list.Length - 1,
                            Value = value,
                        });
                        break;
                    }

                    case NetworkListEvent<T>.EventType.Insert:
                    {
                        ByteUnpacker.ReadValueBitPacked(reader, out int index);
                        var value = new T();
                        NetworkVariableSerialization<T>.Read(reader, ref value);

                        if (index < _list.Length)
                        {
                            _list.InsertRangeWithBeginEnd(index, index + 1);
                            _list[index] = value;
                        }
                        else
                        {
                            index = _list.Length;
                            _list.Add(value);
                        }

                        OnListChanged?.Invoke(new NetworkListEvent<T>
                        {
                            Type = type,
                            Index = index,
                            Value = value,
                        });
                        break;
                    }

                    case NetworkListEvent<T>.EventType.Remove:
                    {
                        var value = new T();
                        NetworkVariableSerialization<T>.Read(reader, ref value);

                        int index = IndexOf(value);
                        if (index < 0)
                        {
                            break;
                        }

                        _list.RemoveAt(index);
                        OnListChanged?.Invoke(new NetworkListEvent<T>
                        {
                            Type = type,
                            Index = index,
                            Value = value,
                        });
                        break;
                    }

                    case NetworkListEvent<T>.EventType.RemoveAt:
                    {
                        ByteUnpacker.ReadValueBitPacked(reader, out int index);
                        if (index < 0 || index >= _list.Length)
                        {
                            break;
                        }

                        var removed = _list[index];
                        _list.RemoveAt(index);
                        OnListChanged?.Invoke(new NetworkListEvent<T>
                        {
                            Type = type,
                            Index = index,
                            Value = removed,
                        });
                        break;
                    }

                    case NetworkListEvent<T>.EventType.Value:
                    {
                        ByteUnpacker.ReadValueBitPacked(reader, out int index);
                        var value = new T();
                        NetworkVariableSerialization<T>.Read(reader, ref value);

                        if (index < 0 || index >= _list.Length)
                        {
                            break;
                        }

                        var previous = _list[index];
                        _list[index] = value;
                        OnListChanged?.Invoke(new NetworkListEvent<T>
                        {
                            Type = type,
                            Index = index,
                            Value = value,
                            PreviousValue = previous,
                        });
                        break;
                    }

                    case NetworkListEvent<T>.EventType.Clear:
                    {
                        _list.Clear();
                        OnListChanged?.Invoke(new NetworkListEvent<T> { Type = type });
                        break;
                    }
                }
            }
        }

        #endregion

        public override void Dispose()
        {
            if (_list.IsCreated)
            {
                _list.Dispose();
            }

            if (_dirtyEvents.IsCreated)
            {
                _dirtyEvents.Dispose();
            }

            OnListChanged = null;
            base.Dispose();
        }

        /// <summary>
        /// Records a local mutation: queue it for the next send, then tell this peer about it so the
        /// server sees the same callback its clients will.
        /// </summary>
        private void Changed(NetworkListEvent<T> change)
        {
            if (Owner.IsSpawned)
            {
                _dirtyEvents.Add(change);

                if (!IsDirty)
                {
                    IsDirty = true;
                    Owner.MarkNetworkVariableDirty(this);
                }
            }

            OnListChanged?.Invoke(change);
        }

        /// <summary>
        /// Before the object spawns there is no authority to test and nothing to replicate — the
        /// contents ride out on the full snapshot sent at spawn. That is what lets OnPreSpawn seed the
        /// list without tripping the server-only check.
        /// </summary>
        private bool CanWrite()
        {
            if (!Owner.IsSpawned || Owner.IsServer)
            {
                return true;
            }

            Debug.LogError($"Write Permission: Attempted to modify {nameof(VaporNetworkList<T>)} [{NetworkVariableId}] on a client.");
            return false;
        }

        private bool InRange(int index, int max, string operation)
        {
            if (index >= 0 && index <= max)
            {
                return true;
            }

            Debug.LogError($"{nameof(VaporNetworkList<T>)}.{operation}: index {index} is outside [0, {max}].");
            return false;
        }
    }
}
