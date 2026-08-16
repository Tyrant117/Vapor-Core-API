using System.Diagnostics;

namespace Vapor.Networking
{
    /// <summary>
    /// The clock the networking layer reads. Injected so a test can step time by hand and a
    /// deterministic replay is possible; the engine binding supplies real time in play.
    /// </summary>
    public interface INetworkClock
    {
        /// <summary>Monotonic seconds. Only differences are meaningful.</summary>
        double Now { get; }
    }

    /// <summary>A clock that only moves when told to. Tests and deterministic simulations.</summary>
    public sealed class ManualClock : INetworkClock
    {
        public double Now { get; private set; }

        public ManualClock(double start = 0) => Now = start;

        public void Advance(double seconds)
        {
            if (seconds < 0) seconds = 0;
            Now += seconds;
        }

        public void Set(double now) => Now = now;
    }

    /// <summary>Wall-clock time from a <see cref="Stopwatch"/>. Engine-independent, so this assembly can use it anywhere.</summary>
    public sealed class StopwatchClock : INetworkClock
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public double Now => _stopwatch.Elapsed.TotalSeconds;
    }
}
