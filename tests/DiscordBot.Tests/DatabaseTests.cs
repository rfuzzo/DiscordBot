using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests;

public class DatabaseTests
{
    [Fact]
    public void MigrationsMatchTheModel()
    {
        // Fails when an entity changed without `dotnet ef migrations add <Name> --project src/DiscordBot`.
        using var db = new TestDatabase();
        using var context = db.CreateDbContext();
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
