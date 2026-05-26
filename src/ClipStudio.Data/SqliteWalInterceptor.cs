using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClipStudio.Data;

/// <summary>
/// EF Core connection interceptor that enables WAL journal mode and a 5-second busy timeout
/// on every SQLite connection. WAL mode allows concurrent readers alongside a single writer so
/// the startup sanitize pass does not block user-initiated DB writes (which would otherwise
/// cause a multi-second UI freeze under the default DELETE journal mode).
/// </summary>
internal sealed class SqliteWalInterceptor : DbConnectionInterceptor
{
    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
