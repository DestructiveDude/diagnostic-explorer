using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using FixPortal.CodeStyle.ArchRules;
using DiagnosticExplorer;
using DiagnosticExplorer.Hosting;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace DiagnosticExplorer.UnitTests;

public class ArchitectureTests
{
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(DiagnosticManager).Assembly,
            typeof(DiagnosticHostingService).Assembly)
        .Build();

    [Fact]
    public void Interfaces_must_have_I_prefix()
    {
        FixPortalArchRules.InterfacesMustHaveIPrefix()
            .Check(Architecture);
    }

    [Fact]
    public void Exception_types_must_inherit_from_Exception()
    {
        FixPortalArchRules.ExceptionsMustInheritFromException()
            .Check(Architecture);
    }

    [Fact]
    public void Async_methods_must_end_in_Async()
    {
        FixPortalArchRules.AsyncMethodsMustEndInAsync()
            .Check(Architecture);
    }

    [Fact]
    public void Namespace_slices_must_be_free_of_cycles()
    {
        FixPortalArchRules.NamespaceSlicesMustBeFreeOfCycles("DiagnosticExplorer.(*)")
            .Check(Architecture);
    }
}
