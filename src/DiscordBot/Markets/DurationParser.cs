using System.Text.RegularExpressions;

namespace DiscordBot.Markets;

/// <summary>Parses short durations such as "90m", "12h", "3d", "1w" or "1d12h".</summary>
public static partial class DurationParser
{
    [GeneratedRegex(@"^\s*(?:(?<w>\d+)\s*w)?\s*(?:(?<d>\d+)\s*d)?\s*(?:(?<h>\d+)\s*h)?\s*(?:(?<m>\d+)\s*m)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static bool TryParse(string? input, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = Pattern().Match(input);
        if (!match.Success)
            return false;

        static int Part(Match m, string name) => m.Groups[name].Success ? int.Parse(m.Groups[name].Value) : 0;

        try
        {
            duration = TimeSpan.FromDays(Part(match, "w") * 7.0 + Part(match, "d"))
                       + TimeSpan.FromHours(Part(match, "h"))
                       + TimeSpan.FromMinutes(Part(match, "m"));
        }
        catch (OverflowException)
        {
            return false;
        }

        return duration > TimeSpan.Zero;
    }

    public static string Format(TimeSpan duration)
    {
        var parts = new List<string>();
        if (duration.Days > 0) parts.Add($"{duration.Days}d");
        if (duration.Hours > 0) parts.Add($"{duration.Hours}h");
        if (duration.Minutes > 0) parts.Add($"{duration.Minutes}m");
        return parts.Count == 0 ? "0m" : string.Join(' ', parts);
    }
}
