using SplunkTui.Models;

namespace SplunkTui.Formatters;

/// <summary>
/// Shared utilities for event formatters.
/// </summary>
internal static class FormatterUtils
{
    /// <summary>
    /// Filters an event to only include specified fields.
    /// If fields is null or empty, returns all fields.
    /// </summary>
    public static Dictionary<string, string?> FilterFields(SplunkEvent evt, string[]? fields)
    {
        if (fields == null || fields.Length == 0)
        {
            return new Dictionary<string, string?>(evt, StringComparer.Ordinal);
        }

        var filtered = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in fields.Where(f => evt.ContainsKey(f)))
        {
            filtered[field] = evt[field];
        }
        return filtered;
    }
}
