using AwesomeAssertions;
using DiagnosticExplorer.Props;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     RateCounter's instance side drives a global timer and UtcNow, but its static
///     GetRates is the pure ring-buffer read behind per-second rate history. These
///     tests pin that extraction without touching the time-dependent machinery.
/// </summary>
public class RateCounterTests
{
    /// <summary>
    ///     GetRates reads backwards from the current index, newest first, wrapping around
    ///     the ring buffer. Parameterized to cover a simple read and the request-clamped-to-
    ///     available case, which is the behaviour the rate graph depends on.
    /// </summary>
    [Theory]
    [InlineData(3, 5, new[] { 50, 40, 30 })] // newest (index 4) first, walking back
    [InlineData(10, 2, new[] { 20, 10 })] // seconds clamped to currentIndex
    public void GetRates_WithinFilledBuffer_ReturnsNewestFirst(
        int seconds,
        int currentIndex,
        int[] expected
    )
    {
        var values = new[] { 10, 20, 30, 40, 50 };

        var rates = RateCounter.GetRates(seconds, currentIndex, values);

        rates.Should().Equal(expected);
    }

    /// <summary>
    ///     Before any sample has been recorded (currentIndex 0) GetRates must return an
    ///     empty array rather than reading stale or out-of-range slots.
    /// </summary>
    [Fact]
    public void GetRates_WithNoSamplesYet_ReturnsEmpty()
    {
        var values = new[] { 10, 20, 30, 40, 50 };

        var rates = RateCounter.GetRates(3, 0, values);

        rates.Should().BeEmpty();
    }

    /// <summary>
    ///     With more increments than buffer slots the index keeps climbing and the read
    ///     must wrap modulo the buffer length, so the newest values still come back in order.
    /// </summary>
    [Fact]
    public void GetRates_WhenIndexHasWrapped_ReadsModuloBufferLength()
    {
        var values = new[] { 10, 20, 30, 40, 50 };

        // currentIndex 7 over a length-5 buffer: read slots 6%5=1, 5%5=0, 4%5=4.
        var rates = RateCounter.GetRates(3, 7, values);

        rates.Should().Equal(20, 10, 50);
    }

    /// <summary>
    ///     When both the requested seconds and the wrapped index exceed the buffer length, the result
    ///     is clamped to the buffer length — without this clamp GetRates would walk back past the ring
    ///     size and re-report the same slots as if they were distinct samples, fabricating history. (M21)
    /// </summary>
    [Fact]
    public void GetRates_WhenSecondsAndIndexExceedBufferLength_ClampsToBufferLength()
    {
        var values = new[] { 10, 20, 30, 40, 50 }; // length 5

        // seconds 8 and currentIndex 9 both exceed the 5-slot buffer; clamp to 5.
        var rates = RateCounter.GetRates(8, 9, values);

        // newest-first from index 9: slots 8%5=3, 7%5=2, 6%5=1, 5%5=0, 4%5=4.
        rates.Should().Equal(40, 30, 20, 10, 50);
    }

    /// <summary>
    ///     The ctor requires a positive averaging window. Zero would give zero-length buffers (a
    ///     swallowed DivideByZeroException on the timer thread, so the counter silently never advances)
    ///     and a negative value an OverflowException at allocation, so both fail fast instead. (M20)
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_WithNonPositiveSecondsAverage_Throws(int secondsAverage)
    {
        var act = () => new RateCounter(secondsAverage);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("secondsAverage");
    }
}
