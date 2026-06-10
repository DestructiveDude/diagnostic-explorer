namespace DiagnosticExplorer.Interface;

public class OperationResponse
{
    public static OperationResponse Success()
    {
        return new() { IsSuccess = true };
    }

    public static OperationResponse Success(string result)
    {
        return new() { IsSuccess = true, Result = result };
    }

    public static OperationResponse Error(string message)
    {
        return new() { IsSuccess = false, ErrorMessage = message };
    }

    public static OperationResponse Error(string message, string detail)
    {
        return new()
        {
            IsSuccess = false,
            ErrorMessage = message,
            ErrorDetail = detail
        };
    }

    public bool IsSuccess { get; set; }
    public string Result { get; set; }
    public string ErrorMessage { get; set; }
    public string ErrorDetail { get; set; }
}
