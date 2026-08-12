using System.Diagnostics;

namespace Relaywright.Web.Infrastructure;

public sealed class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var path = context.Request.Path.HasValue ? context.Request.Path.Value : "/";
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = context.TraceIdentifier
        });

        logger.LogDebug(
            "HTTP request started. Method={Method}; Path={Path}; RemoteIp={RemoteIp}",
            context.Request.Method,
            path,
            context.Connection.RemoteIpAddress?.ToString());

        try
        {
            await next(context);
            var level = context.Response.StatusCode >= 500
                ? LogLevel.Error
                : context.Response.StatusCode >= 400
                    ? LogLevel.Warning
                    : LogLevel.Information;
            logger.Log(
                level,
                "HTTP request completed. Method={Method}; Path={Path}; StatusCode={StatusCode}; ElapsedMs={ElapsedMs}; User={UserName}",
                context.Request.Method,
                path,
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : "anonymous");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "HTTP request failed. Method={Method}; Path={Path}; ElapsedMs={ElapsedMs}",
                context.Request.Method,
                path,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
