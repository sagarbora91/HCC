using System.Data;
using Hcc.Application.Commands;
using Hcc.Application.Stays;
using Hcc.Domain.Stays;
using Hcc.Infrastructure.SqlServer.Commands;
using Hcc.Infrastructure.SqlServer.Connection;
using Microsoft.Data.SqlClient;

namespace Hcc.Infrastructure.SqlServer.Stays;

/// <summary>SQL adapter of the LF desk fields (migrations 4695–4699: purpose / vehicle masters, reservation columns, gst.stay_form_c, gst.stay.late_checkout_decision).</summary>
public sealed class SqlDeskFieldStore(SqlConnectionFactory connections) : IDeskFieldStore
{
    private static readonly string[] BuiltInPurposes = ["BUSINESS", "TOURISM", "FAMILY", "MEDICAL", "EDUCATION", "OFFICIAL", "TRANSIT", "OTHER"];
    private static readonly (string Code, string Name)[] BuiltInVehicles = [("CAR", "Car"), ("BIKE", "Bike"), ("TAXI", "Taxi"), ("BUS", "Bus"), ("OTHER", "Other")];

    private static SqlCommandTransaction Sql(ICommandTransaction tx) => tx as SqlCommandTransaction ?? throw new InvalidOperationException("The desk-field store needs the SQL command transaction.");

    private static void P(SqlCommand cmd, string name, SqlDbType type, object? value, int size = 0)
    {
        var p = size > 0 ? cmd.Parameters.Add(name, type, size) : cmd.Parameters.Add(name, type);
        p.Value = value ?? DBNull.Value;
    }

    private static DateTime D(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    public async Task<bool> PurposeUsableAsync(ICommandTransaction tx, int propertyId, string code, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT COUNT(*), SUM(CASE WHEN code = @c AND active = 1 THEN 1 ELSE 0 END) FROM mst.purpose_of_visit WHERE property_id = @p");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@c", SqlDbType.VarChar, code, 20);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await rd.ReadAsync(ct).ConfigureAwait(false);
        return rd.GetInt32(0) == 0 ? BuiltInPurposes.Contains(code) : !rd.IsDBNull(1) && rd.GetInt32(1) > 0;
    }

    public async Task<string?> VehicleTypeNameAsync(ICommandTransaction tx, int propertyId, string typeOrCode, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT code, name, active FROM mst.vehicle_type WHERE property_id = @p");
        P(cmd, "@p", SqlDbType.Int, propertyId);
        var rows = new List<(string Code, string Name, bool Active)>();
        await using (var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            while (await rd.ReadAsync(ct).ConfigureAwait(false)) rows.Add((rd.GetString(0), rd.GetString(1), rd.GetBoolean(2)));
        if (rows.Count == 0) rows = BuiltInVehicles.Select(v => (v.Code, v.Name, true)).ToList();
        var hit = rows.FirstOrDefault(r => r.Active && (string.Equals(r.Name, typeOrCode, StringComparison.OrdinalIgnoreCase) || string.Equals(r.Code, typeOrCode, StringComparison.OrdinalIgnoreCase)));
        return hit.Name;
    }

    public async Task SetBookingFieldsAsync(ICommandTransaction tx, long reservationId, string? purposeCode, string? vehicleNo, string? vehicleType, string? checkoutBasis, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            UPDATE inv.reservation SET purpose_code = COALESCE(@pc, purpose_code), vehicle_no = COALESCE(@vn, vehicle_no), vehicle_type = CASE WHEN @vn IS NULL THEN vehicle_type ELSE @vt END,
                   checkout_basis = COALESCE(@cb, checkout_basis)
             WHERE reservation_id = @r
            """);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        P(cmd, "@pc", SqlDbType.VarChar, purposeCode, 20);
        P(cmd, "@vn", SqlDbType.VarChar, vehicleNo, 13);
        P(cmd, "@vt", SqlDbType.NVarChar, vehicleType, 40);
        P(cmd, "@cb", SqlDbType.Char, checkoutBasis, 5);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<DeskReservation?> GetAsync(ICommandTransaction tx, long reservationId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            SELECT r.purpose_code, r.vehicle_no, r.vehicle_type, RTRIM(r.checkout_basis), s.checked_in_utc,
                   COALESCE((SELECT TOP 1 n.rate_paise FROM inv.reservation_night n WHERE n.reservation_id = r.reservation_id ORDER BY n.night), r.nightly_rate_paise, 0)
              FROM inv.reservation r LEFT JOIN gst.stay s ON s.reservation_id = r.reservation_id
             WHERE r.reservation_id = @r
            """);
        P(cmd, "@r", SqlDbType.BigInt, reservationId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await rd.ReadAsync(ct).ConfigureAwait(false)) return null;
        return new DeskReservation(rd.IsDBNull(0) ? null : rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1), rd.IsDBNull(2) ? null : rd.GetString(2), rd.GetString(3),
            rd.IsDBNull(4) ? null : DateTime.SpecifyKind(rd.GetDateTime(4), DateTimeKind.Utc), rd.GetInt64(5));
    }

    public async Task<DeskGuestFacts?> GuestFactsAsync(ICommandTransaction tx, long guestId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT nationality, id_type, dob, id_last4 FROM gst.guest WHERE guest_id = @g");
        P(cmd, "@g", SqlDbType.BigInt, guestId);
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await rd.ReadAsync(ct).ConfigureAwait(false) ? new DeskGuestFacts(rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1), !rd.IsDBNull(2), !rd.IsDBNull(3)) : null;
    }

    public async Task UpsertFormCAsync(ICommandTransaction tx, int propertyId, long stayId, FormCFacts f, int by, DateTime utc, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("""
            MERGE gst.stay_form_c AS t USING (SELECT @s AS stay_id) AS x ON t.stay_id = x.stay_id
            WHEN MATCHED THEN UPDATE SET passport_place = @pp, passport_issued = @pi, passport_expiry = @pe, visa_no = @vn, visa_type = @vt, visa_place = @vp, visa_issued = @vi, visa_expiry = @ve,
                 arrived_from = @af, arrival_in_india = @ai, intended_stay_days = @days, next_destination = @nd, employed_in_india = @emp, employer_or_business = @eb, address_abroad = @aa,
                 contact_abroad = @ca, sex = @sex, updated_by = @by, updated_utc = @u
            WHEN NOT MATCHED THEN INSERT(stay_id, property_id, passport_place, passport_issued, passport_expiry, visa_no, visa_type, visa_place, visa_issued, visa_expiry, arrived_from, arrival_in_india,
                 intended_stay_days, next_destination, employed_in_india, employer_or_business, address_abroad, contact_abroad, sex, updated_by, updated_utc)
                 VALUES(@s, @p, @pp, @pi, @pe, @vn, @vt, @vp, @vi, @ve, @af, @ai, @days, @nd, @emp, @eb, @aa, @ca, @sex, @by, @u);
            """);
        P(cmd, "@s", SqlDbType.BigInt, stayId);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@pp", SqlDbType.NVarChar, f.PassportPlace, 60);
        P(cmd, "@pi", SqlDbType.Date, D(f.PassportIssued));
        P(cmd, "@pe", SqlDbType.Date, D(f.PassportExpiry));
        P(cmd, "@vn", SqlDbType.NVarChar, f.VisaNo, 30);
        P(cmd, "@vt", SqlDbType.NVarChar, f.VisaType, 30);
        P(cmd, "@vp", SqlDbType.NVarChar, f.VisaPlace, 60);
        P(cmd, "@vi", SqlDbType.Date, D(f.VisaIssued));
        P(cmd, "@ve", SqlDbType.Date, D(f.VisaExpiry));
        P(cmd, "@af", SqlDbType.NVarChar, f.ArrivedFrom, 60);
        P(cmd, "@ai", SqlDbType.Date, D(f.ArrivalInIndia));
        P(cmd, "@days", SqlDbType.Int, f.IntendedStayDays);
        P(cmd, "@nd", SqlDbType.NVarChar, f.NextDestination, 60);
        P(cmd, "@emp", SqlDbType.Bit, f.EmployedInIndia);
        P(cmd, "@eb", SqlDbType.NVarChar, f.EmployerOrBusiness, 80);
        P(cmd, "@aa", SqlDbType.NVarChar, f.AddressAbroad, 160);
        P(cmd, "@ca", SqlDbType.NVarChar, f.ContactAbroad, 80);
        P(cmd, "@sex", SqlDbType.Char, f.Sex.ToString(), 1);
        P(cmd, "@by", SqlDbType.Int, by);
        P(cmd, "@u", SqlDbType.DateTime2, utc);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<string?> LateDecisionAsync(ICommandTransaction tx, long stayId, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("SELECT late_checkout_decision FROM gst.stay WHERE stay_id = @s");
        P(cmd, "@s", SqlDbType.BigInt, stayId);
        return await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
    }

    public async Task SetLateDecisionAsync(ICommandTransaction tx, long stayId, string decision, CancellationToken ct = default)
    {
        await using var cmd = Sql(tx).Command("UPDATE gst.stay SET late_checkout_decision = @d WHERE stay_id = @s");
        P(cmd, "@s", SqlDbType.BigInt, stayId);
        P(cmd, "@d", SqlDbType.VarChar, decision, 10);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FormCRegisterRaw>> FormCRangeAsync(int propertyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var conn = await connections.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = connections.Command("""
            SELECT s.stay_id, r.public_no, rm.room_no, g.full_name, g.nationality, s.arrival, s.departure, s.status, g.dob,
                   f.passport_place, f.passport_issued, f.passport_expiry, f.visa_no, f.visa_type, f.visa_place, f.visa_issued, f.visa_expiry, f.arrived_from, f.arrival_in_india,
                   f.intended_stay_days, f.next_destination, f.employed_in_india, f.employer_or_business, f.address_abroad, f.contact_abroad, f.sex
              FROM gst.stay s JOIN inv.reservation r ON r.reservation_id = s.reservation_id JOIN gst.guest g ON g.guest_id = s.guest_id
              CROSS APPLY (SELECT TOP (1) room_id FROM gst.stay_segment WHERE stay_id = s.stay_id ORDER BY seq DESC) seg JOIN inv.room rm ON rm.room_id = seg.room_id LEFT JOIN gst.stay_form_c f ON f.stay_id = s.stay_id
             WHERE s.property_id = @p AND s.arrival <= @to AND s.departure >= @from
               AND (f.stay_id IS NOT NULL OR LOWER(LTRIM(RTRIM(g.nationality))) NOT IN (N'indian', N'india', N''))
             ORDER BY s.arrival, rm.room_no
            """, conn);
        P(cmd, "@p", SqlDbType.Int, propertyId);
        P(cmd, "@from", SqlDbType.Date, D(from));
        P(cmd, "@to", SqlDbType.Date, D(to));
        var list = new List<FormCRegisterRaw>();
        await using var rd = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rd.ReadAsync(ct).ConfigureAwait(false))
        {
            FormCFacts? form = rd.IsDBNull(9) ? null : new FormCFacts(rd.GetString(9), DateOnly.FromDateTime(rd.GetDateTime(10)), DateOnly.FromDateTime(rd.GetDateTime(11)), rd.GetString(12), rd.GetString(13),
                rd.GetString(14), DateOnly.FromDateTime(rd.GetDateTime(15)), DateOnly.FromDateTime(rd.GetDateTime(16)), rd.GetString(17), DateOnly.FromDateTime(rd.GetDateTime(18)), rd.GetInt32(19),
                rd.GetString(20), rd.GetBoolean(21), rd.IsDBNull(22) ? null : rd.GetString(22), rd.GetString(23), rd.IsDBNull(24) ? null : rd.GetString(24), rd.GetString(25)[0]);
            list.Add(new FormCRegisterRaw(rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetString(4), DateOnly.FromDateTime(rd.GetDateTime(5)), DateOnly.FromDateTime(rd.GetDateTime(6)),
                rd.GetString(7), rd.IsDBNull(8) ? null : DateOnly.FromDateTime(rd.GetDateTime(8)), form));
        }

        return list;
    }
}
