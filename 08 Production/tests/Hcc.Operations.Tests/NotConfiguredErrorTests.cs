using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Operations.Tests;

/// <summary>W4 F3 (D3): a missing setup step (guest-ID key) is still 503 UNAVAILABLE but is marked so clients do not go read-only.</summary>
public sealed class NotConfiguredErrorTests
{
    [Fact]
    public void Not_configured_is_unavailable_with_a_reason_and_feature_in_the_error_body()
    {
        var ex = HccException.NotConfigured("guest-id", "Full ID numbers cannot be stored.");
        Assert.Equal(HccErrorCode.UNAVAILABLE, ex.Code);
        var body = ex.ToResponse();
        Assert.Equal("UNAVAILABLE", body.Code);
        Assert.Equal("NOT_CONFIGURED", body.Details!["reason"].GetString());
        Assert.Equal("guest-id", body.Details["feature"].GetString());
        Assert.Equal(503, HccErrorCatalog.HttpStatus(HccErrorCode.UNAVAILABLE));
    }
}
