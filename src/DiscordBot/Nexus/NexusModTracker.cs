using Microsoft.Extensions.Logging;

namespace DiscordBot.Nexus;

/// <summary>
/// Keeps an in-memory list of recently created mods so market resolution doesn't re-download
/// weeks of history on every tick. The first request backfills from the requested start; later
/// requests only fetch what is new (plus an overlap for mods that show up late in the API).
/// </summary>
public sealed class NexusModTracker(INexusModsClient client, TimeProvider time, ILogger<NexusModTracker> logger)
{
    /// <summary>How far back incremental refreshes look, to catch mods that are indexed late.</summary>
    public static readonly TimeSpan RefreshOverlap = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<int, NexusMod> _mods = [];
    private DateTimeOffset? _coveredFrom;
    private DateTimeOffset _lastRefresh;

    /// <summary>All known mods created at or after <paramref name="since"/>, refreshed from Nexus.</summary>
    public async Task<IReadOnlyList<NexusMod>> GetModsSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            var fetchFrom = _coveredFrom is { } covered && covered <= since
                ? Min(_lastRefresh - RefreshOverlap, now)
                : since;

            var fresh = await client.GetModsCreatedSinceAsync(fetchFrom, cancellationToken);
            foreach (var mod in fresh)
                _mods[mod.ModId] = mod;

            _coveredFrom = _coveredFrom is { } c && c < since ? c : since;
            _lastRefresh = now;
            logger.LogDebug("Fetched {Count} Nexus mods created since {From}", fresh.Count, fetchFrom);

            return _mods.Values.Where(m => m.CreatedAt >= since).OrderByDescending(m => m.CreatedAt).ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Drops cached mods older than <paramref name="cutoff"/> once no market needs them.</summary>
    public void Prune(DateTimeOffset cutoff)
    {
        _lock.Wait();
        try
        {
            foreach (var id in _mods.Values.Where(m => m.CreatedAt < cutoff).Select(m => m.ModId).ToList())
                _mods.Remove(id);
            if (_coveredFrom < cutoff)
                _coveredFrom = cutoff;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
