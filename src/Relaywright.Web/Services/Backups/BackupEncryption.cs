using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Relaywright.Web.Services.Backups;

public static class BackupEncryption
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RWBKENC1");
    private const int Version = 1;
    private const int SaltLength = 16;
    private const int IvLength = 16;
    private const int TagLength = 32;
    private const int KeyLength = 64;
    private const int HeaderLength = 8 + 4 + 4 + SaltLength + IvLength;
    private const int Iterations = 210_000;

    public static bool LooksEncrypted(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < HeaderLength + TagLength)
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[Magic.Length];
        using var stream = File.OpenRead(path);
        return stream.Read(buffer) == Magic.Length && buffer.SequenceEqual(Magic);
    }

    public static async Task EncryptFileAsync(
        string sourcePath,
        string destinationPath,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Backup encryption requires a password.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var iv = RandomNumberGenerator.GetBytes(IvLength);
        var keys = DeriveKeys(password, salt, Iterations);
        var encKey = keys[..32];
        var macKey = keys[32..];

        await using (var destination = File.Create(destinationPath))
        {
            await WriteHeaderAsync(destination, salt, iv, cancellationToken);

            using var aes = Aes.Create();
            aes.Key = encKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            await using var source = File.OpenRead(sourcePath);
            await using (var crypto = new CryptoStream(destination, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                await source.CopyToAsync(crypto, cancellationToken);
            }
        }

        var tag = await ComputeHmacAsync(destinationPath, macKey, new FileInfo(destinationPath).Length, cancellationToken);
        await using var append = new FileStream(destinationPath, FileMode.Append, FileAccess.Write, FileShare.None);
        await append.WriteAsync(tag, cancellationToken);
    }

    public static async Task DecryptFileAsync(
        string sourcePath,
        string destinationPath,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Encrypted backup requires a password.");
        }

        await using var source = File.OpenRead(sourcePath);
        var header = await ReadHeaderAsync(source, cancellationToken);
        var fileLength = source.Length;
        if (fileLength < HeaderLength + TagLength)
        {
            throw new InvalidOperationException("Encrypted backup is too small.");
        }

        var cipherLength = fileLength - HeaderLength - TagLength;
        var storedTag = new byte[TagLength];
        source.Position = fileLength - TagLength;
        _ = await source.ReadAsync(storedTag, cancellationToken);

        var keys = DeriveKeys(password, header.Salt, header.Iterations);
        var encKey = keys[..32];
        var macKey = keys[32..];
        var computedTag = await ComputeHmacAsync(sourcePath, macKey, fileLength - TagLength, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(storedTag, computedTag))
        {
            throw new InvalidOperationException("Encrypted backup password is invalid or the file is corrupted.");
        }

        source.Position = HeaderLength;
        await using var limited = new LimitedReadStream(source, cipherLength);
        await using var destination = File.Create(destinationPath);

        using var aes = Aes.Create();
        aes.Key = encKey;
        aes.IV = header.Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        await using var crypto = new CryptoStream(limited, aes.CreateDecryptor(), CryptoStreamMode.Read);
        await crypto.CopyToAsync(destination, cancellationToken);
    }

    private static byte[] DeriveKeys(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            KeyLength);
    }

    private static async Task WriteHeaderAsync(
        Stream destination,
        byte[] salt,
        byte[] iv,
        CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), Version);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), Iterations);
        salt.CopyTo(header.AsSpan(16, SaltLength));
        iv.CopyTo(header.AsSpan(16 + SaltLength, IvLength));
        await destination.WriteAsync(header, cancellationToken);
    }

    private static async Task<Header> ReadHeaderAsync(Stream source, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        if (await source.ReadAsync(header, cancellationToken) != HeaderLength)
        {
            throw new InvalidOperationException("Encrypted backup header is incomplete.");
        }

        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidOperationException("File is not a Relaywright encrypted backup.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
        if (version != Version)
        {
            throw new InvalidOperationException("Encrypted backup version is not supported.");
        }

        return new Header(
            BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, 4)),
            header.AsSpan(16, SaltLength).ToArray(),
            header.AsSpan(16 + SaltLength, IvLength).ToArray());
    }

    private static async Task<byte[]> ComputeHmacAsync(
        string path,
        byte[] macKey,
        long bytesToRead,
        CancellationToken cancellationToken)
    {
        using var hmac = new HMACSHA256(macKey);
        await using var stream = File.OpenRead(path);
        var buffer = new byte[81920];
        var remaining = bytesToRead;

        while (remaining > 0)
        {
            var readLength = (int)Math.Min(buffer.Length, remaining);
            var read = await stream.ReadAsync(buffer.AsMemory(0, readLength), cancellationToken);
            if (read == 0)
            {
                throw new InvalidOperationException("Encrypted backup ended unexpectedly while checking integrity.");
            }

            hmac.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }

        hmac.TransformFinalBlock([], 0, 0);
        return hmac.Hash ?? throw new InvalidOperationException("Encrypted backup integrity check failed.");
    }

    private sealed record Header(int Iterations, byte[] Salt, byte[] Iv);

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _length;
        private long _remaining;

        public LimitedReadStream(Stream inner, long length)
        {
            _inner = inner;
            _length = length;
            _remaining = length;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
            {
                return 0;
            }

            var read = await _inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
            _remaining -= read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
