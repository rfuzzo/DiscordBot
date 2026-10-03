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
public sealed class MarketMessenger(GatewayClient client, MarketView view, ILogger<MarketMessenger> logger) : IMarketNotifier
{
    /// <summary>Re-renders the market's original message (prices, status, buttons). Failures are logged, not thrown.</summary>
    public async Task RefreshAsync(Market market)
    {
        if (market.MessageId is not { } messageId)
            return;

        try
        {
            await client.Rest.ModifyMessageAsync(market.ChannelId, messageId, m =>
            {
                m.Embeds = [view.BuildEmbed(market)];
                m.Components = view.BuildComponents(market);
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update message for market #{MarketId}", market.Id);
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
