namespace DiagnosticExplorer.Trace;

internal sealed class ScopeStack
{
    private ScopeStack() { }

    private ScopeStack(TraceScope current, ScopeStack parent)
    {
        Current = current;
        Parent = parent;
    }

    public static ScopeStack Empty { get; } = new();

    public TraceScope Current { get; }

    private ScopeStack Parent { get; }

    public bool IsEmpty => Current == null;

    public ScopeStack Push(TraceScope scope)
    {
        return new ScopeStack(scope, this);
    }

    public ScopeStack Remove(TraceScope scope)
    {
        if (IsEmpty)
        {
            return this;
        }

        if (ReferenceEquals(Current, scope))
        {
            return Parent ?? Empty;
        }

        var updatedParent = Parent?.Remove(scope) ?? Empty;
        if (ReferenceEquals(updatedParent, Parent))
        {
            return this;
        }

        return new ScopeStack(Current, updatedParent);
    }
}
