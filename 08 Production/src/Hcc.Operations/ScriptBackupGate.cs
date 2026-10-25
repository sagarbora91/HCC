using System.Diagnostics;
using System.Globalization;
using Hcc.Infrastructure.SqlServer.Migrations;
using Microsoft.Data.SqlClient;

namespace Hcc.Operations;

/// <summary>Settings for the script-driven backup gate, section <c>Hcc:Operations</c>.</summary>
public sealed class OperationsOptions
{
    public const string Section = "Hcc:Operations";

    /// <summary>Full path of Backup-HccDatabase.ps1. Empty = keep the built-in <see cref="SqlBackupGate"/>.</summary>
    public string? BackupScript { get; set; }

    /// <summary>Backup folder (falls back to Hcc:Sql:BackupDirectory).</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>SQL instance the script connects to, e.g. .\SQLEXPRESS.</summary>
    public string SqlInstance { get; set; } = @".\SQLEXPRESS";

    public OperationsOptions WithBackupDirectory(string? fallback)
    {
        if (string.IsNullOrWhiteSpace(BackupDirectory)) BackupDirectory = fallback;
        return this;
    }
}

/// <summary>Runs an external process; replaceable in tests.</summary>
public interface IScriptRunner
{
    Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);
}

public sealed class ProcessScriptRunner : IScriptRunner
{
    public async Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start " + fileName);
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return (p.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
    }
}

/// <summary>
/// Backup gate wired to the deployment scripts: DBCC CHECKDB must be clean, then Backup-HccDatabase.ps1 takes a COPY_ONLY
/// full backup (same folder, naming, verify and retention as the scheduled backups) before any migration is applied.
/// ETP-REUSE: replace/align with ETP backup gate and its backup scripts when V:\ETP is available.
/// </summary>
public sealed class ScriptBackupGate(OperationsOptions options, IScriptRunner runner) : IBackupGate
{
    public string? LastBackupTarget { get; private set; }

    public string LastOutput { get; private set; } = string.Empty;

    public async Task BeforeMigrateAsync(SqlConnection connection, string database, int fromVersion, int toVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using (var check = new SqlCommand($"DBCC CHECKDB ({QuoteName(database)}) WITH NO_INFOMSGS, ALL_ERRORMSGS;", connection) { CommandTimeout = 0 })
            await check.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await RunBackupScriptAsync(database, fromVersion, toVersion, ct).ConfigureAwait(false);
    }

    public async Task RunBackupScriptAsync(string database, int fromVersion, int toVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.BackupScript)) throw new InvalidOperationException("Hcc:Operations:BackupScript is not configured.");
        var (exe, args) = BuildCommand(options, database, fromVersion, toVersion);
        var (code, output) = await runner.RunAsync(exe, args, ct).ConfigureAwait(false);
        LastOutput = output;
        if (code != 0) throw new InvalidOperationException($"The pre-migration backup script failed (exit {code}); migrations were not applied. {output}");
        LastBackupTarget = options.BackupDirectory;
    }

    /// <summary>The PowerShell command line for the pre-migration backup (tag <c>premigrate_0100_to_0200</c>).</summary>
    public static (string Exe, IReadOnlyList<string> Args) BuildCommand(OperationsOptions o, string database, int fromVersion, int toVersion)
    {
        ArgumentNullException.ThrowIfNull(o);
        var tag = string.Create(CultureInfo.InvariantCulture, $"premigrate_{fromVersion:0000}_to_{toVersion:0000}");
        var args = new List<string>
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", o.BackupScript ?? string.Empty,
            "-Type", "Full", "-CopyOnly", "-Tag", tag, "-SqlInstance", o.SqlInstance, "-Database", database,
        };
        if (!string.IsNullOrWhiteSpace(o.BackupDirectory)) { args.Add("-BackupDirectory"); args.Add(o.BackupDirectory); }
        return ("powershell.exe", args);
    }

    private static string QuoteName(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
}
