using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vapor.Networking
{
    /// <summary>
    /// A uniform grid over the XZ plane that answers "is this object near this client" for relevance,
    /// and "how far" for LOD. Objects report positions; clients report focus points (their pawn, a
    /// spectator camera). Membership changes only when a position crosses a cell edge, so updating it
    /// every tick is cheap.
    /// </summary>
    /// <remarks>
    /// Relevance is a square of cells around each focus (Chebyshev distance), with hysteresis: an
    /// object already observed stays relevant one ring further out than one being discovered, so a
    /// player pacing along a boundary does not spawn and despawn the same things every tick. Distance
    /// for LOD is Euclidean and exact.
    /// </remarks>
    public sealed class SpatialInterestGrid : ISpatialRelevance, INetworkLod
    {
        private struct Entry
        {
            public Vector3 Position;
            public long Cell;
        }

        private readonly Dictionary<ulong, Entry> _objects = new();
        private readonly Dictionary<long, HashSet<ulong>> _cells = new();
        private readonly Dictionary<ulong, List<Vector3>> _focus = new();
        private readonly List<ulong> _idScratch = new();

        public SpatialInterestGrid(float cellSize = 32f, int radiusCells = 3, int hysteresisCells = 1)
        {
            if (cellSize <= 0f) throw new ArgumentOutOfRangeException(nameof(cellSize));
            CellSize = cellSize;
            RadiusCells = Math.Max(0, radiusCells);
            HysteresisCells = Math.Max(0, hysteresisCells);
        }

        public float CellSize { get; }

        /// <summary>Cells in each direction from a focus that are relevant on discovery.</summary>
        public int RadiusCells { get; set; }

        /// <summary>Extra rings an already-observed object may drift into before it drops out.</summary>
        public int HysteresisCells { get; set; }

        /// <summary>Distance → rate-scale tiers for <see cref="INetworkLod"/>. Null keeps every object at full rate.</summary>
        public NetworkLodProfile LodProfile { get; set; } = NetworkLodProfile.Default;

        public int ObjectCount => _objects.Count;

        #region - Objects -

        public void SetPosition(ulong objectId, Vector3 position)
        {
            long cell = CellOf(position);
            if (_objects.TryGetValue(objectId, out var entry))
            {
                if (entry.Cell != cell)
                {
                    RemoveFromCell(entry.Cell, objectId);
                    AddToCell(cell, objectId);
                }

                entry.Position = position;
                entry.Cell = cell;
                _objects[objectId] = entry;
                return;
            }

            _objects[objectId] = new Entry { Position = position, Cell = cell };
            AddToCell(cell, objectId);
        }

        public void Remove(ulong objectId)
        {
            if (_objects.TryGetValue(objectId, out var entry))
            {
                RemoveFromCell(entry.Cell, objectId);
                _objects.Remove(objectId);
            }
        }

        public bool TryGetPosition(ulong objectId, out Vector3 position)
        {
            if (_objects.TryGetValue(objectId, out var entry))
            {
                position = entry.Position;
                return true;
            }

            position = default;
            return false;
        }

        #endregion

        #region - Focus -

        /// <summary>Replaces a client's focus with a single point.</summary>
        public void SetFocus(ulong clientId, Vector3 position)
        {
            if (!_focus.TryGetValue(clientId, out var list))
            {
                list = new List<Vector3>(1);
                _focus[clientId] = list;
            }

            list.Clear();
            list.Add(position);
        }

        /// <summary>Adds a further focus point (a second pawn, a remote camera).</summary>
        public void AddFocus(ulong clientId, Vector3 position)
        {
            if (!_focus.TryGetValue(clientId, out var list))
            {
                list = new List<Vector3>(1);
                _focus[clientId] = list;
            }

            list.Add(position);
        }

        public void ClearFocus(ulong clientId) => _focus.Remove(clientId);

        public bool HasFocus(ulong clientId) => _focus.TryGetValue(clientId, out var list) && list.Count > 0;

        #endregion

        #region - Queries -

        /// <summary>The nearest distance between the object and any of the client's focus points; +∞ when either is unknown.</summary>
        public float Distance(ulong objectId, ulong clientId)
        {
            if (!_objects.TryGetValue(objectId, out var entry) || !_focus.TryGetValue(clientId, out var focus) || focus.Count == 0)
            {
                return float.PositiveInfinity;
            }

            float best = float.PositiveInfinity;
            for (int i = 0; i < focus.Count; i++)
            {
                float d = Vector3.Distance(entry.Position, focus[i]);
                if (d < best) best = d;
            }

            return best;
        }

        /// <summary>Every object within <paramref name="radiusCells"/> cells of any of the client's focus points.</summary>
        public void CollectNear(ulong clientId, int radiusCells, List<ulong> results)
        {
            if (!_focus.TryGetValue(clientId, out var focus)) return;
            _idScratch.Clear();
            foreach (var point in focus)
            {
                Unpack(CellOf(point), out int cx, out int cz);
                for (int x = cx - radiusCells; x <= cx + radiusCells; x++)
                for (int z = cz - radiusCells; z <= cz + radiusCells; z++)
                {
                    if (_cells.TryGetValue(Pack(x, z), out var set))
                    {
                        foreach (var id in set)
                        {
                            if (!_idScratch.Contains(id))
                            {
                                _idScratch.Add(id);
                                results.Add(id);
                            }
                        }
                    }
                }
            }
        }

        bool ISpatialRelevance.IsRelevant(VaporNetworkObject networkObject, ulong clientId, bool currentlyObserving)
        {
            // An object that never reported a position is not spatial in practice; leave it visible.
            if (!_objects.TryGetValue(networkObject.NetworkObjectId, out var entry)) return true;
            if (!_focus.TryGetValue(clientId, out var focus) || focus.Count == 0) return false;

            int reach = RadiusCells + (currentlyObserving ? HysteresisCells : 0);
            Unpack(entry.Cell, out int ox, out int oz);
            for (int i = 0; i < focus.Count; i++)
            {
                Unpack(CellOf(focus[i]), out int fx, out int fz);
                if (Math.Abs(ox - fx) <= reach && Math.Abs(oz - fz) <= reach)
                {
                    return true;
                }
            }

            return false;
        }

        float INetworkLod.RateScale(VaporNetworkObject networkObject, ulong clientId)
        {
            var profile = LodProfile;
            if (profile == null) return 1f;
            return profile.ScaleFor(Distance(networkObject.NetworkObjectId, clientId));
        }

        #endregion

        #region - Cells -

        private long CellOf(Vector3 position)
        {
            int x = (int)Math.Floor(position.x / CellSize);
            int z = (int)Math.Floor(position.z / CellSize);
            return Pack(x, z);
        }

        private static long Pack(int x, int z) => ((long)x << 32) | (uint)z;

        private static void Unpack(long cell, out int x, out int z)
        {
            x = (int)(cell >> 32);
            z = (int)(cell & 0xFFFFFFFF);
        }

        private void AddToCell(long cell, ulong id)
        {
            if (!_cells.TryGetValue(cell, out var set))
            {
                set = new HashSet<ulong>();
                _cells[cell] = set;
            }

            set.Add(id);
        }

        private void RemoveFromCell(long cell, ulong id)
        {
            if (_cells.TryGetValue(cell, out var set))
            {
                set.Remove(id);
                if (set.Count == 0) _cells.Remove(cell);
            }
        }

        #endregion
    }

    /// <summary>Distance tiers for snapshot rate scaling. Sorted ascending by distance; the first tier a distance falls under wins.</summary>
    public sealed class NetworkLodProfile
    {
        public readonly struct Tier
        {
            public readonly float MaxDistance;
            public readonly float RateScale;

            public Tier(float maxDistance, float rateScale)
            {
                MaxDistance = maxDistance;
                RateScale = rateScale;
            }
        }

        public NetworkLodProfile(Tier[] tiers, float beyondScale)
        {
            Tiers = tiers ?? Array.Empty<Tier>();
            BeyondScale = beyondScale;
        }

        public Tier[] Tiers { get; }

        /// <summary>Scale past the last tier. 0 silences distant objects entirely (relevance decides whether they exist at all).</summary>
        public float BeyondScale { get; }

        public float ScaleFor(float distance)
        {
            for (int i = 0; i < Tiers.Length; i++)
            {
                if (distance <= Tiers[i].MaxDistance) return Tiers[i].RateScale;
            }

            return BeyondScale;
        }

        /// <summary>Full rate to 30 m, half to 60, a quarter to 120, a twentieth beyond.</summary>
        public static NetworkLodProfile Default => new(new[]
        {
            new Tier(30f, 1f),
            new Tier(60f, 0.5f),
            new Tier(120f, 0.25f),
        }, 0.05f);

        /// <summary>Everything at full rate.</summary>
        public static NetworkLodProfile Flat => new(Array.Empty<Tier>(), 1f);
    }
}
