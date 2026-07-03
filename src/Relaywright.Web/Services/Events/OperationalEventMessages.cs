namespace Relaywright.Web.Services.Events;

public static class OperationalEventMessages
{
    public const string RelayConfigurationUpdated = "Relay configuration updated.";
    public const string SubmissionPolicyUpdated = "Submission policy updated.";

    public static string TrustedNetworkSaved(bool created, string cidr)
    {
        return created
            ? $"Trusted network created: {cidr}."
            : $"Trusted network updated: {cidr}.";
    }

    public static string TrustedNetworkDeleted(string cidr)
    {
        return $"Trusted network deleted: {cidr}.";
    }
}
