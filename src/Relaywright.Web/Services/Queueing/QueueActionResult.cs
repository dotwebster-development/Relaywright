namespace Relaywright.Web.Services.Queueing;

public sealed class QueueActionResult
{
    private QueueActionResult(QueueActionOutcome outcome, string message)
    {
        Outcome = outcome;
        Message = message;
    }

    public QueueActionOutcome Outcome { get; }

    public bool Succeeded => Outcome == QueueActionOutcome.Succeeded;

    public string Message { get; }

    public static QueueActionResult Success(string message)
    {
        return new QueueActionResult(QueueActionOutcome.Succeeded, message);
    }

    public static QueueActionResult Failure(QueueActionOutcome outcome, string message)
    {
        if (outcome == QueueActionOutcome.Succeeded)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), "A failure cannot have the succeeded outcome.");
        }

        return new QueueActionResult(outcome, message);
    }
}

public enum QueueActionOutcome
{
    Succeeded,
    NotFound,
    InvalidState,
    SpoolDeleteFailed
}
