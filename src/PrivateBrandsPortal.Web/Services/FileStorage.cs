using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Interfaces;

namespace PrivateBrandsPortal.Web.Services;

public sealed class FileStorage(IOptions<FileStorageOptions> options, ILogger<FileStorage> logger) : IFileStorage
{
    private string Root()
    {
        var root = options.Value.RootPath;
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || !Directory.Exists(root))
            throw new IOException("Attachment storage is unavailable.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    private string Resolve(string key)
    {
        // Only server-generated keys are accepted, even by internal callers.
        if (!Regex.IsMatch(key, @"\A(?:projects/[1-9][0-9]*|temp/[1-9][0-9]*/[a-f0-9]{32})/[a-f0-9]{32}\.(xlsx|xlsm|xls|pdf|txt|doc|docx)\z"))
            throw new ValidationException("Invalid attachment reference.");
        var root = Root();
        var path = Path.GetFullPath(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("Invalid attachment reference.");
        // An existing junction/symlink must never redirect a key outside the configured root.
        var current = root;
        foreach (var part in key.Split('/'))
        {
            current = Path.Combine(current, part);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Attachment storage contains an unsupported link.");
        }
        return path;
    }

    public async Task<StoredFile> SaveAsync(string key, Stream source, long maxBytes, CancellationToken ct)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        long size = 0;
        // CreateNew prevents overwriting an existing object, including on accidental key reuse.
        await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(buffer, ct)) != 0)
            {
                size = checked(size + count);
                if (size > maxBytes) throw new ValidationException("The attachment exceeds the permitted file size.");
                hash.AppendData(buffer, 0, count);
                await target.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (size == 0) throw new ValidationException("Empty attachments are not permitted.");
            await target.FlushAsync(ct);
            return new(size, Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch
        {
            await target.DisposeAsync();
            try { File.Delete(path); }
            catch (IOException) { logger.LogWarning("A partial attachment needs storage cleanup."); }
            throw;
        }
    }

    public Task<Stream> OpenAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(Resolve(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.Delete(Resolve(key));
        return Task.CompletedTask;
    }
    public Task<int> CleanupTemporaryAsync(DateTimeOffset olderThan, CancellationToken ct)
    {
        var root = Root();
        var temp = Path.Combine(root, "temp");
        var count = 0;
        if (!Directory.Exists(temp)) return Task.FromResult(count);
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(temp, "*", enumeration))
        {
            ct.ThrowIfCancellationRequested();
            var key = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (File.GetLastWriteTimeUtc(Resolve(key)) < olderThan.UtcDateTime) { File.Delete(file); count++; }
        }
        return Task.FromResult(count);
    }
}
