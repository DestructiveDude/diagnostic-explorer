using System.Reflection;
using AwesomeAssertions;
using Diagnostic.Service.Hubs;
using Xunit;

namespace DiagnosticService.UnitTests.Hubs;

/// <summary>
///     The SPA and the service agree on hub method names purely by string literal: the Angular
///     client invokes and subscribes by name, and SignalR dispatches by name. A server-side rename
///     compiles clean on both sides and only fails at runtime. These reflection tests pin the
///     server-side names against an explicit list that mirrors the SPA's literals. (DE-24)
/// </summary>
/// <remarks>
///     The name lists below are the contract point — duplicated from the TypeScript deliberately
///     (the .NET tests must not read the SPA sources). When you change a list here, update the
///     matching SPA file:
///     <list type="bullet">
///         <item>
///             RPC methods the SPA invokes: diagnostics-web/src/app/services/diag-hub.service.ts
///             (<c>connection.invoke(...)</c>) and diagnostics-web/src/app/Model/RealtimeModel.ts
///             (<c>'Subscribe'</c>).
///         </item>
///         <item>
///             Push handlers the SPA registers: <c>connection.on(...)</c> in
///             diagnostics-web/src/app/Model/RealtimeModel.ts and
///             diagnostics-web/src/app/Model/RetroModel.ts.
///         </item>
///     </list>
/// </remarks>
public sealed class WebHubContractTests
{
    // Mirrors the invoke literals in diag-hub.service.ts, plus 'Subscribe' from RealtimeModel.ts.
    private static readonly string[] SpaInvokedHubMethods =
    [
        "CancelRetroSearch",
        "ExecuteOperation",
        "RemoveProcess",
        "RetroDelete",
        "RetroSupportsDelete",
        "SetProperty",
        "StartRetroSearch",
        "Subscribe",
    ];

    // Mirrors the connection.on(...) registrations in RealtimeModel.ts (first seven) and
    // RetroModel.ts (last three).
    private static readonly string[] SpaRegisteredPushHandlers =
    [
        "SetProcesses",
        "UpdateProcess",
        "RemoveProcess",
        "ShowDiagnostics",
        "ShowDiagnosticsError",
        "SetEvents",
        "StreamEvents",
        "ProcessSearchResults",
        "ProcessSearchEnd",
        "ProcessSearchError",
    ];

    /// <summary>
    ///     Every public RPC method on WebHub must be one the SPA invokes, and every SPA invoke
    ///     literal must have a hub method — a rename on either side breaks the set equality. The
    ///     OnConnected/OnDisconnected lifecycle overrides are excluded: they are not SPA-callable.
    /// </summary>
    [Fact]
    public void WebHub_RpcMethodNames_MatchTheSpaInvokeLiterals()
    {
        string[] hubMethods = typeof(WebHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m =>
                m.Name is not (nameof(WebHub.OnConnectedAsync) or nameof(WebHub.OnDisconnectedAsync))
            )
            .Select(m => m.Name)
            .Order()
            .ToArray();

        hubMethods
            .Should()
            .Equal(
                SpaInvokedHubMethods.Order(),
                "the SPA invokes WebHub methods by string literal; a rename must be mirrored in "
                    + "diag-hub.service.ts / RealtimeModel.ts (DE-24)"
            );
    }

    /// <summary>
    ///     The IWebHubClient callback interface is what the server pushes on; each method name must
    ///     match a connection.on(...) registration in the SPA, and vice versa.
    /// </summary>
    [Fact]
    public void WebHubClient_CallbackNames_MatchTheSpaPushHandlers()
    {
        string[] callbackNames = typeof(IWebHubClient)
            .GetMethods()
            .Select(m => m.Name)
            .Order()
            .ToArray();

        callbackNames
            .Should()
            .Equal(
                SpaRegisteredPushHandlers.Order(),
                "the SPA registers push handlers by string literal; a rename must be mirrored in "
                    + "RealtimeModel.ts / RetroModel.ts (DE-24)"
            );
    }
}
