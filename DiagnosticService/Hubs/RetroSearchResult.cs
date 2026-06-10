using Diagnostic.Service.Transport;

namespace Diagnostic.Service.Hubs;

public class RetroSearchResult
{
    public int SearchId { get; set; }

    // Optional human-readable status for this batch. Note: the client computes its own
    // (asymptotic) progress bar and does not read a server progress value, so the former
    // `decimal Progress` was dead — never set server-side, never deserialized client-side — and
    // has been removed rather than left to always report 0.
    public string? Info { get; set; }
    public IList<RetroMsg> Results { get; set; } = Array.Empty<RetroMsg>();
}