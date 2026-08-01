using DiagnosticExplorer.Props;
using log4net;

namespace Diagnostic.Service.Common;

public class ProcessorBase
{
    protected readonly ILog _log;

    public ProcessorBase()
    {
        _log = LogManager.GetLogger(GetType());
    }

    public string Name { get; set; } = null!;

    public string Type => GetType().Name;

    [RateProperty(ExposeTotal = true, ExposeRate = true)]
    public RateCounter Received { get; } = new(5);

    [RateProperty(ExposeTotal = true, ExposeRate = false)]
    public RateCounter Processed { get; } = new(5);

    [RateProperty(ExposeTotal = true, ExposeRate = false)]
    public RateCounter Errors { get; } = new(5);
}
