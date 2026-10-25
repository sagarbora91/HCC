// W05-D · Cash book day lock (checker) and owner reopen. The cash figures themselves are never stored: see domain/streams/cashbook.ts.
import type {Handler} from '../commands';
import {cbSlice,cbCanLock,cbCanReopen,CB_REOPEN_ROLES} from '../../../domain/streams/cashbook';

const date=(k:Parameters<Handler>[0])=>{const d=String(k.cmd.date||'');k.assert(/^\d{4}-\d{2}-\d{2}$/.test(d),'Choose a business date.');return d;};
export const handlers:Record<string,Handler>={
  // Checker locks a closed day: must be a different person from whoever counted / closed it.
  CB_LOCK:k=>{
    const {s,uid,req,fail}=k;req('reports');const d=date(k);
    const c=cbCanLock(s,uid,d);if(!c.ok)fail(/different person|Manager|only the Owner/.test(c.why!)?'FORBIDDEN':'INVALID_TRANSITION',c.why!);
    const x=cbSlice(s),entry={action:'lock' as const,by:uid,at:s.clock};
    s.cashbookX={locks:{...x.locks,[d]:{lockedBy:uid,lockedAt:s.clock,log:[entry]}},history:{...x.history,[d]:[...(x.history[d]??[]),entry]}};
    return `Cash book ${d} locked`;},
  // Only an Owner reopens a locked day, with a reason (kept in the audit trail and the day's history).
  CB_REOPEN:k=>{
    const {s,uid,user,cmd,req,assert,fail}=k;req('reports');const d=date(k);
    if(!CB_REOPEN_ROLES.includes(user.role))fail('FORBIDDEN','Only the Owner can reopen a locked cash book day.');
    assert(cbCanReopen(s,uid,d),'This day is not locked.','INVALID_TRANSITION');
    const reason=String(cmd.reason||'').trim();assert(reason.length>=3,'A reason is required to reopen a locked day.');assert(reason.length<=200,'Reason: maximum 200 characters.');
    const x=cbSlice(s),entry={action:'reopen' as const,by:uid,at:s.clock,reason},locks={...x.locks};delete locks[d];
    s.cashbookX={locks,history:{...x.history,[d]:[...(x.history[d]??[]),entry]}};
    return `Cash book ${d} reopened · ${reason}`;},
};
