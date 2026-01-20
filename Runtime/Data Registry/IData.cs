using Vapor.Inspector;

namespace Vapor
{
    [TypeCache]
    public interface IData
    {
        string Name { get; }
        uint Key { get; }
    }

    public interface IScriptableData : IData
    {
        int GetOrder();
        void Register();
    }

    public interface IScriptableData<out T> : IScriptableData where T : IData
    {
        T Data { get; }

        void IScriptableData.Register()
        {
            GlobalDataRegistry.Register(Data);
        }
    }
}