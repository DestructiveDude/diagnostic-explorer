using AwesomeAssertions;
using Diagnostic.Service.ClientHandlers;
using Diagnostic.Service.Common;
using Diagnostic.Service.Hubs;
using Diagnostic.Service.Transport;
using DiagnosticExplorer;
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
    public void Register_WhenSubscriberThrowsObjectDisposedException_Propagates()
    {
        RealtimeManager manager = new(TimeProvider.System);
        using var subscription = manager.ProcessChanged.Subscribe(_ => throw new ObjectDisposedException("subscriber"));

        Action register = () => RegisterProcess(manager);

        register.Should().Throw<ObjectDisposedException>();
    }

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
                    .SetProperty(Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<string>(), Arg.Any<string?>())
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
                    .ExecuteOperation(
                        Arg.Any<string>(),
                        Arg.Any<string[]>(),
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<string[]>()
                    )
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

    [Fact]
    public async Task SetWebClientSubscriptions_ReconcilesOnlyChangedMembers()
    {
        RealtimeManager manager = new(TimeProvider.System);
        string processA = RegisterProcess(manager, "a");
        string processB = RegisterProcess(manager, "b");
        manager.AddWebHubClient("web-1", NSubstitute.Substitute.For<IWebHubClient>());

        (await manager.SetWebClientSubscriptions("web-1", [processA, processB])).Should().BeTrue();
        (await manager.SetWebClientSubscriptions("web-1", [processB])).Should().BeTrue();

        manager
            .Subscriptions.Single(subscription => subscription.ProcessId == processA)
            .HasWebClient("web-1")
            .Should()
            .BeFalse();
        manager
            .Subscriptions.Single(subscription => subscription.ProcessId == processB)
            .HasWebClient("web-1")
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task SetWebClientSubscriptions_InvalidMember_PreservesExistingMemberships()
    {
        RealtimeManager manager = new(TimeProvider.System);
        string processA = RegisterProcess(manager, "a");
        manager.AddWebHubClient("web-1", NSubstitute.Substitute.For<IWebHubClient>());

        (await manager.SetWebClientSubscriptions("web-1", [processA])).Should().BeTrue();
        (await manager.SetWebClientSubscriptions("web-1", ["missing"])).Should().BeFalse();

        manager.Subscriptions.Single().HasWebClient("web-1").Should().BeTrue();
    }

    [Fact]
    public async Task SetWebClientSubscriptions_EmptySet_ReleasesEveryMembership()
    {
        RealtimeManager manager = new(TimeProvider.System);
        string processA = RegisterProcess(manager, "a");
        string processB = RegisterProcess(manager, "b");
        manager.AddWebHubClient("web-1", NSubstitute.Substitute.For<IWebHubClient>());

        (await manager.SetWebClientSubscriptions("web-1", [processA, processB])).Should().BeTrue();
        (await manager.SetWebClientSubscriptions("web-1", [])).Should().BeTrue();

        manager.Subscriptions.Should().OnlyContain(subscription => !subscription.HasWebClient("web-1"));
    }

    [Fact]
    public async Task SetWebClientSubscriptions_RepeatedDesiredSet_DoesNotReplayUnchangedMembers()
    {
        RealtimeManager manager = new(TimeProvider.System);
        string processA = RegisterProcess(manager, "a");
        string processB = RegisterProcess(manager, "b");
        IWebHubClient client = NSubstitute.Substitute.For<IWebHubClient>();
        var initializedBoth = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initializationCount = 0;
        client
            .InitializeLogStream(Arg.Any<string>(), Arg.Any<DiagnosticExplorer.Logging.LogStreamInitialization>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref initializationCount) == 2)
                {
                    initializedBoth.TrySetResult();
                }

                return Task.CompletedTask;
            });
        manager.AddWebHubClient("web-1", client);

        (await manager.SetWebClientSubscriptions("web-1", [processA, processB])).Should().BeTrue();
        await initializedBoth.Task.WaitAsync(TestContext.Current.CancellationToken);
        (await manager.SetWebClientSubscriptions("web-1", [processB])).Should().BeTrue();
        (await manager.SetWebClientSubscriptions("web-1", [processB, processB])).Should().BeTrue();

        initializationCount.Should().Be(2, "unchanged B keeps its existing replay chain");
    }

    [Fact]
    public async Task SubscribeWebClient_RemainsExclusiveAfterASetSubscriptionCall()
    {
        RealtimeManager manager = new(TimeProvider.System);
        string processA = RegisterProcess(manager, "a");
        string processB = RegisterProcess(manager, "b");
        manager.AddWebHubClient("web-1", NSubstitute.Substitute.For<IWebHubClient>());

        (await manager.SetWebClientSubscriptions("web-1", [processA, processB])).Should().BeTrue();
        (await manager.SubscribeWebClient("web-1", processA)).Should().BeTrue();

        manager
            .Subscriptions.Single(subscription => subscription.ProcessId == processA)
            .HasWebClient("web-1")
            .Should()
            .BeTrue();
        manager
            .Subscriptions.Single(subscription => subscription.ProcessId == processB)
            .HasWebClient("web-1")
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task SetWebClientSubscriptions_DisconnectDuringAttachment_RollsBackMembership()
    {
        RealtimeManager manager = new(TimeProvider.System);
        string processId = RegisterProcess(manager, "a");
        IWebHubClient client = NSubstitute.Substitute.For<IWebHubClient>();
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client
            .ShowDiagnostics(Arg.Any<string>(), Arg.Any<DiagnosticResponse>())
            .Returns(_ =>
            {
                sendStarted.TrySetResult();
                return allowSend.Task;
            });
        manager.AddWebHubClient("web-1", client);

        (await manager.SetWebClientSubscriptions("web-1", [processId])).Should().BeTrue();
        DiagnosticSubscription subscription = manager.Subscriptions.Single();
        typeof(DiagnosticSubscription)
            .GetField(
                "_lastResponse",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .SetValue(subscription, new DiagnosticResponse());
        (await manager.SetWebClientSubscriptions("web-1", [])).Should().BeTrue();

        Task<bool> attaching = manager.SetWebClientSubscriptions("web-1", [processId]);
        await sendStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        manager.RemoveWebHubClient("web-1");
        allowSend.TrySetResult();

        (await attaching).Should().BeFalse();
        subscription.HasWebClient("web-1").Should().BeFalse();
    }

    [Fact]
    public async Task SetWebClientSubscriptions_ProcessRemovedDuringAttachment_DoesNotRecreateItsSubscription()
    {
        RealtimeManager manager = new(TimeProvider.System);
        string processId = RegisterProcess(manager, "a");
        string processB = RegisterProcess(manager, "b");
        IWebHubClient client = NSubstitute.Substitute.For<IWebHubClient>();
        var sendStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client
            .ShowDiagnostics(Arg.Any<string>(), Arg.Any<DiagnosticResponse>())
            .Returns(_ =>
            {
                sendStarted.TrySetResult();
                return allowSend.Task;
            });
        manager.AddWebHubClient("web-1", client);

        (await manager.SetWebClientSubscriptions("web-1", [processId])).Should().BeTrue();
        DiagnosticSubscription subscription = manager.Subscriptions.Single();
        typeof(DiagnosticSubscription)
            .GetField(
                "_lastResponse",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            )!
            .SetValue(subscription, new DiagnosticResponse());
        (await manager.SetWebClientSubscriptions("web-1", [])).Should().BeTrue();
        (await manager.SetWebClientSubscriptions("web-1", [processB])).Should().BeTrue();

        Task<bool> attaching = manager.SetWebClientSubscriptions("web-1", [processId, processB]);
        await sendStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        manager.RemoveProcess(processId);
        allowSend.TrySetResult();

        (await attaching).Should().BeFalse();
        manager.Subscriptions.Should().NotContain(item => item.ProcessId == processId);
        manager.Subscriptions.Single(item => item.ProcessId == processB).HasWebClient("web-1").Should().BeTrue();
    }

    /// <summary>
    ///     GetDrillDown carries the same never-throw contract as the other two, and the same three
    ///     failure modes, but its own lookups and try/catch live at this layer - DrillDownTests
    ///     exercise DiagnosticManager, one layer below, and cannot reach them.
    /// </summary>
    [Fact]
    public async Task GetDrillDown_ProcessNotFound_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);

        DrillDownResponse response = await manager.GetDrillDown(
            new ProcessDrillDownRequest { Id = "no-such-process", ObjectPaths = ["a|b"] }
        );

        response.ErrorMessage.Should().Be("Process no-such-process not found");
    }

    [Fact]
    public async Task GetDrillDown_ProcessNotConnected_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);
        var processId = RegisterProcess(manager);

        DrillDownResponse response = await manager.GetDrillDown(
            new ProcessDrillDownRequest { Id = processId, ObjectPaths = ["a|b"] }
        );

        response.ErrorMessage.Should().Be($"Process {processId} is not connected");
    }

    [Fact]
    public async Task GetDrillDown_DiagnosticClientThrows_ReturnsErrorResponse()
    {
        RealtimeManager manager = new(TimeProvider.System);
        var processId = await RegisterProcessWithClient(
            manager,
            client =>
                client
                    .GetDrillDown(Arg.Any<DrillDownRequest>())
                    .Returns(Task.FromException<DrillDownResponse>(new InvalidOperationException("client exploded")))
        );

        DrillDownResponse response = await manager.GetDrillDown(
            new ProcessDrillDownRequest { Id = processId, ObjectPaths = ["a|b"] }
        );

        response.ErrorMessage.Should().Be("client exploded");
    }

    private static string RegisterProcess(RealtimeManager manager)
    {
        return RegisterProcess(manager, "default");
    }

    private static string RegisterProcess(RealtimeManager manager, string suffix)
    {
        manager.Register(
            new Registration
            {
                ProcessName = $"test-process-{suffix}",
                MachineName = "test-machine",
                UserName = "test-user",
                InstanceId = $"test-instance-{suffix}",
            }
        );
        return manager.GetProcesses().Single(process => process.InstanceId == $"test-instance-{suffix}").Id;
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
