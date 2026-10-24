using System.Collections.Concurrent;
using DiscordBot.Data;
using DiscordBot.Markets;
using DiscordBot.Nexus;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;

namespace DiscordBot.Discord;

/// <summary>Publishes market changes to Discord.</summary>
public interface IMarketNotifier
{
    Task RefreshAsync(Market market);

    Task AnnounceSettlementAsync(SettlementResult settlement, IReadOnlyList<NexusMod>? matchingMods = null);
}

/// <summary>Keeps posted market messages in sync and announces settlements.</summary>
public sealed class MarketMessenger(GatewayClient client, MarketView view, MarketService markets, ILogger<MarketMessenger> logger)
    : IMarketNotifier
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _editLocks = new();

    /// <summary>Re-renders the market's original message (prices, status, buttons). Failures are logged, not thrown.</summary>
    public async Task RefreshAsync(Market market)
    {
        // Several quick bets each trigger an edit, and those can reach Discord out of order. Editing one at a
        // time and always rendering the market as it is now means the last edit shows the latest odds.
        var editLock = _editLocks.GetOrAdd(market.Id, _ => new SemaphoreSlim(1, 1));
        await editLock.WaitAsync();
        try
        {
            var latest = await markets.GetMarketAsync(market.GuildId, market.Id) ?? market;
            if (latest.MessageId is not { } messageId)
                return;

            await client.Rest.ModifyMessageAsync(latest.ChannelId, messageId, m =>
            {
                m.Embeds = [view.BuildEmbed(latest)];
                m.Components = view.BuildComponents(latest);
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update message for market #{MarketId}", market.Id);
        }
        finally
        {
            editLock.Release();
        }
    }

    public async Task AnnounceSettlementAsync(SettlementResult settlement, IReadOnlyList<NexusMod>? matchingMods = null)
    {
        var market = settlement.Market;
        await RefreshAsync(market);

        try
        {
            var message = new MessageProperties()
                .WithEmbeds([view.BuildSettlementEmbed(settlement, matchingMods)])
                .WithAllowedMentions(AllowedMentionsProperties.None);
            if (market.MessageId is { } messageId)
                message.WithMessageReference(MessageReferenceProperties.Reply(messageId, false));

            await client.Rest.SendMessageAsync(market.ChannelId, message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not announce settlement of market #{MarketId}", market.Id);
        }
    }
}
