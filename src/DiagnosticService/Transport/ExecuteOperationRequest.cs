namespace Diagnostic.Service.Transport;

public class ExecuteOperationRequest
{
    public string Id { get; set; } = null!;

    /// <inheritdoc cref="SetPropertyRequest.RequestId" />
    public string RequestId { get; set; } = "";

    /// <summary>The drilldown the operation was triggered in, empty for the main view.</summary>
    public string[] ObjectPaths { get; set; } = [];
    public string Path { get; set; } = null!;
    public string Operation { get; set; } = null!;
    public string[] Arguments { get; set; } = [];
}
