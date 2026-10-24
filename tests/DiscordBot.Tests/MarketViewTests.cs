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

    [Fact]
    public void FinishedMarketsKeepTheRefreshButtonButNotTheBetButtons()
    {
        using var db = new TestDatabase();
        var view = new MarketView(
            TestFactory.CreateService(db, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(TestFactory.Start)),
            Microsoft.Extensions.Options.Options.Create(new DiscordBot.Nexus.NexusOptions()));
        var market = new Market { Id = 7, Question = "?", Status = MarketStatus.Resolved, Outcome = Side.Yes, Liquidity = 300 };

        var buttons = view.BuildComponents(market).OfType<NetCord.Rest.ActionRowProperties>()
            .SelectMany(r => r).OfType<NetCord.Rest.ButtonProperties>().ToList();

        Assert.All(buttons.Where(b => b.CustomId.StartsWith(MarketView.BetButtonId + ":")), b => Assert.True(b.Disabled));
        var refresh = Assert.Single(buttons, b => b.CustomId == $"{MarketView.RefreshButtonId}:7");
        Assert.False(refresh.Disabled == true);
    }

    [Fact]
    public void OpenUpdateMarketsHaveTheirOwnColor()
    {
        using var db = new TestDatabase();
        var view = new MarketView(
            TestFactory.CreateService(db, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(TestFactory.Start)),
            Microsoft.Extensions.Options.Options.Create(new DiscordBot.Nexus.NexusOptions()));
        Market Open(MarketKind kind) => new() { Question = "?", Kind = kind, Status = MarketStatus.Open, Liquidity = 300 };

        var newMod = view.BuildEmbed(Open(MarketKind.Nexus)).Color;
        var update = view.BuildEmbed(Open(MarketKind.NexusUpdate)).Color;

        Assert.NotEqual(newMod, update);
    }
}
