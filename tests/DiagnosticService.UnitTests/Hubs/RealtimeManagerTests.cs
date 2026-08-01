using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using Diagnostic.Service.Transport;
using DiagnosticExplorer.Interface;
using NSubstitute;
using Xunit;

namespace DiagnosticService.UnitTests.Hubs;

/// <summary>
///     The SPA's error path (diag-hub.service.ts) expects a <em>resolved</em>
///     <see cref="OperationResponse" /> carrying <c>ErrorMessage</c> — a rejected promise breaks the
///     UI's error handling instead of surfacing the error. <see cref="RealtimeManager.SetProperty" />
///     and <see cref="RealtimeManager.ExecuteOperation" /> must therefore never throw: process-not-found,
///     not-connected, and any downstream client failure all return
///     <see cref="OperationResponse.Error(string)" />. (DE-12)
/// </summary>
public sealed class RealtimeManagerTests
{
    [Fact]
    public async Task StopAsync_ReleasesOwnedSubjects()
    {
        RealtimeManager manager = new(TimeProvider.System);

        await manager.StopAsync(TestContext.Current.CancellationToken);

        Action changeProcess = () => manager.ProcessChanged.OnNext(new DiagProcess());
        Action removeProcess = () => manager.ProcessRemoved.OnNext(new DiagProcess());
        changeProcess.Should().Throw<ObjectDisposedException>();
        removeProcess.Should().Throw<ObjectDisposedException>();

        Action lateRegistration = () =>
            manager.Register(
                new Registration
                {
                    ProcessName = "late-process",
                    MachineName = "test-machine",
                    InstanceId = "late-instance",
                }
            );
        lateRegistration.Should().NotThrow();
    }

    [Fact]
    public async Task SetProperty_ProcessNotFound_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);

        OperationResponse response = await manager.SetProperty(
            new SetPropertyRequest
            {
                Id = "no-such-process",
                Path = "a|b||c",
                Value = "1",
            }
        );

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Be("Process no-such-process not found");
    }

    [Fact]
    public async Task ExecuteOperation_ProcessNotFound_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);

        OperationResponse response = await manager.ExecuteOperation(
            new ExecuteOperationRequest
            {
                Id = "no-such-process",
                Path = "a|b",
                Operation = "Run()",
            }
        );

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Be("Process no-such-process not found");
    }

    [Fact]
    public async Task SetProperty_ProcessNotConnected_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);
        var processId = RegisterProcess(manager);

        OperationResponse response = await manager.SetProperty(
            new SetPropertyRequest
            {
                Id = processId,
                Path = "a|b||c",
                Value = "1",
            }
        );

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Be($"Process {processId} is not connected");
    }

    [Fact]
    public async Task ExecuteOperation_ProcessNotConnected_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);
        var processId = RegisterProcess(manager);

        OperationResponse response = await manager.ExecuteOperation(
            new ExecuteOperationRequest
            {
                Id = processId,
                Path = "a|b",
                Operation = "Run()",
            }
        );

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Be($"Process {processId} is not connected");
    }

    [Fact]
    public async Task SetProperty_DiagnosticClientThrows_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);
        var processId = await RegisterProcessWithClient(
            manager,
            client =>
                client
                    .SetProperty(Arg.Any<string>(), Arg.Any<string?>())
                    .Returns(Task.FromException<OperationResponse>(new InvalidOperationException("client exploded")))
        );

        OperationResponse response = await manager.SetProperty(
            new SetPropertyRequest
            {
                Id = processId,
                Path = "a|b||c",
                Value = "1",
            }
        );

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Be("client exploded");
    }

    [Fact]
    public async Task ExecuteOperation_DiagnosticClientThrows_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);
        var processId = await RegisterProcessWithClient(
            manager,
            client =>
                client
                    .ExecuteOperation(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string[]>())
                    .Returns(Task.FromException<OperationResponse>(new InvalidOperationException("client exploded")))
        );

        OperationResponse response = await manager.ExecuteOperation(
            new ExecuteOperationRequest
            {
                Id = processId,
                Path = "a|b",
                Operation = "Run()",
            }
        );

        response.IsSuccess.Should().BeFalse();
        response.ErrorMessage.Should().Be("client exploded");
    }

    private static string RegisterProcess(RealtimeManager manager)
    {
        manager.Register(
            new Registration
            {
                ProcessName = "test-process",
                MachineName = "test-machine",
                UserName = "test-user",
                InstanceId = "test-instance",
            }
        );
        return manager.GetProcesses().Single().Id;
    }

    private static async Task<string> RegisterProcessWithClient(
        RealtimeManager manager,
        Action<IDiagnosticClient> configure
    )
    {
        var processId = RegisterProcess(manager);

        // GetSubscription is private and only materialises the DiagnosticSubscription on first use;
        // a first (not-connected) call creates it, after which the fake client can be attached
        // through the public Subscriptions collection.
        await manager.SetProperty(new SetPropertyRequest { Id = processId, Path = "a|b||c" });

        DiagnosticSubscription subscription = manager.Subscriptions.Single();
        IDiagnosticClient client = Substitute.For<IDiagnosticClient>();
        configure(client);
        subscription.SetDiagnosticClient(client);

        return processId;
    }
}
