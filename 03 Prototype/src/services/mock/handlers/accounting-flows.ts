// ACC-2 flows · command handlers (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4, §8; rulebook §5 A6a, A17b). Return value = audit text.
//   DEPOSIT_APPLY {booking, amount}             apply the refundable deposit held to the folio at check-out (A6a): receipt kind
//                                                'deposit-apply' → Journal Dr 2000 · Cr 1100 (replaces the rulebook scenario's "Advance
//                                                adjusted" method). Payments permission (Reception / Cashier on their own open shift;
//                                                Owner / Manager too); the stay is in house; ≤ the deposit held and ≤ the balance due.
//   EXP_UD_PAY_REVERSE {receivable, settlement, reason}
//                                                Owner: reverse a mistaken Udhaar desk payment (A17b) — a negative settlement dated the
//                                                business date (the same day: the day nets to zero; a later day: the reversing entry), so
//                                                the outstanding comes back and the books post a negative Receipt (Dr 1120 · Cr tender).
//                                                Cash leaves the open shift's drawer (open shift, open cash-book day).
// The OTA payout (CHN_PAYOUT) lives in handlers/channels.ts (OTA module switch); the OTA credit at check-out in handlers/folio.ts (CHECKOUT).
import type {Handler,Kit} from '../commands';
import type {Booking} from '../../../domain/types';
import {money} from '../../../domain/money';
import {totals} from '../../../domain/selectors';
import {cbIsLocked,cbLockedMessage} from '../../../domain/streams/cashbook';
import {cxCashBlocked} from '../../../domain/streams/cashout';
import {mnyIsDrawer} from '../../../domain/streams/money-master';
import {expSlice,expUdOut,type EXPState} from '../../../domain/streams/expenses-scc';

const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const bookingOf=(k:Kit):Booking=>{const id=String(k.cmd.booking??k.cmd.id??'');const x=k.s.bookings.find(b=>b.id===id);k.assert(x,'Booking not found.','NOT_FOUND');return x!;};
const expenses=(k:Kit):EXPState=>{const x={...expSlice(k.s)};k.s.expensesX=x;return x;};

export const handlers:Record<string,Handler>={
  DEPOSIT_APPLY:k=>{
    const {s,cmd,req,open,assert,fail,positive,id,uid}=k;
    req('payments');open();const x=bookingOf(k);
    assert(x.status==='In house','Apply the deposit to an in-house stay at check-out.','INVALID_TRANSITION');
    positive(cmd.amount);
    const t=totals(x);
    assert(t.deposit>0,'This folio holds no refundable deposit.','INVALID_TRANSITION');
    assert(cmd.amount<=t.deposit,`Apply at most the deposit held (${money(t.deposit)}).`);
    assert(t.balance>0,'Nothing is due on this folio — return the deposit instead.','INVALID_TRANSITION');
    assert(cmd.amount<=t.balance,`Apply at most the balance due (${money(t.balance)}); return the rest of the deposit.`);
    // Contract §4: Reception / Cashier on an open shift (their own, PERM-PAY-001 scope S) — the same rule as a folio receipt.
    assert(s.shiftOpen,s.closedDates.includes(s.date)?'The cash shift is closed. Open the next business day first.':'The cash shift is closed. Open a cashier shift first.','INVALID_TRANSITION');
    {const own=cxCashBlocked(s,uid);if(own)fail('FORBIDDEN',own.replace('Cash needs','Applying a deposit needs'));}
    const r={id:id('RCPT'),amount:cmd.amount as number,method:'Deposit applied',date:s.date,kind:'deposit-apply' as const,reference:'Deposit applied at check-out',actor:uid};
    x.receipts.push(r);x.version++;
    const after=totals(x);
    return `${x.id} · deposit ${money(r.amount)} applied to the folio (Dr advance deposits · Cr guest ledger) · deposit held ${money(after.deposit)} · balance ${money(after.balance)}`;
  },
  EXP_UD_PAY_REVERSE:k=>{
    const {s,cmd,open,assert,fail,id,uid,user}=k;
    if(user.role!=='Owner')fail('FORBIDDEN','Only the Owner reverses a mistaken Udhaar payment (rulebook A17b).');
    open();
    const x=expenses(k),r=(x.udhaar??[]).find(u=>u.id===String(cmd.receivable??cmd.id??''));if(!r)fail('NOT_FOUND','Credit not found.');
    const p=r!.settlements.find(z=>z.id===String(cmd.settlement??''));if(!p)fail('NOT_FOUND','Payment not found on this credit.');
    assert(!p!.reversalOf,'A reversal cannot itself be reversed.','INVALID_TRANSITION');
    assert(!p!.reversedBy,`This payment is already reversed (${p!.reversedBy}).`,'INVALID_TRANSITION');
    assert(!r!.void,'This credit was written off — a payment on it cannot be reversed.','INVALID_TRANSITION');
    const reason=txt(cmd.reason);assert(reason.length>=3,'Give the reason for the reversal (at least 3 characters).');assert(reason.length<=200,'Keep the reason to 200 characters.');
    const cash=!!p!.desk&&mnyIsDrawer(s,p!.mode);
    if(cash){
      assert(s.shiftOpen,'Open cash shift required: the reversed Udhaar cash leaves the open shift’s drawer.','INVALID_TRANSITION');
      const own=cxCashBlocked(s,uid);if(own)fail('FORBIDDEN',own);
      assert(!cbIsLocked(s,s.date),cbLockedMessage(s.date,'cash out of the drawer'),'DAY_CLOSED');
    }
    const rid=id('UDP'),same=p!.date===s.date;
    const rev={id:rid,amount:-p!.amount,date:s.date,mode:p!.mode,by:uid,at:s.clock,...(p!.desk?{desk:true as const}:{}),reference:'Reversal of '+p!.id,reversalOf:p!.id,reason};
    x.udhaar=x.udhaar!.map(u=>u.id===r!.id?{...u,settlements:[...u.settlements.map(z=>z.id===p!.id?{...z,reversedBy:rid}:z),rev]}:u);
    const after=x.udhaar.find(u=>u.id===r!.id)!;
    return `${r!.id} · ${r!.name} · payment ${p!.id} ${money(p!.amount)} reversed by ${rid}${same?' the same day':' (reversing entry dated '+s.date+')'} · ${cash?'cash out of the drawer · ':''}outstanding ${money(expUdOut(after))} · ${reason}`;
  },
};
