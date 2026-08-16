using System;

namespace Vapor
{
    /// <summary>
    /// Lets an authored object decide which extension types may be added to it. The editor's
    /// extension picker consults it (in addition to <see cref="DataExtensionFilterAttribute"/>) so a
    /// controller's stack offers controller components and a pawn's offers pawn components; the
    /// runtime enforces the same rule on <c>AddComponent</c>.
    /// </summary>
    public interface IExtensionHostFilter
    {
        bool AcceptsExtension(Type extensionType);
    }
}
