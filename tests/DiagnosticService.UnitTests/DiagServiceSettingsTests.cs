using AwesomeAssertions;
using Diagnostic.Service.Common;
using Xunit;

namespace DiagnosticService.UnitTests;

/// <summary>
///     (DE-19) DiagServiceSettings.CreateRetroLogger selects the retro backend by RetroType. The
///     selection runs inside RetroManager.StartAsync (an IHostedService), so an unsupported
///     RetroType crashes host startup, not just the retro feature. These tests pin each arm of
///     the switch directly — previously only the mongo arm was ever executed, incidentally.
///     Both logger constructors are side-effect free (the Mongo client and the Log Analytics
///     clients are built lazily), so no backend is needed.
/// </summary>
public class DiagServiceSettingsTests
{
    [Fact]
    public void CreateRetroLogger_WhenRetroTypeIsMongo_ReturnsMongoRetroLogger()
    {
        DiagServiceSettings settings = new()
        {
            RetroType = "mongo",
            RetroConnection = "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=500",
        };

        IRetroLogger logger = settings.CreateRetroLogger();

        logger.Should().BeOfType<MongoRetroLogger>();
    }

    [Fact]
    public void CreateRetroLogger_WhenRetroTypeIsLogAnalytics_ReturnsLogAnalyticsRetroLogger()
    {
        DiagServiceSettings settings = new()
        {
            RetroType = "loganalytics",
            LogAnalytics = new LogAnalyticsSettings
            {
                DceEndpoint = "https://dce.example.com",
                DcrImmutableId = "dcr-immutable-id",
                StreamName = "Custom-DiagRetro_CL",
                TableName = "DiagRetro_CL",
                WorkspaceId = "workspace-id",
            },
        };

        IRetroLogger logger = settings.CreateRetroLogger();

        logger.Should().BeOfType<LogAnalyticsRetroLogger>();
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("")]
    public void CreateRetroLogger_WhenRetroTypeIsUnsupported_ThrowsNotSupportedException(string retroType)
    {
        DiagServiceSettings settings = new() { RetroType = retroType };

        Action act = () => settings.CreateRetroLogger();

        act.Should().Throw<NotSupportedException>();
    }
}
