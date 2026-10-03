using DiscordBot.Nexus;
using Microsoft.Extensions.Options;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using static DiscordBot.Discord.Modules.BotModuleHelpers;

namespace DiscordBot.Discord.Modules;

[SlashCommand("nexus", "Nexus Mods lookups")]
public sealed class NexusModule(INexusModsClient nexus, MarketView view, IOptions<NexusOptions> options)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("latest", "Show the newest mods on Nexus Mods")]
    public async Task LatestAsync(
        [SlashCommandParameter(Description = "How many mods to show", MinValue = 1, MaxValue = 10)] int count = 5)
    {
        await RespondAsync(InteractionCallback.DeferredMessage());
        try
        {
            var mods = await nexus.GetLatestModsAsync(count);
            await FollowupAsync(new InteractionMessageProperties()
                .WithEmbeds([view.BuildModListEmbed($"Newest {options.Value.GameName} mods", mods)]));
        }
        catch (NexusApiException ex)
        {
            await FollowupAsync(new InteractionMessageProperties().WithContent($"⚠️ {ex.Message}"));
        }
    }
}
