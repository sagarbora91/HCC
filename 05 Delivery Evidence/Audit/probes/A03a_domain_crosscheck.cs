// A03a cross-check of the server domain kernel (Hcc.Domain.dll as built by A1, copied; no product file is built or changed).
// Run from the repo root:  pwsh "05 Delivery Evidence/Audit/probes/A03a_run_cs_probe.ps1"
// 1) Rounding sweep: MoneyMath.TaxOf / GstMath.Split against an independent decimal reference (Math.Round AwayFromZero).
// 2) The constructed mixed folio through GstMaster.Seed() rates, FolioTotals, InvoiceRules (CGST/SGST and IGST paths).
// 3) Credit note: full credit after a partial whose tax is an exact half paisa (CreditNoteRules.Plan).
// 4) Category guesses, numbering, GSTIN checks, slab boundary and the attrition slab basis.
using Hcc.Domain.Errors;
using Hcc.Domain.Invoices;
using Hcc.Domain.Money;
using Hcc.Domain.Tax;

var o = new List<string>();
void L(string s) { o.Add(s); Console.WriteLine(s); }

// 1 ── rounding sweep
int[] bps = [0, 1, 250, 500, 1200, 1800, 2800, 4999, 5000, 9999, 10000];
long n = 0, bad = 0, badSplit = 0;
void Check(long a, int b)
{
    n++;
    var mine = (long)Math.Round(a * (decimal)b / 10000m, MidpointRounding.AwayFromZero);
    var hcc = MoneyMath.TaxOf(new Paise(a), b).Value;
    if (mine != hcc && bad++ < 10) L($"  TAX MISMATCH {a}@{b}: ref {mine} hcc {hcc}");
    var s = GstMath.Split(new Paise(hcc));
    var sg = hcc / 2; // toward zero
    if ((s.Sgst.Value != sg || s.Cgst.Value != hcc - sg) && badSplit++ < 10) L($"  SPLIT MISMATCH {hcc}: {s.Cgst.Value}/{s.Sgst.Value}");
}
foreach (var b in bps) for (long a = -200_000; a <= 200_000; a++) Check(a, b);
var rnd = new Random(12345);
for (var i = 0; i < 200_000; i++) Check(rnd.NextInt64(-10_000_000_000, 10_000_000_001), bps[i % bps.Length]);
foreach (var a in new long[] { 10_000_000_000, -10_000_000_000, 9_999_999_999 }) foreach (var b in bps) Check(a, b);
L($"rounding sweep: {n} pairs, tax mismatches {bad}, split mismatches {badSplit}");
try { MoneyMath.TaxOf(new Paise(1), 10001); L("bps 10001 accepted (unexpected)"); } catch (HccException) { L("bps > 10000 refused by MoneyMath.PercentOf (VALIDATION)"); }

// 2 ── mixed folio through the seed master
var m = GstMaster.Seed();
var spec = m.SpecifiedPremises(new DateOnly(2026, 10, 24), 450_000);
var d24 = new DateOnly(2026, 10, 24); var d25 = new DateOnly(2026, 10, 25);
var rows = new (string Kind, string Label, DateOnly Date, long Amount, GstRate Rate)[]
{
    ("room", "Room night 24 Oct ₹7,500.00", d24, 750_000, m.RoomNight(d24, new Paise(750_000))),
    ("room", "Room night 25 Oct ₹7,500.01", d25, 750_001, m.RoomNight(d25, new Paise(750_001))),
    ("service", "Restaurant dinner", d25, 123_450, m.Food(d25, spec)),
    ("service", "Room service tea", d25, 1_010, m.Food(d25, spec)),
    ("service", "Laundry", d25, 33_333, m.Service(d25, GstCategory.Laundry)),
};
var roomRate25 = rows[1].Rate.Bps; // discount follows the room-night rate of the business date (BillingService.cs:531)
var all = rows.Append((Kind: "discount", Label: "Discount", Date: d25, Amount: -12_345L, Rate: new GstRate(roomRate25, true, GstCategory.Room, "follows"))).ToList();
var src = all.Select((r, i) => new SourceLine(i + 1, r.Kind, r.Date, r.Label, r.Rate.Category, new Paise(r.Amount), r.Rate.Bps, GstMath.LineTax(new Paise(r.Amount), r.Rate.Bps), null, null)).ToList();
var lines = InvoiceRules.BuildLines(src, interState: false);
foreach (var l in lines) L($"  mixed line {l.LineNo} {l.Description}: {l.Taxable.Value}@{l.Bps} tax {l.Tax.Value} CGST {l.Cgst.Value} SGST {l.Sgst.Value} SAC {l.Sac}");
var t = InvoiceRules.Totals(lines);
var ft = FolioTotals.Compute(all.Select(r => new FolioLine(new Paise(r.Amount), r.Rate.Bps)), []);
L($"mixed folio (HCC): net {t.Taxable.Value} tax {t.Tax.Value} CGST {t.Cgst.Value} SGST {t.Sgst.Value} total {t.Total.Value} · FolioTotals net {ft.Net.Value} tax {ft.Tax.Value} total {ft.Total.Value} · specified={spec.Specified}");
var ti = InvoiceRules.Totals(InvoiceRules.BuildLines(src, interState: true));
L($"same folio inter-state (IGST path): IGST {ti.Igst.Value} CGST {ti.Cgst.Value} SGST {ti.Sgst.Value}; place of supply for accommodation is the hotel (IGST Act s.12(3)) — see A03a-04");
var rec = InvoiceRules.Recipient("B2B", "Demo Co (fictional)", "29AAGCB7383J1Z4", null, "29", "27", t.Total);
L($"Recipient(B2B, GSTIN 29…, placeOfSupply 29, hotel 27) → InterState={rec.InterState} (whole invoice incl. room nights becomes IGST)");

// 3 ── credit note: full after a partial whose tax is an exact half paisa
var inv = InvoiceRules.BuildLines([new SourceLine(1, "room", d24, "Room night", GstCategory.Room, new Paise(200_000), 500, new Paise(10_000), null, null),
    new SourceLine(2, "room", d25, "Room night", GstCategory.Room, new Paise(200_000), 500, new Paise(10_000), null, null)], false);
var fresh = inv.Select(l => new CreditableLine(l, Paise.Zero, Paise.Zero)).ToList();
var part = CreditNoteRules.Plan(fresh, CreditMode.Lines, null, [new RequestedCreditLine(1, new Paise(10_010))], false);
L($"partial credit ₹100.10 on line 1: tax {part[0].Tax.Value} (500.5 → 501)");
var after = inv.Select(l => l.LineNo == 1 ? new CreditableLine(l, part[0].Taxable, part[0].Tax) : new CreditableLine(l, Paise.Zero, Paise.Zero)).ToList();
try { var full = CreditNoteRules.Plan(after, CreditMode.Full, null, null, false); L($"full credit after it: OK total {CreditNoteRules.Totals(full).Total.Value}"); }
catch (HccException ex) { L($"full credit after it: REFUSED {ex.Code}: {ex.Message}"); }
try { CreditNoteRules.Plan(after, CreditMode.Lines, null, [new RequestedCreditLine(1, new Paise(189_990))], false); L("lines-mode remainder of line 1: OK"); }
catch (HccException ex) { L($"lines-mode remainder of line 1 (189990): REFUSED {ex.Code}"); }
var ok = CreditNoteRules.Plan(after, CreditMode.Lines, null, [new RequestedCreditLine(1, new Paise(189_980))], false);
var after2 = inv.Select(l => l.LineNo == 1 ? new CreditableLine(l, part[0].Taxable + ok[0].Taxable, part[0].Tax + ok[0].Tax) : new CreditableLine(l, Paise.Zero, Paise.Zero)).ToList();
try { CreditNoteRules.Plan(after2, CreditMode.Lines, null, [new RequestedCreditLine(1, new Paise(10))], false); L("last 10 paise: OK"); }
catch (HccException ex) { L($"workaround 189980 accepted (tax {ok[0].Tax.Value}); the last 10 paise of line 1: REFUSED {ex.Code} — line can never be fully credited"); }

// 4 ── misc
foreach (var label in new[] { "Express breakfast", "Espresso", "Press conference hall", "Breakfast", "Minibar", "No-show charge", "Extra bed" })
    L($"category of '{label}': {GstCategories.OfLabel(label)}");
L($"numbers: {InvoiceNumbering.Format("INV", InvoiceNumbering.FyCode(d24), 1)} · {InvoiceNumbering.Format(InvoiceNumbering.DefaultPrefix(InvoiceDocType.CreditNote), InvoiceNumbering.FyCode(d24), 1)} · FY of 2027-04-01 {InvoiceNumbering.FyCode(new DateOnly(2027, 4, 1))}");
try { InvoiceNumbering.Format("INV", "2627", 10_000_000); } catch (HccException) { L("INV series full after 9,999,999 (7 digits) — refused, not wrapped"); }
foreach (var g in new[] { "27AAPFU0939F1ZV", "29AAGCB7383J1Z4", "27AAAAA0000A1Z2", "27AAAAA0000A1Z5", "27ZZFIC0001F1Z5", "27ZZFIC0002F1Z4", "27ZZFIC0003F1Z3" })
{
    string groupOk; try { Hcc.Domain.Groups.GroupText.Gstin(g); groupOk = "accepted"; } catch (HccException) { groupOk = "refused"; }
    L($"GSTIN {g}: invoices Gstin.IsValid={Gstin.IsValid(g)} · groups GroupText.Gstin {groupOk}");
}
L($"slab: 750000 → {m.RoomNight(d24, new Paise(750_000)).Bps} bps · 750001 → {m.RoomNight(d24, new Paise(750_001)).Bps} bps · 21 Sep 2025 ₹2,000 → {m.RoomNight(new DateOnly(2025, 9, 21), new Paise(200_000)).Bps} bps");
L($"attrition slab basis: ₹9,000 block rate × 50 % = ₹4,500 retention → {m.RoomNight(d24, MoneyMath.PercentOf(new Paise(900_000), 5000)).Bps} bps (GroupCommands.cs:455 tests the retention price); the underlying ₹9,000 night → {m.RoomNight(d24, new Paise(900_000)).Bps} bps");
var dir = Path.Combine(Environment.CurrentDirectory, "05 Delivery Evidence", "Audit", "evidence", "A03a");
Directory.CreateDirectory(dir);
File.WriteAllLines(Path.Combine(dir, "domain_crosscheck_cs.txt"), o);
