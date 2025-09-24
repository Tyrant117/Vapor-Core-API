using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
#if VAPOR_NETCODE
using Unity.Netcode;
#endif

namespace Vapor.Unsafe
{
    public static class FastBufferWriterExtensions
    {
#if VAPOR_NETCODE
        public static unsafe NativeArray<byte> AsNativeArray(this FastBufferWriter writer)
        {
            byte* ptr = writer.GetUnsafePtr();

            var array = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(
                ptr,
                writer.Length,
                Allocator.None // no ownership — memory managed by FastBufferWriter
            );

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(
                ref array,
                AtomicSafetyHandle.Create()
            );
#endif

            return array;
        }
#endif
    }
}
