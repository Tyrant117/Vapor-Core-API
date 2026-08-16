using System;
using Unity.Scripting.LifecycleManagement;
using Vapor.GameplayTags;
using Vapor.Networking;

namespace Vapor.GameplayFramework
{
    /// <summary>
    /// Puts the gameplay-event payloads and the gameplay tags on the wire: a polymorphic formatter for
    /// <see cref="IGameplayEventData"/> (any implementation travels behind its type tag), a factory
    /// for the generic <see cref="ValueGameplayEventData{T}"/>, and formatters for
    /// <see cref="GameplayTag"/> and <see cref="GameplayTagContainer"/>. Registered on every code
    /// load, so an rpc or a network variable can carry them without anyone asking.
    /// </summary>
    [NoAutoStaticsCleanup]
    public static partial class GameplayEventDataFormatters
    {
        [OnCodeInitializing]
        public static void Register()
        {
            NetworkFormatters.Register<GameplayTag>(
                (NetworkWriter writer, in GameplayTag value) => writer.WriteUInt32(value.Key),
                reader => new GameplayTag(reader.ReadUInt32()));

            NetworkFormatters.Register<GameplayTagContainer>(WriteContainer, ReadContainer);

            NetworkFormatters.RegisterGenericFactory(typeof(ValueGameplayEventData<>), closed =>
            {
                var valueType = closed.GetGenericArguments()[0];
                var formatterType = typeof(ValueGameplayEventDataFormatter<>).MakeGenericType(valueType);
                return (INetworkFormatter)Activator.CreateInstance(formatterType);
            });

            PolymorphicFormatters.Register<IGameplayEventData>();
        }

        private static void WriteContainer(NetworkWriter writer, in GameplayTagContainer value)
        {
            if (value == null)
            {
                writer.WriteVarUInt32(0);
                return;
            }

            var tags = value.GetTags();
            writer.WriteVarUInt32((uint)(tags.Count + 1));
            foreach (var tag in tags)
            {
                writer.WriteUInt32(tag.Key);
            }
        }

        private static GameplayTagContainer ReadContainer(NetworkReader reader)
        {
            uint count = reader.ReadVarUInt32();
            if (count == 0)
            {
                return null;
            }

            var container = new GameplayTagContainer();
            for (uint i = 1; i < count; i++)
            {
                container.AddTag(reader.ReadUInt32());
            }

            return container;
        }

        private sealed class ValueGameplayEventDataFormatter<T> : NetworkFormatter<ValueGameplayEventData<T>> where T : struct, IEquatable<T>
        {
            public override void Write(NetworkWriter writer, in ValueGameplayEventData<T> value) =>
                NetworkFormatters.Get<T>().Write(writer, value.Value);

            public override ValueGameplayEventData<T> Read(NetworkReader reader) =>
                new(NetworkFormatters.Get<T>().Read(reader));
        }
    }
}
