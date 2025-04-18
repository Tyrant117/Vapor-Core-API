namespace Vapor.Inspector
{
    public interface IResizable
    {
        /// <summary>
        /// If true, the <see cref="ElementResizer"/> either allows resizing past parent view edge,
        /// or clamps the size at the edges of parent view
        /// </summary>
        bool CanResizePastParentBounds();

        /// <summary>
        /// Called when resize is started.
        /// </summary>
        void OnResized();

        /// <summary>
        ///     Called when resize is completed.
        /// </summary>
        void OnStartResize();
    }
}
