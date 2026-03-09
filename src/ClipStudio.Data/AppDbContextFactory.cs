using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClipStudio.Data;

/// <summary>
/// Design-time factory used by EF Core CLI tools (dotnet ef migrations) to instantiate
/// <see cref="AppDbContext"/> without requiring the full application host to be running.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=clipstudio_design.db")
            .Options;

        return new AppDbContext(options);
    }
}
