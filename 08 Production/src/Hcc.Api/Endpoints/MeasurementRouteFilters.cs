// F12 MEAS · meas-settings (MEAS-CONTRACT §1.5): route helper like RequireHccModule. Capture lanes put .RequireHccMeasurement("meas.<pack>.<item>")
// on their command routes (after the session filter); reads stay open, data is kept.
using Hcc.Application.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Hcc.Api.Endpoints;

public static class MeasurementRouteFilters
{
    /// <summary>Refuses the endpoint (FORBIDDEN, details.reason MEAS_OFF, details.key) while the measurement item is off. Fails closed without
    /// <see cref="IMeasurementGate"/> in DI. Unknown keys throw at start-up.</summary>
    public static TBuilder RequireHccMeasurement<TBuilder>(this TBuilder builder, string key) where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (MeasurementCatalogue.Item(key) is null) throw new ArgumentException($"'{key}' is not a measurement key.", nameof(key));
        builder.WithMetadata(new HccMeasGateMetadata(key));
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var (property, _) = http.HccActor();
            await http.RequestServices.GetRequiredService<IMeasurementGate>().EnsureOnAsync(property, key, http.RequestAborted).ConfigureAwait(false);
            return await next(ctx).ConfigureAwait(false);
        });
        return builder;
    }
}
