// ACC-4 · Year lock, carry-forward, reopen — command handlers (ACC4-CONTRACT.md §1.2; PERM-ACC-014 Owner + Accountant, PERM-ACC-015 Owner).
// Contract error codes travel as the message prefix (ACC_CF_UNBALANCED, ACC_YEAR_NOT_READY, ACC_YEAR_REOPEN_BLOCKED, ACC_YEAR_LOCKED) on the
// prototype's ErrorCode (VALIDATION / INVALID_TRANSITION); the server returns them as codes. Production: clients/sync (integrator).
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {accSlice} from '../../../domain/streams/accounting';
import {accYearSlice,accYearOf,accFyValid,accFyBounds,accFyNext,accYearPreview,accYearCarryLines,accYearLatestLocked,accCarryForward,
  ACC_YEAR_FIRST,ACC_YEAR_REASON_MIN,type AccYearSlice,type AccYearState,type AccCarryForward} from '../../../domain/streams/accounting-year';

const PREVIEW_ROLES=['Owner','Accountant'];
const slice=(k:Kit):AccYearSlice=>{const x=accYearSlice(k.s);k.s.accYearX={years:[...x.years],carryForwards:[...x.carryForwards]};return k.s.accYearX;};
const fyOf=(k:Kit)=>{const fy=String(k.cmd.fy??'');k.assert(accFyValid(fy),'Choose a financial year like 2026-27.');
  k.assert(fy>=ACC_YEAR_FIRST,`The books start in ${ACC_YEAR_FIRST} (DEC-148): there is no earlier HCC year.`);return fy;};
const preview=(k:Kit)=>{if(!PREVIEW_ROLES.includes(k.user.role))k.fail('FORBIDDEN','Access denied: only the Owner or the Accountant prepares the year end (PERM-ACC-014).');};
const owner=(k:Kit,what:string)=>{if(k.user.role!=='Owner')k.fail('FORBIDDEN',`Access denied: only the Owner ${what} a financial year (PERM-ACC-015).`);};
const setYear=(x:AccYearSlice,y:AccYearState)=>{const i=x.years.findIndex(v=>v.fy===y.fy);if(i<0)x.years.push(y);else x.years[i]=y;};
const notReady=(k:Kit,fy:string)=>{const p=accYearPreview(k.s,fy),bad=p.checks.filter(c=>!c.ok);
  if(bad.length)k.fail('INVALID_TRANSITION',`ACC_YEAR_NOT_READY: ${bad.map(c=>c.label+(c.detail?` (${c.detail})`:'')).join('; ')}`);return p;};
const sameLines=(a:AccCarryForward['lines'],b:AccCarryForward['lines'])=>JSON.stringify(a)===JSON.stringify(b);
const nextMonthLocked=(k:Kit,fy:string)=>{const {from,to}=accFyBounds(accFyNext(fy));return Object.keys(accSlice(k.s).monthLocks??{}).filter(m=>m>=from.slice(0,7)&&m<=to.slice(0,7)).sort();};

export const handlers:Record<string,Handler>={
  /** Read: the checklist and the draft lines (nothing saved). */
  ACC_YEAR_PREVIEW:k=>{preview(k);const fy=fyOf(k),p=accYearPreview(k.s,fy);
    return `Year ${fy} preview · ${p.ready?'ready to lock':`${p.checks.filter(c=>!c.ok).length} check(s) open`} · carry-forward ${money(p.draft.debit)} Dr = ${money(p.draft.credit)} Cr`;},
  ACC_YEAR_CARRY_FORWARD:k=>{
    preview(k);const fy=fyOf(k),{s,uid,id}=k;const y=accYearOf(s,fy);
    k.assert(y.status!=='locked',`Financial year ${fy} is locked.`,'INVALID_TRANSITION');
    const {from,to}=accFyBounds(fy),d=accYearCarryLines(s,from,to);
    if(!d.balanced)k.fail('VALIDATION',`ACC_CF_UNBALANCED: the carry-forward lines total ${money(d.debit)} Dr and ${money(d.credit)} Cr.`);
    const x=slice(k);x.carryForwards=x.carryForwards.filter(c=>!(c.fromFy===fy&&c.status==='draft'));
    const cf:AccCarryForward={id:id('CF'),fromFy:fy,toFy:accFyNext(fy),status:'draft',lines:d.lines,profitToCapital:d.profitToCapital,drawingsToCapital:d.drawingsToCapital,
      generatedAt:s.clock,generatedBy:uid,close:d.close};
    x.carryForwards.push(cf);if(y.status==='open')setYear(x,{...y,status:'closing'});
    return `Carry-forward ${cf.id} ${fy} → ${cf.toFy} drafted · ${d.lines.length} line(s) · ${money(d.debit)} Dr = ${money(d.credit)} Cr · profit ${money(d.profitToCapital)} and drawings ${money(d.drawingsToCapital)} to 3100`;
  },
  ACC_YEAR_LOCK:k=>{
    owner(k,'locks');const fy=fyOf(k),{s,uid}=k;const y=accYearOf(s,fy);
    k.assert(y.status!=='locked',`Financial year ${fy} is already locked.`,'INVALID_TRANSITION');
    k.assert(y.status!=='reopened',`Financial year ${fy} was reopened — relock it (ACC_YEAR_RELOCK).`,'INVALID_TRANSITION');
    const prev=fy>ACC_YEAR_FIRST?accYearOf(s,`${+fy.slice(0,4)-1}-${fy.slice(2,4)}`):undefined;
    if(prev&&prev.status!=='locked')k.fail('INVALID_TRANSITION',`Lock ${prev.fy} first.`);
    const p=notReady(k,fy);
    const cf=accCarryForward(s,String(k.cmd.carryForwardId??''));
    k.assert(cf&&cf.fromFy===fy&&cf.status==='draft','Draft the carry-forward of this year first (ACC_YEAR_CARRY_FORWARD).','NOT_FOUND');
    if(!sameLines(cf!.lines,p.draft.lines))k.fail('INVALID_TRANSITION','The books changed after the carry-forward was drafted — draft it again.');
    const x=slice(k);x.carryForwards=x.carryForwards.map(c=>c.id===cf!.id?{...c,status:'approved',approvedBy:uid,approvedAt:s.clock}:c);
    setYear(x,{...y,status:'locked',lockedAt:s.clock,lockedBy:uid,carryForwardId:cf!.id,provisionalExports:false,history:[...(y.history??[]),{at:s.clock,by:uid,action:'lock'}]});
    return `Financial year ${fy} locked · carry-forward ${cf!.id} approved as the ${cf!.toFy} opening (source carry-forward) · ${money(p.draft.debit)} Dr = ${money(p.draft.credit)} Cr`;
  },
  ACC_YEAR_REOPEN:k=>{
    owner(k,'reopens');const fy=fyOf(k),{s,uid}=k;const y=accYearOf(s,fy);
    k.assert(y.status==='locked',`Financial year ${fy} is not locked.`,'INVALID_TRANSITION');
    const reason=String(k.cmd.reason??'').trim();k.assert(reason.length>=ACC_YEAR_REASON_MIN,`Give a reason of at least ${ACC_YEAR_REASON_MIN} characters (DEC-077).`);
    k.assert(reason.length<=400,'Keep the reason to 400 characters.');
    if(accYearLatestLocked(s)!==fy)k.fail('INVALID_TRANSITION',`ACC_YEAR_REOPEN_BLOCKED: only the latest locked year (${accYearLatestLocked(s)}) can be reopened.`);
    const locked=nextMonthLocked(k,fy);
    if(locked.length)k.fail('INVALID_TRANSITION',`ACC_YEAR_REOPEN_BLOCKED: ${accFyNext(fy)} has a locked month (${locked[0]}).`);
    const x=slice(k);setYear(x,{...y,status:'reopened',reopenedAt:s.clock,reopenedBy:uid,reopenReason:reason,provisionalExports:true,history:[...(y.history??[]),{at:s.clock,by:uid,action:'reopen',reason}]});
    return `Financial year ${fy} reopened · exports of ${fy} and ${accFyNext(fy)} are PROVISIONAL until relocked · issued invoice and credit-note numbers do not change · ${reason}`;
  },
  ACC_YEAR_RELOCK:k=>{
    owner(k,'relocks');const fy=fyOf(k),{s,uid,id}=k;const y=accYearOf(s,fy);
    k.assert(y.status==='reopened',`Financial year ${fy} is not reopened.`,'INVALID_TRANSITION');
    const p=notReady(k,fy);
    if(!p.draft.balanced)k.fail('VALIDATION',`ACC_CF_UNBALANCED: the carry-forward lines total ${money(p.draft.debit)} Dr and ${money(p.draft.credit)} Cr.`);
    const x=slice(k),nid=id('CF'),old=y.carryForwardId;
    x.carryForwards=x.carryForwards.filter(c=>!(c.fromFy===fy&&c.status==='draft')).map(c=>c.id===old?{...c,status:'superseded',supersededBy:nid}:c);
    x.carryForwards.push({id:nid,fromFy:fy,toFy:accFyNext(fy),status:'approved',lines:p.draft.lines,profitToCapital:p.draft.profitToCapital,drawingsToCapital:p.draft.drawingsToCapital,
      generatedAt:s.clock,generatedBy:uid,approvedBy:uid,approvedAt:s.clock,close:p.draft.close});
    setYear(x,{...y,status:'locked',lockedAt:s.clock,lockedBy:uid,carryForwardId:nid,provisionalExports:false,history:[...(y.history??[]),{at:s.clock,by:uid,action:'relock'}]});
    // DEC-077: the regenerated fy+1 opening is a changed date for Tally — the next export carries it again.
    const d=accFyBounds(accFyNext(fy)).from,a={...accSlice(s)};a.reexport={...(a.reexport??{}),[d]:{at:s.clock,reason:`Year ${fy} relocked: opening regenerated`}};k.s.accountingX=a;
    return `Financial year ${fy} relocked · carry-forward ${nid} replaces ${old??'—'} · the ${accFyNext(fy)} opening is regenerated (no delta journal)`;
  },
};
