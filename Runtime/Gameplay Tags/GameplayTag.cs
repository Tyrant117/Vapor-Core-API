using System;
using Unity.Burst;
using Unity.Netcode;
using Vapor.Inspector;
using Vapor.Unsafe;

namespace Vapor.GameplayTags
{
    [Serializable]
    public struct GameplayTag : IEquatable<GameplayTag>, IEquatable<uint>, INetworkSerializable
    {
        public static implicit operator GameplayTag(uint value) => new(value);
        public static implicit operator GameplayTag(string value) => new(value);
        public static implicit operator uint(GameplayTag gameplayTag) => gameplayTag.Key;
        public static implicit operator string(GameplayTag gameplayTag) => gameplayTag.GetName();
        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Key == right.Key;
        public static bool operator !=(GameplayTag left, GameplayTag right) => left.Key != right.Key;
        
        public static GameplayTag None() => new(NoneId);
        public static uint NoneId => 0;
        public bool IsNone() => Key == 0;

        public uint Key;

        public GameplayTag(uint key) => Key = key;
        public GameplayTag(string name) => Key = name.Hash32();

        public bool HasTag(GameplayTag tag, bool exactMatch)
        {
            if (exactMatch)
            {
                return tag.Key == Key;
            }

            return GameplayTagTree.HasParentTag(Key, tag.Key);
        }

        public string GetName() => GameplayTagTree.GetName(Key);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter => serializer.SerializeValue(ref Key);

        public bool Equals(GameplayTag other) => Key == other.Key;

        public bool Equals(uint other) => Key == other;
        
        [BurstDiscard]
        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);

        public override int GetHashCode() => (int)Key;

        public override string ToString() => GetName();

        public static GameplayTag CreateNewTag(string newTagName)
        {
            GameplayTagTree.InsertTag(newTagName);
            return new GameplayTag(newTagName);
        }
    }
}