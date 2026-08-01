namespace WidgetSample;

public static class TraceScopeExample
{
    // Caps the (mutual) recursion below so the demo can't run away into unbounded TraceScope/
    // Task allocation. (M49)
    private const int MaxDepth = 5;
    private static int _count;

    public static async Task TestTraceScope1()
    {
        var ident = $"########## {_count++} ##########";

        using (new TraceScope())
        {
            var times = ThreadSafeRandom.Next(1, 5);
            TraceScope.Trace($"{ident} About to call TestTraceScope2() {times} times");
            for (var i = 0; i < times; i++)
            {
                await Task.Delay(20);
                await TestTraceScope2(ident);
            }

            TraceScope.Trace($"{ident} Just called TestTraceScope2()");
        }
    }

    public static async Task TestTraceScope2(string ident, int depth = 0)
    {
        using (new TraceScope())
        {
            if (depth < MaxDepth && ThreadSafeRandom.Next(100) < 50)
            {
                await TestTraceScope2(ident, depth + 1);
            }

            var times = ThreadSafeRandom.Next(1, 3);
            TraceScope.Trace($"{ident} About to call TestTraceScope3() {times} times");
            for (var i = 0; i < times; i++)
            {
                await Task.Delay(20);
                await TestTraceScope3(ident, depth);
            }

            await Task.Delay(20);
            TraceScope.Trace($"{ident} Just called TestTraceScope3()");
        }
    }

    public static async Task TestTraceScope3(string ident, int depth = 0)
    {
        using (new TraceScope())
        {
            if (depth < MaxDepth && ThreadSafeRandom.Next(100) < 5)
            {
                await TestTraceScope2(ident, depth + 1);
            }

            TraceScope.Trace($"{ident} About to call TestTraceScope4()");
            await Task.Delay(20);
            await TestTraceScope4(ident);
            await Task.Delay(20);
            TraceScope.Trace($"{ident} Just called TestTraceScope4()");
        }
    }

    public static async Task TestTraceScope4(string ident)
    {
        using (new TraceScope())
        {
            await Task.Delay(20);
            TraceScope.Trace($"{ident} Your lucky random number is {ThreadSafeRandom.Next()}");
            await Task.Delay(20);
            TraceScope.Trace(
                $@"{ident} Here's a multiline trace message
which, as you can see,
has more than one line"
            );
        }
    }
}
