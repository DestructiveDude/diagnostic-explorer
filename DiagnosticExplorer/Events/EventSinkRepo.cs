using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DiagnosticExplorer.Events;

public class EventSinkRepo : IDisposable
{

    private readonly List<EventSinkStream> _sinkStreams = [];
    private readonly ReaderWriterLockSlim _eventStreamLock = new(LockRecursionPolicy.NoRecursion);
    // Keyed by the (name, category) tuple, not a "{name}.{category}" string: the latter collided
    // distinct sinks, e.g. ("a.b","c") and ("a","b.c") both mapped to "a.b.c".
    private readonly ConcurrentDictionary<(string Name, string Category), EventSink> _sinks = new();

    public static EventSinkRepo Default { get; } = new();

    public EventSink GetSink(string name, string category)
    {
        return _sinks.GetOrAdd((name, category), key => new EventSink(this, key.Name, key.Category));
    }

    public void LogEvent(SystemEvent evt)
    {
        GetSink(evt.SinkName, evt.SinkCategory).LogEvent(evt);
    }

    public void LogEvents(SystemEvent[] evts)
    {
        foreach (SystemEvent evt in evts)
        {
            LogEvent(evt);
        }
    }

    private bool _disposed;

    public EventSinkStream CreateSinkStream(TimeSpan buffer, int bufferSize)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EventSinkRepo));
        }

        _eventStreamLock.EnterWriteLock();
        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EventSinkRepo));
            }

            EventSinkStream stream = new(_sinks.Values.SelectMany(sink => sink.Events).ToArray(), buffer, bufferSize);
            _sinkStreams.Add(stream);
            stream.Disposed += HandleEventStreamDisposed;
            return stream;
        }
        finally
        {
            _eventStreamLock.ExitWriteLock();
        }
    }

    public SystemEvent[] GetEvents()
    {
        _eventStreamLock.EnterReadLock();
        try
        {
            return _sinks.Values.SelectMany(sink => sink.Events).ToArray();
        }
        finally
        {
            _eventStreamLock.ExitReadLock();
        }
    }

    private void HandleEventStreamDisposed(object sender, EventArgs e)
    {
        EventSinkStream stream = (EventSinkStream) sender;
        UnregisterStream(stream);
    }

    private void UnregisterStream(EventSinkStream stream)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _eventStreamLock.EnterWriteLock();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            _sinkStreams.Remove(stream);
            stream.EventChannel.Writer.TryComplete();
        }
        finally
        {
            _eventStreamLock.ExitWriteLock();
        }
        stream.Disposed -= HandleEventStreamDisposed;
    }

    internal void RegisterEvent(EventSink sink, SystemEvent evt)
    {
        _eventStreamLock.EnterReadLock();
        try
        {
            sink.Events.Enqueue(evt);
            foreach (EventSinkStream stream in _sinkStreams)
            {
                stream.StreamEvent(evt);
            }
        }
        finally
        {
            _eventStreamLock.ExitReadLock();
        }
    }

    public void Clear()
    {
        // Take the write lock so the clear is coherent with the _sinks.Values snapshots in
        // CreateSinkStream/GetEvents (which run under this lock) rather than racing them mid-
        // enumeration. Active _sinkStreams are intentionally left running — they belong to live
        // subscriptions; this only resets the sink set. (M34)
        _eventStreamLock.EnterWriteLock();
        try
        {
            foreach (EventSink sink in _sinks.Values)
            {
                sink.Invalidate();
            }
            _sinks.Clear();
        }
        finally
        {
            _eventStreamLock.ExitWriteLock();
        }
    }

    public void Dispose()
    {
        _eventStreamLock.EnterWriteLock();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (EventSinkStream stream in _sinkStreams.ToArray())
            {
                stream.Disposed -= HandleEventStreamDisposed;
                stream.EventChannel.Writer.TryComplete();
                stream.Dispose();
            }
            _sinkStreams.Clear();
        }
        finally
        {
            _eventStreamLock.ExitWriteLock();
        }
        _eventStreamLock.Dispose();
        GC.SuppressFinalize(this);
    }
}