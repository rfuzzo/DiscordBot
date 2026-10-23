using DiscordBot.Data;
using DiscordBot.Discord;

namespace DiscordBot.Tests;

public class MarketViewTests
{
    // NetCord only parses enum parameters in custom IDs from their numeric value ("0"), not their name ("Yes").
    [Theory]
    [InlineData(Side.Yes)]
    [InlineData(Side.No)]
    public void BetCustomIds_WriteSideAsNumber(Side side)
    {
        Assert.Equal($"{MarketView.BetButtonId}:42:{(int)side}", MarketView.BetButtonCustomId(42, side));
        Assert.Equal($"{MarketView.BetModalId}:42:{(int)side}", MarketView.BetModalCustomId(42, side));
        Assert.True(int.TryParse(MarketView.BetButtonCustomId(42, side).Split(':')[2], out _));
    }
}
