namespace Relaywright.Web.Services.Queueing;

public interface IMessageMetadataService
{
    Task<MessageMetadataSummary?> ReadAsync(string relativePath, CancellationToken cancellationToken);
}

