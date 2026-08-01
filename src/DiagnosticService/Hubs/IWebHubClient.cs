using Diagnostic.Service.Common;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Interface;

namespace Diagnostic.Service.Hubs;

public interface IWebHubClient
{
    Task ShowDiagnostics(string id, DiagnosticResponse response);
    Task ShowDiagnosticsError(string id, string message);
    Task SetProcesses(DiagProcess[] processes);
    Task UpdateProcess(DiagProcess processes);
    Task RemoveProcess(string id);
    Task SetEvents(string id, SystemEvent[] events);
    Task StreamEvents(string id, IList<SystemEvent> evt);
    Task ProcessSearchResults(RetroSearchResult result);
    Task ProcessSearchEnd(int searchId);
    Task ProcessSearchError(int searchId, string message, string detail);
}
