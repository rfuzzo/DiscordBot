using System.Text;
using DiscordBot.Data;
using DiscordBot.Markets;
using DiscordBot.Nexus;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using static DiscordBot.Discord.Modules.BotModuleHelpers;

namespace DiscordBot.Discord.Modules;

[SlashCommand("market", "Bet play money on upcoming Nexus mods", Contexts = [InteractionContextType.Guild])]
public sealed class MarketModule(MarketService markets, MarketView view, IMarketNotifier notifier)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private ulong GuildId => Context.Interaction.GuildId ?? throw new InvalidOperationException("Guild-only command.");

    [SubSlashCommand("create", "Open a market on whether certain mods get released before a deadline")]
    public async Task CreateAsync(
        [SlashCommandParameter(Description = "How long betting stays open, e.g. 12h, 3d, 1w, 1d12h")] string duration,
        [SlashCommandParameter(Description = "Text that must appear in the mod name, e.g. Johnny", MaxLength = 50)] string? keyword = null,
        [SlashCommandParameter(Description = "Nexus category, e.g. Appearance, Gameplay, Vehicles", MaxLength = 50)] string? category = null,
        [SlashCommandParameter(Description = "Mod author or uploader name", MaxLength = 50)] string? author = null,
        [SlashCommandParameter(Description = "How many matching mods are needed for YES (default 1)", MinValue = 1, MaxValue = 10000)] int count = 1)
    {
        if (!DurationParser.TryParse(duration, out var span))
        {
            await RespondAsync(Ephemeral("I couldn't read that duration. Try something like `12h`, `3d`, `1w` or `1d12h`."));
            return;
        }

        if (string.IsNullOrWhiteSpace(keyword) && string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(author) && count < 2)
        {
            await RespondAsync(Ephemeral("Without a keyword, category or author, set `count` to at least 2. " +
                                         "Some new mod gets released pretty much every hour."));
            return;
        }

        Market market;
        try
        {
            market = await markets.CreateMarketAsync(new CreateMarketRequest(
                GuildId,
                Context.Interaction.Channel.Id,
                Context.User.Id,
                span,
                IsModerator(Context.User),
                Criteria: new ModCriteria(keyword, category, author, count)));
        }
        catch (MarketException ex)
        {
            await RespondAsync(Ephemeral(ex.Message));
            return;
        }

        await PostMarketAsync(market);
    }

    [SubSlashCommand("list", "Show the markets that are still running")]
    public async Task ListAsync()
    {
        var open = await markets.ListMarketsAsync(GuildId, includeFinished: false);
        if (open.Count == 0)
        {
            await RespondAsync(Ephemeral("No markets running right now. Start one with `/market create`!"));
            return;
        }

        var sb = new StringBuilder();
        foreach (var m in open)
        {
            var status = m.Status == MarketStatus.Open ? $"closes {MarketView.Timestamp(m.ClosesAtUtc)}" : "closed, resolving";
            sb.AppendLine($"**#{m.Id}** {MarketView.Escape(m.Question)}")
              .AppendLine($"YES {m.YesPrice():P0} · NO {1 - m.YesPrice():P0} · {markets.FormatMoney(m.Volume)} volume · {status}");
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([new EmbedProperties().WithTitle("Running markets").WithDescription(sb.ToString()).WithColor(new Color(0xFCEE0A))])
            .WithAllowedMentions(AllowedMentionsProperties.None)));
    }

    [SubSlashCommand("view", "Show a market with its bet buttons")]
    public async Task ViewAsync(
        [SlashCommandParameter(Description = "Market number", AutocompleteProviderType = typeof(MarketAutocomplete))] int market)
    {
        var m = await markets.GetMarketAsync(GuildId, market);
        if (m is null)
        {
            await RespondAsync(Ephemeral($"Market #{market} doesn't exist."));
            return;
        }

        await RespondAsync(MarketMessage(view, m));
    }

    [SubSlashCommand("buy", "Buy YES or NO shares")]
    public async Task BuyAsync(
        [SlashCommandParameter(Description = "Market number", AutocompleteProviderType = typeof(MarketAutocomplete))] int market,
        [SlashCommandParameter(Description = "Which outcome you're betting on")] Side side,
        [SlashCommandParameter(Description = "Eddies to spend", MinValue = 1)] long amount)
    {
        try
        {
            var trade = await markets.BuyAsync(GuildId, Context.User.Id, market, side, amount);
            await RespondAsync(Ephemeral(DescribeBuy(markets, trade)));
            await notifier.RefreshAsync(trade.Market);
        }
        catch (MarketException ex)
        {
            await RespondAsync(Ephemeral(ex.Message));
        }
    }

    [SubSlashCommand("sell", "Sell shares back before the market closes")]
    public async Task SellAsync(
        [SlashCommandParameter(Description = "Market number", AutocompleteProviderType = typeof(MarketAutocomplete))] int market,
        [SlashCommandParameter(Description = "Which shares to sell")] Side side,
        [SlashCommandParameter(Description = "How many shares (leave empty to sell all)", MinValue = 0.1)] double? shares = null)
    {
        try
        {
            var trade = await markets.SellAsync(GuildId, Context.User.Id, market, side, shares);
            await RespondAsync(Ephemeral(DescribeSell(markets, trade)));
            await notifier.RefreshAsync(trade.Market);
        }
        catch (MarketException ex)
        {
            await RespondAsync(Ephemeral(ex.Message));
        }
    }

    private async Task PostMarketAsync(Market market)
    {
        await RespondAsync(MarketMessage(view, market));
        var response = await GetResponseAsync();
        await markets.AttachMessageAsync(market.Id, response.ChannelId, response.Id);
    }
}

[SlashCommand("market-admin", "Moderator tools for prediction markets",
    DefaultGuildPermissions = Permissions.ManageGuild, Contexts = [InteractionContextType.Guild])]
public sealed class MarketAdminModule(MarketService markets, MarketView view, IMarketNotifier notifier)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    private ulong GuildId => Context.Interaction.GuildId ?? throw new InvalidOperationException("Guild-only command.");

    [SubSlashCommand("custom", "Open a free-form market that a moderator resolves")]
    public async Task CustomAsync(
        [SlashCommandParameter(Description = "The yes/no question", MaxLength = MarketService.MaxQuestionLength)] string question,
        [SlashCommandParameter(Description = "How long betting stays open, e.g. 12h, 3d, 1w")] string duration)
    {
        if (!DurationParser.TryParse(duration, out var span))
        {
            await RespondAsync(Ephemeral("I couldn't read that duration. Try something like `12h`, `3d`, `1w` or `1d12h`."));
            return;
        }

        try
        {
            var market = await markets.CreateMarketAsync(new CreateMarketRequest(
                GuildId, Context.Interaction.Channel.Id, Context.User.Id, span, CreatorIsModerator: true, Question: question));
            await RespondAsync(MarketMessage(view, market));
            var response = await GetResponseAsync();
            await markets.AttachMessageAsync(market.Id, response.ChannelId, response.Id);
        }
        catch (MarketException ex)
        {
            await RespondAsync(Ephemeral(ex.Message));
        }
    }

    [SubSlashCommand("resolve", "Settle a market and pay out the winners")]
    public async Task ResolveAsync(
        [SlashCommandParameter(Description = "Market number", AutocompleteProviderType = typeof(MarketAutocomplete))] int market,
        [SlashCommandParameter(Description = "The winning outcome")] Side outcome,
        [SlashCommandParameter(Description = "Optional explanation shown with the result", MaxLength = 300)] string? note = null)
    {
        try
        {
            var settlement = await markets.ResolveAsync(market, outcome, note ?? $"Resolved by <@{Context.User.Id}>.", GuildId);
            await RespondAsync(Ephemeral($"Market #{market} resolved {outcome.Label()}."));
            await notifier.AnnounceSettlementAsync(settlement);
        }
        catch (MarketException ex)
        {
            await RespondAsync(Ephemeral(ex.Message));
        }
    }

    [SubSlashCommand("cancel", "Cancel a market and refund everyone")]
    public async Task CancelAsync(
        [SlashCommandParameter(Description = "Market number", AutocompleteProviderType = typeof(MarketAutocomplete))] int market,
        [SlashCommandParameter(Description = "Optional reason", MaxLength = 300)] string? reason = null)
    {
        try
        {
            var settlement = await markets.CancelAsync(market, reason ?? $"Cancelled by <@{Context.User.Id}>.", GuildId);
            await RespondAsync(Ephemeral($"Market #{market} cancelled and refunded."));
            await notifier.AnnounceSettlementAsync(settlement);
        }
        catch (MarketException ex)
        {
            await RespondAsync(Ephemeral(ex.Message));
        }
    }
}

/// <summary>Suggests running markets for "market" parameters.</summary>
public sealed class MarketAutocomplete(MarketService markets) : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option, AutocompleteInteractionContext context)
    {
        if (context.Interaction.GuildId is not { } guildId)
            return [];

        var input = option.Value ?? "";
        var list = await markets.ListMarketsAsync(guildId, includeFinished: false);
        return list
            .Where(m => input.Length == 0 || m.Id.ToString().StartsWith(input, StringComparison.Ordinal)
                        || m.Question.Contains(input, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(m =>
            {
                var name = $"#{m.Id} {m.Question}";
                return new ApplicationCommandOptionChoiceProperties(name.Length > 100 ? name[..99] + "…" : name, m.Id);
            });
    }
}
