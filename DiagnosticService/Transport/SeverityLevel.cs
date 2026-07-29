namespace Diagnostic.Service.Transport;

public enum SeverityLevel
{
    All = 0,
    Trace = 20000,
    Debug = 30000,
    Info = 40000,
    Notice = 50000,
    Warn = 60000,
    Error = 70000,
    Severe = 80000,
    Critical = 90000,
    Fatal = 110000,
    Emergency = 120000,
}
