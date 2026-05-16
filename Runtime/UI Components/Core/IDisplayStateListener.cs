namespace Vapor
{
    public interface IDisplayStateListener
    {
        void OnOpened();
        void OnClosed();
    }
}