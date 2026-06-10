namespace DiagnosticExplorer.Trace;

internal sealed class ScopeStack
{
    public static ScopeStack Empty { get; } = new();

    private ScopeStack()
    {
    }

    private ScopeStack(TraceScope current, ScopeStack parent)
    {
        Current = current;
        Parent = parent;
    }

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

        ScopeStack updatedParent = Parent?.Remove(scope) ?? Empty;
        if (ReferenceEquals(updatedParent, Parent))
        {
            return this;
        }

        return new ScopeStack(Current, updatedParent);
    }
}
