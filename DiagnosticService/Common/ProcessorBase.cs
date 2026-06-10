using DiagnosticExplorer;
using log4net;

namespace DiagWebService;

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
    public RateCounter Received { get; } = new RateCounter(5);

    [RateProperty(ExposeTotal = true, ExposeRate = false)]
    public RateCounter Processed { get; } = new RateCounter(5);

    [RateProperty(ExposeTotal = true, ExposeRate = false)]
    public RateCounter Errors { get; } = new RateCounter(5);
}