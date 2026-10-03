using DiscordBot.Data;
using DiscordBot.Discord;
using DiscordBot.Markets;
using DiscordBot.Nexus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Hosting.Services;
using NetCord.Hosting.Services.ApplicationCommands;
using NetCord.Hosting.Services.ComponentInteractions;
using NetCord.Services.ComponentInteractions;

// appsettings.json ships next to the binary; relative paths (the database) resolve from the working directory.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Configuration: appsettings.json, then environment variables (Discord__Token, Nexus__ApiKey, ...).
builder.Services.AddOptions<NexusOptions>().BindConfiguration(NexusOptions.Section);
builder.Services.AddOptions<MarketOptions>().BindConfiguration(MarketOptions.Section);

var databasePath = Path.GetFullPath(builder.Configuration["Database:Path"] ?? "data/discordbot.db");
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
builder.Services.AddDbContextFactory<BotDbContext>(o => o.UseSqlite($"Data Source={databasePath}"));

builder.Services
    .AddSingleton(TimeProvider.System)
    .AddSingleton<MarketService>()
    .AddSingleton<NexusModTracker>()
    .AddSingleton<MarketView>()
    .AddSingleton<IMarketNotifier, MarketMessenger>()
    .AddHostedService<MarketResolverService>();
builder.Services.AddHttpClient<INexusModsClient, NexusModsClient>();

// Discord: reads the bot token from the "Discord" configuration section.
builder.Services
    .AddDiscordGateway(o => o.Intents = GatewayIntents.Guilds)
    .AddApplicationCommands()
    .AddComponentInteractions<ButtonInteraction, ButtonInteractionContext>()
    .AddComponentInteractions<ModalInteraction, ModalInteractionContext>();

var host = builder.Build();
host.AddModules(typeof(Program).Assembly);

await using (var db = await host.Services.GetRequiredService<IDbContextFactory<BotDbContext>>().CreateDbContextAsync())
    await db.Database.EnsureCreatedAsync();

await host.RunAsync();
