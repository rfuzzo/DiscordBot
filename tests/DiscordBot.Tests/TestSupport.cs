using DiscordBot.Data;
using DiscordBot.Markets;
using DiscordBot.Nexus;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests;

/// <summary>An in-memory SQLite database that lives as long as the fixture.</summary>
public sealed class TestDatabase : IDbContextFactory<BotDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<BotDbContext> _options;

    public TestDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(_connection).Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public BotDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}

public static class TestFactory
{
    public const ulong Guild = 1;
    public const ulong Alice = 100;
    public const ulong Bob = 200;
    public const ulong Channel = 10;

    public static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static MarketService CreateService(TestDatabase db, FakeTimeProvider time, MarketOptions? options = null) =>
        new(db,
            Microsoft.Extensions.Options.Options.Create(options ?? new MarketOptions()),
            Microsoft.Extensions.Options.Options.Create(new NexusOptions()),
            time);

    public static NexusMod Mod(int id, string name, DateTimeOffset createdAt, string? category = null, string? author = null) =>
        new(id, name, null, author, author, category, createdAt, null, false);
}
