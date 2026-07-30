namespace DiagnosticExplorer.Interface;

public class OperationResponse
{
    public bool IsSuccess { get; set; }
    public string Result { get; set; }
    public string ErrorMessage { get; set; }
    public string ErrorDetail { get; set; }

    public static OperationResponse Success()
    {
        return new OperationResponse { IsSuccess = true };
    }

    public static OperationResponse Success(string result)
    {
        return new OperationResponse { IsSuccess = true, Result = result };
    }

    public static OperationResponse Error(string message)
    {
        return new OperationResponse { IsSuccess = false, ErrorMessage = message };
    }

    public static OperationResponse Error(string message, string detail)
    {
        return new OperationResponse
        {
            IsSuccess = false,
            ErrorMessage = message,
            ErrorDetail = detail,
        };
    }
}
