using DiscordBot.Markets;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using static DiscordBot.Discord.Modules.BotModuleHelpers;

namespace DiscordBot.Discord.Modules;

/// <summary>Self-timeout lengths; the value is the number of minutes.</summary>
public enum SelfTimeoutLength
{
    [SlashCommandChoice(Name = "1 minute")]
    OneMinute = 1,

    [SlashCommandChoice(Name = "5 minutes")]
    FiveMinutes = 5,

    [SlashCommandChoice(Name = "10 minutes")]
    TenMinutes = 10,

    [SlashCommandChoice(Name = "30 minutes")]
    ThirtyMinutes = 30,
}

/// <summary>/getbacktowork: time yourself out for a fixed number of eddies per minute, up to a daily limit.</summary>
public sealed class SelfTimeoutModule(MarketService markets) : ApplicationCommandModule<ApplicationCommandContext>
{
    public const string ConfirmButtonId = "self-timeout";

    [SlashCommand("getbacktowork", "Time yourself out and earn eddies for it", Contexts = [InteractionContextType.Guild])]
    public async Task GetBackToWorkAsync(
        [SlashCommandParameter(Description = "How long you can't write")] SelfTimeoutLength length)
    {
        if (Context.User is GuildInteractionUser member && member.Permissions.HasFlag(Permissions.Administrator))
        {
            await RespondAsync(Ephemeral("Discord doesn't let bots time out admins, so you'll have to get back to work on your own."));
            return;
        }

        var minutes = (int)length;
        var amount = minutes * markets.Options.SelfTimeoutPerMinute;
        var left = await markets.GetSelfTimeoutAllowanceAsync(Context.Interaction.GuildId!.Value, Context.User.Id);
        if (amount > left)
        {
            await RespondAsync(Ephemeral(left <= 0
                ? $"You've already earned the daily maximum of {markets.FormatMoney(markets.Options.SelfTimeoutDailyLimit)}. Come back tomorrow (UTC)."
                : $"That would pay {markets.FormatMoney(amount)}, but you can only earn {markets.FormatMoney(left)} more today."));
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent($"You won't be able to write, react or use commands for **{Describe(minutes)}**, " +
                         $"and you'll earn **{markets.FormatMoney(amount)}**. Sure?")
            .WithComponents([new ActionRowProperties([
                new ButtonProperties($"{ConfirmButtonId}:{minutes}", "Time me out", ButtonStyle.Danger),
            ])])
            .WithFlags(MessageFlags.Ephemeral)));
    }

    public static string Describe(int minutes) => minutes == 1 ? "1 minute" : $"{minutes} minutes";
}

/// <summary>The confirm button of /getbacktowork.</summary>
public sealed class SelfTimeoutButtonModule(MarketService markets, TimeProvider time, ILogger<SelfTimeoutButtonModule> logger)
    : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction(SelfTimeoutModule.ConfirmButtonId)]
    public async Task ConfirmAsync(int minutes)
    {
        if (!Enum.IsDefined((SelfTimeoutLength)minutes))
            return;

        var guildId = Context.Interaction.GuildId!.Value;
        var userId = Context.User.Id;

        // Pay first and take it back if the timeout fails, so the daily limit can't be raced.
        SelfTimeoutResult paid;
        try
        {
            paid = await markets.PaySelfTimeoutAsync(guildId, userId, minutes);
        }
        catch (MarketException ex)
        {
            await RespondAsync(InteractionCallback.ModifyMessage(m => { m.Content = ex.Message; m.Components = []; }));
            return;
        }

        try
        {
            await Context.Client.Rest.ModifyGuildUserAsync(guildId, userId,
                u => u.TimeOutUntil = time.GetUtcNow().AddMinutes(minutes));
        }
        catch (RestException ex)
        {
            await markets.RefundSelfTimeoutAsync(guildId, userId, paid.Amount);
            logger.LogWarning(ex, "Could not time out user {UserId} in guild {GuildId}", userId, guildId);
            await RespondAsync(InteractionCallback.ModifyMessage(m =>
            {
                m.Content = "I couldn't time you out, so nothing was paid. Bots can't time out admins, the server owner, " +
                            "or anyone with a role above the bot's.";
                m.Components = [];
            }));
            return;
        }

        var length = SelfTimeoutModule.Describe(minutes);
        await RespondAsync(InteractionCallback.ModifyMessage(m =>
        {
            m.Content = $"Back to work! You earned {markets.FormatMoney(paid.Amount)}. Balance: {markets.FormatMoney(paid.Balance)}.";
            m.Components = [];
        }));

        try
        {
            await Context.Client.Rest.SendMessageAsync(Context.Interaction.Channel.Id, new MessageProperties()
                .WithContent($"<@{userId}> timed themself out for {length} and earned {markets.FormatMoney(paid.Amount)}.")
                .WithAllowedMentions(AllowedMentionsProperties.None));
        }
        catch (RestException ex)
        {
            logger.LogWarning(ex, "Could not announce the self-timeout of user {UserId}", userId);
        }
    }
}
