namespace Vapor.GameplayFramework
{
    /// <summary>
    /// The payload of a gameplay event: what happened, carried to whoever listens and across the
    /// network. A marker interface — implementations are plain <c>[NetworkSerializable]</c> structs
    /// (or register a formatter by hand), and <see cref="GameplayEventDataFormatters"/> writes any of
    /// them behind a type tag so an <c>IGameplayEventData</c>-typed rpc argument or field round-trips
    /// as its concrete type.
    /// </summary>
    public interface IGameplayEventData
    {
    }
}
