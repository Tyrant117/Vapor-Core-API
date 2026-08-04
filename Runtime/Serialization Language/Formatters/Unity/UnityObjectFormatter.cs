using Object = UnityEngine.Object;

namespace Vapor.Serialization
{
    /// <summary>
    /// Writes any <see cref="Object"/> as an <c>@id</c> reference rather than by value.
    /// </summary>
    /// <remarks>
    /// Serializing a Unity object by value would either duplicate an asset or recurse through the
    /// whole scene graph. The id comes from <see cref="VslContext.References"/>, which decides what
    /// an id means — by default an <c>EntityId</c>, valid for the current session.
    /// </remarks>
    public sealed class UnityObjectFormatter<T> : VslFormatter<T> where T : Object
    {
        public static readonly UnityObjectFormatter<T> Instance = new UnityObjectFormatter<T>();

        public override bool IsScalar => true;

        public override void Write(ref VslWriter writer, in T value, VslContext context)
        {
            // Compared as Object, not as T: the lifetime-aware == overload is only picked up on the
            // concrete type, so a destroyed object would otherwise look like a live reference.
            Object obj = value;
            var resolver = context.References;

            if (obj == null || resolver == null || !resolver.TryGetReference(obj, out var reference))
            {
                writer.WriteNullReference();
                return;
            }

            writer.WriteReference(reference);
        }

        public override T Read(ref VslReader reader, VslContext context)
        {
            if (!reader.TryReadReference(out var reference))
            {
                return null;
            }

            var resolver = context.References;
            if (resolver != null && resolver.TryResolve(reference, typeof(T), out var obj) && obj is T typed)
            {
                return typed;
            }

            if (context.Options.Strict)
            {
                throw new VslException($"Could not resolve reference {reference} to a {typeof(T).Name}.");
            }

            return null;
        }
    }
}
