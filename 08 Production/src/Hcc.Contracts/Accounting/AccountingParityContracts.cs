namespace Hcc.Contracts.Accounting;

// ACC-2 · lane acc2-parity (02 Planning/agents/ACC2-POSTING-CONTRACT.md §8; ACC1-DONE §7 item 6): the one-off back-fill of the Udhaar credits
// raised before migration 4215 (no UdhaarCreditRaised event, so no Sales voucher). Money = paise.

/// <summary>BackfillUdhaarCredits (Owner): publishes UdhaarCreditRaised for every Udhaar credit of the property that has neither the event nor
/// its sale source. No fields; idempotent (a second run publishes nothing).</summary>
public sealed record BackfillUdhaarCreditsRequest();

/// <summary>What the back-fill published: how many credits, their gross total and the receivable ids, and the audit text.</summary>
public sealed record BackfillUdhaarCreditsResult(int Published, long AmountPaise, IReadOnlyList<long> ReceivableIds, string Message);
