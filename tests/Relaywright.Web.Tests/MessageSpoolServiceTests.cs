using System.Buffers;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Services.Queueing;
using Relaywright.Web.Tests.Support;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class MessageSpoolServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task WriteOpenAndDeleteRoundTripThroughRealSpoolFile()
    {
        using var appData = TempAppData.Create();
        var service = new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance);
        var messageId = Guid.NewGuid();
        var acceptedUtc = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var bytes = TestData.MimeBytes();

        var relativePath = await service.WriteAsync(
            messageId,
            acceptedUtc,
            new ReadOnlySequence<byte>(bytes),
            CancellationToken.None);

        Assert.Equal(Path.Combine("2030", "01", "02", $"{messageId:N}.eml"), relativePath);
        Assert.True(service.Exists(relativePath));
        Assert.Equal(bytes, await ReadAllBytesAsync(service.OpenRead(relativePath)));

        await service.DeleteIfExistsAsync(relativePath, CancellationToken.None);

        Assert.False(service.Exists(relativePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..\\outside.eml")]
    [InlineData("../outside.eml")]
    [Trait("Category", "Unit")]
    public void GetAbsolutePathRejectsEmptyOrEscapingPaths(string relativePath)
    {
        using var appData = TempAppData.Create();
        var service = new MessageSpoolService(appData.Paths, NullLogger<MessageSpoolService>.Instance);

        Assert.Throws<InvalidOperationException>(() => service.GetAbsolutePath(relativePath));
    }

    [Fact]
    public async Task CleanupFailureDoesNotMaskOriginalWriteFailure()
    {
        using var appData = TempAppData.Create();
        var service = new MessageSpoolService(
            appData.Paths,
            NullLogger<MessageSpoolService>.Instance,
            new WriteAndCleanupFailingFileSystem());

        var exception = await Assert.ThrowsAsync<IOException>(() => service.WriteAsync(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            new ReadOnlySequence<byte>(TestData.MimeBytes()),
            CancellationToken.None));

        Assert.Equal("Simulated spool write failure.", exception.Message);
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        await using (stream)
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }
    }

    private sealed class WriteAndCleanupFailingFileSystem : ISpoolFileSystem
    {
        public void CreateDirectory(string path)
        {
        }

        public Stream CreateWriteThrough(string path) => new FailingWriteStream();

        public void FlushToDisk(Stream stream)
        {
        }

        public void MoveFile(string sourcePath, string destinationPath)
        {
            throw new NotSupportedException();
        }

        public Stream OpenRead(string path)
        {
            throw new NotSupportedException();
        }

        public bool FileExists(string path) => true;

        public void DeleteFile(string path)
        {
            throw new UnauthorizedAccessException("Simulated cleanup failure.");
        }
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException(new IOException("Simulated spool write failure."));
        }
    }
}
