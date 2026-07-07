namespace Relaywright.Web.Validation;

public static partial class ValidationRules
{
    private static readonly char[] Delimiters = [',', ';', '\r', '\n', '\t', ' '];

    public static IReadOnlyList<string> SplitDelimitedList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(Delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    public static string? NormalizeDelimitedList(string? value)
    {
        var entries = SplitDelimitedList(value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return entries.Length == 0 ? null : string.Join(Environment.NewLine, entries);
    }
}
