using System.Text;
using DiscordBot.Data;
using DiscordBot.Markets;
using DiscordBot.Nexus;
using NetCord;
using NetCord.Rest;

namespace DiscordBot.Discord;

/// <summary>Builds the Discord embeds and buttons for markets.</summary>
public sealed class MarketView(MarketService markets, Microsoft.Extensions.Options.IOptions<NexusOptions> nexusOptions)
{
    public const string BetButtonId = "market-bet";
    public const string PositionButtonId = "market-position";
    public const string BetModalId = "market-bet-modal";
    public const string BetAmountInputId = "amount";

    private static readonly Color OpenColor = new(0xFCEE0A); // Cyberpunk yellow
    private static readonly Color ClosedColor = new(0x7F8C8D);
    private static readonly Color YesColor = new(0x2ECC71);
    private static readonly Color NoColor = new(0xE74C3C);
    private static readonly Color CancelledColor = new(0x2C2F33);

    private readonly NexusOptions _nexus = nexusOptions.Value;

    public EmbedProperties BuildEmbed(Market market)
    {
        var yes = market.YesPrice();
        var description = new StringBuilder()
            .AppendLine(ProbabilityBar(yes))
            .AppendLine($"**YES {yes:P0}** · **NO {1 - yes:P0}**");

        var embed = new EmbedProperties()
            .WithTitle(Truncate($"#{market.Id} · {market.Question}", 256))
            .WithDescription(description.ToString())
            .WithColor(ColorFor(market))
            .WithFields(
            [
                new EmbedFieldProperties().WithName("Status").WithValue(StatusText(market)).WithInline(),
                new EmbedFieldProperties().WithName("Volume").WithValue(markets.FormatMoney(market.Volume)).WithInline(),
                new EmbedFieldProperties().WithName("Created by").WithValue($"<@{market.CreatorId}>").WithInline(),
                new EmbedFieldProperties().WithName("Resolution").WithValue(ResolutionRules(market)),
            ])
            .WithFooter(new EmbedFooterProperties().WithText(
                $"Prices are the crowd's odds. Each winning share pays 1 {markets.Options.CurrencySymbol}. Play money only."));

        if (market.Kind == MarketKind.Nexus)
            embed.WithUrl($"https://www.nexusmods.com/games/{_nexus.GameDomain}/mods?sort=createdAt");

        return embed;
    }

    public IEnumerable<IMessageComponentProperties> BuildComponents(Market market)
    {
        var tradable = market.Status == MarketStatus.Open;
        return
        [
            new ActionRowProperties(
            [
                new ButtonProperties($"{BetButtonId}:{market.Id}:{Side.Yes}", "Bet YES", ButtonStyle.Success).WithDisabled(!tradable),
                new ButtonProperties($"{BetButtonId}:{market.Id}:{Side.No}", "Bet NO", ButtonStyle.Danger).WithDisabled(!tradable),
                new ButtonProperties($"{PositionButtonId}:{market.Id}", "My position", ButtonStyle.Secondary),
            ]),
        ];
    }

    public ModalProperties BuildBetModal(Market market, Side side, long balance)
    {
        var price = side == Side.Yes ? market.YesPrice() : 1 - market.YesPrice();
        return new ModalProperties(
            $"{BetModalId}:{market.Id}:{side}",
            Truncate($"Bet {side.Label()} on #{market.Id} (now {price:P0})", 45),
            [
                new LabelProperties(
                        $"Amount in {markets.Options.CurrencyName}",
                        new TextInputProperties(BetAmountInputId, TextInputStyle.Short)
                            .WithPlaceholder("e.g. 100")
                            .WithMinLength(1)
                            .WithMaxLength(12)
                            .WithRequired(true))
                    .WithDescription($"You have {markets.FormatMoney(balance)}."),
            ]);
    }

    public EmbedProperties BuildSettlementEmbed(SettlementResult settlement, IReadOnlyList<NexusMod>? matchingMods = null)
    {
        var market = settlement.Market;
        var cancelled = market.Status == MarketStatus.Cancelled;
        var title = cancelled
            ? $"#{market.Id} cancelled, bets refunded"
            : $"#{market.Id} resolved {market.Outcome?.Label()}";

        var sb = new StringBuilder().AppendLine($"**{market.Question}**");
        if (!string.IsNullOrWhiteSpace(market.ResolutionNote))
            sb.AppendLine(market.ResolutionNote);

        if (matchingMods is { Count: > 0 })
        {
            sb.AppendLine().AppendLine("**Matching mods**");
            foreach (var mod in matchingMods.Take(5))
                sb.AppendLine($"• [{Escape(mod.Name)}]({mod.Url(_nexus.GameDomain)}) by {Escape(mod.Author ?? mod.Uploader ?? "unknown")}");
            if (matchingMods.Count > 5)
                sb.AppendLine($"…and {matchingMods.Count - 5} more");
        }

        sb.AppendLine().AppendLine(cancelled ? "**Refunds**" : "**Payouts**");
        if (settlement.Payouts.Count == 0)
            sb.AppendLine(cancelled ? "Nobody had anything to refund." : "Nobody bet on the winning side. 💸");
        foreach (var payout in settlement.Payouts.Take(10))
            sb.AppendLine($"<@{payout.UserId}> +{markets.FormatMoney(payout.Amount)}");
        if (settlement.Payouts.Count > 10)
            sb.AppendLine($"…and {settlement.Payouts.Count - 10} more");

        return new EmbedProperties()
            .WithTitle(Truncate(title, 256))
            .WithDescription(Truncate(sb.ToString(), 4000))
            .WithColor(ColorFor(market));
    }

    public EmbedProperties BuildModListEmbed(string title, IReadOnlyList<NexusMod> mods)
    {
        var sb = new StringBuilder();
        foreach (var mod in mods)
        {
            var category = mod.Category is null ? "" : $" · {Escape(mod.Category)}";
            sb.AppendLine($"**[{Escape(mod.Name)}]({mod.Url(_nexus.GameDomain)})**")
              .AppendLine($"by {Escape(mod.Author ?? mod.Uploader ?? "unknown")}{category} · <t:{mod.CreatedAt.ToUnixTimeSeconds()}:R>");
        }

        return new EmbedProperties()
            .WithTitle(title)
            .WithDescription(sb.Length == 0 ? "No mods found." : Truncate(sb.ToString(), 4000))
            .WithColor(OpenColor);
    }

    public string ResolutionRules(Market market)
    {
        if (market.Kind == MarketKind.Custom)
            return $"Resolved by a moderator. Betting closes {Timestamp(market.ClosesAtUtc, 'f')}.";

        var criteria = market.Criteria();
        var filters = new List<string>();
        if (criteria.Keyword is not null) filters.Add($"name contains \"{Escape(criteria.Keyword)}\"");
        if (criteria.Category is not null) filters.Add($"category matches \"{Escape(criteria.Category)}\"");
        if (criteria.Author is not null) filters.Add($"author is {Escape(criteria.Author)}");
        var filterText = filters.Count == 0 ? "any new mod" : string.Join(", ", filters);

        return $"YES once at least **{criteria.MinCount}** mod(s) on Nexus Mods ({_nexus.GameName}) are created " +
               $"between {Timestamp(market.OpensAtUtc, 'f')} and {Timestamp(market.ClosesAtUtc, 'f')} ({filterText}). " +
               "Resolves YES as soon as that happens, otherwise NO after the deadline.";
    }

    public static string Timestamp(DateTime utc, char style = 'R') =>
        $"<t:{new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:{style}>";

    private static string StatusText(Market market) => market.Status switch
    {
        MarketStatus.Open => $"🟢 Open · closes {Timestamp(market.ClosesAtUtc)}",
        MarketStatus.Closed => market.Kind == MarketKind.Nexus ? "🟡 Closed · checking Nexus…" : "🟡 Closed · awaiting moderator",
        MarketStatus.Resolved => $"🏁 Resolved **{market.Outcome?.Label()}**",
        MarketStatus.Cancelled => "⚫ Cancelled · refunded",
        _ => market.Status.ToString(),
    };

    private static Color ColorFor(Market market) => market.Status switch
    {
        MarketStatus.Open => OpenColor,
        MarketStatus.Closed => ClosedColor,
        MarketStatus.Resolved => market.Outcome == Side.Yes ? YesColor : NoColor,
        _ => CancelledColor,
    };

    private static string ProbabilityBar(double yes)
    {
        const int width = 12;
        var filled = (int)Math.Round(yes * width);
        return new string('█', filled) + new string('░', width - filled);
    }

    public static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`")
            .Replace("[", "\\[").Replace("]", "\\]").Replace("~", "\\~").Replace("|", "\\|");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
