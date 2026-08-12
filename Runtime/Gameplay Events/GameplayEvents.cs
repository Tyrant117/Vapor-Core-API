using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Scripting.LifecycleManagement;
using UnityEngine;
using Vapor.Keys;
using Object = UnityEngine.Object;

namespace Vapor.GameplayFramework
{
    // All four tables are cleared, where the hand-written reset this replaces cleared only two. With
    // domain reload on they are reconstructed either way; the difference was only ever visible with
    // it off, where the channel tables leaked subscribers across play sessions.
    [AutoStaticsCleanup]
    public static partial class GameplayEvents
    {
        private static readonly Dictionary<uint, GameplayEvent> s_Events = new();
        private static readonly Dictionary<GameplayChannelId, GameplayEvent> s_ChannelEvents = new();
        private static readonly Dictionary<EntityId, Dictionary<uint, GameplayEvent>> s_EntityEvents = new();
        private static readonly Dictionary<EntityId, Dictionary<GameplayChannelId, GameplayEvent>> s_EntityChannelEvents = new();

        public static void Subscribe([DataKey("Event")] uint eventId, Action<uint, IGameplayEventData> callback)
        {
            if (s_Events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
            {
                gameplayEvent.OnEventRaised += callback;
            }
            else
            {
                var evt = new GameplayEvent(eventId);
                evt.OnEventRaised += callback;
                s_Events.Add(eventId, evt);
            }
        }

        // public static void Subscribe(GameplayChannelId channelId, Action<uint, IGameplayEventData> callback)
        // {
        //     if (s_ChannelEvents.TryGetValue(channelId, out GameplayEvent gameplayEvent))
        //     {
        //         gameplayEvent.OnEventRaised += callback;
        //     }
        //     else
        //     {
        //         var evt = new GameplayEvent(channelId.EventId);
        //         evt.OnEventRaised += callback;
        //         s_ChannelEvents.Add(channelId, evt);
        //     }
        // }

        public static void SubscribeOnTarget(Object entity, [DataKey("Event")] uint eventId, Action<uint, IGameplayEventData> callback)
        {
            if (s_EntityEvents.TryGetValue(entity.GetEntityId(), out var events))
            {
                if (events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
                {
                    gameplayEvent.OnEventRaised += callback;
                }
                else
                {
                    var evt = new GameplayEvent(eventId);
                    evt.OnEventRaised += callback;
                    events.Add(eventId, evt);
                }
            }
            else
            {
                var evt = new GameplayEvent(eventId);
                evt.OnEventRaised += callback;
                Dictionary<uint, GameplayEvent> newEvents = new() { { eventId, evt } };
                s_EntityEvents.Add(entity.GetEntityId(), newEvents);
            }
        }
        
        // public static void Subscribe(Object entity, GameplayChannelId channelId, Action<uint, IGameplayEventData> callback)
        // {
        //     if (s_EntityChannelEvents.TryGetValue(entity.GetEntityId(), out var events))
        //     {
        //         if (events.TryGetValue(channelId, out GameplayEvent gameplayEvent))
        //         {
        //             gameplayEvent.OnEventRaised += callback;
        //         }
        //         else
        //         {
        //             var evt = new GameplayEvent(channelId.EventId);
        //             evt.OnEventRaised += callback;
        //             events.Add(channelId, evt);
        //         }
        //     }
        //     else
        //     {
        //         var evt = new GameplayEvent(channelId.EventId);
        //         evt.OnEventRaised += callback;
        //         Dictionary<GameplayChannelId, GameplayEvent> newEvents = new() { { channelId, evt } };
        //         s_EntityChannelEvents.Add(entity.GetEntityId(), newEvents);
        //     }
        // }

        public static void Unsubscribe([DataKey("Event")] uint eventId, Action<uint, IGameplayEventData> callback)
        {
            if (s_Events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
            {
                gameplayEvent.OnEventRaised -= callback;
            }
        }
        
        // public static void Unsubscribe(GameplayChannelId channelId, Action<uint, IGameplayEventData> callback)
        // {
        //     if (s_ChannelEvents.TryGetValue(channelId, out GameplayEvent gameplayEvent))
        //     {
        //         gameplayEvent.OnEventRaised -= callback;
        //     }
        // }

        public static void UnsubscribeFromTarget(Object entity, [DataKey("Event")] uint eventId, Action<uint, IGameplayEventData> callback)
        {
            if (!s_EntityEvents.TryGetValue(entity.GetEntityId(), out var events))
            {
                return;
            }

            if (events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
            {
                gameplayEvent.OnEventRaised -= callback;
            }
        }
        
        // public static void Unsubscribe(Object entity, GameplayChannelId channelId, Action<uint, IGameplayEventData> callback)
        // {
        //     if (!s_EntityChannelEvents.TryGetValue(entity.GetEntityId(), out var events))
        //     {
        //         return;
        //     }
        //
        //     if (events.TryGetValue(channelId, out GameplayEvent gameplayEvent))
        //     {
        //         gameplayEvent.OnEventRaised -= callback;
        //     }
        // }

        public static void TriggerEvent([DataKey("Event")] uint eventId, IGameplayEventData gameplayEventData)
        {
            if (s_Events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
            {
                gameplayEvent.TriggerEvent(gameplayEventData);
            }
        }

        public static void TriggerClientEvent([DataKey("Event")] uint eventId, IGameplayEventData gameplayEventData)
        {
            if (!NetworkManager.Singleton.IsClient)
            {
                return;
            }
            
            if (s_Events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
            {
                gameplayEvent.TriggerEvent(gameplayEventData);
            }
        }
        
        public static void TriggerServerEvent([DataKey("Event")] uint eventId, IGameplayEventData gameplayEventData)
        {
            if (!NetworkManager.Singleton.IsServer)
            {
                return;
            }

            if (s_Events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
            {
                gameplayEvent.TriggerEvent(gameplayEventData);
            }
        }
        
        // public static void TriggerEvent(GameplayChannelId channelId, IGameplayEventData gameplayEventData)
        // {
        //     if (s_ChannelEvents.TryGetValue(channelId, out GameplayEvent gameplayEvent))
        //     {
        //         gameplayEvent.TriggerEvent(gameplayEventData);
        //     }
        // }

        public static void TriggerEventOnTarget(Object entity, [DataKey("Event")] uint eventId, IGameplayEventData gameplayEventData)
        {
            if (!s_EntityEvents.TryGetValue(entity.GetEntityId(), out var events))
            {
                return;
            }

            if (events.TryGetValue(eventId, out GameplayEvent gameplayEvent))
            {
                gameplayEvent.TriggerEvent(gameplayEventData);
            }
        }
        
        // public static void TriggerEvent(Object entity, GameplayChannelId channelId, IGameplayEventData gameplayEventData)
        // {
        //     if (!s_EntityChannelEvents.TryGetValue(entity.GetEntityId(), out var events))
        //     {
        //         return;
        //     }
        //
        //     if (events.TryGetValue(channelId, out GameplayEvent gameplayEvent))
        //     {
        //         gameplayEvent.TriggerEvent(gameplayEventData);
        //     }
        // }
    }
}
