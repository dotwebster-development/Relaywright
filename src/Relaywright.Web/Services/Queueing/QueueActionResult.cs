namespace Relaywright.Web.Services.Queueing;

public sealed class QueueActionResult
{
    private QueueActionResult(bool succeeded, string message)
    {
        Succeeded = succeeded;
        Message = message;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public static QueueActionResult Success(string message)
    {
        return new QueueActionResult(true, message);
    }

    public static QueueActionResult Failure(string message)
    {
        return new QueueActionResult(false, message);
    }
}
