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
using System.Threading.Tasks;
using DiagnosticExplorer.Events;

namespace DiagnosticExplorer.Interface;


public interface IDiagnosticHubClient
{
    Task GetDiagnostics(string requestId);
    Task ExecuteOperation(string requestId, string path, string operation, string[] arguments);
    Task SetProperty(string requestId, string path, string value);
    Task SubscribeEvents();
    Task UnsubscribeEvents();
}

public interface IDiagnosticHubServer
{
    Task<RpcResult<RegistrationResponse>> Register(Registration registration);
    Task<RpcResult> Deregister(Registration registration);
    Task<RpcResult> LogEvents(byte[] eventData);
    Task GetDiagnosticsReturn(RpcResult<byte[]> response);
    Task ExecuteOperationReturn(RpcResult<OperationResponse> response);
    Task SetPropertyReturn(RpcResult<OperationResponse> response);
    Task SetEvents(SystemEvent[] events);
    Task StreamEvents(SystemEvent[] evt);
}


public class RpcResult<T> : RpcResult
{
    public T Response { get; set; }


    public static RpcResult<T> Success(T result)
        => new() { IsSuccess = true, Response = result };

    public static RpcResult<T> Success(string requestId, T result)
        => new() { RequestId = requestId, IsSuccess = true, Response = result };

    public static new RpcResult<T> Fail(string requestId, string message, string detail)
        => new() { RequestId = requestId, IsSuccess = false, Message = message, Detail = detail };

    public static new RpcResult<T> Fail(string requestId, Exception ex)
        => new() { RequestId = requestId, IsSuccess = false, Message = ex.Message, Detail = ex.ToString() };


}

public class RpcResult
{
    public static RpcResult Success(string requestId = null)
        => new() { RequestId = requestId, IsSuccess = true };

    public static RpcResult Fail(string requestId, string message, string detail)
        => new() { RequestId = requestId, IsSuccess = false, Message = message, Detail = detail };

    public static RpcResult Fail(string requestId, Exception ex)
        => new() { RequestId = requestId, IsSuccess = false, Message = ex.Message, Detail = ex.ToString() };

    public string RequestId { get; set; }
    public bool IsSuccess { get; set; }
    public string Message { get; set; }
    public string Detail { get; set; }
}