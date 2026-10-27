// ACC-1 · Opening balances entered by hand — command handlers (contract §5, §7; rulebook §G2; DEC-139 / DEC-148).
// ACC_OPENING_SAVE {asOf, lines, note?}: Owner / Manager / Accountant with 'reports' (PERM-ACC-008); replaces the draft, the gap goes to 1400.
// ACC_OPENING_APPROVE {}: Owner (PERM-ACC-009). ACC_OPENING_REOPEN {reason}: Owner, only while no period lock exists on or after asOf.
// The audit row of each command is written by execute() from the returned detail text. Production: clients/sync/accounting-opening.ts.
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {accSlice,ACC_LOCK_ROLES,type ACCState,type ACCOpeningLine} from '../../../domain/streams/accounting';
import {accOpening,accFyStart,accOpeningLineProblems,accOpeningNormalise,accOpeningTotals,accOpeningLocksFrom,ACC_OPENING_DIFF} from '../../../domain/streams/accounting-opening';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
/** Writable slice on the cloned state (creates it for old saves). */
const slice=(k:Kit):ACCState=>{k.s.accountingX={...accSlice(k.s)};return k.s.accountingX;};
const owner=(k:Kit,what:string)=>{if(k.user.role!=='Owner')k.fail('FORBIDDEN',`Access denied: only the Owner ${what} the opening balances (DEC-148).`);};
const diffText=(lines:ACCOpeningLine[])=>{const l=lines.find(x=>x.account===ACC_OPENING_DIFF);return l?` · ${ACC_OPENING_DIFF} difference ${money(l.debit||l.credit)} ${l.debit?'Dr':'Cr'}`:'';};

export const handlers:Record<string,Handler>={
  ACC_OPENING_SAVE:k=>{
    const {s,cmd,req,assert,fail,uid,user}=k;req('reports');
    if(!ACC_LOCK_ROLES.includes(user.role))fail('FORBIDDEN','Access denied: only the Owner, the Manager or the Accountant enters opening balances.');
    const fy=accFyStart(s.date),asOf=String(cmd.asOf??fy);
    assert(DATE.test(asOf)&&asOf===fy,`Opening balances are dated the start of the business financial year (${fy}).`);
    const cur=accOpening(s);
    if(cur?.status==='approved')fail('INVALID_TRANSITION','The opening balances are approved and read-only — the Owner reopens them first.');
    const errors=accOpeningLineProblems(cmd.lines).filter(p=>p.severity==='error');
    if(errors.length)fail('VALIDATION',errors[0].message+(errors.length>1?` (${errors.length-1} more problem${errors.length===2?'':'s'})`:''));
    const note=cmd.note==null?'':String(cmd.note).trim();assert(note.length<=200,'Keep the note to 200 characters.');
    const lines=accOpeningNormalise(cmd.lines as ACCOpeningLine[]),t=accOpeningTotals({lines});
    const x=slice(k);
    x.opening={asOf,lines,status:'draft',by:uid,at:s.clock,...(note?{note}:{})};
    return `Opening balances as of ${asOf} saved (draft) · ${lines.filter(l=>l.account!==ACC_OPENING_DIFF).length} line(s) · ${money(t.debit)} Dr = ${money(t.credit)} Cr${diffText(lines)}`;
  },
  ACC_OPENING_APPROVE:k=>{
    const {s,assert,uid}=k;owner(k,'approves');
    const o=accOpening(s);assert(o,'No opening balances have been entered yet.','NOT_FOUND');
    assert(o!.status==='draft','The opening balances are already approved.','INVALID_TRANSITION');
    assert(o!.lines.some(l=>l.account!==ACC_OPENING_DIFF),'Enter at least one opening line.');
    const t=accOpeningTotals(o);assert(t.debit===t.credit,'The opening entry does not balance.');
    const x=slice(k);x.opening={...o!,status:'approved',approvedBy:uid,approvedAt:s.clock};
    return `Opening balances as of ${o!.asOf} approved · ${money(t.debit)} Dr = ${money(t.credit)} Cr${diffText(o!.lines)}`;
  },
  ACC_OPENING_REOPEN:k=>{
    const {s,cmd,assert}=k;owner(k,'reopens');
    const o=accOpening(s);assert(o,'No opening balances have been entered yet.','NOT_FOUND');
    assert(o!.status==='approved','The opening balances are not approved.','INVALID_TRANSITION');
    const reason=String(cmd.reason??'').trim();assert(reason.length>=3,'A reason is required to reopen the opening balances.');
    assert(reason.length<=200,'Keep the reason to 200 characters.');
    const locked=accOpeningLocksFrom(s,o!.asOf);
    assert(!locked.length,`A period is locked on or after ${o!.asOf} (${locked[0]}): correct the opening balances with an Owner-approved journal instead.`,'INVALID_TRANSITION');
    const x=slice(k),next={...o!,status:'draft' as const};delete next.approvedBy;delete next.approvedAt;x.opening=next;
    return `Opening balances as of ${o!.asOf} reopened · ${reason}`;
  },
};
