namespace Relaywright.Web.Validation;

internal static class ServiceValidation
{
    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void RequireSingleLineText(string? value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        Require(value.Length <= maxLength, ValidationMessages.MaximumLength(label, maxLength));
        Require(
            !ValidationRules.ContainsDisallowedControlCharacter(value, allowLineBreaks: false, out _),
            ValidationMessages.UnsupportedControlCharacter(label));
    }

    public static void RequirePolicyListLength(string? value, string label)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Require(
                value.Length <= ValidationLimits.MaximumPolicyListLength,
                ValidationMessages.MaximumLength(label, ValidationLimits.MaximumPolicyListLength));
        }
    }

    public static void RequirePositive(long? value, string label)
    {
        Require(value is not <= 0, $"{label} must be at least 1.");
    }

    public static void RequirePositive(int? value, string label)
    {
        Require(value is not <= 0, $"{label} must be at least 1.");
    }
}
