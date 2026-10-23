using DiscordBot.Data;
using DiscordBot.Discord;
using DiscordBot.Nexus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Markets;

/// <summary>
/// Background loop that closes markets at their deadline and settles Nexus markets:
/// YES as soon as enough matching mods appear, NO once the deadline (plus a grace period) has passed.
/// </summary>
public sealed class MarketResolverService(
    MarketService markets,
    NexusModTracker tracker,
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

        var pending = await markets.GetUnsettledNexusMarketsAsync();
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

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var market in pending)
        {
            var matching = MatchingMods(market, mods);
            Side? outcome = null;
            string? note = null;

            if (matching.Count >= market.MinCount)
            {
                outcome = Side.Yes;
                note = $"{matching.Count} matching mod(s) found on Nexus Mods.";
            }
            else if (now >= market.ClosesAtUtc + options.Value.ResolutionDelay)
            {
                outcome = Side.No;
                note = matching.Count == 0
                    ? "No matching mods were released in time."
                    : $"Only {matching.Count} of the required {market.MinCount} matching mods were released in time.";
            }

            if (outcome is not { } result)
                continue;

            try
            {
                var settlement = await markets.ResolveAsync(market.Id, result, note);
                logger.LogInformation("Market #{MarketId} resolved {Outcome}", market.Id, result);
                await messenger.AnnounceSettlementAsync(settlement, matching);
            }
            catch (MarketException ex)
            {
                // Settled concurrently (e.g. by a moderator) — nothing to do.
                logger.LogDebug(ex, "Skipped resolving market #{MarketId}", market.Id);
            }
        }

        tracker.Prune(since);
    }

    /// <summary>Mods matching the market's criteria that were created inside its betting window.</summary>
    public static IReadOnlyList<NexusMod> MatchingMods(Market market, IEnumerable<NexusMod> mods)
    {
        var criteria = market.Criteria();
        var opens = new DateTimeOffset(market.OpensAtUtc, TimeSpan.Zero);
        var closes = new DateTimeOffset(market.ClosesAtUtc, TimeSpan.Zero);
        return mods
            .Where(m => m.CreatedAt >= opens && m.CreatedAt < closes && criteria.Matches(m))
            .OrderBy(m => m.CreatedAt)
            .ToList();
    }
}
