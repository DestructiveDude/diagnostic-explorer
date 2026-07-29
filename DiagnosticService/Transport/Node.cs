namespace Diagnostic.Service.Transport;

public class Node
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Uri { get; set; } = null!;

    public string? ParentId { get; set; }
}
