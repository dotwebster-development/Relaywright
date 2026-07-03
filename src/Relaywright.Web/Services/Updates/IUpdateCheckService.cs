namespace Relaywright.Web.Services.Updates;

public interface IUpdateCheckService
{
    Task<UpdateCheckStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<UpdateCheckStatus> RefreshAsync(CancellationToken cancellationToken);
}
