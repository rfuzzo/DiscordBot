using DiscordBot.Eta;
using Microsoft.Extensions.Time.Testing;
using static DiscordBot.Tests.TestFactory;

namespace DiscordBot.Tests;

public sealed class EtaServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly EtaService _eta;

    public EtaServiceTests() => _eta = new EtaService(_db, _time);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task EachNagAddsADayPerGuild()
    {
        Assert.Equal(1, (await _eta.AddNagAsync(Guild)).DelayDays);
        Assert.Equal(2, (await _eta.AddNagAsync(Guild)).DelayDays);
        Assert.Equal(1, (await _eta.AddNagAsync(Guild + 1)).DelayDays);
        Assert.Equal(3, (await _eta.AddNagAsync(Guild)).DelayDays);
    }

    [Fact]
    public async Task CounterSurvivesARestart()
    {
        await _eta.AddNagAsync(Guild);
        var afterRestart = new EtaService(_db, _time);
        Assert.Equal(2, (await afterRestart.AddNagAsync(Guild)).DelayDays);
    }

    [Fact]
    public async Task StatusShowsTheLastAnnouncedDateWithoutAddingANag()
    {
        await _eta.AddNagAsync(Guild);
        _time.Advance(TimeSpan.FromHours(5));
        var nag = await _eta.AddNagAsync(Guild);
        _time.Advance(TimeSpan.FromDays(1));

        var status = await _eta.GetStatusAsync(Guild);
        Assert.Equal(new EtaStatus(2, nag.ReleaseAt), status);
        Assert.Equal(status, await _eta.GetStatusAsync(Guild));
        Assert.Equal(
            $"Due to 2 users nagging, the core mods update has been delayed by 2 days. It is now scheduled for <t:{nag.ReleaseAt.ToUnixTimeSeconds()}:D>.",
            EtaService.Status(status));
    }

    [Fact]
    public async Task StatusBeforeAnyNag()
    {
        var status = await _eta.GetStatusAsync(Guild);
        Assert.Equal(0, status.DelayDays);
        Assert.StartsWith("Nobody has nagged yet", EtaService.Status(status));
        Assert.StartsWith("Due to 1 user nagging, the core mods update has been delayed by 1 day.",
            EtaService.Status(new EtaStatus(1, Start)));
    }

    [Fact]
    public void AnnouncementMentionsTheNaggerAndTheNewDate()
    {
        var releaseAt = Start.AddDays(3);
        Assert.Equal(
            $"Due to <@{Bob}>'s incessant nagging, the core mods update will be delayed by 3 days and will now release on <t:{releaseAt.ToUnixTimeSeconds()}:D>.",
            EtaService.Announcement(Bob, new EtaNag(3, releaseAt)));
        Assert.Contains("by 1 day and will now release on", EtaService.Announcement(Bob, new EtaNag(1, Start.AddDays(1))));
    }
}
