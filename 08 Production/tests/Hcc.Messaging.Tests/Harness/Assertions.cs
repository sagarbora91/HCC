using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Messaging.Tests.Harness;

public static class Expect
{
    /// <summary>The HCC error code thrown by <paramref name="action"/>, or "NO_ERROR".</summary>
    public static async Task<string> CodeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
        }
        catch (HccException e)
        {
            return e.Code.ToString();
        }

        return "NO_ERROR";
    }

    public static async Task<HccException> ErrorAsync(HccErrorCode code, Func<Task> action)
    {
        var e = await Assert.ThrowsAsync<HccException>(action);
        Assert.Equal(code, e.Code);
        return e;
    }
}
