namespace DiscordBot.Markets;

public sealed class MarketOptions
{
    public const string Section = "Market";

    /// <summary>Name of the play-money currency.</summary>
    public string CurrencyName { get; set; } = "eddies";

    /// <summary>Short currency symbol shown next to amounts.</summary>
    public string CurrencySymbol { get; set; } = "€$";

    /// <summary>Balance a user starts with the first time they use the economy in a server.</summary>
    public long StartingBalance { get; set; } = 1000;

    /// <summary>Eddies handed out by /daily.</summary>
    public long DailyBonus { get; set; } = 100;

    /// <summary>
    /// LMSR liquidity for new markets. Higher = prices move less per bet.
    /// With 300, a 100 €$ bet on a fresh market moves it from 50% to about 64%.
    /// </summary>
    public double Liquidity { get; set; } = 300;

    public TimeSpan MinDuration { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan MaxDuration { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How long after closing a Nexus market waits before it is resolved, so late-indexed mods are counted.</summary>
    public TimeSpan ResolutionDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How often the background job checks for markets to close and resolve.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Open markets one user may have created at the same time (moderators are exempt).</summary>
    public int MaxOpenMarketsPerUser { get; set; } = 3;
}
