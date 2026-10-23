using System.Text;
using DiscordBot.Markets;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using static DiscordBot.Discord.Modules.BotModuleHelpers;

namespace DiscordBot.Discord.Modules;

public sealed class EconomyModule(MarketService markets) : ApplicationCommandModule<ApplicationCommandContext>
{
    private ulong GuildId => Context.Interaction.GuildId ?? throw new InvalidOperationException("Guild-only command.");

    [SlashCommand("wallet", "Show your eddies and open bets", Contexts = [InteractionContextType.Guild])]
    public async Task WalletAsync()
    {
        var wallet = await markets.GetWalletAsync(GuildId, Context.User.Id);
        var positions = await markets.GetPositionsAsync(GuildId, Context.User.Id);

        var embed = new EmbedProperties()
            .WithTitle($"{Context.User.GlobalName ?? Context.User.Username}'s wallet")
            .WithDescription($"**Balance:** {markets.FormatMoney(wallet.Balance)}\n\n**Open positions**\n{DescribePositions(markets, positions)}")
            .WithColor(new Color(0xFCEE0A));
        await RespondAsync(Ephemeral(embed));
    }

    [SlashCommand("daily", "Collect your daily eddies", Contexts = [InteractionContextType.Guild])]
    public async Task DailyAsync()
    {
        var result = await markets.ClaimDailyAsync(GuildId, Context.User.Id);
        var message = result.Claimed
            ? $"💸 +{markets.FormatMoney(result.Amount)}! Balance: {markets.FormatMoney(result.Balance)}."
            : $"Already collected today. Come back {MarketView.Timestamp(result.NextClaimUtc)}. Balance: {markets.FormatMoney(result.Balance)}.";
        await RespondAsync(Ephemeral(message));
    }

    [SlashCommand("leaderboard", "The richest forecasters on this server", Contexts = [InteractionContextType.Guild])]
    public async Task LeaderboardAsync()
    {
        var top = await markets.GetLeaderboardAsync(GuildId, 10);
        var sb = new StringBuilder();
        for (var i = 0; i < top.Count; i++)
        {
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => $"`{i + 1}.`" };
            sb.AppendLine($"{medal} <@{top[i].UserId}> · {markets.FormatMoney(top[i].Balance)}");
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithEmbeds([new EmbedProperties()
                .WithTitle("Leaderboard")
                .WithDescription(sb.Length == 0 ? "Nobody has placed a bet yet." : sb.ToString())
                .WithColor(new Color(0xFCEE0A))])
            .WithAllowedMentions(AllowedMentionsProperties.None)));
    }
}
