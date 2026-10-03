using DiscordBot.Data;
using DiscordBot.Discord;
using DiscordBot.Markets;
using DiscordBot.Nexus;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static DiscordBot.Tests.TestFactory;

namespace DiscordBot.Tests;

public sealed class MarketResolverServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly FakeNexusClient _nexus = new();
    private readonly RecordingNotifier _notifier = new();
    private readonly MarketService _markets;
    private readonly MarketResolverService _resolver;

    public MarketResolverServiceTests()
    {
        _markets = CreateService(_db, _time);
        _resolver = new MarketResolverService(
            _markets,
            new NexusModTracker(_nexus, _time, NullLogger<NexusModTracker>.Instance),
            _notifier,
            Microsoft.Extensions.Options.Options.Create(new MarketOptions()),
            _time,
            NullLogger<MarketResolverService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private Task<Market> CreateJohnnyMarketAsync(int count = 1) =>
        _markets.CreateMarketAsync(new CreateMarketRequest(
            Guild, Channel, Alice, TimeSpan.FromDays(2), false, Criteria: new ModCriteria("Johnny", null, null, count)));

    [Fact]
    public async Task ResolvesYesAsSoonAsAMatchingModAppears()
    {
        var market = await CreateJohnnyMarketAsync();
        await _markets.BuyAsync(Guild, Bob, market.Id, Side.Yes, 100);

        await _resolver.RunOnceAsync();
        Assert.Empty(_notifier.Settlements);

        _time.Advance(TimeSpan.FromHours(5));
        _nexus.Mods.Add(Mod(1, "Johnny's Jacket", _time.GetUtcNow()));
        await _resolver.RunOnceAsync();

        var settlement = Assert.Single(_notifier.Settlements);
        Assert.Equal(Side.Yes, settlement.Market.Outcome);
        Assert.Equal(Bob, Assert.Single(settlement.Payouts).UserId);
    }

    [Fact]
    public async Task ResolvesNoAfterTheDeadlinePlusGracePeriod()
    {
        var market = await CreateJohnnyMarketAsync();
        _nexus.Mods.Add(Mod(1, "Unrelated Mod", Start.AddHours(1)));

        _time.Advance(TimeSpan.FromDays(2));
        await _resolver.RunOnceAsync();
        Assert.Empty(_notifier.Settlements);
        Assert.Contains(_notifier.Refreshed, m => m.Id == market.Id && m.Status == MarketStatus.Closed);

        _time.Advance(new MarketOptions().ResolutionDelay);
        await _resolver.RunOnceAsync();
        Assert.Equal(Side.No, Assert.Single(_notifier.Settlements).Market.Outcome);
    }

    [Fact]
    public async Task IgnoresModsOutsideTheWindow()
    {
        _nexus.Mods.Add(Mod(1, "Johnny Before", Start.AddMinutes(-1)));
        var market = await CreateJohnnyMarketAsync();
        _nexus.Mods.Add(Mod(2, "Johnny After", Start.AddDays(2).AddMinutes(1)));

        Assert.Empty(MarketResolverService.MatchingMods(market, _nexus.Mods));
    }

    [Fact]
    public async Task NeedsTheFullCount()
    {
        await CreateJohnnyMarketAsync(count: 2);
        _nexus.Mods.Add(Mod(1, "Johnny A", Start.AddHours(1)));
        await _resolver.RunOnceAsync();
        Assert.Empty(_notifier.Settlements);

        _nexus.Mods.Add(Mod(2, "Johnny B", Start.AddHours(2)));
        await _resolver.RunOnceAsync();
        Assert.Equal(Side.Yes, Assert.Single(_notifier.Settlements).Market.Outcome);
    }

    [Fact]
    public async Task NexusOutageLeavesMarketsPending()
    {
        await CreateJohnnyMarketAsync();
        _nexus.Fail = true;
        _time.Advance(TimeSpan.FromDays(3));

        await _resolver.RunOnceAsync();

        Assert.Empty(_notifier.Settlements);
        Assert.Single(await _markets.GetUnsettledNexusMarketsAsync());
    }

    private sealed class FakeNexusClient : INexusModsClient
    {
        public List<NexusMod> Mods { get; } = [];
        public bool Fail { get; set; }

        public Task<IReadOnlyList<NexusMod>> GetLatestModsAsync(int count, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NexusMod>>(Mods.OrderByDescending(m => m.CreatedAt).Take(count).ToList());

        public Task<IReadOnlyList<NexusMod>> GetModsCreatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Fail
                ? throw new NexusApiException("down")
                : Task.FromResult<IReadOnlyList<NexusMod>>(Mods.Where(m => m.CreatedAt >= since).ToList());
    }

    private sealed class RecordingNotifier : IMarketNotifier
    {
        public List<Market> Refreshed { get; } = [];
        public List<SettlementResult> Settlements { get; } = [];

        public Task RefreshAsync(Market market)
        {
            Refreshed.Add(market);
            return Task.CompletedTask;
        }

        public Task AnnounceSettlementAsync(SettlementResult settlement, IReadOnlyList<NexusMod>? matchingMods = null)
        {
            Settlements.Add(settlement);
            return Task.CompletedTask;
        }
    }
}
