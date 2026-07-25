using System;
using Unity.Burst;

namespace Vapor.GameplayFramework
{
    public readonly struct GameplayChannelId : IEquatable<GameplayChannelId>
    {
        public readonly uint EventId;
        public readonly uint ChannelId;
        public GameplayChannelId(uint eventId, uint channelId)
        {
            EventId = eventId;
            ChannelId = channelId;
        }

        public override int GetHashCode() => HashCode.Combine(EventId, ChannelId);
        
        [BurstDiscard]
        public override bool Equals(object obj) => obj is GameplayChannelId other && Equals(other);

        public bool Equals(GameplayChannelId other) => EventId == other.EventId && ChannelId == other.ChannelId;

        public static bool operator ==(GameplayChannelId left, GameplayChannelId right) => left.Equals(right);
        public static bool operator !=(GameplayChannelId left, GameplayChannelId right) => !left.Equals(right);
    }
}