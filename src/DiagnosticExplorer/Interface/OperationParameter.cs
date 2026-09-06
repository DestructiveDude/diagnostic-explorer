namespace DiagnosticExplorer;

public class OperationParameter
{
    public OperationParameter() { }

    public OperationParameter(string name, string type)
    {
        Name = name;
        Type = type;
    }

    public string Name { get; set; }

    public string Type { get; set; }
}
