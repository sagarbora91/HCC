// F7-R3 · A16 print layouts in the Lodge look with the HCC letterhead (guest registration card, day-end sheet). Server twin of
// 03 Prototype/src/domain/reports/guests/layouts.ts: the same labels, order and CSS. Pure HTML builders over label/value pairs; the registration-card pairs are
// loaded by RegistrationCardAsync (ID number masked, no photos). Printed through the existing print path (native.ts printablePage / PDF renderer).
using System.Data;
using System.Net;
using System.Text;
using Hcc.Application.Billing;
using Hcc.Contracts;
using Hcc.Domain.Errors;
using Hcc.Infrastructure.SqlServer;
using Microsoft.Data.SqlClient;

namespace Hcc.Reporting.Reports.Guests;

public sealed record LodgeLayout(string Title, string Body, string Css, IReadOnlyList<(string Label, string Value)> Pairs);

public static class LodgeLayouts
{
    public const string Letterhead = "Hotel Control Centre";

    public const string Css = "@page{size:A4 portrait}body{font-family:Georgia,\"Times New Roman\",serif;color:black}.lg-head{text-align:center;border-bottom:2px solid black;padding-bottom:6px;margin-bottom:10px}"
        + ".lg-head .brand{font-size:11pt;letter-spacing:.12em;text-transform:uppercase}.lg-head h1{font-size:18pt;margin:2px 0}.lg-head .sub{font-size:10pt}"
        + ".lg-grid{display:grid;grid-template-columns:1fr 1fr;border:1px solid black}.lg-cell{border:1px solid black;padding:4px 6px;font-size:10.5pt}.lg-cell b{display:block;font-size:8.5pt;text-transform:uppercase;letter-spacing:.05em}"
        + ".lg-sign{display:flex;justify-content:space-between;margin-top:46px;font-size:10pt}.lg-sign div{border-top:1px solid black;width:42%;text-align:center;padding-top:3px}"
        + ".lg-note{font-size:9pt;margin-top:10px}.lg-sec{font-weight:bold;margin:12px 0 4px;border-bottom:1px solid black}";

    private static string E(string? t) => WebUtility.HtmlEncode(t ?? "");

    private static string Head(string property, string title, string sub) =>
        $"<div class=\"lg-head\"><div class=\"brand\">{E(Letterhead)}</div><h1>{E(property)}</h1><div class=\"sub\">{E(title)}{(sub.Length > 0 ? " · " + E(sub) : "")}</div></div>";

    private static string Cell(string label, string value, bool wide = false) =>
        $"<div class=\"lg-cell\"{(wide ? " style=\"grid-column:1/3\"" : "")}><b>{E(label)}</b>{(value.Length > 0 ? E(value) : "&nbsp;")}</div>";

    public static LodgeLayout RegistrationCard(string property, string bookingNo, IReadOnlyList<(string Label, string Value)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var sb = new StringBuilder(Head(property, "Guest registration card", bookingNo)).Append("<div class=\"lg-grid\">");
        foreach (var (k, v) in pairs) sb.Append(Cell(k, v, k == "Address"));
        sb.Append("</div><div class=\"lg-sign\"><div>Guest signature</div><div>Reception</div></div><p class=\"lg-note\">I agree to the house rules and to settle my account at check-out. The ID number is masked on this card; no ID images are kept on it.</p>");
        return new LodgeLayout("Registration card " + bookingNo, sb.ToString(), Css, pairs);
    }

    /// <summary>Day end: A movements always; B money only when <paramref name="money"/> pairs are given (rule M — the caller decides with PERM-RPT-002).</summary>
    public static LodgeLayout DayEnd(string property, DateOnly date, string clockText, IReadOnlyList<(string Label, string Value)> movements, IReadOnlyList<(string Label, string Value)>? money)
    {
        ArgumentNullException.ThrowIfNull(movements);
        var pairs = movements.Concat(money ?? []).ToList();
        var sb = new StringBuilder(Head(property, "Day end report", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        sb.Append("<div class=\"lg-sec\">A · Movements</div><div class=\"lg-grid\">").AppendJoin("", movements.Select(p => Cell(p.Label, p.Value))).Append("</div>");
        if (money is { Count: > 0 }) sb.Append("<div class=\"lg-sec\">B · Money</div><div class=\"lg-grid\">").AppendJoin("", money.Select(p => Cell(p.Label, p.Value))).Append("</div>");
        sb.Append($"<div class=\"lg-sign\"><div>Prepared by</div><div>Checked by</div></div><p class=\"lg-note\">Figures as at {E(clockText)} (business clock). Same figures as the Closing report and the Arrivals and departures register.</p>");
        return new LodgeLayout("Day end report " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), sb.ToString(), Css, pairs);
    }

    private const string CardSql = """
        SELECT r.public_no, rm.room_no, r.arrival, r.departure, DATEDIFF(DAY, r.arrival, r.departure), s.checked_in_utc, COALESCE(g.full_name, r.guest_name), g.nationality, g.address, g.phone, g.id_type, g.id_last4,
               r.adults, r.children, COALESCE(pv.name, r.purpose_code), r.vehicle_no, fc.arrived_from, fc.next_destination, s.checked_out_utc, p.name
          FROM inv.reservation r
          JOIN inv.property p ON p.property_id = r.property_id
          LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
          LEFT JOIN gst.registration reg ON reg.reservation_id = r.reservation_id
          LEFT JOIN gst.guest g ON g.guest_id = COALESCE(s.guest_id, reg.guest_id)
          LEFT JOIN gst.stay_form_c fc ON fc.stay_id = s.stay_id
          LEFT JOIN mst.purpose_of_visit pv ON pv.property_id = r.property_id AND pv.code = r.purpose_code
          LEFT JOIN inv.room rm ON rm.room_id = COALESCE((SELECT TOP (1) sg.room_id FROM gst.stay_segment sg WHERE sg.stay_id = s.stay_id ORDER BY sg.seq DESC), r.room_id)
         WHERE r.property_id = @p AND r.public_no = @no;
        """;

    /// <summary>The registration card of one booking (public number). The caller has already been checked for the check-in desk right or PERM-RPT-001.</summary>
    public static async Task<LodgeLayout> RegistrationCardAsync(SqlPlatform platform, Caller caller, string bookingNo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(caller);
        var clock = await GuestsSql.ClockAsync(platform.Connections, caller.PropertyId, ct).ConfigureAwait(false);
        await using var conn = await platform.Connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(CardSql, conn);
        GuestsSql.Add(cmd, "@p", SqlDbType.Int, caller.PropertyId);
        GuestsSql.Add(cmd, "@no", SqlDbType.VarChar, bookingNo);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) throw new HccException(HccErrorCode.NOT_FOUND, "No such booking.");
        var last4 = GuestsSql.Str(rd, 11);
        var adults = Convert.ToInt64(rd.GetValue(12));
        var children = Convert.ToInt64(rd.GetValue(13));
        var c = System.Globalization.CultureInfo.InvariantCulture;
        List<(string, string)> pairs =
        [
            ("Registration no", rd.GetString(0)), ("Room", GuestsSql.Str(rd, 1)), ("Arrival", GuestsSql.Iso(GuestsSql.Date(rd, 2))), ("Departure", GuestsSql.Iso(GuestsSql.Date(rd, 3))),
            ("Nights", Convert.ToString(rd.GetValue(4), c)!), ("Check-in", rd.IsDBNull(5) ? "" : GuestsSql.Local(clock, rd.GetDateTime(5))), ("Guest", GuestsSql.Str(rd, 6)), ("Nationality", GuestsSql.Str(rd, 7)),
            ("City", GuestsSql.CityOfAddress(GuestsSql.Str(rd, 8))), ("Phone", GuestsSql.Str(rd, 9)), ("ID type", GuestsSql.Str(rd, 10)),
            ("ID number", last4.Length > 0 ? MoneyMasker.Mask(Hcc.Application.Reporting.Rpt.ReportMask.IdNumber, last4) : ""), ("Adults", adults.ToString(c)), ("Children", children.ToString(c)),
            ("Pax", (adults + children).ToString(c)), ("Purpose of visit", GuestsSql.Str(rd, 14)), ("Vehicle no", GuestsSql.Str(rd, 15)), ("Coming from", GuestsSql.Str(rd, 16)), ("Going to", GuestsSql.Str(rd, 17)),
            ("Check-out", rd.IsDBNull(18) ? "" : GuestsSql.Local(clock, rd.GetDateTime(18))), ("Address", GuestsSql.Str(rd, 8)),
        ];
        return RegistrationCard(GuestsSql.Str(rd, 19), rd.GetString(0), pairs);
    }
}
