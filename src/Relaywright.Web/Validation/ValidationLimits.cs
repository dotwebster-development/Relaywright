namespace Relaywright.Web.Validation;

public static class ValidationLimits
{
    public const int MinimumPort = 1;
    public const int MaximumPort = 65535;
    public const long MinimumMessageSizeBytes = 1024;
    public const int MaximumSecretLength = 1024;
    public const int MaximumTextLength = 1024;
    public const int MaximumPolicyListLength = 4096;

    public static IReadOnlyCollection<string> CertificateFileExtensions { get; } =
        [".pfx", ".p12", ".cer", ".crt", ".pem"];

    public static IReadOnlyCollection<string> BackupFileExtensions { get; } =
        [".zip", ".rwbak"];
}
