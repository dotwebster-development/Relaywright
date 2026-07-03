using System.ComponentModel.DataAnnotations;

namespace Relaywright.Web.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class MailboxAddressAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        return value is string text && ValidationRules.IsMailboxAddress(text)
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be a mailbox address like user@example.com.");
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class SenderPolicyListAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        if (value is not string text)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be text.");
        }

        foreach (var entry in ValidationRules.SplitDelimitedList(text))
        {
            if (!ValidationRules.IsSenderPolicyPattern(entry))
            {
                return new ValidationResult($"{validationContext.DisplayName} contains an invalid sender entry: {entry}.");
            }
        }

        return ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class RecipientDomainPolicyListAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        if (value is not string text)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be text.");
        }

        foreach (var entry in ValidationRules.SplitDelimitedList(text))
        {
            if (!ValidationRules.IsRecipientDomainPattern(entry))
            {
                return new ValidationResult($"{validationContext.DisplayName} contains an invalid domain entry: {entry}.");
            }
        }

        return ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class MailboxListAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        if (value is not string text)
        {
            return new ValidationResult($"{validationContext.DisplayName} must be text.");
        }

        foreach (var entry in ValidationRules.SplitDelimitedList(text))
        {
            if (!ValidationRules.IsMailboxAddress(entry))
            {
                return new ValidationResult($"{validationContext.DisplayName} contains an invalid mailbox address: {entry}.");
            }
        }

        return ValidationResult.Success;
    }
}
