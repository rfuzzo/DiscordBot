namespace DiscordBot.Nexus;

public sealed class NexusOptions
{
    public const string Section = "Nexus";

    /// <summary>Personal API key from https://next.nexusmods.com/settings/api-keys. Optional for public mod queries.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Nexus game domain the bot follows.</summary>
    public string GameDomain { get; set; } = "cyberpunk2077";

    /// <summary>Human-readable game name used in messages.</summary>
    public string GameName { get; set; } = "Cyberpunk 2077";

    public Uri GraphQlEndpoint { get; set; } = new("https://api.nexusmods.com/v2/graphql");

    /// <summary>Mods requested per GraphQL page.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>Upper bound on pages fetched for one time window, to protect against runaway paging.</summary>
    public int MaxPages { get; set; } = 40;
}
