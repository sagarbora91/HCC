// F12-RPT-REVENUE · common constructor of the six report classes (one DI shape: platform, yield store, optional settings reader, optional measurement gate, clock).
using Hcc.Application.Configuration;
using Hcc.Application.Yield;
using Hcc.Infrastructure.SqlServer;

namespace Hcc.Reporting.Reports.MeasRevenue;

public abstract class MeasRevenueBase(SqlPlatform platform, IYieldStore yield, ISettingsReader? settings, IMeasurementGate? gate, TimeProvider? clock)
{
    internal SqlPlatform Platform { get; } = platform;

    internal IYieldStore Yield { get; } = yield;

    internal ISettingsReader? Settings { get; } = settings;

    internal IMeasurementGate? Gate { get; } = gate;

    internal TimeProvider? Clk { get; } = clock;
}
