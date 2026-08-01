namespace WidgetSample;

/// <summary>
///     Thread-safe random source for the sample.
/// </summary>
internal static class ThreadSafeRandom
{
    private static readonly System.Security.Cryptography.RandomNumberGenerator _generator =
        System.Security.Cryptography.RandomNumberGenerator.Create();

    public static int Next()
    {
        return Next(int.MaxValue);
    }

    public static int Next(int maxValue)
    {
        if (maxValue < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue));
        }

        return Next(0, maxValue);
    }

    public static int Next(int minValue, int maxValue)
    {
        if (minValue > maxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(minValue));
        }

        long range = (long)maxValue - minValue;
        if (range == 0)
        {
            return minValue;
        }

        // ponytail: modulo bias is immaterial for this UI sample; use rejection sampling if distribution quality matters.
        return (int)(minValue + (long)(NextUInt32() % (ulong)range));
    }

    private static uint NextUInt32()
    {
        byte[] bytes = new byte[sizeof(uint)];
        _generator.GetBytes(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }
}
