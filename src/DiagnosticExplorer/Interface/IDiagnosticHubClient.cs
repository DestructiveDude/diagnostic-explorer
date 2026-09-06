#region Copyright

// Diagnostic Explorer, a .Net diagnostic toolset
// Copyright (C) 2010 Cameron Elliot
//
// This file is part of Diagnostic Explorer.
//
// Diagnostic Explorer is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Diagnostic Explorer is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with Diagnostic Explorer.  If not, see <http://www.gnu.org/licenses/>.
//
// http://diagexplorer.sourceforge.net/

#endregion

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DiagnosticExplorer;

/// <summary>
///     Calls the service makes INTO an agent. The request/response members return a value, which
///     makes them SignalR client results: SignalR owns the correlation and the disconnect
///     handling that this codebase previously hand-rolled with a request id and a shared
///     AsyncResultBucket.
/// </summary>
/// <remarks>
///     SignalR does NOT own the timeout, which the AsyncResultBucket did (a 10 second ceiling).
///     A client result invoked without a token waits forever: SignalR's TypedClientBuilder passes
///     CancellationToken.None when the interface method declares no trailing CancellationToken
///     (TypedClientBuilder.cs, release/8.0, lines 148-152 and 214-222), and an agent that never
///     sends a completion — because its response failed to serialise, say — then parks the caller
///     indefinitely on a connection that is otherwise healthy. Hence the trailing token on every
///     member that waits for a value; TypedClientBuilder strips it from the wire arguments and
///     hands it to InvokeCoreAsync.
/// </remarks>
public interface IDiagnosticHubClient
{
    Task<DiagnosticResponse> GetDiagnostics(CancellationToken cancel);
    Task<OperationResponse> ExecuteOperation(
        string path,
        string operation,
        string[] arguments,
        CancellationToken cancel
    );
    Task<OperationResponse> SetProperty(string path, string value, CancellationToken cancel);
    Task SubscribeEvents();
    Task UnsubscribeEvents();
}

public interface IDiagnosticHubServer
{
    Task<RpcResult<RegistrationResponse>> Register(Registration registration);
    Task<RpcResult> Deregister(Registration registration);

    // Sent as a typed array rather than a protobuf-compressed blob: MessagePack frames it on the
    // wire, so the manual serialize-and-gzip step is gone.
    Task<RpcResult> LogEvents(DiagnosticMsg[] messages);
    Task SetEvents(SystemEvent[] events);
    Task StreamEvents(SystemEvent[] evt);
}

public class RpcResult<T> : RpcResult
{
    public T Response { get; set; }

    public static RpcResult<T> Success(T result)
    {
        return new RpcResult<T> { IsSuccess = true, Response = result };
    }

    public static RpcResult<T> Success(string requestId, T result)
    {
        return new RpcResult<T>
        {
            RequestId = requestId,
            IsSuccess = true,
            Response = result,
        };
    }

    public static new RpcResult<T> Fail(string requestId, string message, string detail)
    {
        return new RpcResult<T>
        {
            RequestId = requestId,
            IsSuccess = false,
            Message = message,
            Detail = detail,
        };
    }

    public static new RpcResult<T> Fail(string requestId, Exception ex)
    {
        return new RpcResult<T>
        {
            RequestId = requestId,
            IsSuccess = false,
            Message = ex.Message,
            Detail = ex.ToString(),
        };
    }
}

public class RpcResult
{
    public string RequestId { get; set; }
    public bool IsSuccess { get; set; }
    public string Message { get; set; }
    public string Detail { get; set; }

    public static RpcResult Success(string requestId = null)
    {
        return new RpcResult { RequestId = requestId, IsSuccess = true };
    }

    public static RpcResult Fail(string requestId, string message, string detail)
    {
        return new RpcResult
        {
            RequestId = requestId,
            IsSuccess = false,
            Message = message,
            Detail = detail,
        };
    }

    public static RpcResult Fail(string requestId, Exception ex)
    {
        return new RpcResult
        {
            RequestId = requestId,
            IsSuccess = false,
            Message = ex.Message,
            Detail = ex.ToString(),
        };
    }
}
