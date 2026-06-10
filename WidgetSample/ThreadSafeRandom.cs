using System;

namespace WidgetSample;

/// <summary>
/// Thread-safe random source for the sample. The sample previously shared a single
/// <see cref="Random"/> instance across the UI thread, the background timer threads and
/// the diagnostic RPC threads. <see cref="Random"/> is not thread-safe, and concurrent
/// calls can corrupt its internal state (and return 0 indefinitely). On .NET 6+ this
/// delegates to the shared, lock-free <see cref="Random.Shared"/>; on net48 (where that
/// API does not exist) it falls back to a per-thread instance.
/// </summary>
internal static class ThreadSafeRandom
{
#if NET48
    [ThreadStatic] private static Random _local;

    // Seed per thread from a Guid so threads created close together don't share a
    // time-based seed and therefore the same sequence.
    private static Random Instance => _local ??= new Random(Guid.NewGuid().GetHashCode());
#else
    private static Random Instance => Random.Shared;
#endif

    public static int Next() => Instance.Next();

    public static int Next(int maxValue) => Instance.Next(maxValue);

    public static int Next(int minValue, int maxValue) => Instance.Next(minValue, maxValue);
}
