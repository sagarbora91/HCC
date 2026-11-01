using System.Globalization;
using System.Text.RegularExpressions;
using Hcc.Application.Access;
using Hcc.Application.Billing;
using Hcc.Application.Commands;
using Hcc.Application.Tax;
using Hcc.Contracts;
using Hcc.Contracts.Accounting;
using Hcc.Domain.Accounting;
using Hcc.Domain.Dates;
using Hcc.Domain.Errors;
using Hcc.Domain.Money;

namespace Hcc.Application.Accounting;

// ACC-2 · lane acc2-tds (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §5, §9; rulebook §2.9, A9, C9, E5, E6; DEC-146, DEC-058).
// Prototype twin: 03 Prototype/src/domain/tds.ts + domain/streams/accounting-tds.ts + services/mock/handlers/accounting-tds.ts.
//   SetTdsSettings (Owner, PERM-CFG-003)  the hotel deducts TDS / the rate column (acc.tds_setting, migration 4243).
//   SetTdsRate (Owner, PERM-CFG-003)      a dated override of the TDS table (acc.tds_rate); never backdated.
//   RecordTdsRemittance (PERM-ACC-013)    TDS deposited (acc.tds_remit, 4244) → the manual template tds-remittance (ITdsRemittancePoster).
//   GET state / deductee report / Form 168 reconciliation (PERM-ACC-013).
//   ForBillAsync                          for the purchase lane: settings + overrides + vendor + the vendor's bills → TdsCalculator.ForBill.

/// <summary>PERM-ACC-013 (migration 4245): accounting.tax — Owner, Accountant.</summary>
public static class AccountingTaxPermissions
{
    public const string Tax = "PERM-ACC-013";
}

/// <summary>An Owner override row (acc.tds_rate, active).</summary>
public sealed record TdsRateRecord(long RateId, string Key, DateOnly EffectiveFrom, long Value, string Reason);

/// <summary>A stored remittance (acc.tds_remit).</summary>
public sealed record TdsRemitRecord(long RemitId, string Month, string Section, long AmountPaise, string Tender, string Challan, DateOnly BusinessDate, string? ManualVoucherId,
    string? Form168Json = null); // ACC-3 C7

/// <summary>A company payment that carried TDS (grp.company_payment.tds_paise &gt; 0).</summary>
public sealed record TdsReceivablePayment(long CompanyId, string Company, DateOnly BusinessDate, long AmountPaise, long TdsPaise);

/// <summary>Persistence of the TDS module (migrations 4242–4244; SQL: Hcc.Infrastructure.SqlServer/Accounting/SqlTdsStore).</summary>
public interface ITdsStore
{
    Task SerializeAsync(ICommandTransaction tx, int propertyId, CancellationToken ct);

    Task<DateOnly?> BusinessDateAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task<TdsSettings> SettingsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task SaveSettingsAsync(ICommandTransaction tx, int propertyId, TdsSettings settings, int actor, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<TdsRateRecord>> RatesAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task<long> InsertRateAsync(ICommandTransaction tx, int propertyId, string key, DateOnly effectiveFrom, long value, string reason, int actor, DateTime now, CancellationToken ct);

    Task<IReadOnlyList<TdsRemitRecord>> RemitsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    Task<long> InsertRemitAsync(ICommandTransaction tx, int propertyId, TdsRemitRecord remit, int actor, DateTime now, CancellationToken ct);

    /// <summary>Links the remittance to its manual voucher once posted (the only update acc.tds_remit allows).</summary>
    Task LinkVoucherAsync(ICommandTransaction tx, long remitId, string manualVoucherId, CancellationToken ct);

    /// <summary>ACC-3 C7: replaces the Form 168 figures on the remittance (form168_json, migration 4272).</summary>
    Task SetForm168Async(ICommandTransaction tx, int propertyId, long remitId, string json, CancellationToken ct);

    /// <summary>ACC-3 C7: the company ids of the groups master (the figures must name one).</summary>
    Task<IReadOnlySet<long>> CompanyIdsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>Posted bills that carry TDS (ops.expense tds_paise / tds_section — the purchase lane's columns; none before they exist).</summary>
    Task<IReadOnlyList<TdsBillRow>> BillsAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct);

    /// <summary>The expense vendor by name or code (ops.expense_vendor; tds_section from the purchase lane's column).</summary>
    Task<TdsVendor?> VendorAsync(ICommandTransaction? tx, int propertyId, string vendor, CancellationToken ct);

    /// <summary>The vendor's posted bills dated <paramref name="from"/> … <paramref name="to"/> except <paramref name="excludeExpenseId"/>.</summary>
    Task<IReadOnlyList<TdsPriorBill>> VendorBillsAsync(ICommandTransaction? tx, int propertyId, string vendor, DateOnly from, DateOnly to, long? excludeExpenseId, CancellationToken ct);

    Task<IReadOnlyList<TdsReceivablePayment>> ReceivablePaymentsAsync(ICommandTransaction? tx, int propertyId, DateOnly from, DateOnly to, CancellationToken ct);
}

/// <summary>The voucher ACC_TDS_REMIT asks the manual-voucher service to save (template tds-remittance): Dr 2150 · Cr the tender's bank (1030).</summary>
public sealed record TdsRemittanceVoucher(DateOnly Date, string Narration, string Reference, string Tender, long AmountPaise, string PayableLedger, string Template = "tds-remittance");

/// <summary>
/// Port to the manual-voucher service of lane acc2-manual (contract §2: an Owner's voucher is approved at once, an Accountant's waits). Returns the
/// manual voucher id. Until the integration wires it, no implementation is registered and the remittance is stored unlinked (DONE file).
/// </summary>
public interface ITdsRemittancePoster
{
    Task<string?> PostAsync(ICommandTransaction tx, Caller caller, TdsRemittanceVoucher voucher, CancellationToken ct);
}

public sealed partial class TdsService(ICommandRunner runner, ITdsStore store, IAccessPolicy policy, TimeProvider clock, ITdsRemittancePoster? poster = null,
    IComplianceCardHook? cards = null) // ACC-2 integration (ruling 9): the remittance marks the month's TDS compliance card filed with the challan
{
    public const string OpSettings = "SetTdsSettings";
    public const string OpRate = "SetTdsRate";
    public const string OpRemit = "RecordTdsRemittance";

    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex MonthPattern();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static CommandContext Ctx(Caller c, string operation, string key) => new(c.PropertyId, c.PrincipalId, operation, key);

    private static string Column(TdsColumn c) => c == TdsColumn.Individual ? "individual" : "other";

    private async Task<DateOnly> TodayAsync(ICommandTransaction? tx, int propertyId, CancellationToken ct)
        => await store.BusinessDateAsync(tx, propertyId, ct).ConfigureAwait(false) ?? throw new HccException(HccErrorCode.NOT_FOUND, "Property not found.");

    private static IReadOnlyList<TdsRateVersion> Versions(IReadOnlyList<TdsRateRecord> rows) => rows.Select(r => new TdsRateVersion(r.Key, r.EffectiveFrom, r.Value)).ToList();

    public async Task<CommandResult<TdsSettingsResult>> SetSettingsAsync(Caller caller, string idempotencyKey, SetTdsSettingsRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, TaxPermissions.Configuration, ct: ct).ConfigureAwait(false);
        if (req.HotelDeductsTds is null && req.DeductorType is null) throw HccException.Validation("Choose “hotel deducts TDS” or the rate column.");
        if (req.DeductorType is not null and not ("individual" or "other")) throw HccException.Validation("Choose the rate column: individual / HUF or other.");
        return await runner.RunAsync(Ctx(caller, OpSettings, idempotencyKey), req, async (tx, c) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var before = await store.SettingsAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var after = new TdsSettings(req.HotelDeductsTds ?? before.HotelDeductsTds,
                req.DeductorType is null ? before.Column : req.DeductorType == "individual" ? TdsColumn.Individual : TdsColumn.Other);
            await store.SaveSettingsAsync(tx, caller.PropertyId, after, caller.PrincipalId, Now, c).ConfigureAwait(false);
            await tx.AuditAsync("TDS_SETTINGS_SET", "tds-settings",
                $"TDS settings · hotel deducts TDS {(before.HotelDeductsTds ? "on" : "off")} → {(after.HotelDeductsTds ? "on" : "off")} · rate column {Column(before.Column)} → {Column(after.Column)} (DEC-146; {TdsRates.CaNote})",
                c).ConfigureAwait(false);
            return new TdsSettingsResult(after.HotelDeductsTds, Column(after.Column));
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<TdsRateChangeResult>> SetRateAsync(Caller caller, string idempotencyKey, SetTdsRateRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, TaxPermissions.Configuration, ct: ct).ConfigureAwait(false);
        TdsRates.ValidateValue(req.Key, req.Value);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is < 3 or > 200) throw HccException.Validation("Give a reason for the TDS change (3–200 characters).");
        DateOnly? from = string.IsNullOrWhiteSpace(req.EffectiveFrom) ? null : StayDates.Parse(req.EffectiveFrom, "effectiveFrom");
        return await runner.RunAsync(Ctx(caller, OpRate, idempotencyKey), req, async (tx, c) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var bd = await TodayAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var effective = from ?? bd;
            if (effective < bd) throw HccException.Validation($"A TDS change cannot be backdated: choose {StayDates.ToWire(bd)} (the business date) or later.");
            var rows = await store.RatesAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var before = TdsRates.ValueOn(req.Key, effective, Versions(rows));
            if (before == req.Value) throw HccException.Validation("That is already the value in force on that date — nothing to change.");
            var id = await store.InsertRateAsync(tx, caller.PropertyId, req.Key, effective, req.Value, reason, caller.PrincipalId, Now, c).ConfigureAwait(false);
            var show = (long v) => TdsRates.IsRate(req.Key) ? TdsRates.Pct(checked((int)v)) : new Paise(v).ToString();
            await tx.AuditAsync("TDS_RATE_SET", "tds-rate:" + req.Key,
                $"{req.Key} {show(before)} → {show(req.Value)} from {StayDates.ToWire(effective)} · {reason} · {TdsRates.Illustrative}", c).ConfigureAwait(false);
            return new TdsRateChangeResult(id, req.Key, StayDates.ToWire(effective), before, req.Value, effective > bd ? "Scheduled" : "InForce");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>TDS held back for a code (ACC-3 C6: the sub-section — rent:plant apart from rent) and month less every remittance recorded for it.</summary>
    public static long Outstanding(IEnumerable<TdsBillRow> bills, IEnumerable<TdsRemitRecord> remits, string section, string month)
        => bills.Where(b => b.Section == section && TdsReports.Month(b.Date) == month).Sum(b => b.TdsPaise)
            - remits.Where(r => r.Section == section && r.Month == month).Sum(r => r.AmountPaise);

    /// <summary>ACC-3 C6: the old-Act sub-sections the remittance and the compliance card keep apart (prototype TDS_SUB_OLD).</summary>
    public static readonly IReadOnlyDictionary<string, string> SubOld = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["rent"] = "194-I(b)", ["rent:plant"] = "194-I(a)", ["professional"] = "194J(b)", ["professional:technical"] = "194J(a)",
    };

    /// <summary>Old-Act section of a code (sub-section where the Act splits it).</summary>
    public static string OldOf(string code) => SubOld.TryGetValue(code, out var o) ? o : TdsRates.Row(TdsRates.BaseSection(code) ?? "")?.Old ?? code;

    /// <summary>Label of a code: the variant's label or the row's (prototype tdsCodeLabel).</summary>
    public static string LabelOf(string code)
    {
        var row = TdsRates.Row(TdsRates.BaseSection(code) ?? "");
        if (row is null) return code;
        var parts = code.Split(':');
        return parts.Length == 2 ? row.Variants.FirstOrDefault(v => v.Id == parts[1])?.Label ?? row.Label : row.Label;
    }

    /// <summary>The narration of a remittance voucher (prototype tdsRemitNarration).</summary>
    public static string Narration(string section, string month, string challan)
    {
        var row = TdsRates.Row(TdsRates.BaseSection(section) ?? "") ?? throw new ArgumentException("Unknown TDS row " + section, nameof(section));
        return $"TDS remittance · {section} ({row.Section}, old {OldOf(section)}) · {month} · challan {challan}";
    }

    /// <summary>ACC-3 C7: Form 168 figures as stored (JSON) and back.</summary>
    public static string Form168Json(IEnumerable<Form168FigureDto> figures)
        => System.Text.Json.JsonSerializer.Serialize(figures.Select(f => new { companyId = f.CompanyId, amountPaise = f.AmountPaise }));

    public static IReadOnlyList<Form168FigureDto>? Form168Of(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(e => new Form168FigureDto(e.GetProperty("companyId").GetInt64(), e.GetProperty("amountPaise").GetInt64())).ToList();
    }

    private async Task<string?> Form168RefusalAsync(ICommandTransaction? tx, int propertyId, IReadOnlyList<Form168FigureDto> figures, CancellationToken ct)
    {
        if (figures.Count > 50) return "Form 168: at most 50 companies.";
        var ids = await store.CompanyIdsAsync(tx, propertyId, ct).ConfigureAwait(false);
        var seen = new HashSet<long>();
        foreach (var f in figures)
        {
            if (!ids.Contains(f.CompanyId)) return $"Form 168: {f.CompanyId.ToString(CultureInfo.InvariantCulture)} is not a company of the groups master.";
            if (!seen.Add(f.CompanyId)) return $"Form 168: company {f.CompanyId.ToString(CultureInfo.InvariantCulture)} is listed twice.";
            if (f.AmountPaise is < 0 or > 10_000_000_000) return $"Form 168: enter whole paise for {f.CompanyId.ToString(CultureInfo.InvariantCulture)}.";
        }

        return null;
    }

    public const string OpForm168 = "SetTdsForm168";

    /// <summary>ACC-3 C7: replaces the Form 168 figures on a remittance (PERM-ACC-013).</summary>
    public async Task<CommandResult<TdsForm168Result>> SetForm168Async(Caller caller, string idempotencyKey, SetTdsForm168Request req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingTaxPermissions.Tax, ct: ct).ConfigureAwait(false);
        var figures = req.Figures ?? throw HccException.Validation("Form 168: a list of {companyId, amountPaise}.");
        return await runner.RunAsync(Ctx(caller, OpForm168, idempotencyKey), req, async (tx, c) =>
        {
            var r = (await store.RemitsAsync(tx, caller.PropertyId, c).ConfigureAwait(false)).FirstOrDefault(x => x.RemitId == req.RemitId)
                ?? throw new HccException(HccErrorCode.NOT_FOUND, "TDS remittance not found.");
            if (await Form168RefusalAsync(tx, caller.PropertyId, figures, c).ConfigureAwait(false) is { } why) throw HccException.Validation(why);
            await store.SetForm168Async(tx, caller.PropertyId, r.RemitId, Form168Json(figures), c).ConfigureAwait(false);
            await tx.AuditAsync("TDS_FORM168_SET", "tds-remit:" + r.RemitId.ToString(CultureInfo.InvariantCulture),
                "Form 168 figures · " + (figures.Count == 0 ? "cleared" : string.Join(", ", figures.Select(f => f.CompanyId.ToString(CultureInfo.InvariantCulture) + " " + new Paise(f.AmountPaise)))), c)
                .ConfigureAwait(false);
            return new TdsForm168Result(r.RemitId, figures);
        }, ct).ConfigureAwait(false);
    }

    public async Task<CommandResult<TdsRemittanceResult>> RecordRemittanceAsync(Caller caller, string idempotencyKey, RecordTdsRemittanceRequest req, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(req);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingTaxPermissions.Tax, ct: ct).ConfigureAwait(false);
        var month = (req.Month ?? "").Trim();
        if (!MonthPattern().IsMatch(month)) throw HccException.Validation("Choose the month the TDS was deducted in (YYYY-MM).");
        var section = (req.Section ?? "").Trim();
        if (TdsRates.BaseSection(section) is null) throw HccException.Validation("Choose the TDS section (rent, contractor, professional or commission)."); // ACC-3 C6: sub-sections
        var amount = Paise.FromWire(req.AmountPaise).EnsureReceiptAmount();
        var challan = (req.Challan ?? "").Trim();
        if (challan.Length is < 3 or > 40) throw HccException.Validation("Enter the challan number (3–40 characters).");
        if (req.Tender is not ("Bank transfer" or "UPI")) throw HccException.Validation("TDS is paid from a bank account: choose Bank transfer or UPI.");
        DateOnly? paid = string.IsNullOrWhiteSpace(req.Date) ? null : StayDates.Parse(req.Date, "date");
        return await runner.RunAsync(Ctx(caller, OpRemit, idempotencyKey), req, async (tx, c) =>
        {
            await store.SerializeAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var bd = await TodayAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var date = paid ?? bd;
            if (string.CompareOrdinal(month, TdsReports.Month(bd)) > 0) throw HccException.Validation("The month cannot be after the business date.");
            if (date > bd) throw HccException.Validation("The payment date cannot be after the business date.");
            if (string.CompareOrdinal(TdsReports.Month(date), month) < 0) throw HccException.Validation("The payment date cannot be before the month it pays for.");
            if (req.Form168 is { } f168 && await Form168RefusalAsync(tx, caller.PropertyId, f168, c).ConfigureAwait(false) is { } bad) throw HccException.Validation(bad);
            var remits = await store.RemitsAsync(tx, caller.PropertyId, c).ConfigureAwait(false);
            var out0 = Outstanding(await store.BillsAsync(tx, caller.PropertyId, c).ConfigureAwait(false), remits, section, month);
            var label = $"{LabelOf(section).ToLowerInvariant()} (old {OldOf(section)})";
            if (out0 <= 0) throw new HccException(HccErrorCode.INVALID_TRANSITION, $"No TDS is held back for {label} in {month}.");
            if (amount.Value > out0)
                throw HccException.Validation($"The remittance cannot exceed the TDS held back for {label} in {month} and not yet remitted ({new Paise(out0)}). Interest or a late fee is a separate entry on 5400.");
            var id = await store.InsertRemitAsync(tx, caller.PropertyId, new TdsRemitRecord(0, month, section, amount.Value, req.Tender, challan, date, null,
                req.Form168 is { } fx ? Form168Json(fx) : null), caller.PrincipalId, Now, c).ConfigureAwait(false);
            string? voucher = null;
            if (poster is not null)
            {
                voucher = await poster.PostAsync(tx, caller, new TdsRemittanceVoucher(date, Narration(section, month, challan), challan, req.Tender, amount.Value,
                    ChartOfAccounts.DefaultMapping[TdsCalculator.PayableKey]), c).ConfigureAwait(false);
                if (voucher is not null) await store.LinkVoucherAsync(tx, id, voucher, c).ConfigureAwait(false);
            }

            var card = cards is null ? "" : " · " + (await cards.MarkFiledAsync(tx, caller.PropertyId, "TDS", month, date, challan,
                $"TDS {LabelOf(section)} (old {OldOf(section)}) {amount} remitted · {req.Tender}" + (voucher is null ? "" : " · manual voucher " + voucher), caller.PrincipalId, Now, c).ConfigureAwait(false)).Note;
            await tx.AuditAsync("TDS_REMITTED", "tds-remit:" + id.ToString(CultureInfo.InvariantCulture),
                $"TDS remittance · {LabelOf(section)} (old {OldOf(section)}) · {month} · {amount} by {req.Tender} · challan {challan}" + (voucher is null ? " · voucher pending (manual-voucher service)" : " · voucher " + voucher) + card, c)
                .ConfigureAwait(false);
            return new TdsRemittanceResult(id, month, section, amount.Value, date, voucher, out0 - amount.Value);
        }, ct).ConfigureAwait(false);
    }

    public async Task<TdsStateView> GetAsync(Caller caller, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingTaxPermissions.Tax, ct: ct).ConfigureAwait(false);
        var bd = await TodayAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var set = await store.SettingsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var rates = await store.RatesAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var v = Versions(rates);
        var table = TdsRates.Sections.SelectMany(s =>
        {
            var th = TdsRates.ValueOn($"tds.{s.Id}.threshold", bd, v);
            long? year = s.Basis == TdsBasis.SingleAndYear ? TdsRates.ValueOn($"tds.{s.Id}.thresholdYear", bd, v) : null;
            var basis = s.Basis switch { TdsBasis.Month => "month", TdsBasis.SingleAndYear => "single+year", _ => "year" };
            var rows = new List<TdsTableRowView>
            {
                new(s.Id, s.Label, $"{s.Section} (old {s.Old})", basis, th, year, TdsRates.RateBps(s.Id, bd, TdsColumn.Individual, v), TdsRates.RateBps(s.Id, bd, TdsColumn.Other, v), s.Source, s.Note),
            };
            foreach (var x in s.Variants)
            {
                var b = TdsRates.RateBps(s.Id + ":" + x.Id, bd, TdsColumn.Other, v);
                rows.Add(new(s.Id + ":" + x.Id, x.Label, $"{s.Section} (old {s.Old})", basis, th, year, b, b, s.Source, s.Note));
            }

            return rows;
        }).ToList();
        var remits = await store.RemitsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        return new TdsStateView(set.HotelDeductsTds, Column(set.Column), bd, table,
            rates.Select(r => new TdsRateView(r.RateId, r.Key, StayDates.ToWire(r.EffectiveFrom), r.Value, r.Reason)).ToList(),
            remits.Select(r => new TdsRemittanceView(r.RemitId, r.Month, r.Section, r.AmountPaise, r.Tender, r.Challan, r.BusinessDate, r.ManualVoucherId, Form168Of(r.Form168Json), OldOf(r.Section)))
                .ToList());
    }

    /// <summary>'2026-27' → that financial year; '2026-04' → that month; null / anything else → the business date's financial year.</summary>
    public static (string Label, DateOnly From, DateOnly To) Period(string? period, DateOnly businessDate)
    {
        var p = (period ?? "").Trim();
        if (p.Length == 7 && p[4] == '-' && int.TryParse(p[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            && int.TryParse(p[5..], NumberStyles.None, CultureInfo.InvariantCulture, out var two))
        {
            if (two == (y + 1) % 100)
            {
                var f = TdsRates.Fy(new DateOnly(y, 4, 1));
                return ("FY " + f.Label, f.From, f.To);
            }

            if (two is >= 1 and <= 12)
            {
                var from = new DateOnly(y, two, 1);
                return (p, from, from.AddMonths(1).AddDays(-1));
            }
        }

        var d = TdsRates.Fy(businessDate);
        return ("FY " + d.Label, d.From, d.To);
    }

    public async Task<TdsDeducteeReportView> DeducteesAsync(Caller caller, string? period, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingTaxPermissions.Tax, ct: ct).ConfigureAwait(false);
        var (label, from, to) = Period(period, await TodayAsync(null, caller.PropertyId, ct).ConfigureAwait(false));
        var bills = await store.BillsAsync(null, caller.PropertyId, ct).ConfigureAwait(false);
        var remits = (await store.RemitsAsync(null, caller.PropertyId, ct).ConfigureAwait(false)).Select(r => new TdsRemitRow(r.Section, r.Month, r.AmountPaise));
        var rows = TdsReports.Deductees(bills, remits, from, to);
        return new TdsDeducteeReportView(label, from, to,
            rows.Select(r => new TdsDeducteeRowView(r.Vendor, r.Pan, r.Section, r.Bills, r.TaxablePaise, r.TdsPaise, r.RemittedPaise, r.PendingPaise)).ToList(),
            rows.Sum(r => r.TaxablePaise), rows.Sum(r => r.TdsPaise), rows.Sum(r => r.RemittedPaise), rows.Sum(r => r.PendingPaise));
    }

    public async Task<TdsReceivableReportView> ReceivableAsync(Caller caller, string? fy, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(caller);
        await policy.RequireAsync(caller.PropertyId, caller.PrincipalId, AccountingTaxPermissions.Tax, ct: ct).ConfigureAwait(false);
        var (label, from, to) = Period(fy, await TodayAsync(null, caller.PropertyId, ct).ConfigureAwait(false));
        var pays = await store.ReceivablePaymentsAsync(null, caller.PropertyId, from, to, ct).ConfigureAwait(false);
        var rows = pays.GroupBy(p => (p.CompanyId, p.Company, Month: TdsReports.Month(p.BusinessDate)))
            .Select(g => new TdsReceivableRowView(g.Key.CompanyId, g.Key.Company, g.Key.Month, g.Count(), g.Sum(p => p.AmountPaise), g.Sum(p => p.TdsPaise),
                g.Sum(p => p.AmountPaise + p.TdsPaise)))
            .OrderBy(r => r.Company, StringComparer.Ordinal).ThenBy(r => r.Month, StringComparer.Ordinal).ToList();
        // ACC-3 C7: per company the figure on the latest remittance dated in the period that carries one.
        var f168 = new Dictionary<long, long>();
        foreach (var r in (await store.RemitsAsync(null, caller.PropertyId, ct).ConfigureAwait(false)).Where(r => r.BusinessDate >= from && r.BusinessDate <= to))
            foreach (var f in Form168Of(r.Form168Json) ?? []) f168[f.CompanyId] = f.AmountPaise;
        return new TdsReceivableReportView(label, from, to, rows, rows.Sum(r => r.TdsPaise), f168.OrderBy(k => k.Key).Select(k => new Form168FigureDto(k.Key, k.Value)).ToList());
    }

    /// <summary>
    /// For the purchase lane (contract §5): the TDS to hold back on a bill being posted, read inside its command transaction — the firm's
    /// switches, the Owner's overrides, the vendor and the vendor's bills of the period. Pure rule: <see cref="TdsCalculator.ForBill"/>.
    /// </summary>
    public async Task<TdsResult> ForBillAsync(ICommandTransaction? tx, int propertyId, string vendor, long amountPaise, long gstPaise, DateOnly date, long? excludeExpenseId,
        CancellationToken ct = default)
    {
        var set = await store.SettingsAsync(tx, propertyId, ct).ConfigureAwait(false);
        var v = string.IsNullOrWhiteSpace(vendor) ? null : await store.VendorAsync(tx, propertyId, vendor, ct).ConfigureAwait(false);
        var fy = TdsRates.Fy(date);
        IReadOnlyList<TdsPriorBill> bills = v is null ? [] : await store.VendorBillsAsync(tx, propertyId, vendor, fy.From, fy.To, excludeExpenseId, ct).ConfigureAwait(false);
        var rates = Versions(await store.RatesAsync(tx, propertyId, ct).ConfigureAwait(false));
        return TdsCalculator.ForBill(new TdsBill(amountPaise, gstPaise, date), v, bills, set, rates);
    }
}
