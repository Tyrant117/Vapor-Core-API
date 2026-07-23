using Vapor.Inspector;

namespace Vapor
{
    [TypeCache]
    public interface IData
    {
        string Name { get; }
        uint Key { get; }
    }
}