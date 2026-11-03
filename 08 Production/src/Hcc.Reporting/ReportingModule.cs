// F7-R1 · DI wiring of the report module (F7-CONTRACT §2): AddReportingModule() once (HccComposition F7-R1), AddReport<T>() per report
// (HccComposition F7-RPT, one line each).
using Hcc.Application.Reporting.Rpt;
using Hcc.Infrastructure.SqlServer;
using Hcc.Reporting.Reports.Money;
using Hcc.Reporting.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hcc.Reporting;

public static class ReportingModule
{
    public static IServiceCollection AddReport<T>(this IServiceCollection services) where T : class, IReportSelector
    {
        services.AddSingleton<IReportSelector, T>();
        return services;
    }

    public static IServiceCollection AddReportingModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new SqlReportStores(sp.GetRequiredService<SqlPlatform>().Connections));
        services.AddSingleton<IReportPropertyInfo>(sp => sp.GetRequiredService<SqlReportStores>());
        services.AddSingleton<IReportScheduleStore>(sp => sp.GetRequiredService<SqlReportStores>());
        services.AddSingleton<IReportRunStore>(sp => sp.GetRequiredService<SqlReportStores>());
        services.AddSingleton<IReportExportLog>(sp => sp.GetRequiredService<SqlReportStores>());
        services.AddSingleton<IReportFavourites>(sp => sp.GetRequiredService<SqlReportStores>());
        services.AddSingleton<IReportPrincipalRoles>(sp => new SqlReportPrincipalRoles(sp.GetRequiredService<SqlPlatform>().Connections, sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IReportFileStore, ReferenceOnlyFileStore>();
        services.TryAddSingleton<IReportDelivery, EgressReportDelivery>();
        services.AddSingleton<IComputedColumnEngine, FixedCodeEngine>();
        services.AddSingleton<IReportRenderer, XlsxRenderer>();
        services.AddSingleton<IReportRenderer, PdfRenderer>();
        services.AddSingleton<IReportRenderer, CsvRenderer>();
        services.AddSingleton<IReportRenderer, PrintHtmlRenderer>();
        services.AddSingleton<IReportCatalogue, ReportCatalogue>();
        services.AddSingleton<ReportRunner>();
        services.AddSingleton<ReportService>();
        services.AddReport<RevenueByCategoryTenderReport>(); // engine demo report (F7-CONTRACT §4)
        return services;
    }
}
