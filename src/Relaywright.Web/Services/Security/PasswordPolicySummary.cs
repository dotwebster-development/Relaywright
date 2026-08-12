using Microsoft.AspNetCore.Identity;

namespace Relaywright.Web.Services.Security;

public sealed record PasswordPolicySummary(
    int RequiredLength,
    int RequiredUniqueChars,
    bool RequireDigit,
    bool RequireLowercase,
    bool RequireUppercase,
    bool RequireNonAlphanumeric)
{
    public static PasswordPolicySummary FromOptions(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new PasswordPolicySummary(
            options.Password.RequiredLength,
            options.Password.RequiredUniqueChars,
            options.Password.RequireDigit,
            options.Password.RequireLowercase,
            options.Password.RequireUppercase,
            options.Password.RequireNonAlphanumeric);
    }

    public IReadOnlyList<string> Requirements
    {
        get
        {
            var requirements = new List<string> { $"At least {RequiredLength} characters" };
            if (RequiredUniqueChars > 1) requirements.Add($"At least {RequiredUniqueChars} unique characters");
            if (RequireDigit) requirements.Add("At least one number");
            if (RequireLowercase) requirements.Add("At least one lowercase letter");
            if (RequireUppercase) requirements.Add("At least one uppercase letter");
            if (RequireNonAlphanumeric) requirements.Add("At least one symbol");
            return requirements;
        }
    }

    public string CompactDescription => string.Join(", ", Requirements);
}
