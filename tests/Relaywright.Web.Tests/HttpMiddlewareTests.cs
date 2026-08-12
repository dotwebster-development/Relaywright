using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Relaywright.Web.Infrastructure;
using Xunit;

namespace Relaywright.Web.Tests;

public sealed class HttpMiddlewareTests
{
    [Fact]
    public async Task SecurityHeadersAreAppliedBeforeCallingNextMiddleware()
    {
        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Contains("script-src 'self'", context.Response.Headers.ContentSecurityPolicy.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestLoggingRethrowsApplicationFailure()
    {
        var expected = new InvalidOperationException("Simulated request failure.");
        var middleware = new RequestLoggingMiddleware(
            _ => Task.FromException(expected),
            NullLogger<RequestLoggingMiddleware>.Instance);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            middleware.InvokeAsync(new DefaultHttpContext()));

        Assert.Same(expected, actual);
    }
}
