using Hcc.Application.Commands;
using Hcc.Contracts.Billing;
using Hcc.Domain.Billing;
using Hcc.Contracts;
using Hcc.Domain.Errors;

namespace Hcc.Application.Billing;

/// <summary>
/// LF-money approvals through the shared DecideApproval command (LF-CONTRACT §2, §4, §6, §7): each action goes to the module that owns it.
/// A module that is not wired (unit tests) refuses the decision.
/// </summary>
public sealed class LfApprovalDecisions(IEnumerable<ILfApprovalHandler> handlers) : ILfApprovalDecisions
{
    public Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow approval, bool approve, string reason, string expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(approval);
        var h = handlers.FirstOrDefault(x => x.Action == approval.Action)
            ?? throw new HccException(HccErrorCode.INVALID_TRANSITION, "This request is decided by its own module.");
        return h.DecideAsync(tx, caller, approval, approve, reason, expectedVersion, ct);
    }
}

/// <summary>One LF-money approval action's decision (registered by the module that owns it).</summary>
public interface ILfApprovalHandler
{
    AdjustmentAction Action { get; }

    Task<ApprovalResult> DecideAsync(ICommandTransaction tx, Caller caller, ApprovalRow approval, bool approve, string reason, string expectedVersion, CancellationToken ct);
}
