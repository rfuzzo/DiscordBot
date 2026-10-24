using DiscordBot.Data;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Eta;

/// <summary>A nag's result: the update's total delay so far and its new release date.</summary>
public sealed record EtaNag(int DelayDays, DateTimeOffset ReleaseAt);

/// <summary>Where the core mods update stands in a guild. Every nag counts as one more user and one more day.</summary>
public sealed record EtaStatus(int DelayDays, DateTimeOffset? ReleaseAt);

/// <summary>The running joke: every time someone asks for an ETA, the core mods update slips one more day.</summary>
public sealed class EtaService(IDbContextFactory<BotDbContext> dbFactory, TimeProvider time)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Counts one more nag; the delay grows by a day (1 on the first use).</summary>
    public async Task<EtaNag> AddNagAsync(ulong guildId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var counter = await db.EtaCounters.FindAsync(guildId);
            if (counter is null)
            {
                counter = new EtaCounter { GuildId = guildId };
                db.EtaCounters.Add(counter);
            }

            counter.Count++;
            var releaseAt = time.GetUtcNow().AddDays(counter.Count);
            counter.ReleaseAtUtc = releaseAt.UtcDateTime;
            await db.SaveChangesAsync();
            return new EtaNag(counter.Count, releaseAt);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<EtaStatus> GetStatusAsync(ulong guildId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var counter = await db.EtaCounters.AsNoTracking().FirstOrDefaultAsync(c => c.GuildId == guildId);
        return new EtaStatus(
            counter?.Count ?? 0,
            counter?.ReleaseAtUtc is { } releaseAt ? new DateTimeOffset(releaseAt, TimeSpan.Zero) : null);
    }

    /// <summary>The /eta announcement, e.g. "Due to @x's incessant nagging, the core mods update will be delayed by 3 days and will now release on 27 October 2026."</summary>
    public static string Announcement(ulong naggerId, EtaNag nag) =>
        $"Due to <@{naggerId}>'s incessant nagging, the core mods update will be delayed by {Days(nag.DelayDays)} " +
        $"and will now release on {Date(nag.ReleaseAt)}.";

    /// <summary>The /when status, e.g. "Due to 7 users nagging, the core mods update has been delayed by 7 days. It is now scheduled for 31 October 2026."</summary>
    public static string Status(EtaStatus status)
    {
        if (status is not { DelayDays: > 0, ReleaseAt: { } releaseAt })
            return "Nobody has nagged yet, so the core mods update is right on schedule. Let's keep it that way.";

        var users = status.DelayDays == 1 ? "1 user" : $"{status.DelayDays} users";
        return $"Due to {users} nagging, the core mods update has been delayed by {Days(status.DelayDays)}. " +
               $"It is now scheduled for {Date(releaseAt)}.";
    }

    private static string Days(int days) => days == 1 ? "1 day" : $"{days} days";

    private static string Date(DateTimeOffset at) => $"<t:{at.ToUnixTimeSeconds()}:D>";
}
