using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Threading.Channels;
using DiagnosticExplorer.Util;

namespace DiagnosticExplorer.Events;

public sealed class EventSinkStream : IDisposable
{
    public event EventHandler Disposed;
    private readonly Subject<SystemEvent> _innerSubject;
    private ISubject<SystemEvent> _eventSubject;
    private readonly IDisposable _eventSubscription;
    private readonly int bufferSize = 100;

    public EventSinkStream(SystemEvent[] initialEvents, TimeSpan buffer, int bufferSize)
    {
        if (bufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferSize), "bufferSize must be positive");
        }

        InitialEvents = initialEvents;
        this.bufferSize = bufferSize;

        _innerSubject = new Subject<SystemEvent>();
        _eventSubject = Subject.Synchronize(_innerSubject);
        _eventSubscription = _eventSubject.BufferWhenAvailable(buffer)
            .Subscribe(WriteEvents, () => EventChannel?.Writer.Complete());

        EventChannel = Channel.CreateBounded<IList<SystemEvent>>(
            new BoundedChannelOptions(10000)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropWrite,
            });
    }

    private void WriteEvents(IList<SystemEvent> evts)
    {
        if (evts.Count <= bufferSize)
        {
            EventChannel?.Writer.TryWrite(evts);
            return;
        }

        // Split into bufferSize-sized chunks synchronously. The previous
        // evts.ToObservable().Buffer(bufferSize).ForEachAsync(...) returned a Task that was never
        // awaited or observed (fire-and-forget) — any fault was swallowed and completion ordering
        // was undefined. A plain loop has the same chunking effect with none of that.
        for (int i = 0; i < evts.Count; i += bufferSize)
        {
            var chunk = new List<SystemEvent>(Math.Min(bufferSize, evts.Count - i));
            for (int j = i; j < evts.Count && j < i + bufferSize; j++)
            {
                chunk.Add(evts[j]);
            }

            EventChannel?.Writer.TryWrite(chunk);
        }
    }

    public SystemEvent[] InitialEvents { get; }

    public void StreamEvent(SystemEvent evt)
    {
        try
        {
            _eventSubject?.OnNext(evt);
        }
        catch (ObjectDisposedException)
        {
        }
    }


    public Channel<IList<SystemEvent>> EventChannel { get; }

    public void Dispose()
    {
        Disposed?.Invoke(this, EventArgs.Empty);
        Disposed = null;

        _eventSubscription?.Dispose();
        _innerSubject.Dispose();
        _eventSubject = null;
    }
}