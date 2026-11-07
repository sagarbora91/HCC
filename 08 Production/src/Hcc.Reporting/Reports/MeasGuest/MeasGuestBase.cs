// F12-RPT-GUEST · base of the six guest measurement selectors: the switch key (IMeasuredReport, honoured by ReportRunner), target reads, empty-result helper.
using Hcc.Application.Billing;
using System.Data;
using Hcc.Application.Configuration;
using Hcc.Application.Reporting.Rpt;
using Hcc.Domain.Configuration;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Guests;

namespace Hcc.Reporting.Reports.MeasGuest;

public abstract class MeasGuestBase(SqlPlatform platform, ISettingsReader? settings) : IReportSelector, IMeasuredReport
{
    protected SqlPlatform Platform { get; } = platform;

    public abstract ReportDefinition Definition { get; }

    public abstract string MeasKey { get; }

    public abstract Task<ReportResult> SelectAsync(Caller caller, BoundParams parameters, CancellationToken ct = default);

    /// <summary>A stored target (meas.target.*) or the contract default.</summary>
    protected async Task<long> TargetAsync(int propertyId, string key, long fallback, CancellationToken ct)
    {
        if (settings is null) return fallback;
        try
        {
            var v = await settings.GetAsync(propertyId, key, null, ct).ConfigureAwait(false);
            return v.Kind == ConfigValueKind.Number ? v.Number : fallback;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return fallback; // review H1: an unregistered key falls back to the contract default
        }
    }

    protected ReportResult Result(BoundParams p, IReadOnlyList<ReportRow> rows, string? emptyNeeds = null, string? basisOverride = null)
    {
        var basis = basisOverride ?? (rows.Count == 0 && emptyNeeds is not null ? MeasGuestRules.CapNote(Definition.DateBasis, emptyNeeds) : Definition.DateBasis);
        return new ReportResult(Definition.Id, Definition.Title, p, Definition.Columns, rows, new ReportAbout(DateTimeOffset.MinValue, "", basis, false, p.Legacy, p.From, p.To));
    }

    protected static ReportRow Row(Dictionary<string, object?> cells, bool red = false) => new(ReportRowKind.Row, cells, red ? "red" : null);

    protected static string Param(BoundParams p, string key) => p.Values.GetValueOrDefault(key) ?? "";

    internal static DateTime LocalOf(GuestsSql.Clock clock, DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.Zone);

    protected static void Add(Microsoft.Data.SqlClient.SqlCommand c, string name, SqlDbType type, object? value) => GuestsSql.Add(c, name, type, value);
}
