namespace Vapor
{
    public interface IDataExtension
    {
        IData Owner { get; set; }

        T GetOwner<T>() where T : IData => Owner is T owner ? owner : default;
    }
}