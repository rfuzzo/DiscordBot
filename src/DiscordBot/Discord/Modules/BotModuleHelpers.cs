using DiscordBot.Data;
using DiscordBot.Markets;
using NetCord;
using NetCord.Rest;

namespace DiscordBot.Discord.Modules;

internal static class BotModuleHelpers
{
    public static InteractionCallbackProperties Ephemeral(string content) =>
        InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(content)
            .WithFlags(MessageFlags.Ephemeral)
            .WithAllowedMentions(AllowedMentionsProperties.None));

    public static InteractionCallbackProperties Ephemeral(EmbedProperties embed) =>
        InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([embed])
            .WithFlags(MessageFlags.Ephemeral)
            .WithAllowedMentions(AllowedMentionsProperties.None));

    /// <summary>A public market message with the live embed and bet buttons.</summary>
    public static InteractionCallbackProperties MarketMessage(MarketView view, Market market) =>
        InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([view.BuildEmbed(market)])
            .WithComponents(view.BuildComponents(market))
            .WithAllowedMentions(AllowedMentionsProperties.None));

    public static bool IsModerator(User user) =>
        user is GuildInteractionUser member
        && (member.Permissions.HasFlag(Permissions.ManageGuild) || member.Permissions.HasFlag(Permissions.Administrator));

    public static string DescribeBuy(MarketService markets, TradeResult trade)
    {
        var price = trade.Side == Side.Yes ? trade.Market.YesPrice() : 1 - trade.Market.YesPrice();
        return $"✅ Bought **{trade.Shares:N1} {trade.Side.Label()}** shares in #{trade.Market.Id} for {markets.FormatMoney(trade.Amount)}.\n" +
               $"{trade.Side.Label()} is now at **{price:P0}**. If {trade.Side.Label()} wins, your {trade.PositionShares:N1} shares pay " +
               $"**{markets.FormatMoney((long)Math.Floor(trade.PositionShares))}**.\n" +
               $"Balance: {markets.FormatMoney(trade.NewBalance)}";
    }

    public static string DescribeSell(MarketService markets, TradeResult trade)
    {
        var price = trade.Side == Side.Yes ? trade.Market.YesPrice() : 1 - trade.Market.YesPrice();
        return $"💰 Sold **{trade.Shares:N1} {trade.Side.Label()}** shares in #{trade.Market.Id} for {markets.FormatMoney(trade.Amount)}.\n" +
               $"{trade.Side.Label()} is now at **{price:P0}**. You have {trade.PositionShares:N1} {trade.Side.Label()} shares left.\n" +
               $"Balance: {markets.FormatMoney(trade.NewBalance)}";
    }

    public static string DescribePositions(MarketService markets, IReadOnlyList<PositionView> positions)
    {
        if (positions.Count == 0)
            return "No open positions.";

        return string.Join('\n', positions.Select(p =>
        {
            var price = p.Side == Side.Yes ? p.Market.YesPrice() : 1 - p.Market.YesPrice();
            var value = (long)Math.Floor(Lmsr.RefundForShares(p.Market.YesShares, p.Market.NoShares, p.Market.Liquidity, p.Side, p.Shares));
            return $"**#{p.Market.Id}** {p.Side.Label()} · {p.Shares:N1} shares @ {price:P0} · " +
                   $"worth ~{markets.FormatMoney(value)} now, pays {markets.FormatMoney((long)Math.Floor(p.Shares))} if right";
        }));
    }
}
