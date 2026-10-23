using DiscordBot.Data;
using DiscordBot.Discord;
using DiscordBot.Nexus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Markets;

/// <summary>
/// Background loop that closes markets at their deadline and settles Nexus markets:
/// YES as soon as enough matching mods appear (or the target mod gets a new file),
/// NO once the deadline (plus a grace period) has passed.
/// </summary>
public sealed class MarketResolverService(
    MarketService markets,
    NexusModTracker tracker,
    INexusModsClient nexus,
    IMarketNotifier messenger,
    IOptions<MarketOptions> options,
    TimeProvider time,
    ILogger<MarketResolverService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval, time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Market resolution pass failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        foreach (var market in await markets.CloseExpiredAsync())
        {
            logger.LogInformation("Market #{MarketId} closed", market.Id);
            await messenger.RefreshAsync(market);
        }

        await ResolveNewModMarketsAsync(cancellationToken);
        await ResolveUpdateMarketsAsync(cancellationToken);
    }

    private async Task ResolveNewModMarketsAsync(CancellationToken cancellationToken)
    {
        var pending = await markets.GetUnsettledMarketsAsync(MarketKind.Nexus);
        if (pending.Count == 0)
            return;

        var since = new DateTimeOffset(pending.Min(m => m.OpensAtUtc), TimeSpan.Zero);
        IReadOnlyList<NexusMod> mods;
        try
        {
            mods = await tracker.GetModsSinceAsync(since, cancellationToken);
        }
        catch (NexusApiException ex)
        {
            logger.LogWarning(ex, "Could not fetch mods from Nexus; will retry next pass");
            return;
        }

        foreach (var market in pending)
        {
            var matching = MatchingMods(market, mods);
            if (matching.Count >= market.MinCount)
            {
                await SettleAsync(market, Side.Yes, $"{matching.Count} matching mod(s) found on Nexus Mods.", matching);
            }
            else if (IsPastGracePeriod(market))
            {
                var note = matching.Count == 0
                    ? "No matching mods were released in time."
                    : $"Only {matching.Count} of the required {market.MinCount} matching mods were released in time.";
                await SettleAsync(market, Side.No, note, matching);
            }
        }

        tracker.Prune(since);
    }

    private async Task ResolveUpdateMarketsAsync(CancellationToken cancellationToken)
    {
        var pending = await markets.GetUnsettledMarketsAsync(MarketKind.NexusUpdate);
        if (pending.Count == 0)
            return;

        Dictionary<int, NexusMod> mods;
        try
        {
            mods = (await nexus.GetModsAsync(pending.Select(m => m.TargetModId!.Value).ToList(), cancellationToken))
                .ToDictionary(m => m.ModId);
        }
        catch (NexusApiException ex)
        {
            logger.LogWarning(ex, "Could not fetch tracked mods from Nexus; will retry next pass");
            return;
        }

        foreach (var market in pending)
        {
            var mod = mods.GetValueOrDefault(market.TargetModId!.Value);
            if (mod is not null && IsInWindow(market, mod.UpdatedAt))
            {
                await SettleAsync(market, Side.Yes, UpdatedNote(mod.UpdatedAt, mod.Version), [mod]);
                continue;
            }

            if (!IsPastGracePeriod(market))
                continue;

            // updatedAt only shows the latest upload. A file uploaded inside the window may have been
            // followed by another one after the deadline, so check every file before settling NO.
            IReadOnlyList<DateTimeOffset> uploads;
            try
            {
                uploads = mod is null ? [] : await nexus.GetFileUploadTimesAsync(mod.ModId, cancellationToken);
            }
            catch (NexusApiException ex)
            {
                logger.LogWarning(ex, "Could not fetch files of mod {ModId}; will retry next pass", market.TargetModId);
                continue;
            }

            var lastInWindow = uploads.Where(t => IsInWindow(market, t)).Order().LastOrDefault();
            if (lastInWindow != default)
                await SettleAsync(market, Side.Yes, UpdatedNote(lastInWindow, version: null), [mod!]);
            else
                await SettleAsync(market, Side.No,
                    mod is null ? "The mod is no longer available on Nexus Mods." : "No new file was uploaded in time.", []);
        }
    }

    private async Task SettleAsync(Market market, Side outcome, string note, IReadOnlyList<NexusMod> mods)
    {
        try
        {
            var settlement = await markets.ResolveAsync(market.Id, outcome, note);
            logger.LogInformation("Market #{MarketId} resolved {Outcome}", market.Id, outcome);
            await messenger.AnnounceSettlementAsync(settlement, mods);
        }
        catch (MarketException ex)
        {
            // Settled concurrently (e.g. by a moderator) — nothing to do.
            logger.LogDebug(ex, "Skipped resolving market #{MarketId}", market.Id);
        }
    }

    private bool IsPastGracePeriod(Market market) =>
        time.GetUtcNow().UtcDateTime >= market.ClosesAtUtc + options.Value.ResolutionDelay;

    private static bool IsInWindow(Market market, DateTimeOffset at) =>
        at >= new DateTimeOffset(market.OpensAtUtc, TimeSpan.Zero) && at < new DateTimeOffset(market.ClosesAtUtc, TimeSpan.Zero);

    private static string UpdatedNote(DateTimeOffset at, string? version) =>
        $"A new file was uploaded {MarketView.Timestamp(at.UtcDateTime, 'f')}" +
        (string.IsNullOrWhiteSpace(version) ? "." : $" (now version {version}).");

    /// <summary>Mods matching the market's criteria that were created inside its betting window.</summary>
    public static IReadOnlyList<NexusMod> MatchingMods(Market market, IEnumerable<NexusMod> mods)
    {
        var criteria = market.Criteria();
        return mods
            .Where(m => IsInWindow(market, m.CreatedAt) && criteria.Matches(m))
            .OrderBy(m => m.CreatedAt)
            .ToList();
    }
}
