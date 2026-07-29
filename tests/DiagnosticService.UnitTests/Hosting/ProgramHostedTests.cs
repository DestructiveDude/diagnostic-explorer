using AwesomeAssertions;
using Diagnostic.Service;
using Diagnostic.Service.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiagnosticService.UnitTests.Hosting;

/// <summary>
/// Hosted tests for <see cref="Program"/> startup validation, API-key authentication,
/// and configuration precedence. These use <see cref="WebApplicationFactory{TEntryPoint}"/>
/// to exercise the real service pipeline.
/// </summary>
public sealed class ProgramHostedTests
{
    private const string TestApiKey = "test-api-key-42";
    private const string TestOrigin = "http://localhost:2803";
    private const string TestSpaProxy = "http://localhost:4201";
    private static readonly string TestSpaDirectory = Path.GetTempPath();

    /// <summary>
    /// When <see cref="SecuritySettings.AuthMode"/> is <see cref="AuthMode.ApiKey"/>
    /// but no non-empty API keys are configured, startup must fail closed with
    /// <see cref="ApplicationException"/>.
    /// </summary>
    [Fact]
    public void ApiKeyMode_WithoutValidKeys_ThrowsApplicationExceptionAtStartup()
    {
        using EnvironmentVariableScope authMode = new(
            "DiagServiceSettings__Security__AuthMode",
            AuthMode.ApiKey.ToString()
        );
        using EnvironmentVariableScope cors = new(
            "DiagServiceSettings__Security__AllowedCorsOrigins__0",
            TestOrigin
        );
        using EnvironmentVariableScope spaProxy = new("DiagServiceSettings__UseSpaProxy", "true");
        using EnvironmentVariableScope spaProxyAddress = new(
            "DiagServiceSettings__SpaProxy",
            TestSpaProxy
        );
        using EnvironmentVariableScope spaDirectory = new(
            "DiagServiceSettings__SpaDirectory",
            TestSpaDirectory
        );

        using WebApplicationFactory<Program> factory = new();

        Exception? ex = null;
        try
        {
            // Accessing Server forces the deferred host to start and run the validation in Main.
            _ = factory.Server.BaseAddress;
        }
        catch (Exception e)
        {
            ex = e;
        }

        ex.Should().NotBeNull();
        ex.Should().BeOfType<ApplicationException>();
        ex!
            .Message.Should()
            .Contain(
                "DiagServiceSettings:Security:AuthMode is ApiKey but no non-empty ApiKeys are configured"
            );
    }

    /// <summary>
    /// An anonymous SignalR connection to <c>/web-hub</c> is rejected when API-key auth is enabled.
    /// </summary>
    [Fact]
    public async Task ApiKeyMode_AnonymousConnectionToWebHub_IsRejected()
    {
        using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        HubConnection connection = CreateConnection(factory, "/web-hub", apiKey: null);

        Exception? thrown = await Record.ExceptionAsync(() =>
            connection.StartAsync(TestContext.Current.CancellationToken)
        );

        thrown.Should().NotBeNull();
    }

    /// <summary>
    /// An anonymous SignalR connection to <c>/diagnostics</c> is rejected when API-key auth is enabled.
    /// </summary>
    [Fact]
    public async Task ApiKeyMode_AnonymousConnectionToDiagnostics_IsRejected()
    {
        using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        HubConnection connection = CreateConnection(factory, "/diagnostics", apiKey: null);

        Exception? thrown = await Record.ExceptionAsync(() =>
            connection.StartAsync(TestContext.Current.CancellationToken)
        );

        thrown.Should().NotBeNull();
    }

    /// <summary>
    /// A SignalR connection to <c>/web-hub</c> succeeds when a valid API key is supplied.
    /// </summary>
    [Fact]
    public async Task ApiKeyMode_ValidKey_ConnectionToWebHub_Succeeds()
    {
        using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        HubConnection connection = CreateConnection(factory, "/web-hub", TestApiKey);

        await connection.StartAsync(TestContext.Current.CancellationToken);

        connection.State.Should().Be(HubConnectionState.Connected);
        await connection.DisposeAsync();
    }

    /// <summary>
    /// A SignalR connection to <c>/diagnostics</c> succeeds when a valid API key is supplied.
    /// </summary>
    [Fact]
    public async Task ApiKeyMode_ValidKey_ConnectionToDiagnostics_Succeeds()
    {
        using WebApplicationFactory<Program> factory = CreateAuthenticatedFactory();
        HubConnection connection = CreateConnection(factory, "/diagnostics", TestApiKey);

        await connection.StartAsync(TestContext.Current.CancellationToken);

        connection.State.Should().Be(HubConnectionState.Connected);
        await connection.DisposeAsync();
    }

    /// <summary>
    /// Program.cs re-adds environment variables after the JSON config file so that
    /// deployment overrides win when both sources define the same key.
    /// </summary>
    [Fact]
    public void EnvironmentVariable_Overrides_JsonConfigurationValue()
    {
        const string envVarName = "DiagServiceSettings__RetroConnection";
        const string envVarValue = "env-var-override";

        using EnvironmentVariableScope retro = new(envVarName, envVarValue);
        using EnvironmentVariableScope spaProxy = new("DiagServiceSettings__UseSpaProxy", "true");
        using EnvironmentVariableScope spaProxyAddress = new(
            "DiagServiceSettings__SpaProxy",
            TestSpaProxy
        );
        using EnvironmentVariableScope spaDirectory = new(
            "DiagServiceSettings__SpaDirectory",
            TestSpaDirectory
        );
        using WebApplicationFactory<Program> factory = new();

        IConfiguration config = factory.Services.GetRequiredService<IConfiguration>();

        config["DiagServiceSettings:RetroConnection"].Should().Be(envVarValue);
    }

    /// <summary>
    /// Creates a factory with API-key auth enabled and a valid key/origin configured.
    /// </summary>
    private static WebApplicationFactory<Program> CreateAuthenticatedFactory()
    {
        EnvironmentVariableScope authMode = new(
            "DiagServiceSettings__Security__AuthMode",
            AuthMode.ApiKey.ToString()
        );
        EnvironmentVariableScope apiKey = new(
            "DiagServiceSettings__Security__ApiKeys__0",
            TestApiKey
        );
        EnvironmentVariableScope cors = new(
            "DiagServiceSettings__Security__AllowedCorsOrigins__0",
            TestOrigin
        );
        EnvironmentVariableScope spaProxy = new("DiagServiceSettings__UseSpaProxy", "true");
        EnvironmentVariableScope spaProxyAddress = new(
            "DiagServiceSettings__SpaProxy",
            TestSpaProxy
        );
        EnvironmentVariableScope spaDirectory = new(
            "DiagServiceSettings__SpaDirectory",
            TestSpaDirectory
        );

        // The factory must outlive the scopes, so dispose them when the factory is disposed.
        return new AuthenticatedFactory(
            authMode,
            apiKey,
            cors,
            spaProxy,
            spaProxyAddress,
            spaDirectory
        );
    }

    private static HubConnection CreateConnection(
        WebApplicationFactory<Program> factory,
        string hubPath,
        string? apiKey
    )
    {
        Uri baseAddress = factory.Server.BaseAddress;
        var builder = new HubConnectionBuilder().WithUrl(
            new Uri(baseAddress, hubPath),
            options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider =
                    apiKey == null ? null : () => Task.FromResult<string?>(apiKey);
            }
        );

        return builder.Build();
    }

    /// <summary>
    /// Sets an environment variable for the current process and restores the original value on disposal.
    /// </summary>
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

    /// <summary>
    /// Wraps a <see cref="WebApplicationFactory{Program}"/> so the auth-related environment
    /// variables are reset when the factory is disposed.
    /// </summary>
    private sealed class AuthenticatedFactory : WebApplicationFactory<Program>
    {
        private readonly IDisposable[] _scopes;

        public AuthenticatedFactory(params IDisposable[] scopes)
        {
            _scopes = scopes;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (IDisposable scope in _scopes)
                {
                    scope.Dispose();
                }
            }
        }
    }
}
