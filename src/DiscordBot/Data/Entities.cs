namespace DiscordBot.Data;

public enum Side
{
    Yes,
    No,
}

public enum MarketKind
{
    /// <summary>Resolved automatically by counting new mods on Nexus Mods.</summary>
    Nexus,

    /// <summary>Free-form question resolved by a moderator.</summary>
    Custom,
}

public enum MarketStatus
{
    /// <summary>Accepting trades.</summary>
    Open,

    /// <summary>Past its close time and waiting for resolution.</summary>
    Closed,

    Resolved,
    Cancelled,
}

/// <summary>A user's eddies balance in one guild.</summary>
public class Wallet
{
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public long Balance { get; set; }
    public DateTime? LastDailyClaimUtc { get; set; }
}

public class Market
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong? MessageId { get; set; }
    public ulong CreatorId { get; set; }
    public required string Question { get; set; }
    public MarketKind Kind { get; set; }

    // Nexus resolution criteria (only used for MarketKind.Nexus)
    public string? Keyword { get; set; }
    public string? Category { get; set; }
    public string? Author { get; set; }
    public int MinCount { get; set; } = 1;

    public DateTime OpensAtUtc { get; set; }
    public DateTime ClosesAtUtc { get; set; }

    public MarketStatus Status { get; set; }
    public Side? Outcome { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolutionNote { get; set; }

    /// <summary>LMSR liquidity parameter <c>b</c>.</summary>
    public double Liquidity { get; set; }

    /// <summary>Outstanding YES shares held by traders.</summary>
    public double YesShares { get; set; }

    /// <summary>Outstanding NO shares held by traders.</summary>
    public double NoShares { get; set; }

    /// <summary>Total eddies that changed hands in this market.</summary>
    public long Volume { get; set; }

    public bool IsOpen(DateTime nowUtc) => Status == MarketStatus.Open && nowUtc < ClosesAtUtc;
}

/// <summary>Shares a user holds on one side of a market.</summary>
public class Position
{
    public int MarketId { get; set; }
    public ulong GuildId { get; set; }
    public ulong UserId { get; set; }
    public Side Side { get; set; }
    public double Shares { get; set; }

    /// <summary>Eddies spent minus eddies received from selling. Refunded if the market is cancelled.</summary>
    public long NetCost { get; set; }
}

/// <summary>Append-only trade log.</summary>
public class Trade
{
    public int Id { get; set; }
    public int MarketId { get; set; }
    public ulong UserId { get; set; }
    public Side Side { get; set; }

    /// <summary>Positive for buys, negative for sells.</summary>
    public double Shares { get; set; }

    /// <summary>Eddies paid (positive) or received (negative).</summary>
    public long Amount { get; set; }

    public double YesPriceAfter { get; set; }
    public DateTime AtUtc { get; set; }
}
