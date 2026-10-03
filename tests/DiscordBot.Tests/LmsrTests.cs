using DiscordBot.Data;
using DiscordBot.Markets;

namespace DiscordBot.Tests;

public class LmsrTests
{
    private const double B = 300;

    [Fact]
    public void FreshMarketIsFiftyFifty()
    {
        Assert.Equal(0.5, Lmsr.Price(0, 0, B, Side.Yes), 10);
        Assert.Equal(0.5, Lmsr.Price(0, 0, B, Side.No), 10);
    }

    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(500, 20, 1)]
    [InlineData(20, 500, 250)]
    [InlineData(100_000, 99_000, 5000)]
    public void BuyingCostsExactlyTheAmountSpent(double qYes, double qNo, double amount)
    {
        foreach (var side in new[] { Side.Yes, Side.No })
        {
            var shares = Lmsr.SharesForAmount(qYes, qNo, B, side, amount);
            var cost = side == Side.Yes
                ? Lmsr.Cost(qYes + shares, qNo, B) - Lmsr.Cost(qYes, qNo, B)
                : Lmsr.Cost(qYes, qNo + shares, B) - Lmsr.Cost(qYes, qNo, B);

            Assert.True(shares > 0);
            Assert.Equal(amount, cost, 6);
        }
    }

    [Fact]
    public void SellingRightAwayRefundsTheStake()
    {
        var shares = Lmsr.SharesForAmount(0, 0, B, Side.Yes, 100);
        var refund = Lmsr.RefundForShares(shares, 0, B, Side.Yes, shares);
        Assert.Equal(100, refund, 6);
    }

    [Fact]
    public void BuyingMovesThePriceTowardsThatSide()
    {
        var shares = Lmsr.SharesForAmount(0, 0, B, Side.Yes, 100);
        var price = Lmsr.Price(shares, 0, B, Side.Yes);

        // 100 €$ on a fresh market with b = 300 moves YES to roughly 64%.
        Assert.InRange(price, 0.63, 0.65);
        // Shares cost less than 1 each, so a winning bet always profits.
        Assert.True(shares > 100);
    }

    [Fact]
    public void PricesStaySaneForHugePositions()
    {
        var price = Lmsr.Price(1_000_000, 0, B, Side.Yes);
        Assert.False(double.IsNaN(price));
        Assert.InRange(price, 0.999, 1.0);
        Assert.False(double.IsInfinity(Lmsr.Cost(1_000_000, 0, B)));
    }
}
