using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

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
            ? new ValidationResult($"{validationContext.DisplayName} contains an unsupported control character.")
            : ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class PortNumberAttribute() : RangeAttribute(1, 65535)
{
    public override string FormatErrorMessage(string name)
    {
        return $"{name} must be between 1 and 65535.";
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class IpAddressAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        return value is string text && ValidationRules.IsIpAddress(text)
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be a valid IP address.");
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class CidrRangeAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        return value is string text && ValidationRules.IsCidrRange(text)
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be a valid IP address or CIDR range.");
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class HostNameOrIpAddressAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        return value is string text && ValidationRules.IsHostNameOrIpAddress(text)
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be a hostname or IP address, not a URL or path.");
    }
}

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

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class CertificateNamesAttribute : ValidationAttribute
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
            if (!ValidationRules.IsCertificateName(entry))
            {
                return new ValidationResult($"{validationContext.DisplayName} contains an invalid certificate name: {entry}.");
            }
        }

        return ValidationResult.Success;
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class MicrosoftTenantIdAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        return value is string text && ValidationRules.IsMicrosoftTenantId(text)
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be a tenant GUID or tenant domain.");
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class GuidTextAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        return value is string text && ValidationRules.IsGuidText(text)
            ? ValidationResult.Success
            : new ValidationResult($"{validationContext.DisplayName} must be a GUID.");
    }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class AllowedFileExtensionsAttribute(params string[] extensions) : ValidationAttribute
{
    private readonly string[] _extensions = extensions
        .Select(extension => extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 })
        {
            return ValidationResult.Success;
        }

        var fileName = value switch
        {
            IFormFile file => file.FileName,
            string text => text,
            _ => null
        };

        if (fileName is not null && ValidationRules.HasAllowedExtension(fileName, _extensions))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult($"{validationContext.DisplayName} must use one of these file extensions: {string.Join(", ", _extensions)}.");
    }
}
