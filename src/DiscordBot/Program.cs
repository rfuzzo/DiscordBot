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
{
    // Databases from before migrations were added have the tables but no migration history, so Migrate would fail on them.
    var legacy = !(await db.Database.GetAppliedMigrationsAsync()).Any()
                 && await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Markets'").SingleAsync() > 0;
    if (legacy)
        throw new InvalidOperationException(
            $"The database at {databasePath} was created by an older version of the bot and can't be upgraded. " +
            "Delete it (together with its -wal and -shm files) to start fresh.");

    await db.Database.MigrateAsync();
}

await host.RunAsync();
