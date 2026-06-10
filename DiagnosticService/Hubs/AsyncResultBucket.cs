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

internal class AsyncCallException : ApplicationException
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
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (_results.TryGetValue(result.RequestId, out var completionSource))
        {
            // Try* (not Set*): a duplicate reply for the same request id — possible under
            // MaximumParallelInvocationsPerClient, or a late reply racing GetResult's finally — would
            // otherwise throw "already completed" out of the hub return method and fault the hub
            // invocation. The first reply wins; later ones no-op. (A7)
            if (result.IsSuccess)
            {
                completionSource.TrySetResult(returnValue);
            }
            else
            {
                completionSource.TrySetException(new AsyncCallException(result.Message, result.Detail));
            }
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
        if (requestId == null)
        {
            throw new ArgumentNullException(nameof(requestId));
        }

        var completionSource = _results.GetOrAdd(requestId, _ => new TaskCompletionSource<object>());
        try
        {
            Task awaitResult = await Task.WhenAny(Task.Delay(timeout, cancel), completionSource.Task);

            if (awaitResult == completionSource.Task)
            {
                // await (not .Result): a faulted task surfaces the original AsyncCallException
                // with its message/detail, instead of an AggregateException wrapping it.
                return (T) await completionSource.Task;
            }

            // Task.Delay won the race: either the timeout elapsed OR the caller cancelled (e.g. the
            // client disconnected, passing ConnectionAborted). Awaiting the delay task surfaces
            // cancellation as cancellation (TaskCanceledException) rather than a misleading
            // TimeoutException; only a genuine timeout falls through to throw below. (A5)
            await awaitResult;

            throw new TimeoutException($"{requestId} GetResult Timed out waiting");
        }
        finally
        {
            _results.TryRemove(requestId, out _);
        }
    }
}