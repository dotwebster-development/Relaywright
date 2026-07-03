using System.ComponentModel.DataAnnotations;

namespace Relaywright.Web.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class RequiredNonWhiteSpaceAttribute : ValidationAttribute
{
    public RequiredNonWhiteSpaceAttribute()
    {
        ErrorMessage = "The {0} field is required.";
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        return value is string text && string.IsNullOrWhiteSpace(text)
            ? new ValidationResult(FormatErrorMessage(validationContext.DisplayName))
            : value is null
                ? new ValidationResult(FormatErrorMessage(validationContext.DisplayName))
                : ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class NoControlCharactersAttribute(bool allowLineBreaks = false) : ValidationAttribute
{
    public bool AllowLineBreaks { get; } = allowLineBreaks;

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        if (value is not string text)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be text.");
        }

        return ValidationRules.ContainsDisallowedControlCharacter(text, AllowLineBreaks, out _)
            ? new ValidationResult(ValidationMessages.UnsupportedControlCharacter(validationContext.DisplayName))
            : ValidationResult.Success;
    }
}
