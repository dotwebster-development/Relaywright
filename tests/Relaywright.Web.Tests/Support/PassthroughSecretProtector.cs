using Relaywright.Web.Services.Security;

namespace Relaywright.Web.Tests.Support;

internal sealed class PassthroughSecretProtector : ISecretProtector
{
    public string Protect(string? plainText)
    {
        return plainText ?? string.Empty;
    }

    public string? Unprotect(string? protectedText)
    {
        return protectedText;
    }
}
