using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using PrivateBrandsPortal.Web.ViewModels;
namespace PrivateBrandsPortal.Web.Services;

public sealed class WizardState
{
    public Guid Token { get; set; } = Guid.NewGuid();
    public int OwnerId { get; set; }
    public int Revision { get; set; }
    public bool BriefCompleted { get; set; }
    public int? SavedProjectId { get; set; }
    public DraftInput Draft { get; set; } = new();
}
public sealed class WizardStore : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 1000 });
    private sealed class Entry(WizardState state)
    {
        public WizardState State { get; } = state;
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
    public Guid Create(int ownerId, DraftInput? draft = null)
    {
        var state = new WizardState { OwnerId = ownerId, Draft = draft ?? new(), BriefCompleted = draft is not null };
        cache.Set(state.Token, new Entry(state), new MemoryCacheEntryOptions {
            SlidingExpiration = TimeSpan.FromHours(2), AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(8), Size = 1 });
        return state.Token;
    }
    public async Task<T?> WithAsync<T>(Guid token, int ownerId, Func<WizardState, Task<T>> action, CancellationToken ct) where T : class
    {
        if (!cache.TryGetValue<Entry>(token, out var entry) || entry is null || entry.State.OwnerId != ownerId) return null;
        await entry.Gate.WaitAsync(ct);
        try { return await action(entry.State); }
        finally { entry.Gate.Release(); }
    }
    public static DraftInput Snapshot(DraftInput input) =>
        JsonSerializer.Deserialize<DraftInput>(JsonSerializer.Serialize(input))!;
    public void Dispose() => cache.Dispose();
}

