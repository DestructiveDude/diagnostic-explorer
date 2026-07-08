using System;
using System.Threading;
using log4net.Appender;
using log4net.Core;
using log4net.Util;

namespace DiagnosticExplorer.Log4Net.Util;


public class MultiErrorHandler : IErrorHandler
{
    // Copy-on-write: AddHandler runs at appender-configuration time, Error iterates at
    // runtime on logging threads. A plain List mutated while iterated throws
    // InvalidOperationException; swapping an immutable snapshot under a lock avoids it.
    private volatile IErrorHandler[] _handlers;
    private readonly object _sync = new object();

    public MultiErrorHandler()
    {
        _handlers = new IErrorHandler[] { new OnlyOnceErrorHandler() };
    }

    public void AddHandler(IErrorHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_sync)
        {
            IErrorHandler[] updated = new IErrorHandler[_handlers.Length + 1];
            Array.Copy(_handlers, updated, _handlers.Length);
            updated[_handlers.Length] = handler;
            _handlers = updated;
        }
    }

    public void Error(string message)
    {
        foreach (IErrorHandler handler in _handlers)
        {
            handler.Error(message);
        }
    }

    public void Error(string message, Exception e)
    {
        foreach (IErrorHandler handler in _handlers)
        {
            handler.Error(message, e);
        }
    }

    public void Error(string message, Exception e, ErrorCode errorCode)
    {
        foreach (IErrorHandler handler in _handlers)
        {
            handler.Error(message, e, errorCode);
        }
    }

    public static void SetErrorHandler(AppenderSkeleton appender, AppenderProxyErrorHandler handler)
    {
        IErrorHandler existingHandler = appender.ErrorHandler;

        if (existingHandler is not MultiErrorHandler multiHandler)
        {
            multiHandler = new MultiErrorHandler();
            appender.ErrorHandler = multiHandler;
            if (existingHandler != null)
            {
                multiHandler.AddHandler(existingHandler);
            }
        }
        multiHandler.AddHandler(handler);
    }
}


/// <summary>
/// This object records whether an error has been recorded
/// </summary>
public class AppenderProxyErrorHandler : IErrorHandler
{
    // Per-thread error state. FireAppendAction enables on the appending thread, the wrapped
    // appender raises Error synchronously on that same thread, then we read it back. Holding
    // the enabled flag + captured error per-thread means concurrent appends through the same
    // proxy (e.g. ForwardingAppender's Parallel.ForEach) can't stomp each other's tracking,
    // and the previous Thread.CurrentThread == _enabledThread gate (which dropped any error
    // raised on a different thread) is gone.
    private sealed class ErrorState
    {
        public bool Enabled;
        public bool HasError;
        public string Message;
    }

    private readonly ThreadLocal<ErrorState> _state = new ThreadLocal<ErrorState>(() => new ErrorState());

    public bool HasError => _state.Value.HasError;

    public string Message => _state.Value.Message;

    public void EnableForCurrentThread()
    {
        _state.Value.Enabled = true;
    }

    public void Disable()
    {
        _state.Value.Enabled = false;
    }

    public void Error(string message)
    {
        Record(message, null);
    }

    public void Error(string message, Exception e)
    {
        Record(message, e);
    }

    public void Error(string message, Exception e, ErrorCode errorCode)
    {
        Record(message, e);
    }

    private void Record(string message, Exception exception)
    {
        ErrorState state = _state.Value;
        if (!state.Enabled)
        {
            return;
        }

        state.Message = exception?.Message ?? message;
        state.HasError = true;
    }

    internal void ResetError()
    {
        ErrorState state = _state.Value;
        state.HasError = false;
        state.Message = null;
    }
}
