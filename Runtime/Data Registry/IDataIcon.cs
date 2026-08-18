using UnityEngine;

namespace Vapor
{
    /// <summary>
    /// Data that carries an icon.
    /// </summary>
    /// <remarks>
    /// An <see cref="AssetRef{T}"/> rather than a <c>Sprite</c>: reading a document full of icons
    /// loads none of them, so a headless server or a tool pays nothing for artwork it will never
    /// draw. The picker and the locator both come from the reference, so an implementer needs no
    /// attribute on the property to get either.
    /// </remarks>
    public interface IDataIcon
    {
        public AssetRef<Sprite> IconRef { get; set; }
    }
}
