using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Diagnostic.Service.Common;

/// <summary>
/// Minimal API-key authentication for the SignalR hubs (audit finding H1). The key is accepted
/// from the <c>X-Diag-ApiKey</c> header, an <c>Authorization: Bearer</c> header, or the
/// <c>access_token</c> query string — the last covers the WebSocket upgrade, where browsers and the
/// SignalR client can't set request headers. Keys are matched with a fixed-time comparison; list
/// several in configuration to rotate. Only registered when <see cref="AuthMode"/> is not None.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Diag-ApiKey";

    private readonly SecuritySettings _security;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<DiagServiceSettings> settings)
        : base(options, logger, encoder)
    {
        _security = settings.Value.Security;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? presented = ExtractKey();

        // Fail (not NoResult) when no key is presented: ApiKey is the sole/default scheme, so a
        // definitive failure keeps rejection unambiguous at every layer even if a second scheme or
        // an AllowAnonymous policy is added later. (F5)
        if (string.IsNullOrEmpty(presented))
        {
            return Task.FromResult(AuthenticateResult.Fail("No API key provided"));
        }

        bool valid = _security.ApiKeys.Any(key => KeysEqual(key, presented));
        if (!valid)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.Name, "diagnostic-client")], SchemeName);
        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ExtractKey()
    {
        // Check for the short-lived cookie first to avoid query string leak in browsers.
        if (Request.Cookies.TryGetValue("Diag-Hub-Auth", out var cookieToken) && !string.IsNullOrEmpty(cookieToken))
        {
            return cookieToken.ToString().Trim();
        }

        // All paths are Trim()'d consistently — a whitespace-padded key (copy-paste, padding proxy)
        // must not silently fail the fixed-time comparison. (F7)
        if (Request.Headers.TryGetValue(HeaderName, out var apiKeyHeader) && !string.IsNullOrEmpty(apiKeyHeader))
        {
            return apiKeyHeader.ToString().Trim();
        }

        // SignalR's AccessTokenProvider sends "Authorization: Bearer <key>" on the negotiate request.
        string auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth.Substring("Bearer ".Length).Trim();
        }

        // ...and "access_token=<key>" on the WebSocket/SSE/long-polling requests.
        if (Request.Query.TryGetValue("access_token", out var token) && !string.IsNullOrEmpty(token))
        {
            return token.ToString().Trim();
        }

        return null;
    }

    private static bool KeysEqual(string configured, string? presented)
    {
        if (string.IsNullOrEmpty(configured) || presented == null)
        {
            return false;
        }

        // FixedTimeEquals returns false for differing lengths (it only reveals length, not content).
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(configured),
            Encoding.UTF8.GetBytes(presented));
    }
}
