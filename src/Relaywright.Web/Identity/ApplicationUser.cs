using Microsoft.AspNetCore.Identity;

namespace Relaywright.Web.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "Administrator";
}

