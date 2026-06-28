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

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        await using (stream)
        {
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.ToArray();
        }
    }
}
