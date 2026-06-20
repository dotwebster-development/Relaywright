namespace Relaywright.Web.Services.Queueing;

public sealed class RetryDelayCalculator
{
    public TimeSpan Calculate(int attemptNumber, int initialRetryDelaySeconds, int maxRetryDelaySeconds)
    {
        var sanitizedAttempt = Math.Max(1, attemptNumber);
        var baseDelay = Math.Max(1, initialRetryDelaySeconds);
        var maxDelay = Math.Max(baseDelay, maxRetryDelaySeconds);
        var multiplier = Math.Pow(2, sanitizedAttempt - 1);
        var seconds = Math.Min(maxDelay, baseDelay * multiplier);
        return TimeSpan.FromSeconds(seconds);
    }
}

