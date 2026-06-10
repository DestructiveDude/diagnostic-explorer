namespace Diagnostic.Service.Transport;

public class SetPropertyRequest
{
    public string Id { get; set; } = null!;

    public string Path { get; set; } = null!;

    public string? Value { get; set; }
}