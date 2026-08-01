namespace Diagnostic.Service.Transport;

public class ExecuteOperationRequest
{
    public string Id { get; set; } = null!;

    public string Path { get; set; } = null!;

    public string Operation { get; set; } = null!;

    public string[] Arguments { get; set; } = Array.Empty<string>();
}
