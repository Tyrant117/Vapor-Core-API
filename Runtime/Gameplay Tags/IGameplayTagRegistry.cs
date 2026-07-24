namespace Vapor.GameplayTags
{
    public interface IGameplayTagRegistry : IDataRegistry
    {
        int IDataRegistry.GetOrder() => -500;
    }
}