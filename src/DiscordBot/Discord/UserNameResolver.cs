using System.Collections.Concurrent;
using NetCord.Gateway;
using NetCord.Rest;

namespace DiscordBot.Discord;

/// <summary>
/// Display names for user IDs. Mentions inside embeds only render as names for members the viewer's client has
/// loaded, which on a big server leaves many as raw IDs, so lists of users show looked-up names instead.
/// </summary>
public sealed class UserNameResolver(GatewayClient client, TimeProvider time)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<(ulong GuildId, ulong UserId), (string Name, DateTimeOffset At)> _cache = new();

    public async Task<IReadOnlyDictionary<ulong, string>> GetNamesAsync(ulong guildId, IEnumerable<ulong> userIds)
    {
        var ids = userIds.Distinct().ToList();
        var names = await Task.WhenAll(ids.Select(id => GetNameAsync(guildId, id)));
        return ids.Zip(names).ToDictionary(x => x.First, x => x.Second);
    }

    private async Task<string> GetNameAsync(ulong guildId, ulong userId)
    {
        var now = time.GetUtcNow();
        if (_cache.TryGetValue((guildId, userId), out var cached) && now - cached.At < CacheFor)
            return cached.Name;

        string name;
        try
        {
            var member = await client.Rest.GetGuildUserAsync(guildId, userId);
            name = member.Nickname ?? member.GlobalName ?? member.Username;
        }
        catch (RestException)
        {
            // Not on the server anymore: fall back to their account name.
            try
            {
                var user = await client.Rest.GetUserAsync(userId);
                name = user.GlobalName ?? user.Username;
            }
            catch (RestException)
            {
                name = $"User {userId}";
            }
        }

        _cache[(guildId, userId)] = (name, now);
        return name;
    }
}
