namespace Relaywright.Web.Services.Runtime;

public interface IApplicationRestartService
{
    Task<ApplicationRestartRequestResult> RequestRestartAsync(
        string reason,
        string? userName,
        CancellationToken cancellationToken);

    Task ClearAppliedRestartIfNeededAsync(CancellationToken cancellationToken);
}
