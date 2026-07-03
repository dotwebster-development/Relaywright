using System.ComponentModel.DataAnnotations;

namespace Relaywright.Web.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class PortNumberAttribute()
    : RangeAttribute(ValidationLimits.MinimumPort, ValidationLimits.MaximumPort)
{
    public override string FormatErrorMessage(string name)
    {
        return ValidationMessages.PortRange(name);
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
            : new ValidationResult(ValidationMessages.HostNameOrIpAddress(validationContext.DisplayName));
    }
}
