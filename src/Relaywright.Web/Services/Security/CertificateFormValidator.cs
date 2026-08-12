namespace Relaywright.Web.Services.Security;

public sealed class CertificateFormValidator
{
    public IReadOnlyList<CertificateFormValidationError> Validate(
        AdminHttpsCertificateMode mode,
        IFormFile? pfxFile,
        IFormFile? certificateFile,
        IFormFile? keyFile)
    {
        return mode switch
        {
            AdminHttpsCertificateMode.Pfx when !HasContent(pfxFile) =>
            [
                new CertificateFormValidationError(
                    nameof(pfxFile),
                    "Select a PFX certificate file.")
            ],
            AdminHttpsCertificateMode.Pem =>
                ValidatePem(certificateFile, keyFile),
            AdminHttpsCertificateMode.SelfSigned => [],
            AdminHttpsCertificateMode.Pfx => [],
            _ =>
            [
                new CertificateFormValidationError(
                    nameof(mode),
                    "Choose a certificate option.")
            ]
        };
    }

    private static IReadOnlyList<CertificateFormValidationError> ValidatePem(
        IFormFile? certificateFile,
        IFormFile? keyFile)
    {
        var errors = new List<CertificateFormValidationError>();
        if (!HasContent(certificateFile))
        {
            errors.Add(new CertificateFormValidationError(
                nameof(certificateFile),
                "Select a certificate file."));
        }

        if (!HasContent(keyFile))
        {
            errors.Add(new CertificateFormValidationError(
                nameof(keyFile),
                "Select a private key file."));
        }

        return errors;
    }

    private static bool HasContent(IFormFile? file) => file is { Length: > 0 };
}

public sealed record CertificateFormValidationError(string Field, string Message);
