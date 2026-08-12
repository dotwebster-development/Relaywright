using Microsoft.AspNetCore.Identity;

namespace Relaywright.Web.Services.Security;

public static class AdminSecurityDefaults
{
    public static void ConfigureSecurityStampValidator(SecurityStampValidatorOptions options)
    {
        options.ValidationInterval = TimeSpan.Zero;
    }
}
