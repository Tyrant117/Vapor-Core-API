using UnityEngine.Localization;
using Vapor.Inspector;

namespace Vapor
{
    public interface IDataExtension
    {
        IData Owner { get; set; }

        T GetOwner<T>() where T : IData { return (T) Owner; }
    }

    [TypeCache]
    public interface IData
    {
        string Name { get; }
        uint Key { get; }
    }

    public interface IDataIcon
    {
        public uint IconAddressableKey { get; set; }
    }

    public interface ILocalizedData : IData
    {
        public LocalizedString LocalizedName { get; set; }
        public LocalizedString LocalizedDescription { get; set; }
    }

    public interface IScriptableData : IData
    {
        int GetOrder();
        void Register();
        string GetPrefix();
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