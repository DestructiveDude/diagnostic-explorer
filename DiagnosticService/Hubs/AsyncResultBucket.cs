using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiagnosticExplorer;

namespace Diagnostics.Service.Common.Hubs;

class AsyncCallException : ApplicationException
{
    public AsyncCallException()
    {
    }

    public AsyncCallException(string? message, string? detail) : base(message)
    {
        Detail = detail;
    }

    public string? Detail { get; set; }

    public override string ToString()
    {
        return Message + Environment.NewLine + Detail;
    }
}

public class AsyncResultBucket
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _results = new();

    public void SetResult(RpcResult result, object returnValue)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        if (_results.TryGetValue(result.RequestId, out var completionSource))
        {
            if (result.IsSuccess)
                completionSource.SetResult(returnValue);
            else
                completionSource.SetException(new AsyncCallException(result.Message, result.Detail));
        }
        else
        {
            // No waiter for this request id — the caller already timed out/cancelled, or this is a
            // duplicate reply. Previously dropped silently; log it so post-timeout late replies are
            // diagnosable rather than invisible.
            Debug.WriteLine($"AsyncResultBucket: no pending request for {result.RequestId}; result discarded");
        }
    }

    public async Task<T> GetResult<T>(string requestId, TimeSpan timeout, CancellationToken cancel)
    {
        if (requestId == null) throw new ArgumentNullException(nameof(requestId));

        var completionSource = _results.GetOrAdd(requestId, _ => new TaskCompletionSource<object>());
        try
        {
            Task awaitResult = await Task.WhenAny(Task.Delay(timeout, cancel), completionSource.Task);

            if (awaitResult == completionSource.Task)
                // await (not .Result): a faulted task surfaces the original AsyncCallException
                // with its message/detail, instead of an AggregateException wrapping it.
                return (T) await completionSource.Task;

            await awaitResult;

            throw new TimeoutException($"{requestId} GetResult Timed out waiting");
        }
        finally
        {
            _results.TryRemove(requestId, out _);
        }
    }
}