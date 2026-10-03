using DiscordBot.Data;
using DiscordBot.Nexus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DiscordBot.Markets;

/// <summary>A rule violation that should be shown to the user as-is.</summary>
public sealed class MarketException(string message) : Exception(message);

public sealed record CreateMarketRequest(
    ulong GuildId,
    ulong ChannelId,
    ulong CreatorId,
    TimeSpan Duration,
    bool CreatorIsModerator,
    string? Question = null,
    ModCriteria? Criteria = null);

public sealed record TradeResult(Market Market, Side Side, double Shares, long Amount, long NewBalance, double PositionShares);

public sealed record Payout(ulong UserId, long Amount);

public sealed record SettlementResult(Market Market, IReadOnlyList<Payout> Payouts);

public sealed record DailyResult(bool Claimed, long Amount, long Balance, DateTime NextClaimUtc);

public sealed record PositionView(Market Market, Side Side, double Shares, long NetCost);

/// <summary>
/// All economy and market state changes go through here. Mutations are serialized with a lock so
/// balance checks and LMSR share counts can't race; the bot runs as a single process.
/// </summary>
public sealed class MarketService(
    IDbContextFactory<BotDbContext> dbFactory,
    IOptions<MarketOptions> options,
    IOptions<NexusOptions> nexusOptions,
    TimeProvider time)
{
    private const double Epsilon = 1e-9;
    public const int MaxQuestionLength = 200;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly MarketOptions _options = options.Value;

    public MarketOptions Options => _options;

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    // ── Wallets ────────────────────────────────────────────────────────────

    public async Task<Wallet> GetWalletAsync(ulong guildId, ulong userId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var wallet = await GetOrCreateWalletAsync(db, guildId, userId);
            await db.SaveChangesAsync();
            return wallet;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DailyResult> ClaimDailyAsync(ulong guildId, ulong userId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var wallet = await GetOrCreateWalletAsync(db, guildId, userId);
            var now = UtcNow;
            var nextClaim = now.Date.AddDays(1);

            if (wallet.LastDailyClaimUtc is { } last && last.Date == now.Date)
            {
                await db.SaveChangesAsync();
                return new DailyResult(false, 0, wallet.Balance, nextClaim);
            }

            wallet.Balance += _options.DailyBonus;
            wallet.LastDailyClaimUtc = now;
            await db.SaveChangesAsync();
            return new DailyResult(true, _options.DailyBonus, wallet.Balance, nextClaim);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<Wallet>> GetLeaderboardAsync(ulong guildId, int count)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Wallets.AsNoTracking()
            .Where(w => w.GuildId == guildId)
            .OrderByDescending(w => w.Balance)
            .Take(count)
            .ToListAsync();
    }

    // ── Markets ────────────────────────────────────────────────────────────

    public async Task<Market> CreateMarketAsync(CreateMarketRequest request)
    {
        if (request.Duration < _options.MinDuration || request.Duration > _options.MaxDuration)
            throw new MarketException(
                $"Markets must run between {DurationParser.Format(_options.MinDuration)} and {DurationParser.Format(_options.MaxDuration)}.");

        string question;
        if (request.Criteria is { } criteria)
        {
            if (criteria.MinCount < 1)
                throw new MarketException("The minimum number of mods must be at least 1.");
            question = criteria.ToQuestion(nexusOptions.Value.GameName);
        }
        else
        {
            question = request.Question?.Trim() ?? "";
            if (question.Length == 0)
                throw new MarketException("A market needs a question.");
        }

        if (question.Length > MaxQuestionLength)
            throw new MarketException($"The question is too long (max {MaxQuestionLength} characters).");

        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();

            if (!request.CreatorIsModerator)
            {
                var openByUser = await db.Markets.CountAsync(m =>
                    m.GuildId == request.GuildId && m.CreatorId == request.CreatorId && m.Status == MarketStatus.Open);
                if (openByUser >= _options.MaxOpenMarketsPerUser)
                    throw new MarketException($"You already have {openByUser} open markets. Wait for one to close first.");
            }

            var now = UtcNow;
            var market = new Market
            {
                GuildId = request.GuildId,
                ChannelId = request.ChannelId,
                CreatorId = request.CreatorId,
                Question = question,
                Kind = request.Criteria is null ? MarketKind.Custom : MarketKind.Nexus,
                Keyword = Normalize(request.Criteria?.Keyword),
                Category = Normalize(request.Criteria?.Category),
                Author = Normalize(request.Criteria?.Author),
                MinCount = request.Criteria?.MinCount ?? 1,
                OpensAtUtc = now,
                ClosesAtUtc = now + request.Duration,
                Status = MarketStatus.Open,
                Liquidity = _options.Liquidity,
            };
            db.Markets.Add(market);
            await db.SaveChangesAsync();
            return market;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AttachMessageAsync(int marketId, ulong channelId, ulong messageId)
    {
        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var market = await db.Markets.FindAsync(marketId) ?? throw new MarketException($"Market #{marketId} not found.");
            market.ChannelId = channelId;
            market.MessageId = messageId;
            await db.SaveChangesAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<Market?> GetMarketAsync(ulong guildId, int marketId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Markets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == marketId && m.GuildId == guildId);
    }

    public async Task<IReadOnlyList<Market>> ListMarketsAsync(ulong guildId, bool includeFinished, int count = 25)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query = db.Markets.AsNoTracking().Where(m => m.GuildId == guildId);
        if (!includeFinished)
            query = query.Where(m => m.Status == MarketStatus.Open || m.Status == MarketStatus.Closed);
        return await query.OrderByDescending(m => m.Id).Take(count).ToListAsync();
    }

    public async Task<IReadOnlyList<PositionView>> GetPositionsAsync(ulong guildId, ulong userId, int? marketId = null, bool activeOnly = true)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var query =
            from p in db.Positions.AsNoTracking()
            join m in db.Markets.AsNoTracking() on p.MarketId equals m.Id
            where p.GuildId == guildId && p.UserId == userId && p.Shares > Epsilon
            select new { p, m };
        if (marketId is { } id)
            query = query.Where(x => x.m.Id == id);
        if (activeOnly)
            query = query.Where(x => x.m.Status == MarketStatus.Open || x.m.Status == MarketStatus.Closed);

        var rows = await query.OrderByDescending(x => x.m.Id).ToListAsync();
        return rows.Select(x => new PositionView(x.m, x.p.Side, x.p.Shares, x.p.NetCost)).ToList();
    }

    // ── Trading ────────────────────────────────────────────────────────────

    public async Task<TradeResult> BuyAsync(ulong guildId, ulong userId, int marketId, Side side, long amount)
    {
        if (amount < 1)
            throw new MarketException($"You have to bet at least 1 {_options.CurrencySymbol}.");

        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var market = await GetTradableMarketAsync(db, guildId, marketId);
            var wallet = await GetOrCreateWalletAsync(db, guildId, userId);
            if (wallet.Balance < amount)
                throw new MarketException($"You only have {FormatMoney(wallet.Balance)}.");

            var shares = Lmsr.SharesForAmount(market.YesShares, market.NoShares, market.Liquidity, side, amount);
            if (side == Side.Yes) market.YesShares += shares;
            else market.NoShares += shares;
            market.Volume += amount;
            wallet.Balance -= amount;

            var position = await GetOrCreatePositionAsync(db, market, userId, side);
            position.Shares += shares;
            position.NetCost += amount;

            db.Trades.Add(new Trade
            {
                MarketId = market.Id,
                UserId = userId,
                Side = side,
                Shares = shares,
                Amount = amount,
                YesPriceAfter = market.YesPrice(),
                AtUtc = UtcNow,
            });

            await db.SaveChangesAsync();
            return new TradeResult(market, side, shares, amount, wallet.Balance, position.Shares);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Sells <paramref name="shares"/> (or the whole position when null) back to the market maker.</summary>
    public async Task<TradeResult> SellAsync(ulong guildId, ulong userId, int marketId, Side side, double? shares)
    {
        if (shares is <= 0)
            throw new MarketException("You have to sell a positive number of shares.");

        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var market = await GetTradableMarketAsync(db, guildId, marketId);
            var position = await db.Positions.FindAsync(marketId, userId, side);
            if (position is null || position.Shares <= Epsilon)
                throw new MarketException($"You don't hold any {side.Label()} shares in market #{marketId}.");

            var toSell = Math.Min(shares ?? position.Shares, position.Shares);
            var refund = (long)Math.Floor(Lmsr.RefundForShares(market.YesShares, market.NoShares, market.Liquidity, side, toSell) + Epsilon);
            if (refund < 1)
                throw new MarketException($"Those shares are worth less than 1 {_options.CurrencySymbol} right now.");

            if (side == Side.Yes) market.YesShares -= toSell;
            else market.NoShares -= toSell;
            market.Volume += refund;

            position.Shares -= toSell;
            if (position.Shares < 1e-6)
                position.Shares = 0;
            position.NetCost -= refund;

            var wallet = await GetOrCreateWalletAsync(db, guildId, userId);
            wallet.Balance += refund;

            db.Trades.Add(new Trade
            {
                MarketId = market.Id,
                UserId = userId,
                Side = side,
                Shares = -toSell,
                Amount = -refund,
                YesPriceAfter = market.YesPrice(),
                AtUtc = UtcNow,
            });

            await db.SaveChangesAsync();
            return new TradeResult(market, side, toSell, refund, wallet.Balance, position.Shares);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    /// <summary>Marks open markets whose close time has passed as closed and returns them.</summary>
    public async Task<IReadOnlyList<Market>> CloseExpiredAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var now = UtcNow;
            var expired = await db.Markets
                .Where(m => m.Status == MarketStatus.Open && m.ClosesAtUtc <= now)
                .ToListAsync();
            foreach (var market in expired)
                market.Status = MarketStatus.Closed;
            await db.SaveChangesAsync();
            return expired;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Nexus markets that are not settled yet (open or closed), across all guilds.</summary>
    public async Task<IReadOnlyList<Market>> GetUnsettledNexusMarketsAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Markets.AsNoTracking()
            .Where(m => m.Kind == MarketKind.Nexus && (m.Status == MarketStatus.Open || m.Status == MarketStatus.Closed))
            .ToListAsync();
    }

    /// <summary>Settles a market: every winning share pays 1 eddie.</summary>
    public async Task<SettlementResult> ResolveAsync(int marketId, Side outcome, string? note, ulong? guildId = null)
    {
        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var market = await GetUnsettledMarketAsync(db, marketId, guildId);

            var winners = await db.Positions
                .Where(p => p.MarketId == marketId && p.Side == outcome && p.Shares > Epsilon)
                .ToListAsync();

            var payouts = new List<Payout>();
            foreach (var position in winners)
            {
                var amount = (long)Math.Floor(position.Shares + Epsilon);
                if (amount <= 0)
                    continue;
                var wallet = await GetOrCreateWalletAsync(db, market.GuildId, position.UserId);
                wallet.Balance += amount;
                payouts.Add(new Payout(position.UserId, amount));
            }

            market.Status = MarketStatus.Resolved;
            market.Outcome = outcome;
            market.ResolvedAtUtc = UtcNow;
            market.ResolutionNote = note;
            await db.SaveChangesAsync();
            return new SettlementResult(market, payouts.OrderByDescending(p => p.Amount).ToList());
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Cancels a market and refunds what each trader put in (minus what they already took out by selling).</summary>
    public async Task<SettlementResult> CancelAsync(int marketId, string? note, ulong? guildId = null)
    {
        await _lock.WaitAsync();
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var market = await GetUnsettledMarketAsync(db, marketId, guildId);

            var positions = await db.Positions.Where(p => p.MarketId == marketId).ToListAsync();
            var refunds = new List<Payout>();
            foreach (var group in positions.GroupBy(p => p.UserId))
            {
                var amount = Math.Max(0, group.Sum(p => p.NetCost));
                if (amount <= 0)
                    continue;
                var wallet = await GetOrCreateWalletAsync(db, market.GuildId, group.Key);
                wallet.Balance += amount;
                refunds.Add(new Payout(group.Key, amount));
            }

            market.Status = MarketStatus.Cancelled;
            market.ResolvedAtUtc = UtcNow;
            market.ResolutionNote = note;
            await db.SaveChangesAsync();
            return new SettlementResult(market, refunds.OrderByDescending(p => p.Amount).ToList());
        }
        finally
        {
            _lock.Release();
        }
    }

    public string FormatMoney(long amount) => $"{amount:N0} {_options.CurrencySymbol}";

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Wallet> GetOrCreateWalletAsync(BotDbContext db, ulong guildId, ulong userId)
    {
        var wallet = await db.Wallets.FindAsync(guildId, userId);
        if (wallet is not null)
            return wallet;

        wallet = new Wallet { GuildId = guildId, UserId = userId, Balance = _options.StartingBalance };
        db.Wallets.Add(wallet);
        return wallet;
    }

    private static async Task<Position> GetOrCreatePositionAsync(BotDbContext db, Market market, ulong userId, Side side)
    {
        var position = await db.Positions.FindAsync(market.Id, userId, side);
        if (position is not null)
            return position;

        position = new Position { MarketId = market.Id, GuildId = market.GuildId, UserId = userId, Side = side };
        db.Positions.Add(position);
        return position;
    }

    private async Task<Market> GetTradableMarketAsync(BotDbContext db, ulong guildId, int marketId)
    {
        var market = await db.Markets.FirstOrDefaultAsync(m => m.Id == marketId && m.GuildId == guildId)
                     ?? throw new MarketException($"Market #{marketId} doesn't exist.");
        if (!market.IsOpen(UtcNow))
            throw new MarketException($"Market #{marketId} is no longer taking bets.");
        return market;
    }

    private static async Task<Market> GetUnsettledMarketAsync(BotDbContext db, int marketId, ulong? guildId)
    {
        var market = await db.Markets.FirstOrDefaultAsync(m => m.Id == marketId && (guildId == null || m.GuildId == guildId))
                     ?? throw new MarketException($"Market #{marketId} doesn't exist.");
        if (market.Status is MarketStatus.Resolved or MarketStatus.Cancelled)
            throw new MarketException($"Market #{marketId} is already {market.Status.ToString().ToLowerInvariant()}.");
        return market;
    }

    private static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class MarketExtensions
{
    public static double YesPrice(this Market market) =>
        Lmsr.Price(market.YesShares, market.NoShares, market.Liquidity, Side.Yes);

    public static ModCriteria Criteria(this Market market) =>
        new(market.Keyword, market.Category, market.Author, market.MinCount);

    public static string Label(this Side side) => side == Side.Yes ? "YES" : "NO";
}
