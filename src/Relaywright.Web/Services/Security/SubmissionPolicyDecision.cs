namespace Relaywright.Web.Services.Security;

public sealed class SubmissionPolicyDecision
{
    private SubmissionPolicyDecision(bool allowed, string message)
    {
        Allowed = allowed;
        Message = message;
    }

    public bool Allowed { get; }

    public string Message { get; }

    public static SubmissionPolicyDecision Allow(string message = "Allowed.")
    {
        return new SubmissionPolicyDecision(true, message);
    }

    public static SubmissionPolicyDecision Deny(string message)
    {
        return new SubmissionPolicyDecision(false, message);
    }
}
