using System;

namespace Vapor.GameplayFramework
{
    public class GameplayEvent
    {
        private readonly uint _eventId;

        public GameplayEvent(uint eventId)
        {
            _eventId = eventId;
        }

        public event Action<uint, IGameplayEventData> OnEventRaised;

        public void TriggerEvent(IGameplayEventData gameplayEventData)
        {
            OnEventRaised?.Invoke(_eventId, gameplayEventData);
        }
    }
}