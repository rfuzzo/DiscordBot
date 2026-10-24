using DiscordBot.Eta;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace DiscordBot.Discord.Modules;

/// <summary>
/// "/eta" blames the caller (or a chosen user); the "ETA" message command (Apps menu on a message) blames that
/// message's author. Discord doesn't tell slash commands which message they replied to, hence both.
/// "/when" only shows where things stand.
/// </summary>
public sealed class EtaModule(EtaService eta) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("eta", "Ask when the core mods update is coming (it won't help)", Contexts = [InteractionContextType.Guild])]
    public Task EtaAsync(
        [SlashCommandParameter(Description = "Who is nagging (default: you)")] User? user = null) =>
        AnnounceAsync((user ?? Context.User).Id);

    [MessageCommand("ETA", Contexts = [InteractionContextType.Guild])]
    public Task EtaForMessageAsync(RestMessage message) => AnnounceAsync(message.Author.Id);

    [SlashCommand("when", "When is the core mods update coming? (asking doesn't delay it)", Contexts = [InteractionContextType.Guild])]
    public async Task WhenAsync()
    {
        var status = await eta.GetStatusAsync(Context.Interaction.GuildId!.Value);
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(EtaService.Status(status))
            .WithAllowedMentions(AllowedMentionsProperties.None)));
    }

    private async Task AnnounceAsync(ulong naggerId)
    {
        var nag = await eta.AddNagAsync(Context.Interaction.GuildId!.Value);
        await RespondAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .WithContent(EtaService.Announcement(naggerId, nag))
            .WithAllowedMentions(new AllowedMentionsProperties { AllowedUsers = [naggerId] })));
    }
}
