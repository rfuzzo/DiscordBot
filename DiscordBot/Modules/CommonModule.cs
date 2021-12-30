using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace DiscordBot.Modules;

/// <summary>
/// Test module
/// All modules must inherit from InteractionModuleBase
/// More info: https://discordnet.dev/guides/int_framework/intro.html
/// </summary>
public class CommonModule : InteractionModuleBase
{
    /// <summary>
    /// A simple slash command with no parameters
    /// </summary>
    [SlashCommand("ping", "Checks the bot connection")]
    public async Task Ping()
    {
        await RespondAsync("pong");
    }
    
    /// <summary>
    /// A simple slash command no parameters
    /// </summary>
    [SlashCommand("echo", "Prints input")]
    public async Task Echo(string input)
    {
        await RespondAsync(input);
    }
    
    /// <summary>
    /// A simple slash command with embeds
    /// </summary>
    [SlashCommand("embed", "Creates an embedded message")]
    public async Task Embed(string input)
    {
        // embed
        // more info: https://discord.com/developers/docs/resources/channel#embed-limits
        var embedBuilder = new EmbedBuilder()
            .WithTitle($"EmbedTitle")
            .WithDescription($"EmbedDescription:\n```{input}```")
            .WithColor(Color.Green)
            .WithCurrentTimestamp();
        embedBuilder.AddField("Field1", $"some field input");
        
        // menu
        // more info: https://discord.com/developers/docs/interactions/message-components#select-menus
        var menuBuilder = new SelectMenuBuilder()
            .WithPlaceholder("Select an option")
            .WithCustomId("menu-1")
            .WithMinValues(1)
            .WithMaxValues(1)
            .AddOption("Option A", "opt-a", "Option B is lying!")
            .AddOption("Option B", "opt-b", "Option A is telling the truth!");
        
        // button
        // more info: https://discord.com/developers/docs/interactions/message-components#buttons
        var componentBuilder = new ComponentBuilder()
            .WithSelectMenu(menuBuilder)
            //.WithButton("label", "custom-id")
            .WithButton("❌️", "delete");

        // builders need to be built
        await ReplyAsync("Here is a button!", 
            embed: embedBuilder.Build(),
            components: componentBuilder.Build());
    }
    
    
    /// <summary>
    /// An response for a button with ID "delete"
    /// </summary>
    [ComponentInteraction("delete")]
    public async Task ComponentInteractionDelete()
    {
        if (Context.Interaction is SocketMessageComponent socket)
            await Context.Channel.DeleteMessageAsync(socket.Message);
    }
    
    /// <summary>
    /// An response for a menu with ID "menu-*"
    /// You can use * to match wildcards
    /// </summary>
    /// <param name="menus"></param>
    [ComponentInteraction("menu-file")]
    public async Task ComponentInterActionMoreInfo(string[] menus)
    {
        foreach (var content in menus)
        {
            await RespondAsync(content, 
                ephemeral: true); // this is only visible to the user
        }
    }
}