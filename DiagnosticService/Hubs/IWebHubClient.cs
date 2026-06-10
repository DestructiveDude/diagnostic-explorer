using System.Threading.Tasks;
using DiagnosticExplorer;
using DiagnosticExplorer.Common;

namespace Diagnostics.Service.Common.Hubs;

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