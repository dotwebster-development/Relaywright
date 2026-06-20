using System.Buffers;

namespace Relaywright.Web.Services.Queueing;

public interface IMessageSpoolService
{
    Task<string> WriteAsync(Guid messageId, DateTimeOffset acceptedUtc, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken);

    Stream OpenRead(string relativePath);

    string GetAbsolutePath(string relativePath);

    bool Exists(string relativePath);

    Task DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken);
}

