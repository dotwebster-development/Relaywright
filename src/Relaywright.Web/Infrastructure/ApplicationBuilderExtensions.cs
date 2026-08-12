namespace Relaywright.Web.Infrastructure;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseRelaywrightHttpPipeline(this IApplicationBuilder app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();
        return app;
    }
}
