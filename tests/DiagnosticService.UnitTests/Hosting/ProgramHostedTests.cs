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
