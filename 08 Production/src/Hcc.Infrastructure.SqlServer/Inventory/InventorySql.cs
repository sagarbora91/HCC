using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Inventory;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Inventory;

/// <summary>A connection + transaction pair the P2 SQL code writes into (a command transaction or the sweep's own transaction).</summary>
public sealed class SqlScope(SqlConnection connection, SqlTransaction? transaction, SqlConnectionFactory factory)
{
    public SqlConnection Connection { get; } = connection;

    public SqlTransaction? Transaction { get; } = transaction;

    public SqlCommand Command(string sql) => factory.Command(sql, Connection, Transaction);

    public static SqlScope Of(ICommandTransaction tx, SqlConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(tx);
        var sql = tx as SqlCommandTransaction ?? throw new InvalidOperationException("The P2 SQL stores need a SQL command transaction.");
        return new SqlScope(sql.Connection, sql.Transaction, factory);
    }
}

/// <summary>Parameter, reader and error helpers for the P2 SQL adapters.</summary>
public static class InventorySql
{
    /// <summary>Thrown by inv.usp_apply_type_night_deltas: 'TYPE_FULL|&lt;room type id&gt;|&lt;night&gt;'.</summary>
    public const int TypeFull = 52001;

    /// <summary>A room night of the target room is already taken (locked check before insert).</summary>
    public const int RoomTaken = 52002;

    public static SqlParameter Date(this SqlParameterCollection p, string name, DateOnly value)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Add(name, SqlDbType.Date).Also(x => x.Value = value.ToDateTime(TimeOnly.MinValue));
    }

    public static SqlParameter NullableDate(this SqlParameterCollection p, string name, DateOnly? value)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Add(name, SqlDbType.Date).Also(x => x.Value = value is { } v ? v.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
    }

    public static SqlParameter Int(this SqlParameterCollection p, string name, int? value)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Add(name, SqlDbType.Int).Also(x => x.Value = value is { } v ? v : DBNull.Value);
    }

    public static SqlParameter Big(this SqlParameterCollection p, string name, long? value)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Add(name, SqlDbType.BigInt).Also(x => x.Value = value is { } v ? v : DBNull.Value);
    }

    public static SqlParameter Text(this SqlParameterCollection p, string name, string? value, int size)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Add(name, SqlDbType.NVarChar, size).Also(x => x.Value = (object?)value ?? DBNull.Value);
    }

    public static SqlParameter Utc(this SqlParameterCollection p, string name, DateTime? value)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Add(name, SqlDbType.DateTime2).Also(x => { x.Scale = 3; x.Value = value is { } v ? v : DBNull.Value; });
    }

    public static SqlParameter Version(this SqlParameterCollection p, string name, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Add(name, SqlDbType.Binary, 8).Also(x => x.Value = value);
    }

    public static DateOnly GetDate(this SqlDataReader r, int i)
    {
        ArgumentNullException.ThrowIfNull(r);
        return DateOnly.FromDateTime(r.GetDateTime(i));
    }

    public static DateOnly? GetDateOrNull(this SqlDataReader r, int i)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
    }

    public static DateTime? GetUtcOrNull(this SqlDataReader r, int i)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.IsDBNull(i) ? null : DateTime.SpecifyKind(r.GetDateTime(i), DateTimeKind.Utc);
    }

    public static string? GetStringOrNull(this SqlDataReader r, int i)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    public static long? GetInt64OrNull(this SqlDataReader r, int i)
    {
        ArgumentNullException.ThrowIfNull(r);
        return r.IsDBNull(i) ? null : r.GetInt64(i);
    }

    /// <summary>Maps the P2 inventory errors to NOT_AVAILABLE; null for anything else (the runner translates the rest).</summary>
    public static HccException? Translate(SqlException ex, Func<int, string>? typeName = null)
    {
        ArgumentNullException.ThrowIfNull(ex);
        foreach (SqlError e in ex.Errors)
        {
            if (e.Number == TypeFull)
            {
                var parts = e.Message.Split('|');
                var type = parts.Length > 1 && int.TryParse(parts[1], out var t) && typeName is not null ? typeName(t) + " " : "";
                var night = parts.Length > 2 ? parts[2] : "";
                return new HccException(HccErrorCode.NOT_AVAILABLE, $"Not available: no {type}room of this type is left to sell on {night}.", null, ex);
            }

            if (e.Number == RoomTaken)
                return new HccException(HccErrorCode.NOT_AVAILABLE, "This room is not available for those dates.", null, ex);
        }

        return null;
    }

    /// <summary>Runs a database call and converts P2 inventory errors to NOT_AVAILABLE.</summary>
    public static async Task<T> Guard<T>(Func<Task<T>> work, Func<int, string>? typeName = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (SqlException ex) when (Translate(ex, typeName) is { } h)
        {
            throw h;
        }
    }

    private static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}

/// <summary>Per-(room type, night) count change for inv.usp_apply_type_night_deltas.</summary>
public sealed class TypeNightDeltas
{
    private readonly SortedDictionary<(int Type, DateOnly Night), (int Sold, int Held, int Sellable)> _rows = new();

    public bool IsEmpty => _rows.Count == 0;

    public void Add(int roomTypeId, DateOnly from, DateOnly to, int sold = 0, int held = 0, int sellable = 0)
    {
        for (var n = from; n < to; n = n.AddDays(1))
        {
            _rows.TryGetValue((roomTypeId, n), out var cur);
            _rows[(roomTypeId, n)] = (cur.Sold + sold, cur.Held + held, cur.Sellable + sellable);
        }
    }

    /// <summary>Applies the deltas in the caller's transaction (ascending room type, night).</summary>
    public async Task ApplyAsync(SqlScope scope, int propertyId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (IsEmpty) return;
        using var table = new DataTable();
        table.Locale = System.Globalization.CultureInfo.InvariantCulture;
        table.Columns.Add("room_type_id", typeof(int));
        table.Columns.Add("night", typeof(DateTime));
        table.Columns.Add("d_sold", typeof(int));
        table.Columns.Add("d_held", typeof(int));
        table.Columns.Add("d_sellable", typeof(int));
        foreach (var ((type, night), (sold, held, sellable)) in _rows) table.Rows.Add(type, night.ToDateTime(TimeOnly.MinValue), sold, held, sellable);
        await using var cmd = scope.Command("inv.usp_apply_type_night_deltas");
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Int("@property_id", propertyId);
        var p = cmd.Parameters.Add("@deltas", SqlDbType.Structured);
        p.TypeName = "inv.type_night_delta";
        p.Value = table;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
