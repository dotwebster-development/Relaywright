namespace Relaywright.Web.Services.Security;

public interface ISecretProtector
{
    string Protect(string? plainText);

    string? Unprotect(string? protectedText);
}

