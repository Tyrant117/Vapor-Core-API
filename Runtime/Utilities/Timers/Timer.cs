using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Vapor
{
    public class Timer : ITimer
    {
        public double StartTime { get; private set; } = Time.timeAsDouble;
        public double Elapsed => Time.timeAsDouble - StartTime;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Pause(float pauseTime) => StartTime += pauseTime;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Reset() => StartTime = Time.timeAsDouble;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsOver(float duration) => Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsOver(double duration) => Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Timer timer, float duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Timer timer, float duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Timer timer, float duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Timer timer, float duration) => timer.Elapsed <= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(Timer timer, double duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(Timer timer, double duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(Timer timer, double duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(Timer timer, double duration) => timer.Elapsed <= duration;
    }
}
