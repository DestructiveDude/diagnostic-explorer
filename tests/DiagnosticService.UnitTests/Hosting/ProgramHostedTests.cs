using System.Net;
using AwesomeAssertions;
using Diagnostic.Service;
using Diagnostic.Service.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiagnosticService.UnitTests.Hosting;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProgramHostedTests
{
    private const string TestApiKey = "test-api-key-42";
    private const string TestOrigin = "http://localhost:2803";
    private const string TestSpaProxy = "http://localhost:4201";

    [Fact]
    public void ApiKeyModeWithoutKeys_FailsAtStartup()
    {
        using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["DiagServiceSettings:Security:AuthMode"] = nameof(AuthMode.ApiKey),
                ["DiagServiceSettings:Security:AllowedCorsOrigins:0"] = TestOrigin,
            }
        );

        Exception? exception = null;
        try
        {
            _ = factory.Server.BaseAddress;
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        exception
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("no non-empty ApiKeys are configured");
    }

    [Theory]
    [InlineData("/web-hub")]
    [InlineData("/diagnostics")]
    public async Task ApiKeyModeRejectsAnonymousHubConnection(string hubPath)
    {
        using var factory = CreateAuthenticatedFactory();
        await using var connection = CreateConnection(factory, hubPath, null);

        var exception = await Record.ExceptionAsync(() =>
            connection.StartAsync(TestContext.Current.CancellationToken)
        );

        exception.Should().NotBeNull();
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    [Theory]
    [InlineData("/web-hub")]
    [InlineData("/diagnostics")]
    public async Task ApiKeyModeAcceptsValidHubConnection(string hubPath)
    {
        using var factory = CreateAuthenticatedFactory();
        await using var connection = CreateConnection(factory, hubPath, TestApiKey);

        await connection.StartAsync(TestContext.Current.CancellationToken);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    /// <summary>
    ///     (DE-3) A presented key that does not match any configured key must be rejected — the
    ///     handler's <c>if (!valid)</c> branch is the only thing standing between a wrong-but-non-empty
    ///     key and a hub connection.
    /// </summary>
    [Theory]
    [InlineData("/web-hub")]
    [InlineData("/diagnostics")]
    public async Task ApiKeyModeRejectsWrongApiKeyHubConnection(string hubPath)
    {
        using var factory = CreateAuthenticatedFactory();
        await using var connection = CreateConnection(factory, hubPath, "wrong-api-key-99");

        var exception = await Record.ExceptionAsync(() =>
            connection.StartAsync(TestContext.Current.CancellationToken)
        );

        exception.Should().NotBeNull();
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    /// <summary>
    ///     (DE-3) The documented <c>X-Diag-ApiKey</c> header extraction path has no fallback — a
    ///     client presenting the valid key via the header (rather than the SignalR bearer token /
    ///     access_token query pair) must be accepted on both hubs.
    /// </summary>
    [Theory]
    [InlineData("/web-hub")]
    [InlineData("/diagnostics")]
    public async Task ApiKeyModeAcceptsHeaderApiKeyHubConnection(string hubPath)
    {
        using var factory = CreateAuthenticatedFactory();
        await using var connection = CreateHeaderConnection(factory, hubPath, TestApiKey);

        await connection.StartAsync(TestContext.Current.CancellationToken);

        connection.State.Should().Be(HubConnectionState.Connected);
    }

    /// <summary>
    ///     (DE-5) CORS does not police the WebSocket upgrade (F9), so the pipeline middleware
    ///     validates the Origin header on the hub paths. A cross-origin browser holding a valid key
    ///     must still get 403. The key must be present: <c>UseAuthorization</c> runs before the
    ///     Origin middleware, so an unauthenticated request 401s before the 403 branch is reachable.
    /// </summary>
    [Fact]
    public async Task DisallowedOriginWithValidKey_ForbiddenOnHubPath()
    {
        using var factory = CreateAuthenticatedFactory();
        using var client = factory.CreateClient();
        using var request = CreateHubOriginRequest("http://evil.example");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    ///     (DE-5) Control for the 403 case: an allowlisted Origin with a valid key must pass the
    ///     Origin middleware — the request may still fail downstream (a plain GET is not a hub
    ///     handshake), but anything other than 403 proves the allowlist accepted it.
    /// </summary>
    [Fact]
    public async Task AllowedOriginWithValidKey_NotForbiddenOnHubPath()
    {
        using var factory = CreateAuthenticatedFactory();
        using var client = factory.CreateClient();
        using var request = CreateHubOriginRequest(TestOrigin);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    ///     (DE-18) ApiKey mode with an empty <c>AllowedCorsOrigins</c> allowlist must fail closed at
    ///     startup — otherwise the service would boot key auth alongside credentialed any-origin CORS.
    /// </summary>
    [Fact]
    public void ApiKeyModeWithoutCorsOrigins_FailsAtStartup()
    {
        using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["DiagServiceSettings:Security:AuthMode"] = nameof(AuthMode.ApiKey),
                ["DiagServiceSettings:Security:ApiKeys:0"] = TestApiKey,
            }
        );

        Exception? exception = null;
        try
        {
            _ = factory.Server.BaseAddress;
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        exception
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("AllowedCorsOrigins is empty");
    }

    /// <summary>
    ///     (DE-20) With UseSpaProxy=false the service serves the SPA from SpaDirectory, so
    ///     Program.cs refuses to boot when that directory does not exist — otherwise a production
    ///     deploy missing diagnostics-web/dist would start and fail on every request. Every other
    ///     fixture here sets UseSpaProxy=true, which skips the guard.
    /// </summary>
    [Fact]
    public void SpaProxyDisabledWithMissingSpaDirectory_FailsAtStartup()
    {
        using var factory = CreateFactory(
            new Dictionary<string, string?>
            {
                ["DiagServiceSettings:UseSpaProxy"] = "false",
                ["DiagServiceSettings:SpaDirectory"] = Path.Combine(
                    Path.GetTempPath(),
                    $"de20-missing-spa-directory-{Guid.NewGuid():N}"
                ),
            }
        );

        Exception? exception = null;
        try
        {
            _ = factory.Server.BaseAddress;
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        exception
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("Diagnostics SPA directory not found");
    }

    private static HttpRequestMessage CreateHubOriginRequest(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/web-hub");
        request.Headers.Add("X-Diag-ApiKey", TestApiKey);
        request.Headers.Add("Origin", origin);
        return request;
    }

    [Fact]
    public void EnvironmentVariablesOverrideJsonConfiguration()
    {
        const string variableName = "DiagServiceSettings__RetroConnection";
        const string variableValue = "environment-override";
        using EnvironmentVariableScope environment = new(variableName, variableValue);
        using var factory = CreateFactory();

        var configuration = factory.Services.GetRequiredService<IConfiguration>();

        configuration["DiagServiceSettings:RetroConnection"].Should().Be(variableValue);
    }

    private static DiagnosticServiceFactory CreateAuthenticatedFactory()
    {
        return CreateFactory(
            new Dictionary<string, string?>
            {
                ["DiagServiceSettings:Security:AuthMode"] = nameof(AuthMode.ApiKey),
                ["DiagServiceSettings:Security:ApiKeys:0"] = TestApiKey,
                ["DiagServiceSettings:Security:AllowedCorsOrigins:0"] = TestOrigin,
            }
        );
    }

    private static DiagnosticServiceFactory CreateFactory(
        IReadOnlyDictionary<string, string?>? overrides = null
    )
    {
        Dictionary<string, string?> settings = new()
        {
            ["DiagServiceSettings:UseSpaProxy"] = "true",
            ["DiagServiceSettings:SpaProxy"] = TestSpaProxy,
            ["DiagServiceSettings:SpaDirectory"] = Path.GetTempPath(),
        };

        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        return new DiagnosticServiceFactory(settings);
    }

    private static HubConnection CreateConnection(
        WebApplicationFactory<Program> factory,
        string hubPath,
        string? apiKey
    )
    {
        var baseAddress = factory.Server.BaseAddress;
        return new HubConnectionBuilder()
            .WithUrl(
                new Uri(baseAddress, hubPath),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.AccessTokenProvider =
                        apiKey == null ? null : () => Task.FromResult<string?>(apiKey);
                }
            )
            .Build();
    }

    private static HubConnection CreateHeaderConnection(
        WebApplicationFactory<Program> factory,
        string hubPath,
        string apiKey
    )
    {
        var baseAddress = factory.Server.BaseAddress;
        return new HubConnectionBuilder()
            .WithUrl(
                new Uri(baseAddress, hubPath),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Headers["X-Diag-ApiKey"] = apiKey;
                }
            )
            .Build();
    }

    private sealed class DiagnosticServiceFactory : WebApplicationFactory<Program>
    {
        private readonly EnvironmentVariableScope[] _environment;

        public DiagnosticServiceFactory(IReadOnlyDictionary<string, string?> settings)
        {
            _environment = settings
                .Select(setting => new EnvironmentVariableScope(
                    setting.Key.Replace(":", "__"),
                    setting.Value
                ))
                .ToArray();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                foreach (var variable in _environment.Reverse())
                {
                    variable.Dispose();
                }
            }
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";

    private ProcessEnvironmentCollection() { }
}
