using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Security;

public interface ITrustedDeviceRateLimiter
{
    SubmissionPolicyDecision CanAcceptMessage(TrustedNetwork profile, string? remoteIpAddress);
}
