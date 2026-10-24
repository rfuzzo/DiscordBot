using DiscordBot.Data;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Eta;

/// <summary>The running joke: every time someone asks for an ETA, the core mods update slips one more day.</summary>
public sealed class EtaService(IDbContextFactory<BotDbContext> dbFactory, TimeProvider time)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>Counts one more nag in the guild and returns the new delay in days (1 on the first use).</summary>
    public async Task<int> AddNagAsync(ulong guildId)
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
            await db.SaveChangesAsync();
            return counter.Count;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>The announcement, e.g. "Due to @x's incessant nagging, the core mods update will be delayed by 3 days on 27 October 2026."</summary>
    public string Announcement(ulong naggerId, int delayDays)
    {
        var releaseDate = time.GetUtcNow().AddDays(delayDays);
        var days = delayDays == 1 ? "1 day" : $"{delayDays} days";
        return $"Due to <@{naggerId}>'s incessant nagging, the core mods update will be delayed by {days} " +
               $"on <t:{releaseDate.ToUnixTimeSeconds()}:D>.";
    }
}
