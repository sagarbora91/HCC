using System.Data;
using Hcc.Application.Accounting;
using Hcc.Application.Commands;
using Hcc.Infrastructure.SqlServer.Commands;

namespace Hcc.Infrastructure.SqlServer.Accounting;

/// <summary>F5-int: <see cref="IYearLockGuard"/> over <c>acc.v_locked_year</c> (migration 4283), read in the writer's transaction.</summary>
public sealed class SqlYearLockGuard : IYearLockGuard
{
    public async Task<string?> LockedFyAsync(ICommandTransaction tx, int propertyId, DateOnly? date, CancellationToken ct = default)
    {
        if (tx is not SqlCommandTransaction s) return null;
        await using var cmd = s.Command("""
            IF OBJECT_ID(N'acc.v_locked_year', N'V') IS NOT NULL
              SELECT TOP 1 fy FROM acc.v_locked_year
              WHERE property_id = @p AND ISNULL(@d, (SELECT business_date FROM inv.property WHERE property_id = @p)) BETWEEN from_date AND to_date;
            ELSE SELECT CAST(NULL AS CHAR(7));
            """);
        cmd.Parameters.Add("@p", SqlDbType.Int).Value = propertyId;
        cmd.Parameters.Add("@d", SqlDbType.Date).Value = date is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is string fy ? fy.Trim() : null;
    }
}
