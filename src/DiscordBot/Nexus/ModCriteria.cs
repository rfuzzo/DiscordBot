namespace DiscordBot.Nexus;

/// <summary>
/// What a Nexus market is betting on: at least <see cref="MinCount"/> new mods matching all given filters.
/// Filters are case-insensitive; empty filters match everything.
/// </summary>
public sealed record ModCriteria(string? Keyword, string? Category, string? Author, int MinCount = 1)
{
    public bool Matches(NexusMod mod) =>
        (string.IsNullOrWhiteSpace(Keyword) || mod.Name.Contains(Keyword.Trim(), StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(Category) || (mod.Category?.Contains(Category.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
        && (string.IsNullOrWhiteSpace(Author) || IsAuthor(mod, Author.Trim()));

    private static bool IsAuthor(NexusMod mod, string author) =>
        string.Equals(mod.Author?.Trim(), author, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mod.Uploader?.Trim(), author, StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the market question, e.g. "Will at least 2 new Cyberpunk 2077 mods with "Johnny" in the name be released?".</summary>
    public string ToQuestion(string gameName)
    {
        var subject = MinCount == 1 ? $"a new {gameName} mod" : $"at least {MinCount} new {gameName} mods";
        var parts = new List<string> { subject };
        if (!string.IsNullOrWhiteSpace(Keyword))
            parts.Add($"with \"{Keyword.Trim()}\" in the name");
        if (!string.IsNullOrWhiteSpace(Category))
            parts.Add($"in the \"{Category.Trim()}\" category");
        if (!string.IsNullOrWhiteSpace(Author))
            parts.Add($"by {Author.Trim()}");
        return $"Will {string.Join(' ', parts)} be released?";
    }
}
