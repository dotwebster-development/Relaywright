namespace Relaywright.Web.Options;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "BootstrapAdmin";

    public const string DefaultDevelopmentPassword = "ChangeMe!12345";

    public string UserName { get; set; } = "admin";

    public string Email { get; set; } = "admin@localhost";

    public string Password { get; set; } = DefaultDevelopmentPassword;
}
