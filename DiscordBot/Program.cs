using System.IO.Compression;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot;

public class Program
{
    // start async context
    public static Task Main(string[] args) => new Program().MainAsync();

    // main async entry 
    private async Task MainAsync()
    {
        // register services
        using var services = ConfigureServices( /*configuration*/);
        var client = services.GetRequiredService<DiscordSocketClient>();
        var commands = services.GetRequiredService<InteractionService>();

        // logging
        client.Log += LoggingProvider.Log;
        commands.Log += LoggingProvider.Log;
        
        // on bot ready
        client.Ready += async () =>
        {
#if DEBUG
            await commands.RegisterCommandsToGuildAsync(Constants.TestGuild);
#else
            await commands.RegisterCommandsToGuildAsync(Constants.ProductionGuild);
#endif

            Console.WriteLine("Bot is connected!");
        };

        // initialize commands
        await services.GetRequiredService<CommandHandler>().InitializeAsync();

        // login for bot !!! USE ENVIRONMENT VARIABLES FOR TOKEN !!!
        await client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("DISCORD_TOKEN"));
        
        // start the bot
        await client.StartAsync();
        
        // run bot forever
        await Task.Delay(Timeout.Infinite);
    }

    /// <summary>
    ///  Configure dependency injection
    /// </summary>
    /// <returns></returns>
    private static ServiceProvider ConfigureServices( /*IConfiguration configuration*/)
    {
        return new ServiceCollection()
            .AddSingleton(x => new DiscordSocketClient(
                new DiscordSocketConfig
                {
                    LogLevel = LogSeverity.Info,

                    // If you or another service needs to do anything with messages
                    // (eg. checking Reactions, checking the content of edited/deleted messages),
                    // you must set the MessageCacheSize. You may adjust the number as needed.
                    MessageCacheSize = 50
                }))
            .AddSingleton(x => new InteractionService(
                x.GetRequiredService<DiscordSocketClient>(), 
                new InteractionServiceConfig
                {
                    LogLevel = LogSeverity.Info
                }))
            // Add custom services here
            .AddSingleton<CommandHandler>()
            .BuildServiceProvider();
    }
}