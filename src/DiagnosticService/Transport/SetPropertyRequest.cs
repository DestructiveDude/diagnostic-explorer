namespace Diagnostic.Service.Transport;

public class SetPropertyRequest
{
    public string Id { get; set; } = null!;

    /// <summary>The drilldown the edit was made in, empty for the main view.</summary>
    public string[] ObjectPaths { get; set; } = [];
    public string Path { get; set; } = null!;
    public string? Value { get; set; }
}
