namespace Vapor
{
    public interface IScriptableData<out T> : IScriptableData where T : IData
    {
        T Data { get; }

        void IScriptableData.Register()
        {
            GlobalDataRegistry.Register(Data);
        }
    }

    public interface IScriptableData : IData
    {
        int GetOrder();
        void Register();
        string GetPrefix();
    }
}