using DiagnosticExplorer.Interface;

namespace Diagnostic.Service.Transport;

public class Operation
{
    public string ReturnType { get; set; } = null!;

    public string Signature { get; set; } = null!;

    public string Name { get; set; } = null!;

    public List<KeyValuePair<string, string>> Parameters { get; set; } = [];

    public static List<Operation> GetOperationSet(string operationSetId, List<OperationSet> operationSets)
    {
        OperationSet? operationSet = operationSets.FirstOrDefault(x => x.Id == operationSetId);
        if (operationSet == null)
        {
            return [];
        }

        return GetOperationSet(operationSet);
    }

    public static List<Operation> GetOperationSet(OperationSet operationSet)
    {
        List<Operation> result = [];
        operationSet.Operations.ForEach(op => {
            result.Add(new Operation
            {
                ReturnType = op.ReturnType,
                Parameters = op.Parameters != null ? op.Parameters.Select(x => new KeyValuePair<string, string>(x.Name, x.Type)).ToList() : [],
                Signature = op.Signature,
                Name = op.Signature.Substring(0, op.Signature.IndexOf('('))
            });
        });

        return result;
    }


}