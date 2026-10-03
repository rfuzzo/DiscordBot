using DiscordBot.Data;
using DiscordBot.Markets;
using DiscordBot.Nexus;
using Microsoft.Extensions.Time.Testing;
using static DiscordBot.Tests.TestFactory;

namespace DiscordBot.Tests;

public sealed class MarketServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _time = new(Start);
    private readonly MarketService _service;

    public MarketServiceTests() => _service = CreateService(_db, _time);

    public void Dispose() => _db.Dispose();

    private Task<Market> CreateMarketAsync(ulong creator = Alice, bool moderator = false, TimeSpan? duration = null) =>
        _service.CreateMarketAsync(new CreateMarketRequest(
            Guild, Channel, creator, duration ?? TimeSpan.FromDays(1), moderator,
            Criteria: new ModCriteria("Johnny", null, null)));

    [Fact]
    public async Task NewUsersGetTheStartingBalance()
    {
        var wallet = await _service.GetWalletAsync(Guild, Alice);
        Assert.Equal(1000, wallet.Balance);
    }

    [Fact]
    public async Task BuyingTakesEddiesAndGivesShares()
    {
        var market = await CreateMarketAsync();

        var trade = await _service.BuyAsync(Guild, Alice, market.Id, Side.Yes, 100);

        Assert.Equal(900, trade.NewBalance);
        Assert.True(trade.Shares > 100);
        Assert.True(trade.Market.YesPrice() > 0.5);
        Assert.Equal(100, trade.Market.Volume);
        var position = Assert.Single(await _service.GetPositionsAsync(Guild, Alice));
        Assert.Equal(trade.Shares, position.Shares, 9);
    }

    [Fact]
    public async Task CannotBetMoreThanYouHave()
    {
        var market = await CreateMarketAsync();
        var ex = await Assert.ThrowsAsync<MarketException>(() => _service.BuyAsync(Guild, Alice, market.Id, Side.Yes, 1001));
        Assert.Contains("1,000", ex.Message);
    }

    [Fact]
    public async Task CannotBetAfterTheDeadline()
    {
        var market = await CreateMarketAsync();
        _time.Advance(TimeSpan.FromDays(1));
        await Assert.ThrowsAsync<MarketException>(() => _service.BuyAsync(Guild, Alice, market.Id, Side.Yes, 10));
    }

    [Fact]
    public async Task CannotTradeOnAnotherServersMarket()
    {
        var market = await CreateMarketAsync();
        await Assert.ThrowsAsync<MarketException>(() => _service.BuyAsync(Guild + 1, Alice, market.Id, Side.Yes, 10));
    }

    [Fact]
    public async Task SellingEverythingRightAwayGivesTheMoneyBack()
    {
        var market = await CreateMarketAsync();
        await _service.BuyAsync(Guild, Alice, market.Id, Side.No, 250);

        var sale = await _service.SellAsync(Guild, Alice, market.Id, Side.No, null);

        Assert.Equal(1000, sale.NewBalance);
        Assert.Equal(0, sale.PositionShares);
        Assert.Equal(0.5, sale.Market.YesPrice(), 6);
    }

    [Fact]
    public async Task ResolvingPaysOneEddiePerWinningShare()
    {
        var market = await CreateMarketAsync();
        var aliceTrade = await _service.BuyAsync(Guild, Alice, market.Id, Side.Yes, 100);
        await _service.BuyAsync(Guild, Bob, market.Id, Side.No, 200);

        var result = await _service.ResolveAsync(market.Id, Side.Yes, "test");

        var payout = Assert.Single(result.Payouts);
        Assert.Equal(Alice, payout.UserId);
        Assert.Equal((long)Math.Floor(aliceTrade.Shares), payout.Amount);
        Assert.Equal(900 + payout.Amount, (await _service.GetWalletAsync(Guild, Alice)).Balance);
        Assert.Equal(800, (await _service.GetWalletAsync(Guild, Bob)).Balance);
        Assert.Equal(MarketStatus.Resolved, result.Market.Status);
        await Assert.ThrowsAsync<MarketException>(() => _service.ResolveAsync(market.Id, Side.No, null));
    }

    [Fact]
    public async Task CancellingRefundsNetStakes()
    {
        var market = await CreateMarketAsync();
        await _service.BuyAsync(Guild, Alice, market.Id, Side.Yes, 100);
        await _service.BuyAsync(Guild, Bob, market.Id, Side.No, 300);
        await _service.BuyAsync(Guild, Bob, market.Id, Side.Yes, 50);

        var result = await _service.CancelAsync(market.Id, "test");

        Assert.Equal(2, result.Payouts.Count);
        Assert.Equal(1000, (await _service.GetWalletAsync(Guild, Alice)).Balance);
        Assert.Equal(1000, (await _service.GetWalletAsync(Guild, Bob)).Balance);
    }

    [Fact]
    public async Task DailyBonusOncePerUtcDay()
    {
        var first = await _service.ClaimDailyAsync(Guild, Alice);
        var second = await _service.ClaimDailyAsync(Guild, Alice);
        _time.Advance(TimeSpan.FromHours(12)); // past midnight UTC
        var third = await _service.ClaimDailyAsync(Guild, Alice);

        Assert.True(first.Claimed);
        Assert.False(second.Claimed);
        Assert.True(third.Claimed);
        Assert.Equal(1200, third.Balance);
    }

    [Fact]
    public async Task LimitsOpenMarketsPerUserButNotForModerators()
    {
        for (var i = 0; i < 3; i++)
            await CreateMarketAsync();

        await Assert.ThrowsAsync<MarketException>(() => CreateMarketAsync());
        await CreateMarketAsync(moderator: true);
        await CreateMarketAsync(creator: Bob);
    }

    [Fact]
    public async Task RejectsDurationsOutsideTheLimits()
    {
        await Assert.ThrowsAsync<MarketException>(() => CreateMarketAsync(duration: TimeSpan.FromMinutes(5)));
        await Assert.ThrowsAsync<MarketException>(() => CreateMarketAsync(duration: TimeSpan.FromDays(60)));
    }

    [Fact]
    public async Task ClosesExpiredMarkets()
    {
        var market = await CreateMarketAsync();
        Assert.Empty(await _service.CloseExpiredAsync());

        _time.Advance(TimeSpan.FromDays(1));
        var closed = Assert.Single(await _service.CloseExpiredAsync());
        Assert.Equal(market.Id, closed.Id);
        Assert.Equal(MarketStatus.Closed, (await _service.GetMarketAsync(Guild, market.Id))!.Status);
    }

    [Fact]
    public async Task LeaderboardIsSortedByBalance()
    {
        var market = await CreateMarketAsync();
        await _service.BuyAsync(Guild, Alice, market.Id, Side.Yes, 300);
        await _service.GetWalletAsync(Guild, Bob);

        var top = await _service.GetLeaderboardAsync(Guild, 10);
        Assert.Equal([Bob, Alice], top.Select(w => w.UserId));
    }
}
