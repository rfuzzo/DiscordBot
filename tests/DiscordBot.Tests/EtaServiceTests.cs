using DiscordBot.Eta;
using Microsoft.Extensions.Time.Testing;
using static DiscordBot.Tests.TestFactory;

namespace DiscordBot.Tests;

public sealed class EtaServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly EtaService _eta;

    public EtaServiceTests() => _eta = new EtaService(_db, new FakeTimeProvider(Start));

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task EachNagAddsADayPerGuild()
    {
        Assert.Equal(1, await _eta.AddNagAsync(Guild));
        Assert.Equal(2, await _eta.AddNagAsync(Guild));
        Assert.Equal(1, await _eta.AddNagAsync(Guild + 1));
        Assert.Equal(3, await _eta.AddNagAsync(Guild));
    }

    [Fact]
    public async Task CounterSurvivesARestart()
    {
        await _eta.AddNagAsync(Guild);
        var afterRestart = new EtaService(_db, new FakeTimeProvider(Start));
        Assert.Equal(2, await afterRestart.AddNagAsync(Guild));
    }

    [Fact]
    public void AnnouncementMentionsTheNaggerAndTheNewDate()
    {
        var text = _eta.Announcement(Bob, 3);
        var releaseDate = Start.AddDays(3).ToUnixTimeSeconds();
        Assert.Equal(
            $"Due to <@{Bob}>'s incessant nagging, the core mods update will be delayed by 3 days and will now release on <t:{releaseDate}:D>.",
            text);
        Assert.Contains("by 1 day and will now release on", _eta.Announcement(Bob, 1));
    }
}
