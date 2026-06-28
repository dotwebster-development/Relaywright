namespace Relaywright.Web.Services.Queueing;

public sealed class QueueBulkActionResult
{
    public int Requested { get; init; }

    public int Succeeded { get; init; }

    public int Rejected { get; init; }

    public int Missing { get; init; }

    public int SpoolDeleteFailures { get; init; }

    public string Message =>
        $"Processed {Requested} selected message(s): {Succeeded} succeeded, {Rejected} rejected, {Missing} missing, {SpoolDeleteFailures} spool delete failure(s).";
}
