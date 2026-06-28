using System.Collections.Concurrent;
using Relaywright.Web.Data.Entities;

namespace Relaywright.Web.Services.Security;

public sealed class TrustedDeviceRateLimiter : ITrustedDeviceRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _acceptedMessages = new(StringComparer.OrdinalIgnoreCase);

    public SubmissionPolicyDecision CanAcceptMessage(TrustedNetwork profile, string? remoteIpAddress)
    {
        if (profile.RateLimitMessagesPerHour is null or <= 0)
        {
            return SubmissionPolicyDecision.Allow();
        }

        var key = $"{profile.Id}:{remoteIpAddress ?? "unknown"}";
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddHours(-1);
        var queue = _acceptedMessages.GetOrAdd(key, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() < cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count >= profile.RateLimitMessagesPerHour.Value)
            {
                return SubmissionPolicyDecision.Deny(
                    $"Device profile rate limit exceeded ({profile.RateLimitMessagesPerHour.Value} message(s) per hour).");
            }

            queue.Enqueue(now);
        }

        return SubmissionPolicyDecision.Allow();
    }
}
