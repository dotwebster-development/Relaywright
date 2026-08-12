using System.Buffers;
using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Queueing;

public sealed class MessageSpoolService(
    AppPaths appPaths,
    ILogger<MessageSpoolService> logger,
    ISpoolFileSystem? spoolFileSystem = null) : IMessageSpoolService
{
    private readonly ISpoolFileSystem fileSystem = spoolFileSystem ?? new PhysicalSpoolFileSystem();

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
            fileSystem.CreateDirectory(directory);
        }

        try
        {
            await using (var stream = fileSystem.CreateWriteThrough(tempPath))
            {
                foreach (var segment in buffer)
                {
                    await stream.WriteAsync(segment, cancellationToken);
                }

                await stream.FlushAsync(cancellationToken);
                fileSystem.FlushToDisk(stream);
            }

            fileSystem.MoveFile(tempPath, absolutePath);

            logger.LogInformation(
                "Spool file committed. MessageId={MessageId}; RelativePath={RelativePath}; Bytes={Bytes}",
                messageId,
                relativePath,
                buffer.Length);
        }
        catch (Exception exception)
        {
            try
            {
                if (fileSystem.FileExists(tempPath))
                {
                    fileSystem.DeleteFile(tempPath);
                    logger.LogWarning(
                        exception,
                        "Removed temporary spool file after write failure. MessageId={MessageId}; TempPath={TempPath}",
                        messageId,
                        tempPath);
                }
            }
            catch (Exception cleanupException)
            {
                logger.LogError(
                    cleanupException,
                    "Failed to remove temporary spool file after write failure. MessageId={MessageId}; TempPath={TempPath}; OriginalExceptionType={OriginalExceptionType}",
                    messageId,
                    tempPath,
                    exception.GetType().Name);
            }

            throw;
        }

        return relativePath;
    }

    public Stream OpenRead(string relativePath)
    {
        logger.LogDebug("Opening spool file for read. RelativePath={RelativePath}", relativePath);

        return fileSystem.OpenRead(appPaths.GetSpoolAbsolutePath(relativePath));
    }

    public string GetAbsolutePath(string relativePath)
    {
        return appPaths.GetSpoolAbsolutePath(relativePath);
    }

    public bool Exists(string relativePath)
    {
        var exists = fileSystem.FileExists(appPaths.GetSpoolAbsolutePath(relativePath));
        logger.LogDebug("Checked spool file existence. RelativePath={RelativePath}; Exists={Exists}", relativePath, exists);
        return exists;
    }

    public Task DeleteIfExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var path = appPaths.GetSpoolAbsolutePath(relativePath);
        if (fileSystem.FileExists(path))
        {
            fileSystem.DeleteFile(path);
            logger.LogInformation("Deleted spool file. RelativePath={RelativePath}", relativePath);
        }
        else
        {
            logger.LogDebug("Skipped spool delete because file was missing. RelativePath={RelativePath}", relativePath);
        }

        return Task.CompletedTask;
    }
}
