namespace DiscordBot.Nexus;

public sealed record NexusMod(
    int ModId,
    string Name,
    string? Summary,
    string? Author,
    string? Uploader,
    string? Category,
    DateTimeOffset CreatedAt,
    string? PictureUrl,
    bool AdultContent,
    DateTimeOffset UpdatedAt,
    string? Version)
{
    public string Url(string gameDomain) => PageUrl(gameDomain, ModId);

    public static string PageUrl(string gameDomain, int modId) => $"https://www.nexusmods.com/{gameDomain}/mods/{modId}";
}
