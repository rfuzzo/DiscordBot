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
    bool AdultContent)
{
    public string Url(string gameDomain) => $"https://www.nexusmods.com/{gameDomain}/mods/{ModId}";
}
