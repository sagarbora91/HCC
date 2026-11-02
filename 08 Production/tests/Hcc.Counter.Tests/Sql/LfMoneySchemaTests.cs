using System.Globalization;
using Hcc.Infrastructure.SqlServer;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Counter.Tests.Sql;

/// <summary>Every LF-money migration (4675–4694) applies on a fresh HccTest_* database together with the rest of the stream, and the new
/// tables / widened constraints are there (stay rate change, agreed rate, approval actions, invoice revision + supersession, bill correction,
/// counter bills, guest-credit receipt kind, guest credit account).</summary>
public sealed class LfMoneySchemaTests
{
    [Fact]
    public async Task Migrations_apply_and_the_lf_money_objects_exist()
    {
        var ct = TestContext.Current.CancellationToken;
        var server = Environment.GetEnvironmentVariable("HCC_TEST_SQL_SERVER") is { Length: > 0 } s ? s : @".\SQLEXPRESS";
        var name = "HccTest_" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];
        await Exec(server, "master", $"CREATE DATABASE [{name}];", ct);
        try
        {
            var platform = new SqlPlatform(new SqlOptions { ConnectionString = SqlConnectionFactory.LocalDevConnectionString(server, name), BackupDirectory = "NUL" });
            await platform.Migrations.MigrateAsync(ct);
            string[] tables = ["gst.stay_rate_change", "gst.guest_agreed_rate", "ivc.invoice_supersession", "ivc.bill_correction", "ivc.bill_correction_change", "ctr.bill", "ctr.bill_line", "gst.guest_credit_account"];
            foreach (var t in tables) Assert.Equal(1, await Scalar(server, name, $"SELECT COUNT(*) FROM sys.objects WHERE object_id = OBJECT_ID(N'{t}') AND type = 'U'", ct));
            Assert.Equal(1, await Scalar(server, name, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'ivc.invoice') AND name = 'revision'", ct));
            Assert.Equal(1, await Scalar(server, name, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_receipt_kind' AND definition LIKE '%guest-credit%'", ct));
            Assert.Equal(1, await Scalar(server, name, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_ivc_invoice_source' AND definition LIKE '%counter%'", ct));
            Assert.Equal(1, await Scalar(server, name, "SELECT COUNT(*) FROM sys.check_constraints WHERE name = 'CK_approval_action' AND definition LIKE '%bill-correction%' AND definition LIKE '%counter-credit%'", ct));
            Assert.Equal(6, await Scalar(server, name, "SELECT COUNT(*) FROM plat.permission WHERE permission_id IN ('PERM-STY-010','PERM-GST-010','PERM-FOL-010','PERM-CTR-001','PERM-CTR-002','PERM-GST-011')", ct));
            Assert.Equal(1, await Scalar(server, name, "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_lgc_bill_date'", ct));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await Exec(server, "master", $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", CancellationToken.None);
        }
    }

    private static async Task Exec(string server, string db, string sql, CancellationToken ct)
    {
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, db));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 300 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<int> Scalar(string server, string db, string sql, CancellationToken ct)
    {
        await using var conn = new SqlConnection(SqlConnectionFactory.LocalDevConnectionString(server, db));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }
}
