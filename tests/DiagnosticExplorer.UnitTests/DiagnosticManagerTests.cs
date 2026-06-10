using AwesomeAssertions;
using DiagnosticExplorer.Events;
using DiagnosticExplorer.Props;

namespace DiagnosticExplorer.UnitTests;

public class DiagnosticManagerTests
{
    public class GenericAndNonGenericMethodsClass
    {
        [DiagnosticMethod]
        public void NonGenericMethod()
        {
            // Empty method for testing
        }

        [DiagnosticMethod]
        public T GenericMethod<T>(T val)
        {
            return val;
        }
    }

    [Fact]
    public void IsMethodValidOperationTarget_FiltersOutGenericMethods()
    {
        var obj = new GenericAndNonGenericMethodsClass();
        var registered = new RegisteredObject(obj, "TestCategory", "TestBag");
        var response = DiagnosticManager.GetDiagnostics(new[] { registered });

        response.Should().NotBeNull();
        response.OperationSets.Should().HaveCount(1);
        var opSet = response.OperationSets[0];

        opSet.Operations.Should().Contain(o => o.Signature.StartsWith(nameof(GenericAndNonGenericMethodsClass.NonGenericMethod)));
        opSet.Operations.Should().NotContain(o => o.Signature.StartsWith(nameof(GenericAndNonGenericMethodsClass.GenericMethod)));
    }

    public class CustomParseException : Exception
    {
        public CustomParseException(string message) : base(message) { }
    }

    public class CustomParsableType
    {
        public static CustomParsableType Parse(string value)
        {
            throw new CustomParseException("Parse failed for value: " + value);
        }
    }

    public class OperationWithCustomParsableType
    {
        [DiagnosticMethod]
#pragma warning disable IDE0060
        public void Run(CustomParsableType arg)
#pragma warning restore IDE0060
        {
            // Empty method for testing
        }
    }

    [Fact]
    public void ExecuteOperation_UnwrapsTargetInvocationException_ForCustomParsableType()
    {
        var obj = new OperationWithCustomParsableType();
        var registered = new RegisteredObject(obj, "TestCategory", "TestBag");

        // Let's obtain the operation signature
        var response = DiagnosticManager.GetDiagnostics(new[] { registered });
        response.OperationSets.Should().HaveCount(1);
        var signature = response.OperationSets[0].Operations[0].Signature;

        var result = DiagnosticManager.ExecuteOperation(
            new[] { registered },
            "TestCategory|TestBag",
            signature,
            new[] { "invalid-val" }
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Match(m =>
            m.Contains("can't convert 'invalid-val' to CustomParsableType") ||
            m.Contains("Parse failed for value: invalid-val"));
        result.ErrorDetail.Should().Contain(nameof(CustomParseException));
        result.ErrorDetail.Should().Contain("Parse failed for value: invalid-val");
    }

    [Fact]
    public void EventSinkRepo_CanBeDisposed()
    {
        var repo = new EventSinkRepo();
        Action act = () => repo.Dispose();
        act.Should().NotThrow();
    }
}
