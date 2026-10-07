namespace PrivateBrandsPortal.Web.Interfaces;

public sealed record StoredFile(long Size, string Sha256);
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(string key, Stream source, long maxBytes, CancellationToken ct);
    Task<Stream> OpenAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
    Task<int> CleanupTemporaryAsync(DateTimeOffset olderThan, CancellationToken ct);
}
