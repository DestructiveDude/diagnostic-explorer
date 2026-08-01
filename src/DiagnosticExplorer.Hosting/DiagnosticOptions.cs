namespace DiagnosticExplorer.Hosting;

public class DiagnosticOptions
{
    public const string DiagnosticExplorer = "DiagnosticExplorer";

    public string Uri { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Optional API key sent to the diagnostic hub when the service runs in ApiKey auth mode (H1).
    ///     Null/empty (the default) connects with no key — matching a hub in the default None mode.
    /// </summary>
    public string ApiKey { get; set; }
}
