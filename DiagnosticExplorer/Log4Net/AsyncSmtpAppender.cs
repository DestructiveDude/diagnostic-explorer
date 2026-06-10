using System;
using DiagnosticExplorer.Log4Net.Util;
using DiagnosticExplorer.Props;
using log4net.Core;

namespace DiagnosticExplorer.Log4Net;

[DiagnosticClass(AttributedPropertiesOnly = true, DeclaringTypeOnly = false)]
public class AsyncSmtpAppender : SmtpAppender, IDisposable
{
    private AsyncProcessor _processor;

    public override void ActivateOptions()
    {
        base.ActivateOptions();

        _processor = new AsyncProcessor(Overflow, MaxQueueSize, PerformSend)
        {
            Fix = Fix
        };
        _processor.Start();
    }

    [Property]
    public int MaxQueueSize { get; set; } = 1000;


    [Property]
    public int? CurrentQueueSize => _processor?.QueueSize;

    [Property]
    public BufferOverflowMode Overflow { get; set; } = BufferOverflowMode.Block;

    public FixFlags Fix { get; set; } = FixFlags.Partial;

    protected override void Append(LoggingEvent loggingEvent)
    {
        EventsIn.Register(1);
        var processor = _processor;
        processor?.Append(loggingEvent);
    }

    protected override void Append(LoggingEvent[] loggingEvents)
    {
        EventsIn.Register(loggingEvents.Length);
        var processor = _processor;
        processor?.Append(loggingEvents);
    }

    protected override void OnClose()
    {
        _processor?.Close();
        base.OnClose();
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _processor?.Dispose();
                _processor = null;
            }
            _disposed = true;
        }
    }


    // Use C# destructor syntax for finalization code.
    ~AsyncSmtpAppender()
    {
        // Simply call Dispose(false).
        Dispose(false);
    }

}
