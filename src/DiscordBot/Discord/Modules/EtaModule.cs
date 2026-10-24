using DiscordBot.Eta;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using static DiscordBot.Discord.Modules.BotModuleHelpers;

namespace DiscordBot.Discord.Modules;

/// <summary>
/// The "ETA" message command (Apps menu on a message) blames that message's author and delays the update a day.
/// Each message only counts once, and nobody can blame their own message. "/when" only shows where things stand.
/// </summary>
public sealed class EtaModule(EtaService eta) : ApplicationCommandModule<ApplicationCommandContext>
{
    [MessageCommand("ETA", Contexts = [InteractionContextType.Guild])]
    public async Task EtaAsync(RestMessage message)
    {
        var naggerId = message.Author.Id;
        if (naggerId == Context.User.Id)
        {
            await RespondAsync(Ephemeral("You can't blame yourself for the delay. Someone else has to do that."));
            return;
        }

        var nag = await eta.AddNagAsync(Context.Interaction.GuildId!.Value, message.Id);
        if (nag is null)
        {
            await RespondAsync(Ephemeral("This message already delayed the update. Each message only counts once."));
            return;
        }

        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(EtaService.Announcement(naggerId, nag))
            .WithAllowedMentions(new AllowedMentionsProperties { AllowedUsers = [naggerId] })));
    }

    [SlashCommand("when", "When is the core mods update coming? (asking doesn't delay it)", Contexts = [InteractionContextType.Guild])]
    public async Task WhenAsync()
    {
        var status = await eta.GetStatusAsync(Context.Interaction.GuildId!.Value);
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(EtaService.Status(status))
            .WithAllowedMentions(AllowedMentionsProperties.None)));
    }
}
