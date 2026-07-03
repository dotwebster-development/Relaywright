using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Relaywright.Web.Validation;

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
            : new ValidationResult(ValidationMessages.TenantId(validationContext.DisplayName));
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

        return new ValidationResult(ValidationMessages.FileExtension(validationContext.DisplayName, _extensions));
    }
}
