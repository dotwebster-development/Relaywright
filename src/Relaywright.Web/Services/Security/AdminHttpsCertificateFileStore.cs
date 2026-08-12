using Relaywright.Web.Infrastructure;

namespace Relaywright.Web.Services.Security;

public sealed class AdminHttpsCertificateFileStore(AppPaths paths)
{
    public string GetCertificatePath(string fileName)
    {
        Directory.CreateDirectory(paths.CertificateDirectory);
        return Path.Combine(paths.CertificateDirectory, fileName);
    }

    public string CreateTemporaryPath(string extension)
    {
        Directory.CreateDirectory(paths.CertificateDirectory);
        return Path.Combine(paths.CertificateDirectory, $"{Guid.NewGuid():N}{extension}");
    }

    public async Task CopyUploadAsync(
        IFormFile file,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var destination = File.Create(destinationPath);
        await file.CopyToAsync(destination, cancellationToken);
    }

    public Task WriteAsync(string destinationPath, byte[] contents, CancellationToken cancellationToken)
    {
        return File.WriteAllBytesAsync(destinationPath, contents, cancellationToken);
    }

    public void MoveIntoPlace(string temporaryPath, string targetPath)
    {
        File.Move(temporaryPath, targetPath, overwrite: true);
    }

    public void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
