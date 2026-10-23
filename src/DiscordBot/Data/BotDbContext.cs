using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DiscordBot.Data;

public class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<Market> Markets => Set<Market>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Trade> Trades => Set<Trade>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Wallet>(e =>
        {
            e.HasKey(w => new { w.GuildId, w.UserId });
            e.HasIndex(w => new { w.GuildId, w.Balance });
        });

        modelBuilder.Entity<Market>(e =>
        {
            e.Property(m => m.Kind).HasConversion<string>();
            e.Property(m => m.Status).HasConversion<string>();
            e.Property(m => m.Outcome).HasConversion<string>();
            e.HasIndex(m => new { m.GuildId, m.Status });
            e.HasIndex(m => new { m.Status, m.ClosesAtUtc });
        });

        modelBuilder.Entity<Position>(e =>
        {
            e.HasKey(p => new { p.MarketId, p.UserId, p.Side });
            e.Property(p => p.Side).HasConversion<string>();
            e.HasIndex(p => new { p.GuildId, p.UserId });
        });

        modelBuilder.Entity<Trade>(e =>
        {
            e.Property(t => t.Side).HasConversion<string>();
            e.HasIndex(t => t.MarketId);
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no timezone-aware type; store UTC and mark values read back as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
