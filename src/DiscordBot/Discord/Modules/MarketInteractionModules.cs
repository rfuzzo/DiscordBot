using DiscordBot.Data;
using DiscordBot.Markets;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;
using static DiscordBot.Discord.Modules.BotModuleHelpers;

namespace DiscordBot.Discord.Modules;

/// <summary>Buttons on market messages.</summary>
public sealed class MarketButtonModule(MarketService markets, MarketView view)
    : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction(MarketView.BetButtonId)]
    public async Task BetAsync(int marketId, Side side)
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var market = await markets.GetMarketAsync(guildId, marketId);
        if (market is null || market.Status != MarketStatus.Open)
        {
            await RespondAsync(Ephemeral($"Market #{marketId} is no longer taking bets."));
            return;
        }

        var wallet = await markets.GetWalletAsync(guildId, Context.User.Id);
        await RespondAsync(InteractionCallback.Modal(view.BuildBetModal(market, side, wallet.Balance)));
    }

    [ComponentInteraction(MarketView.PositionButtonId)]
    public async Task PositionAsync(int marketId)
    {
        var guildId = Context.Interaction.GuildId!.Value;
        var positions = await markets.GetPositionsAsync(guildId, Context.User.Id, marketId, activeOnly: false);
        var wallet = await markets.GetWalletAsync(guildId, Context.User.Id);
        await RespondAsync(Ephemeral(
            $"{DescribePositions(markets, positions)}\nBalance: {markets.FormatMoney(wallet.Balance)}"));
    }
}

/// <summary>The "how much do you want to bet" popup.</summary>
public sealed class MarketModalModule(MarketService markets, IMarketNotifier notifier)
    : ComponentInteractionModule<ModalInteractionContext>
{
    [ComponentInteraction(MarketView.BetModalId)]
    public async Task SubmitBetAsync(int marketId, Side side)
    {
        var input = Context.Components
            .OfType<Label>()
            .Select(l => l.Component)
            .OfType<TextInput>()
            .FirstOrDefault(t => t.CustomId == MarketView.BetAmountInputId)?.Value;

        if (!long.TryParse(input?.Replace(",", "").Replace("_", "").Trim(), out var amount) || amount < 1)
        {
            await RespondAsync(Ephemeral("Please enter a whole number of eddies, like `100`."));
            return;
        }

        try
        {
            var trade = await markets.BuyAsync(Context.Interaction.GuildId!.Value, Context.User.Id, marketId, side, amount);
            await RespondAsync(Ephemeral(DescribeBuy(markets, trade)));
            await notifier.RefreshAsync(trade.Market);
        }
        catch (MarketException ex)
        {
            await RespondAsync(Ephemeral(ex.Message));
        }
    }
}
