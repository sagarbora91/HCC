using Hcc.Application.Commands;
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
        Commands = new SqlCommandRunner(Connections);
        Outbox = new OutboxConsumer(Connections);
    }

    public SqlOptions Options { get; }

    public SqlConnectionFactory Connections { get; }

    public SqlBackupGate BackupGate { get; }

    public MigrationRunner Migrations { get; }

    public ICommandRunner Commands { get; }

    public OutboxConsumer Outbox { get; }
}
