using Hcc.Application.Commands;
using Hcc.Application.Documents;
using Hcc.Infrastructure.SqlServer.Documents;
using Hcc.Infrastructure.SqlServer.DocumentObjects;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Hcc.Infrastructure.SqlServer.Migrations;
using Hcc.Infrastructure.SqlServer.Outbox;

namespace Hcc.Infrastructure.SqlServer;

/// <summary>Builds the platform services for one database (used by the API host and by tests, no DI container dependency).</summary>
public sealed class SqlPlatform
{
    public SqlPlatform(SqlOptions options)
    {
        Options = options;
        Connections = new SqlConnectionFactory(options);
        BackupGate = new SqlBackupGate(options);
        Migrations = new MigrationRunner(Connections, new EmbeddedMigrationSource(), BackupGate);
        Commands = new Hcc.Application.Accounting.YearLockedCommandRunner(new SqlCommandRunner(Connections), new Accounting.SqlYearLockGuard()); // F5-int: ACC-4 year lock on every command
        Outbox = new OutboxConsumer(Connections);
        // LF-docdb (DEC-200, contract §1.2): the separate documents database, its own migration stream and its object store.
        Documents = new SqlDocumentConnectionFactory(options);
        DocumentMigrations = new MigrationRunner(Documents.Inner, new EmbeddedMigrationSource(EmbeddedMigrationSource.DocsPrefix), new SqlBackupGate(Documents.Options), "HCC.DocMigrationRunner");
        DocumentObjects = new SqlDocumentObjectStore(Documents);
        DocumentMove = new DocumentMoveJob(Connections, Documents, DocumentObjects);
        DocumentSweep = new DocumentSweepJob(Connections, DocumentObjects);
        DocumentRefs = new DocumentRefCheck(Connections, Documents, DocumentObjects);
    }

    public SqlDocumentConnectionFactory Documents { get; }

    public MigrationRunner DocumentMigrations { get; }

    public IDocumentObjectStore DocumentObjects { get; }

    public DocumentMoveJob DocumentMove { get; }

    public DocumentSweepJob DocumentSweep { get; }

    public DocumentRefCheck DocumentRefs { get; }

    public SqlOptions Options { get; }

    public SqlConnectionFactory Connections { get; }

    public SqlBackupGate BackupGate { get; }

    public MigrationRunner Migrations { get; }

    public ICommandRunner Commands { get; }

    public OutboxConsumer Outbox { get; }
}
