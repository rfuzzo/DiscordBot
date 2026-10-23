using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DiscordBot.Data;

/// <summary>Lets <c>dotnet ef</c> build the context without starting the bot (which needs a Discord token).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BotDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
