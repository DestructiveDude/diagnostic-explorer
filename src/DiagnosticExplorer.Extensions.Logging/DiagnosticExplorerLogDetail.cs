using System.Text;
using Microsoft.Extensions.Logging;

namespace DiagnosticExplorer.Extensions.Logging;

/// <summary>
///     Splits a rendered log message into the one-line headline the event list shows and the
///     multi-line detail the event pane shows, folding the exception, event id, state and scopes
///     into the latter.
/// </summary>
internal static class DiagnosticExplorerLogDetail
{
    public static string? GetHeadline(string? message)
    {
        // Written as an explicit null test rather than string.IsNullOrEmpty because only net10.0
        // carries the [NotNullWhen] annotation that narrows the latter; net48 does not.
        if (message is null || message.Length == 0)
        {
            return message;
        }

        int newLine = message.IndexOfAny(['\r', '\n']);
        return newLine < 0 ? message : message.Substring(0, newLine);
    }

    public static string? Create<TState>(
        string? message,
        Exception? exception,
        EventId eventId,
        TState state,
        IExternalScopeProvider? scopeProvider
    )
    {
        StringBuilder detail = new();
        string? headline = GetHeadline(message);
        if (message is not null && headline is not null && message.Length != headline.Length)
        {
            detail.AppendLine(message);
        }

        if (exception != null)
        {
            detail.AppendLine(exception.ToString());
        }

        if (eventId.Id != 0 || !string.IsNullOrEmpty(eventId.Name))
        {
            detail.AppendLine($"EventId: {eventId.Id} {eventId.Name}".TrimEnd());
        }

        AppendState(detail, state, "State");
        scopeProvider?.ForEachScope((scope, builder) => AppendState(builder, scope, "Scope"), detail);
        return detail.Length == 0 ? null : detail.ToString().TrimEnd();
    }

    private static void AppendState<TState>(StringBuilder detail, TState state, string prefix)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> properties)
        {
            return;
        }

        foreach (KeyValuePair<string, object?> property in properties)
        {
            if (property.Key == "{OriginalFormat}")
            {
                continue;
            }

            detail.Append(prefix).Append('.').Append(property.Key).Append(": ").AppendLine(property.Value?.ToString());
        }
    }
}
