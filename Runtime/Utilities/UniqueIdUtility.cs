using System;
using UnityEngine;

namespace Vapor
{
    public static class UniqueIdUtility
    {
        private static long _counter = 0;

        public static ulong Generate()
        {
            ulong time = (ulong)DateTimeOffset.UtcNow.Ticks; // 100-nanosecond intervals
            ulong count = (ulong)(System.Threading.Interlocked.Increment(ref _counter) & 0xFFFF); // 16 bits of entropy
            return (time << 16) | count;
        }
    }
}
