using System.Text.Json;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Application.Inventory;

/// <summary>
/// Group-block switch (DEC-080, audit A02b-04). Blocks hold rooms that the front desk can neither sell nor pick up until the cut-off,
/// because pickup (rooming list), partial release, cancel and re-allotment are not built yet. So production <b>refuses
/// CreateGroupBlock</b> by default; set <c>Hcc:GroupBlocks:Enabled</c> to <c>true</c> once those four exist. ReleaseGroupBlock and
/// the cut-off sweep stay available for any block that already exists.
/// </summary>
public sealed class GroupBlockOptions
{
    /// <summary>Written reason shown to staff (the web app shows it as is).</summary>
    public const string RefusalMessage = "Group blocks are not available yet: pickup and release are still being built. Book the rooms as separate reservations.";

    /// <summary>Wire value of <c>details.reason</c> on the refusal.</summary>
    public const string RefusalReason = "GROUP_BLOCKS_OFF";

    public bool Enabled { get; set; }

    /// <summary>NOT_AVAILABLE (HTTP 409) with <see cref="RefusalMessage"/> and <c>details.reason = GROUP_BLOCKS_OFF</c>.</summary>
    public static HccException Refusal() => new(HccErrorCode.NOT_AVAILABLE, RefusalMessage,
        new Dictionary<string, JsonElement> { ["reason"] = JsonSerializer.SerializeToElement(RefusalReason) });
}
