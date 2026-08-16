using System;
using Vapor.Networking;

namespace Vapor.GameplayFramework
{
    /// <summary>One value of any network-serializable type.</summary>
    /// <remarks>
    /// Generic, so the source generator cannot emit its formatter; <see cref="GameplayEventDataFormatters"/>
    /// registers a factory that builds one per closed type from the value type's own formatter.
    /// </remarks>
    public struct ValueGameplayEventData<T> : IGameplayEventData, IEquatable<ValueGameplayEventData<T>> where T : struct, IEquatable<T>
    {
        public T Value;

        public ValueGameplayEventData(T value) => Value = value;

        public bool Equals(ValueGameplayEventData<T> other) => Value.Equals(other.Value);
        public override bool Equals(object obj) => obj is ValueGameplayEventData<T> other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
    }
}
