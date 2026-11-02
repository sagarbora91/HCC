using Hcc.Contracts;
using Hcc.Domain.Errors;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Connection;

/// <summary>Database connection settings (bound from configuration section <c>Hcc:Sql</c>).</summary>
public sealed class SqlOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// LF-docdb (DEC-200, contract §1.2): connection string of the separate documents database (<c>HccDocuments</c>). Empty = derived from
    /// <see cref="ConnectionString"/> by appending <c>Documents</c> to the catalog (<c>Hcc</c> gives <c>HccDocuments</c>).
    /// </summary>
    public string? DocumentsConnectionString { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Folder for the pre-migration backup (instance service account must be able to write it). Empty = instance default.</summary>
    public string? BackupDirectory { get; set; }
}

/// <summary>LF-docdb (contract §1.2): derives and checks the documents database connection string.</summary>
public static class DocumentsConnection
{
    public const string RefusalText = "The documents database name must start with 'Hcc' and differ from the main database.";

    /// <summary>The explicit documents connection string, or the main one with the catalog <c>&lt;catalog&gt;Documents</c>. Throws when the result does not start with Hcc or equals the main catalog.</summary>
    public static string Resolve(SqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var main = new SqlConnectionStringBuilder(options.ConnectionString);
        SqlConnectionStringBuilder docs;
        if (!string.IsNullOrWhiteSpace(options.DocumentsConnectionString)) docs = new SqlConnectionStringBuilder(options.DocumentsConnectionString);
        else
        {
            docs = new SqlConnectionStringBuilder(options.ConnectionString) { InitialCatalog = main.InitialCatalog + "Documents" };
        }

        if (string.IsNullOrWhiteSpace(docs.InitialCatalog) || !docs.InitialCatalog.StartsWith("Hcc", StringComparison.OrdinalIgnoreCase)
            || string.Equals(docs.InitialCatalog, main.InitialCatalog, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(RefusalText + $" (main '{main.InitialCatalog}', documents '{docs.InitialCatalog}').");
        return docs.ConnectionString;
    }

    /// <summary>Options for the documents database: same login policy and timeout, the resolved connection string.</summary>
    public static SqlOptions OptionsFor(SqlOptions main) => new()
    {
        ConnectionString = Resolve(main),
        CommandTimeoutSeconds = main.CommandTimeoutSeconds,
        BackupDirectory = main.BackupDirectory,
    };
}

/// <summary>
/// LF-docdb (contract §1.2): the connection factory of <c>HccDocuments</c> with the same session policy as <see cref="SqlConnectionFactory"/>
/// (XACT_ABORT ON, QUOTED_IDENTIFIER and ANSI_NULLS checks). An unreachable database surfaces as the documents-specific UNAVAILABLE text.
/// </summary>
public sealed class SqlDocumentConnectionFactory(SqlOptions mainOptions)
{
    public const string UnavailableText = "The documents database is not reachable (or full). The rest of HCC keeps working; try the photo again later.";

    private readonly SqlConnectionFactory _inner = new(DocumentsConnection.OptionsFor(mainOptions));

    public SqlOptions Options => _inner.Options;

    /// <summary>The plain factory (used by the migration runner).</summary>
    public SqlConnectionFactory Inner => _inner;

    public string Database => new SqlConnectionStringBuilder(Options.ConnectionString).InitialCatalog;

    public async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        try
        {
            return await _inner.OpenAsync(ct).ConfigureAwait(false);
        }
        catch (HccException ex) when (ex.Code == HccErrorCode.UNAVAILABLE)
        {
            throw new HccException(HccErrorCode.UNAVAILABLE, UnavailableText, null, ex);
        }
    }

    public SqlCommand Command(string sql, SqlConnection connection, SqlTransaction? transaction = null) => _inner.Command(sql, connection, transaction);
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
    public static string LocalDevConnectionString(string server, string database)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            IntegratedSecurity = true,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = true,
            ApplicationName = "HCC",
            ConnectTimeout = 15,
        };
        // Development and test only: a Linux test host (cloud container) has no Windows login, so it may pass a
        // SQL login through HCC_TEST_SQL_USER / HCC_TEST_SQL_PASSWORD. Production never calls this helper.
        if (Environment.GetEnvironmentVariable("HCC_TEST_SQL_USER") is { Length: > 0 } user)
        {
            b.IntegratedSecurity = false;
            b.UserID = user;
            b.Password = Environment.GetEnvironmentVariable("HCC_TEST_SQL_PASSWORD") ?? "";
        }
        return b.ConnectionString;
    }

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
