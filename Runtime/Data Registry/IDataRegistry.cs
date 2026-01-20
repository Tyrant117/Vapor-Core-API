namespace Vapor
{
    [TypeCache]
    public interface IDataRegistry
    {
        /// <summary>
        /// The order this registry is built in. Lower numbers are built first.
        /// </summary>
        /// <remarks>If using the Gameplay Ability System: Build Attributes -> Effects -> Abilities</remarks>
        int GetOrder();
        void BuildRegistry();
    }
}