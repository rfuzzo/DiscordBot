using DiscordBot.Data;

namespace DiscordBot.Markets;

/// <summary>
/// Logarithmic Market Scoring Rule (Hanson) for a binary YES/NO market.
/// <para>
/// The market maker tracks the outstanding YES and NO shares (<c>qYes</c>, <c>qNo</c>).
/// The cost function is <c>C(q) = b · ln(e^(qYes/b) + e^(qNo/b))</c>; a trade costs the
/// difference in C before and after it. Each winning share pays out exactly 1 coin, so the
/// instantaneous price of a side is the market's probability for that outcome.
/// </para>
/// <para>
/// <c>b</c> is the liquidity parameter: larger values mean prices move less per coin traded.
/// All functions are written in log-sum-exp form so they stay numerically stable for large share counts.
/// </para>
/// </summary>
public static class Lmsr
{
    public static double Cost(double qYes, double qNo, double b)
    {
        var max = Math.Max(qYes, qNo);
        return max + b * Math.Log(Math.Exp((qYes - max) / b) + Math.Exp((qNo - max) / b));
    }

    /// <summary>Probability (0..1) the market currently assigns to <paramref name="side"/>.</summary>
    public static double Price(double qYes, double qNo, double b, Side side)
    {
        // Logistic of the share difference: e^(qYes/b) / (e^(qYes/b) + e^(qNo/b)).
        var yes = 1.0 / (1.0 + Math.Exp((qNo - qYes) / b));
        return side == Side.Yes ? yes : 1.0 - yes;
    }

    /// <summary>Number of <paramref name="side"/> shares that spending exactly <paramref name="amount"/> coins buys.</summary>
    public static double SharesForAmount(double qYes, double qNo, double b, Side side, double amount)
    {
        if (amount <= 0)
            return 0;

        var (mine, other) = side == Side.Yes ? (qYes, qNo) : (qNo, qYes);
        var target = Cost(qYes, qNo, b) + amount;

        // Solve C(mine + Δ, other) = target for Δ:
        //   e^((mine+Δ)/b) = e^(target/b) - e^(other/b) = e^(other/b) · (e^((target-other)/b) - 1)
        //   Δ = other + b · ln(expm1((target - other)/b)) - mine
        return other + b * Math.Log(ExpM1((target - other) / b)) - mine;
    }

    /// <summary>Coins received for selling <paramref name="shares"/> of <paramref name="side"/> back to the market maker.</summary>
    public static double RefundForShares(double qYes, double qNo, double b, Side side, double shares)
    {
        if (shares <= 0)
            return 0;

        var before = Cost(qYes, qNo, b);
        var after = side == Side.Yes
            ? Cost(qYes - shares, qNo, b)
            : Cost(qYes, qNo - shares, b);
        return before - after;
    }

    // e^x - 1 without losing precision for small x.
    private static double ExpM1(double x) => Math.Abs(x) < 1e-5 ? x + x * x / 2 + x * x * x / 6 : Math.Exp(x) - 1;
}
