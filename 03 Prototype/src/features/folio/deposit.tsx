// ACC-2 · acc2-ui — Apply deposit at check-out (contract §4 A6a): the advance deposit held for the stay is applied against the folio balance
// (DEPOSIT_APPLY {booking, amount}: Dr 2000 advance deposits · Cr 1100 guest ledger). Shown in the desktop checkout dialog and the phone checkout sheet.
// Screens only; the command checks the shift, the amount and the (optional) GST-on-advances reversal.
import React,{useState} from 'react';
import {Wallet} from 'lucide-react';
import {money,totals,cmpSlice} from '../../services/mock';
import type {Booking} from '../../services/mock';
import {useHcc} from '../../app/context';
import {toPaise,fromPaise} from '../accounting/shared';
import type {DepositApply} from '../accounting/_contract-stubs-2';

export function ApplyDepositPanel({b,mobile}:{b:Booking;mobile?:boolean}){
  const {s,run,busy,permission}=useHcc();const t=totals(b);
  const max=Math.min(t.deposit,Math.max(t.balance,0));const [amount,setAmount]=useState(fromPaise(max));
  if(b.status!=='In house'||t.deposit<=0||t.balance<=0)return null;
  const paise=toPaise(amount),gstAdv=cmpSlice(s).profile?.gstOnAdvances===true;
  const why=!permission('payments')?'Your role cannot apply a deposit — reception or a cashier does.':!s.shiftOpen?'A cash shift must be open to apply a deposit.':paise<=0?'Enter an amount.':paise>max?`At most ${money(max)} (the smaller of the deposit held and the balance due).`:'';
  return <div className={'info-note folio-deposit'+(mobile?' mm-cc':'')}><Wallet size={mobile?18:16} aria-hidden/><div>
    <strong>Apply the deposit</strong>
    <p>{money(t.deposit)} deposit is held for this stay and {money(t.balance)} is due. Applying it moves the amount from advance deposits to the guest’s bill{gstAdv?' and reverses the GST booked on the advance':''}; no money changes hands.</p>
    <div className={mobile?'grp-link grp-link-m':'grp-link'}><label>Amount to apply (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)}/></label>
      <button type="button" className={mobile?'btn':'btn small-btn'} disabled={busy||!!why} onClick={()=>void run({type:'DEPOSIT_APPLY',booking:b.id,amount:paise,key:`depapply:${b.id}:${b.version}:${paise}`} satisfies DepositApply,`${money(paise)} of the deposit applied`)}>Apply deposit</button></div>
    {why&&<small className="muted" role="status">{why}</small>}</div></div>;
}
