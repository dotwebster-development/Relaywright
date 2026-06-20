using System.Buffers;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Queueing;

public sealed class MessageSpoolService(
    AppPaths appPaths,
    ILogger<MessageSpoolService> logger) : IMessageSpoolService
{
    public async Task<string> WriteAsync(Guid messageId, DateTimeOffset acceptedUtc, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        var relativePath = appPaths.CreateSpoolRelativePath(messageId, acceptedUtc);
        var absolutePath = appPaths.GetSpoolAbsolutePath(relativePath);
        var tempPath = $"{absolutePath}.{Guid.NewGuid():N}.tmp";

        logger.LogDebug(
            "Writing spool file. MessageId={MessageId}; RelativePath={RelativePath}; Bytes={Bytes}; AcceptedUtc={AcceptedUtc}",
            messageId,
            relativePath,
            buffer.Length,
            acceptedUtc);

        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                foreach (var segment in buffer)
                {
                    await stream.WriteAsync(segment, cancellationToken);
                }

                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, absolutePath, overwrite: false);

            logger.LogInformation(
                "Spool file committed. MessageId={MessageId}; RelativePath={RelativePath}; Bytes={Bytes}",
                messageId,
                relativePath,
                buffer.Length);
        }
        catch (Exception exception)
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
                logger.LogWarning(
                    exception,
                    "Removed temporary spool file after write failure. MessageId={MessageId}; TempPath={TempPath}",
                    messageId,
                    tempPath);
            }

            throw;
        }

        return relativePath;
    }

    public Stream OpenRead(string relativePath)
    {
        logger.LogDebug("Opening spool file for read. RelativePath={RelativePath}", relativePath);

        return new FileStream(
            appPaths.GetSpoolAbsolutePath(relativePath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous);
    }

    public string GetAbsolutePath(string relativePath)
    {
        return appPaths.GetSpoolAbsolutePath(relativePath);
    }

    public bool Exists(string relativePath)
    {
        var exists = File.Exists(appPaths.GetSpoolAbsolutePath(relativePath));
        logger.LogDebug("Checked spool file existence. RelativePath={RelativePath}; Exists={Exists}", relativePath, exists);
        return exists;
    }

    public Task DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = appPaths.GetSpoolAbsolutePath(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
            logger.LogInformation("Deleted spool file. RelativePath={RelativePath}", relativePath);
        }
        else
        {
            logger.LogDebug("Skipped spool delete because file was missing. RelativePath={RelativePath}", relativePath);
        }

        return Task.CompletedTask;
    }
}
