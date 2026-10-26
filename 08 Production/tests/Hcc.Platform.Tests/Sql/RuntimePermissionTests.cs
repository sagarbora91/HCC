using System.Diagnostics;
using Hcc.Platform.Tests.Harness;
using Microsoft.Data.SqlClient;

namespace Hcc.Platform.Tests.Sql;

/// <summary>
/// F-L08: the runtime login of the HCC service gets only the grants of <c>Get-HccRuntimeGrantSql</c> (Initialize-HccDatabase.ps1),
/// not db_owner. Proven on a disposable migrated <c>HccTest_*</c> database with a database user WITHOUT a login (no server login or
/// server permission is touched): the data access HCC needs works; schema changes and disabling the append-only audit trigger fail.
/// </summary>
[Trait("Category", "Sql")]
public class RuntimePermissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string GrantSql(string user)
    {
        string? common = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null && common is null; dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "deployment", "windows", "Hcc-Common.ps1");
            if (File.Exists(c)) common = c;
        }

        Assert.NotNull(common);
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", $". '{common!.Replace("'", "''", StringComparison.Ordinal)}'; Get-HccRuntimeGrantSql -User '{user}'" }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, p.StandardError.ReadToEnd());
        return output.Trim();
    }

    private static async Task<SqlException?> TryAsync(SqlConnection conn, string sql)
    {
        try
        {
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (SqlException ex)
        {
            return ex;
        }
    }

    [Fact]
    public async Task Runtime_grants_allow_data_access_but_no_schema_change_or_trigger_disable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var grants = GrantSql("[hcc_runtime_test]");
        Assert.DoesNotContain("db_owner", grants, StringComparison.Ordinal);
        await using var db = await DisposableDatabase.CreateAsync(migrate: true, Ct);
        await db.ExecAsync("CREATE USER [hcc_runtime_test] WITHOUT LOGIN; " + grants, Ct);

        await using var conn = await db.OpenAsync(Ct);
        Assert.Null(await TryAsync(conn, "EXECUTE AS USER = 'hcc_runtime_test';"));
        try
        {
            // What the service does at run time: reads, writes, sequences, procedures with table-valued parameters, app locks, temp tables.
            Assert.Null(await TryAsync(conn, "SELECT TOP (1) * FROM plat.audit_event; SELECT TOP (1) * FROM dbo.schema_migration;"));
            Assert.Null(await TryAsync(conn, "DECLARE @n BIGINT = NEXT VALUE FOR inv.seq_reservation_no;"));
            Assert.Null(await TryAsync(conn, "DECLARE @t inv.type_night_delta;"));
            Assert.Null(await TryAsync(conn, "BEGIN TRAN; DECLARE @r int; EXEC @r = sp_getapplock @Resource = 'x', @LockMode = 'Exclusive', @LockOwner = 'Transaction'; COMMIT;"));
            Assert.Null(await TryAsync(conn, "CREATE TABLE #t(i INT); INSERT #t VALUES (1); DROP TABLE #t;"));
            Assert.Null(await TryAsync(conn, "UPDATE inv.property SET name = name WHERE 1 = 0;"));

            // What it must not be able to do.
            Assert.NotNull(await TryAsync(conn, "DISABLE TRIGGER plat.TR_audit_event_append_only ON plat.audit_event;"));
            Assert.NotNull(await TryAsync(conn, "CREATE TABLE dbo.should_fail(i INT);"));
            Assert.NotNull(await TryAsync(conn, "ALTER TABLE plat.audit_event ADD x INT NULL;"));
            Assert.NotNull(await TryAsync(conn, "DROP TABLE plat.outbox;"));
        }
        finally
        {
            await TryAsync(conn, "REVERT;");
        }

        Assert.Equal(1, await db.CountAsync("SELECT CASE WHEN OBJECTPROPERTY(OBJECT_ID('plat.TR_audit_event_append_only'), 'ExecIsTriggerDisabled') = 0 THEN 1 ELSE 0 END", Ct));
    }
}
