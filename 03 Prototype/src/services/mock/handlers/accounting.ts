// Module 20 · Accounting automation — command handlers. Command types MUST start with 'ACC_'. Return value = audit/summary text.
import type {Handler,Kit} from '../commands';
import {money} from '../../../domain/money';
import {rbacMoneyAdmin} from '../../../domain/streams/rbac';
import {accSlice,accLiveVouchers,accTotals,accLockBlocker,accMapping,accAccount,accMapRows,ACC_DEFAULT_MAP,ACC_KEY_PATTERN,ACC_SUSPENSE,ACC_LOCK_ROLES,accLedgerName,type ACCState} from '../../../domain/streams/accounting';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
/** Writable slice on the cloned state (creates it for old saves). */
const slice=(k:Kit):ACCState=>{k.s.accountingX={...accSlice(k.s)};return k.s.accountingX;};

export const handlers:Record<string,Handler>={
  // Lock a closed business date: freeze its derived vouchers as a snapshot.
  ACC_LOCK:k=>{
    const {s,cmd,req,assert,fail,uid,user,id}=k;req('reports');
    if(!ACC_LOCK_ROLES.includes(user.role))fail('FORBIDDEN','Access denied: only Owner, Manager or Accountant can lock an accounting period.');
    assert(typeof cmd.date==='string'&&DATE.test(cmd.date),'Choose a business date to lock.');
    const why=accLockBlocker(s,cmd.date);
    if(why)fail(why.startsWith('Resolve')?'VALIDATION':'INVALID_TRANSITION',why);
    const vouchers=accLiveVouchers(s,cmd.date),t=accTotals(vouchers);
    assert(t.debit===t.credit,'Debits and credits do not agree.');
    const x=slice(k);
    x.locks={...(x.locks??{}),[cmd.date]:{date:cmd.date,lockedAt:s.clock,lockedBy:uid,vouchers,debit:t.debit,credit:t.credit,count:vouchers.length}};
    x.lockHistory=[...(x.lockHistory??[]),{id:id('ACCL'),at:s.clock,actor:uid,date:cmd.date,action:'lock'}];
    return `Accounting period ${cmd.date} locked · ${vouchers.length} voucher(s) · ${money(t.debit)} Dr = ${money(t.credit)} Cr`;
  },
  // Owner-only: return a locked date to live derivation (reason required).
  ACC_UNLOCK:k=>{
    const {s,cmd,req,assert,uid,id}=k;req('reports');req('admin');
    assert(typeof cmd.date==='string'&&accSlice(s).locks?.[cmd.date],'This date is not locked.','INVALID_TRANSITION');
    const reason=String(cmd.reason??'').trim();assert(reason.length>=3,'A reason is required to unlock a period.');
    const x=slice(k),locks={...(x.locks??{})};delete locks[cmd.date];x.locks=locks;
    x.lockHistory=[...(x.lockHistory??[]),{id:id('ACCL'),at:s.clock,actor:uid,date:cmd.date,action:'unlock',reason}];
    return `Accounting period ${cmd.date} unlocked · ${reason}`;
  },
  // Change the ledger for one source classification (affects unlocked dates only).
  ACC_MAP_SET:k=>{
    // DEC-063 G11: ledger mapping is a money decision — Owner only (admin + reports), never the Tech admin.
    const {s,cmd,assert,uid,id,fail}=k;if(!rbacMoneyAdmin(s,uid))fail('FORBIDDEN','Access denied: only the Owner changes the ledger mapping (DEC-063).');
    const key=String(cmd.source??'');
    assert(ACC_KEY_PATTERN.test(key)&&(key in ACC_DEFAULT_MAP||accMapRows(s).some(r=>r.key===key)),'Unknown source classification.','NOT_FOUND');
    const acct=accAccount(String(cmd.account??''));
    assert(acct&&acct.code!==ACC_SUSPENSE,'Choose a ledger from the chart of accounts (not Suspense).');
    const before=accMapping(s)[key]??'';
    assert(before!==acct!.code,'This classification already maps to that ledger.','INVALID_TRANSITION');
    const x=slice(k);x.mapping={...(x.mapping??{}),[key]:acct!.code};
    x.mapHistory=[...(x.mapHistory??[]),{id:id('ACCM'),at:s.clock,actor:uid,key,before,after:acct!.code}];
    return `Mapping ${key} → ${acct!.code} ${acct!.name}${before?' (was '+before+' '+accLedgerName(before)+')':''} · locked periods unchanged`;
  },
  ACC_MAP_RESET:k=>{
    const {s,cmd,assert,uid,id,fail}=k;if(!rbacMoneyAdmin(s,uid))fail('FORBIDDEN','Access denied: only the Owner changes the ledger mapping (DEC-063).');
    const key=String(cmd.source??''),x=accSlice(s);
    assert(x.mapping&&key in x.mapping,'This classification already uses its default ledger.','INVALID_TRANSITION');
    const before=x.mapping![key],w=slice(k),m={...(w.mapping??{})};delete m[key];w.mapping=m;
    const after=ACC_DEFAULT_MAP[key]??'';
    w.mapHistory=[...(w.mapHistory??[]),{id:id('ACCM'),at:s.clock,actor:uid,key,before,after}];
    return `Mapping ${key} reset to default${after?' '+after+' '+accLedgerName(after):' (unmapped)'}`;
  },
};
