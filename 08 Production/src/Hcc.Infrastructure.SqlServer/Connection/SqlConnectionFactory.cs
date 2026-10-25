using Hcc.Contracts;
using Hcc.Domain.Errors;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Connection;

/// <summary>Database connection settings (bound from configuration section <c>Hcc:Sql</c>).</summary>
public sealed class SqlOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Folder for the pre-migration backup (instance service account must be able to write it). Empty = instance default.</summary>
    public string? BackupDirectory { get; set; }
}

/// <summary>
/// Connection policy (contracts §4 rules 7 and 9): every connection asserts QUOTED_IDENTIFIER and ANSI_NULLS ON (filtered
/// indexes) and sets XACT_ABORT ON so any error dooms and rolls back the whole application transaction.
/// ETP-REUSE: replace/align with ETP connection policy (connection string builder, retry, encryption settings) when V:\ETP is available.
/// </summary>
public sealed class SqlConnectionFactory
{
    private const string SessionSetup =
        "SET XACT_ABORT ON; SELECT CAST(SESSIONPROPERTY('QUOTED_IDENTIFIER') AS int), CAST(SESSIONPROPERTY('ANSI_NULLS') AS int);";

    public SqlConnectionFactory(SqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) throw new ArgumentException("A connection string is required.", nameof(options));
        Options = options;
    }

    public SqlOptions Options { get; }

    /// <summary>
    /// Local development connection: Windows authentication, encrypted, and TrustServerCertificate=True because the
    /// local SQL Express certificate is self-signed. DEV ONLY — production uses a trusted certificate (P7).
    /// </summary>
    public static string LocalDevConnectionString(string server, string database) => new SqlConnectionStringBuilder
    {
        DataSource = server,
        InitialCatalog = database,
        IntegratedSecurity = true,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = true,
        ApplicationName = "HCC",
        ConnectTimeout = 15,
    }.ConnectionString;

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqlConnection(Options.ConnectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new SqlCommand(SessionSetup, conn) { CommandTimeout = Options.CommandTimeoutSeconds };
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await r.ReadAsync(ct).ConfigureAwait(false) || r.GetInt32(0) != 1 || r.GetInt32(1) != 1)
                throw new InvalidOperationException("Connection policy violated: QUOTED_IDENTIFIER and ANSI_NULLS must be ON.");
            return conn;
        }
        catch (SqlException ex)
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw new HccException(HccErrorCode.UNAVAILABLE, "The database is not reachable.", null, ex);
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public SqlCommand Command(string sql, SqlConnection connection, SqlTransaction? transaction = null)
        => new(sql, connection, transaction) { CommandTimeout = Options.CommandTimeoutSeconds };
}
