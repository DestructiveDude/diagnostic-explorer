namespace Diagnostic.Service.Common;

/// <summary>
/// Authentication mode for the SignalR hubs. <see cref="None"/> is the default and preserves the
/// historical open behaviour so existing trusted-network clients and the EMS consumer flow keep
/// working; an operator opts into <see cref="ApiKey"/> once every client is configured to send a key.
/// </summary>
public enum AuthMode
{
    None,
    ApiKey
}

/// <summary>
/// Opt-in security configuration for the DiagnosticService hubs (audit findings H1/H2). All
/// defaults reproduce today's behaviour, so shipping this is a zero-break change until an operator
/// turns it on.
/// </summary>
public class SecuritySettings
{
    /// <summary>None (default, == today) or ApiKey (every hub connection must present a valid key).</summary>
    public AuthMode AuthMode { get; set; } = AuthMode.None;

    /// <summary>Accepted API keys when <see cref="AuthMode"/> is ApiKey. List several to rotate.</summary>
    public string[] ApiKeys { get; set; } = [];

    /// <summary>
    /// CORS allowlist. When non-empty the service reflects only these origins (with credentials);
    /// when empty it keeps today's permissive any-origin policy and logs a startup warning.
    /// </summary>
    public string[] AllowedCorsOrigins { get; set; } = [];
}
