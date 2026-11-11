using System.Reflection;
using Hcc.Application.MoneyMasters;
using Hcc.Closing.Tests.Harness;
using Hcc.Contracts.MoneyMasters;
using Microsoft.Data.SqlClient;

namespace Hcc.Closing.Tests.Sql;

/// <summary>
/// CU-PAY-B: the invoice register's cash / non-cash split (TaxSql.ExtraSql, folio + POS + counter receipts) decides "cash" from the Payment methods
/// master (a method that settles to a till, by its current or an earlier name), not from the literal word. The same set feeds all three sources.
/// Figures that read the split: the Invoice register columns "Cash" and "Card, UPI, cheque" (Hcc.Reporting Tax/InvoiceRegisterReport).
/// </summary>
[Trait("Category", "Sql")]
public sealed class CuPayBTaxCashSplitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string ExtraSql() =>
        (string)typeof(Hcc.Reporting.Reports.Tax.InvoiceRegisterReport).Assembly.GetType("Hcc.Reporting.Reports.Tax.TaxSql", true)!
            .GetField("ExtraSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

    private static async Task<List<string>> CashNamesAsync(Hotel h)
    {
        var sql = ExtraSql();
        var cte = sql[..sql.IndexOf("SELECT i.invoice_id", StringComparison.Ordinal)];
        return await h.RowsAsync(cte.Replace("@p", h.PropertyId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal) + " SELECT n FROM cash_names ORDER BY n;", r => r.GetString(0), Ct);
    }

    [Fact]
    public Task Cash_is_decided_by_the_master_for_folio_pos_and_counter_alike()
    {
        var sql = ExtraSql();
        Assert.DoesNotContain("LOWER(", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("tender = 'Cash'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("tender <> 'Cash'", sql, StringComparison.Ordinal);
        Assert.Equal(6, sql.Split("LEFT JOIN cash_names cn").Length - 1); // folio cash + non-cash, POS cash + non-cash, counter cash + non-cash
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_cash_name_set_follows_a_rename_and_an_added_till_method()
    {
        await using var h = await Hotel.CreateAsync(Ct);
        Assert.Equal(["Cash"], await CashNamesAsync(h)); // seeded list: Cash settles to the till
        var cmds = h.Get<MoneyMastersCommands>();
        var cash = (await h.Get<MoneyMastersQueries>().PaymentMethodsAsync(h.PropertyId, h.Owner, ct: Ct)).Items.Single(m => m.Code == "CASH");
        await cmds.SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(cash.PaymentMethodId, "CASH", "Front till", cash.Kind, cash.SettlesTo, cash.NeedsReference, cash.UsableAt, Ledger: cash.Ledger, ExpectedVersion: cash.Version), Ct);
        await cmds.SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, "BOX", "Cash box", "cash", cash.SettlesTo, false, ["frontdesk"]), Ct);
        await cmds.SavePaymentMethodAsync(h.Ctx(h.Owner, "SavePaymentMethod"),
            new SavePaymentMethodRequest(null, "WALLET", "Paytm wallet", "wallet", "BANK-1", true, ["frontdesk"]), Ct);

        var names = await CashNamesAsync(h);
        Assert.Equal(["Cash", "Cash box", "Front till"], names); // earlier name + current name + second till method; UPI / wallet / bank are not cash
        // The full statement still runs (no invoices in this database: no rows, no SQL error).
        await using var conn = await h.Platform.Connections.OpenAsync(Ct);
        await using var cmd = new SqlCommand(ExtraSql(), conn);
        cmd.Parameters.AddWithValue("@p", h.PropertyId);
        cmd.Parameters.AddWithValue("@from", new DateTime(2026, 4, 1));
        cmd.Parameters.AddWithValue("@to", new DateTime(2027, 3, 31));
        await using var rd = await cmd.ExecuteReaderAsync(Ct);
        Assert.False(await rd.ReadAsync(Ct));
    }
}
