namespace DiagnosticExplorer.UnitTests;

/// <summary>
///     Serialises the tests that replace the process-wide diagnostic configuration.
/// </summary>
/// <remarks>
///     The configuration and the getter cache it feeds are static, so two classes reconfiguring at
///     once race: one test's configuration is in force while another renders. Each class already
///     restores the default when it finishes, which is enough against ordering but not against
///     concurrency, and the failure it produces is intermittent and blames the wrong test.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiagnosticConfigurationCollection
{
    public const string Name = "Diagnostic configuration";

    private DiagnosticConfigurationCollection() { }
}
