using System.Text;
using Microsoft.CodeAnalysis;

namespace Vapor.Network.SourceGenerator
{
    /// <summary>
    /// Computes the rpc id that goes on the wire.
    /// </summary>
    /// <remarks>
    /// Nothing persists an rpc id — it is baked into the send call and the registration by this
    /// generator on every build, and the receiving peer looks it up in a table it built the same way.
    /// So the only requirement is that every peer in a session computes the same value from the same
    /// source. The scheme (xxHash32 over "{module} / {method full name}", with names spelled the way
    /// Mono.Cecil spells them) is carried over from the IL weaver this replaced, which keeps the
    /// documented invariant on VaporNetworkObject true and means a build that mixes the two agrees.
    /// </remarks>
    internal static class RpcHash
    {
        public static uint Compute(IMethodSymbol method, string assemblyName) =>
            Hash32(Encoding.UTF8.GetBytes(BuildKey(method, assemblyName)));

        /// <summary>
        /// The exact string that gets hashed. Separated out so a test can hold it against what
        /// Mono.Cecil reports for the same method in the compiled assembly.
        /// </summary>
        public static string BuildKey(IMethodSymbol method, string assemblyName)
        {
            var builder = new StringBuilder();
            builder.Append(assemblyName).Append(".dll / ");
            AppendMethodName(builder, method);
            return builder.ToString();
        }

        /// <summary>
        /// Spells the method the way <c>Mono.Cecil</c>'s <c>MethodReference.FullName</c> does:
        /// <c>System.Void Namespace.Type::Method(System.Int32,System.String)</c>.
        /// </summary>
        private static void AppendMethodName(StringBuilder builder, IMethodSymbol method)
        {
            AppendTypeName(builder, method.ReturnType);
            builder.Append(' ');
            AppendTypeName(builder, method.ContainingType);
            builder.Append("::").Append(method.Name).Append('(');

            for (int i = 0; i < method.Parameters.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendTypeName(builder, method.Parameters[i].Type);
            }

            builder.Append(')');
        }

        /// <summary>
        /// Cecil's <c>TypeReference.FullName</c>: metadata names (so generics keep their `arity
        /// suffix), nested types separated by '/', and generic instances closed with angle brackets.
        /// </summary>
        private static void AppendTypeName(StringBuilder builder, ITypeSymbol type)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    AppendTypeName(builder, array.ElementType);

                    // Cecil writes a vector as "[]" but spells every other rank out with its bounds:
                    // float[,] is "System.Single[0...,0...]".
                    if (array.IsSZArray)
                    {
                        builder.Append("[]");
                        return;
                    }

                    builder.Append('[');
                    for (int i = 0; i < array.Rank; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(',');
                        }

                        builder.Append("0...");
                    }

                    builder.Append(']');
                    return;

                case IPointerTypeSymbol pointer:
                    AppendTypeName(builder, pointer.PointedAtType);
                    builder.Append('*');
                    return;

                case INamedTypeSymbol named:
                    if (named.ContainingType != null)
                    {
                        AppendTypeName(builder, named.ContainingType);
                        builder.Append('/');
                    }
                    else if (named.ContainingNamespace != null && !named.ContainingNamespace.IsGlobalNamespace)
                    {
                        builder.Append(named.ContainingNamespace.ToDisplayString()).Append('.');
                    }

                    builder.Append(named.MetadataName);

                    if (named.IsGenericType && !named.TypeArguments.IsDefaultOrEmpty &&
                        !SymbolEqualityComparer.Default.Equals(named, named.ConstructedFrom))
                    {
                        builder.Append('<');
                        for (int i = 0; i < named.TypeArguments.Length; i++)
                        {
                            if (i > 0)
                            {
                                builder.Append(',');
                            }

                            AppendTypeName(builder, named.TypeArguments[i]);
                        }

                        builder.Append('>');
                    }

                    return;

                default:
                    builder.Append(type.Name);
                    return;
            }
        }

        /// <summary>
        /// xxHash32, seed 0 — the same algorithm as <c>Vapor.Unsafe.XxHash</c> and Unity Netcode's own
        /// rpc ids.
        /// </summary>
        private static uint Hash32(byte[] buffer)
        {
            const uint prime1 = 2654435761u;
            const uint prime2 = 2246822519u;
            const uint prime3 = 3266489917u;
            const uint prime4 = 0668265263u;
            const uint prime5 = 0374761393u;

            unchecked
            {
                int length = buffer.Length;
                int index = 0;

                uint hash = prime5;

                if (length >= 16)
                {
                    uint val0 = prime1 + prime2;
                    uint val1 = prime2;
                    uint val2 = 0;
                    uint val3 = (uint)-(int)prime1;

                    int count = length >> 4;
                    for (int i = 0; i < count; i++)
                    {
                        val0 += ReadUInt32(buffer, index + 0) * prime2;
                        val0 = Rol(val0, 13);
                        val0 *= prime1;

                        val1 += ReadUInt32(buffer, index + 4) * prime2;
                        val1 = Rol(val1, 13);
                        val1 *= prime1;

                        val2 += ReadUInt32(buffer, index + 8) * prime2;
                        val2 = Rol(val2, 13);
                        val2 *= prime1;

                        val3 += ReadUInt32(buffer, index + 12) * prime2;
                        val3 = Rol(val3, 13);
                        val3 *= prime1;

                        index += 16;
                    }

                    hash = Rol(val0, 1) + Rol(val1, 7) + Rol(val2, 12) + Rol(val3, 18);
                }

                hash += (uint)length;

                while (length - index >= 4)
                {
                    hash += ReadUInt32(buffer, index) * prime3;
                    hash = Rol(hash, 17) * prime4;
                    index += 4;
                }

                while (index < length)
                {
                    hash += buffer[index] * prime5;
                    hash = Rol(hash, 11) * prime1;
                    index++;
                }

                hash ^= hash >> 15;
                hash *= prime2;
                hash ^= hash >> 13;
                hash *= prime3;
                hash ^= hash >> 16;

                return hash;
            }
        }

        private static uint Rol(uint value, int count) => (value << count) | (value >> (32 - count));

        private static uint ReadUInt32(byte[] buffer, int index) =>
            buffer[index] | ((uint)buffer[index + 1] << 8) | ((uint)buffer[index + 2] << 16) | ((uint)buffer[index + 3] << 24);
    }
}
