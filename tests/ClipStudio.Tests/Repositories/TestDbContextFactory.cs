using ClipStudio.Data;
using Microsoft.EntityFrameworkCore;

namespace ClipStudio.Tests.Repositories;

/// <summary>
/// Creates a fresh in-memory <see cref="AppDbContext"/> instance for each test,
/// ensuring full isolation between test runs.
/// </summary>
internal static class TestDbContextFactory
{
    /// <summary>
    /// Creates and returns a new <see cref="AppDbContext"/> backed by an in-memory SQLite database.
    /// The schema is created automatically before the context is returned.
    /// </summary>
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source=:memory:")
            .Options;

        var context = new AppDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }
}
