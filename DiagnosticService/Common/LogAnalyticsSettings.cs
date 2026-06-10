namespace Diagnostic.Service.Common;

/// <summary>
/// Configuration for the Log Analytics Retro backend (DiagServiceSettings:LogAnalytics).
/// Populated from the outputs of infra/retro-loganalytics.bicep. Credentials are NOT held
/// here — the logger authenticates via DefaultAzureCredential (env-var service principal in
/// the container, or `az login` locally).
/// </summary>
public class LogAnalyticsSettings
{
    /// <summary>Data Collection Endpoint logs-ingestion URL (bicep output dceEndpoint).</summary>
    public string DceEndpoint { get; set; } = "";

    /// <summary>Data Collection Rule immutable ID (bicep output dcrImmutableId).</summary>
    public string DcrImmutableId { get; set; } = "";

    /// <summary>DCR stream name; 'Custom-&lt;TableName&gt;' (bicep output streamName).</summary>
    public string StreamName { get; set; } = "Custom-DiagRetro_CL";

    /// <summary>Custom table name queried by KQL (bicep output tableName).</summary>
    public string TableName { get; set; } = "DiagRetro_CL";

    /// <summary>Workspace customer/GUID ID used by the query API (bicep output workspaceId).</summary>
    public string WorkspaceId { get; set; } = "";
}
