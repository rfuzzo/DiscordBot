using DiscordBot.Markets;

namespace DiscordBot.Tests;

public class DurationParserTests
{
    [Theory]
    [InlineData("90m", 0, 1, 30)]
    [InlineData("12h", 0, 12, 0)]
    [InlineData("3d", 3, 0, 0)]
    [InlineData("1w", 7, 0, 0)]
    [InlineData("1d12h", 1, 12, 0)]
    [InlineData(" 2D 6H ", 2, 6, 0)]
    [InlineData("1w2d3h4m", 9, 3, 4)]
    public void ParsesValidDurations(string input, int days, int hours, int minutes)
    {
        Assert.True(DurationParser.TryParse(input, out var duration));
        Assert.Equal(new TimeSpan(days, hours, minutes, 0), duration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("0h")]
    [InlineData("12")]
    [InlineData("3h2d")]
    [InlineData("99999999999999d")]
    public void RejectsInvalidDurations(string? input)
    {
        Assert.False(DurationParser.TryParse(input, out _));
    }
}
