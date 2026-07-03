namespace Relaywright.Web.Validation;

public static partial class ValidationRules
{
    public static bool ContainsDisallowedControlCharacter(
        string? value,
        bool allowLineBreaks,
        out char invalidCharacter)
    {
        invalidCharacter = '\0';
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsControl(character))
            {
                continue;
            }

            if (allowLineBreaks && character is '\r' or '\n' or '\t')
            {
                continue;
            }

            invalidCharacter = character;
            return true;
        }

        return false;
    }
}
