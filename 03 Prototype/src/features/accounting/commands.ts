// ACC-1 · the command payloads the accounting screens send, typed as the real handlers read them (integration of the acc1-ui lane:
// services/mock/handlers/accounting-chart.ts and accounting-opening.ts; contract §2, §5, §7).
import type {ACCOpeningLine} from '../../services/mock';

/** ACC_LEDGER_UPSERT: `create:true` adds an Owner ledger (unused 4-digit code); otherwise edits one (a built-in: name, group within the same
 *  Tally nature, GST flag, Tally name, note). `active:true` alone reactivates a retired ledger. Empty tallyName / note clear them. */
export type AccLedgerUpsert={type:'ACC_LEDGER_UPSERT';create?:true;code:string;name?:string;group?:string;gst?:'none'|'input'|'output';tallyName?:string;note?:string;active?:true};
/** ACC_LEDGER_RETIRE: refused while mapped, with a (party) balance, and always for 9999 / 1400. Reactivate = AccLedgerUpsert {active:true}. */
export type AccLedgerRetire={type:'ACC_LEDGER_RETIRE';code:string;reason?:string};
/** ACC_GROUP_UPSERT: a new custom sub-group (no id) or a rename / re-parent of a custom one. */
export type AccGroupUpsert={type:'ACC_GROUP_UPSERT';id?:string;name:string;parent:string};
/** ACC_OPENING_SAVE: lines WITHOUT the 1400 line (the handler adds it); asOf = the start of the business financial year. */
export type AccOpeningSave={type:'ACC_OPENING_SAVE';asOf:string;lines:ACCOpeningLine[];note?:string};
export type AccOpeningApprove={type:'ACC_OPENING_APPROVE'};
export type AccOpeningReopen={type:'ACC_OPENING_REOPEN';reason:string};
