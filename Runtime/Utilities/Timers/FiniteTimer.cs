using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Vapor
{
    public readonly struct FiniteTimer : ITimer
    {
        public double StartTime { get; }
        public double Elapsed => Time.timeAsDouble - StartTime;

        public FiniteTimer(double startTime)
        {
            StartTime = startTime;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FiniteTimer Pause(float pauseTime) => new(StartTime + pauseTime);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FiniteTimer Reset() => new(Time.timeAsDouble);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FiniteTimer timer, float duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FiniteTimer timer, float duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FiniteTimer timer, float duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FiniteTimer timer, float duration) => timer.Elapsed <= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FiniteTimer timer, double duration) => timer.Elapsed > duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FiniteTimer timer, double duration) => timer.Elapsed < duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FiniteTimer timer, double duration) => timer.Elapsed >= duration;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FiniteTimer timer, double duration) => timer.Elapsed <= duration;
    }
}
