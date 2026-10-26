using System.Data;
using System.Text.Json;
using Hcc.Application.Commands;
using Hcc.Application.Groups;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Sql;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Groups;

/// <summary>SQL adapter of the company accounts (schema grp, migration 2202). Writes only grp.company_account / grp.company_limit_request.</summary>
public sealed class SqlCompanyStore(SqlConnectionFactory connections) : ICompanyStore
{
    private const string Columns = "company_id, property_id, name, gstin, contact, phone, email, city, rates_json, credit_limit_paise, row_ver";
    private const string RequestColumns = "request_id, company_id, from_paise, to_paise, reason, status, requested_by, requested_utc, decided_by, note";

    private static SqlCommandTransaction Sql(ICommandTransaction tx) =>
        tx as SqlCommandTransaction ?? throw new InvalidOperationException("The company SQL store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static string? S(SqlDataReader rd, int i) => rd.IsDBNull(i) ? null : rd.GetString(i);

    private static CompanyRow Read(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt32(1), rd.GetString(2), S(rd, 3), S(rd, 4), S(rd, 5), S(rd, 6), S(rd, 7),
        JsonSerializer.Deserialize<Dictionary<string, long>>(rd.GetString(8)) ?? [], rd.GetInt64(9), SqlValues.RowVersionToWire((byte[])rd[10]));

    private static CompanyLimitRow ReadRequest(SqlDataReader rd) => new(rd.GetInt64(0), rd.GetInt64(1), rd.GetInt64(2), rd.GetInt64(3), rd.GetString(4), rd.GetString(5),
        rd.GetInt32(6), rd.GetDateTime(7), rd.IsDBNull(8) ? null : rd.GetInt32(8), S(rd, 9));

    private static async Task<CompanyRow?> OneAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? Read(rd) : null;
    }

    private static async Task<string> VersionAsync(SqlCommand cmd, CancellationToken ct)
    {
        var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
        return v is null
            ? throw new HccException(HccErrorCode.STALE_VERSION, "The company account was changed by someone else. Reload and try again.")
            : SqlValues.RowVersionToWire(v);
    }

    private static void Bind(SqlCommand cmd, CompanyDetails d)
    {
        P(cmd, "@name", SqlDbType.NVarChar, d.Name, 200);
        P(cmd, "@gstin", SqlDbType.Char, d.Gstin, 15);
        P(cmd, "@contact", SqlDbType.NVarChar, d.Contact, 120);
        P(cmd, "@phone", SqlDbType.VarChar, d.Phone, 20);
        P(cmd, "@email", SqlDbType.NVarChar, d.Email, 120);
        P(cmd, "@city", SqlDbType.NVarChar, d.City, 80);
        P(cmd, "@rates", SqlDbType.NVarChar, JsonSerializer.Serialize(d.Rates));
    }

    public async Task<CompanyRow?> LockAsync(ICommandTransaction tx, int propertyId, long companyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {Columns} FROM grp.company_account WITH (UPDLOCK, HOLDLOCK) WHERE company_id = @c AND property_id = @p;");
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        return await OneAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<bool> NameTakenAsync(ICommandTransaction tx, int propertyId, string name, long? exceptCompanyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT 1 FROM grp.company_account WITH (UPDLOCK, HOLDLOCK) WHERE property_id = @p AND name = @n AND (@x IS NULL OR company_id <> @x);");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@n", SqlDbType.NVarChar, name, 200);
        P(cmd, "@x", SqlDbType.BigInt, exceptCompanyId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task<CompanyRow> InsertAsync(ICommandTransaction tx, int propertyId, CompanyDetails details, int principalId, DateTime utc, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        await using var cmd = Sql(tx).Command($"""
            INSERT grp.company_account(property_id, name, gstin, contact, phone, email, city, rates_json, created_by, created_utc)
            OUTPUT {string.Join(", ", Columns.Split(", ").Select(c => "INSERTED." + c))}
            VALUES (@p, @name, @gstin, @contact, @phone, @email, @city, @rates, @u, @utc);
            """);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        Bind(cmd, details);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        return await OneAsync(cmd, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Company insert returned no row.");
    }

    public async Task<string> UpdateAsync(ICommandTransaction tx, long companyId, string expectedVersion, CompanyDetails details, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        await using var cmd = Sql(tx).Command("""
            UPDATE grp.company_account SET name = @name, gstin = @gstin, contact = @contact, phone = @phone, email = @email, city = @city, rates_json = @rates
            OUTPUT INSERTED.row_ver WHERE company_id = @c AND row_ver = @v;
            """);
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        P(cmd, "@v", SqlDbType.Binary, SqlValues.RowVersionFromWire(expectedVersion), 8);
        Bind(cmd, details);
        return await VersionAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<string> SetLimitAsync(ICommandTransaction tx, long companyId, long limitPaise, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE grp.company_account SET credit_limit_paise = @l OUTPUT INSERTED.row_ver WHERE company_id = @c;");
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        P(cmd, "@l", SqlDbType.BigInt, limitPaise);
        return await VersionAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task SupersedePendingAsync(ICommandTransaction tx, long companyId, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE grp.company_limit_request SET status = 'Rejected', decided_by = @u, decided_utc = @utc, note = 'Superseded by a direct change'
             WHERE company_id = @c AND status = 'Pending' AND requested_by <> @u;
            """);
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<long> InsertRequestAsync(ICommandTransaction tx, long companyId, long from, long to, string reason, int principalId, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            INSERT grp.company_limit_request(company_id, from_paise, to_paise, reason, status, requested_by, requested_utc)
            OUTPUT INSERTED.request_id VALUES (@c, @f, @t, @r, 'Pending', @u, @utc);
            """);
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        P(cmd, "@f", SqlDbType.BigInt, from);
        P(cmd, "@t", SqlDbType.BigInt, to);
        P(cmd, "@r", SqlDbType.NVarChar, reason, 200);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        return (long)(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    private static async Task<CompanyLimitRow?> OneRequestAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? ReadRequest(rd) : null;
    }

    public async Task<CompanyLimitRow?> PendingRequestAsync(ICommandTransaction tx, long companyId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} FROM grp.company_limit_request WHERE company_id = @c AND status = 'Pending';");
        P(cmd, "@c", SqlDbType.BigInt, companyId);
        return await OneRequestAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task<CompanyLimitRow?> LockRequestAsync(ICommandTransaction tx, long requestId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command($"SELECT {RequestColumns} FROM grp.company_limit_request WITH (UPDLOCK, HOLDLOCK) WHERE request_id = @r;");
        P(cmd, "@r", SqlDbType.BigInt, requestId);
        return await OneRequestAsync(cmd, ct).ConfigureAwait(false);
    }

    public async Task DecideRequestAsync(ICommandTransaction tx, long requestId, string status, int principalId, DateTime utc, string? note, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE grp.company_limit_request SET status = @s, decided_by = @u, decided_utc = @utc, note = @n WHERE request_id = @r AND status = 'Pending';");
        P(cmd, "@r", SqlDbType.BigInt, requestId);
        P(cmd, "@s", SqlDbType.VarChar, status, 10);
        P(cmd, "@u", SqlDbType.Int, principalId);
        P(cmd, "@utc", SqlDbType.DateTime2, utc);
        P(cmd, "@n", SqlDbType.NVarChar, note, 200);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CompanyRow>> ListAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"SELECT {Columns} FROM grp.company_account WHERE property_id = @p ORDER BY name;", conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<CompanyRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(Read(rd));
        return list;
    }

    public async Task<IReadOnlyList<CompanyLimitRow>> ListRequestsAsync(int propertyId, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command($"""
            SELECT {string.Join(", ", RequestColumns.Split(", ").Select(c => "r." + c))} FROM grp.company_limit_request r
              JOIN grp.company_account a ON a.company_id = r.company_id WHERE a.property_id = @p ORDER BY r.request_id DESC;
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var list = new List<CompanyLimitRow>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false)) list.Add(ReadRequest(rd));
        return list;
    }
}
