using System.Net;
using AwesomeAssertions;
using DiagnosticExplorer.Hosting;
using DiagnosticExplorer.Interface;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     HubServerAdapter's RPC wrappers must surface a failed <see cref="RpcResult" /> as an
///     <see cref="InvalidOperationException" /> instead of treating a failed round trip as
///     success — otherwise a rejected Register is mistaken for a live registration and a
///     failed Deregister/LogEvents passes silently. Removing the IsSuccess check turns these
///     red. (DE-15)
///     HubServerAdapter is internal and DiagnosticExplorer.Hosting grants no
///     InternalsVisibleTo, so the adapter is reached by reflection; its HubConnection is
///     ctor-injected, which is the seam that makes the fake possible without production
///     changes.
/// </summary>
public class HubServerAdapterFailureTests
{
    private static readonly Type AdapterType =
        typeof(RegistrationHandler).Assembly.GetType("DiagnosticExplorer.Hosting.HubServerAdapter")
        ?? throw new InvalidOperationException(
            "DiagnosticExplorer.Hosting.HubServerAdapter not found"
        );

    /// <summary>
    ///     A failed RpcResult from the hub must throw InvalidOperationException carrying the
    ///     hub's failure message — the round trip failed even though the transport succeeded.
    ///     (DE-15)
    /// </summary>
    [Theory]
    [InlineData("Register")]
    [InlineData("Deregister")]
    [InlineData("LogEvents")]
    public async Task FailedRpcResult_ThrowsInvalidOperationException(string methodName)
    {
        HubConnection hub = CreateHubSubstitute();
        hub.InvokeCoreAsync(
                Arg.Any<string>(),
                Arg.Any<Type>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo =>
            {
                RpcResult failure =
                    callInfo.Arg<Type>() == typeof(RpcResult)
                        ? RpcResult.Fail("request-1", "hub exploded", "detail")
                        : RpcResult<RegistrationResponse>.Fail(
                            "request-1",
                            "hub exploded",
                            "detail"
                        );
                return Task.FromResult<object?>(failure);
            });

        using IDisposable adapter = CreateAdapter(hub);

        Func<Task> act = async () => await Invoke(adapter, methodName);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("hub exploded");
    }

    /// <summary>
    ///     A successful Register round trip returns the hub's RegistrationResponse untouched.
    ///     (DE-15)
    /// </summary>
    [Fact]
    public async Task Register_SuccessfulRpcResult_ReturnsResponse()
    {
        RegistrationResponse response = new(TimeSpan.FromSeconds(30));
        HubConnection hub = CreateHubSubstitute();
        hub.InvokeCoreAsync(
                Arg.Any<string>(),
                Arg.Any<Type>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<object?>(RpcResult<RegistrationResponse>.Success(response)));

        using IDisposable adapter = CreateAdapter(hub);

        RegistrationResponse result = await (Task<RegistrationResponse>)Invoke(adapter, "Register");

        result.Should().BeSameAs(response);
    }

    /// <summary>
    ///     A successful Deregister/LogEvents round trip completes quietly — the throw must be
    ///     conditional on IsSuccess, not unconditional. (DE-15)
    /// </summary>
    [Theory]
    [InlineData("Deregister")]
    [InlineData("LogEvents")]
    public async Task SuccessfulRpcResult_CompletesWithoutThrowing(string methodName)
    {
        HubConnection hub = CreateHubSubstitute();
        hub.InvokeCoreAsync(
                Arg.Any<string>(),
                Arg.Any<Type>(),
                Arg.Any<object?[]>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<object?>(RpcResult.Success("request-1")));

        using IDisposable adapter = CreateAdapter(hub);

        Func<Task> act = async () => await Invoke(adapter, methodName);

        await act.Should().NotThrowAsync();
    }

    // HubConnection (SignalR.Client 8.x) is concrete with virtual RPC methods and no
    // parameterless ctor, so the substitute must be given its five ctor dependencies.
    private static HubConnection CreateHubSubstitute()
    {
        return Substitute.For<HubConnection>(
            Substitute.For<IConnectionFactory>(),
            Substitute.For<IHubProtocol>(),
            new IPEndPoint(IPAddress.Loopback, 5000),
            Substitute.For<IServiceProvider>(),
            NullLoggerFactory.Instance
        );
    }

    private static IDisposable CreateAdapter(HubConnection hub)
    {
        return (IDisposable)(
            Activator.CreateInstance(AdapterType, hub)
            ?? throw new InvalidOperationException("Failed to construct HubServerAdapter")
        );
    }

    private static Task Invoke(IDisposable adapter, string methodName)
    {
        object?[] args = methodName switch
        {
            "Register" => [new Registration(), CancellationToken.None],
            "Deregister" => [new Registration(), CancellationToken.None],
            "LogEvents" => [new byte[] { 1, 2, 3 }, CancellationToken.None],
            _ => throw new ArgumentOutOfRangeException(nameof(methodName)),
        };

        return (Task)(
            AdapterType.GetMethod(methodName)!.Invoke(adapter, args)
            ?? throw new InvalidOperationException($"{methodName} returned null")
        );
    }
}
