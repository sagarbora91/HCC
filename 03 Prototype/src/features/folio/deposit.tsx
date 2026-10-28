// ACC-2 · acc2-ui — Apply deposit at check-out (contract §4 A6a): the advance deposit held for the stay is applied against the folio balance
// (DEPOSIT_APPLY {booking, amount}: Dr 2000 advance deposits · Cr 1100 guest ledger). Shown in the desktop checkout dialog and the phone checkout sheet.
// Screens only; the command checks the shift, the amount and the (optional) GST-on-advances reversal.
import React,{useState} from 'react';
import {Wallet} from 'lucide-react';
import {money,totals,cmpSlice,chnSlice} from '../../services/mock';
import type {Booking} from '../../services/mock';
import {useHcc} from '../../app/context';
import {toPaise,fromPaise} from '../accounting/shared';
import type {DepositApply} from '../accounting/_contract-stubs-2';

/** OTA-paid stay (contract §4, DEC-150): at check-out the folio balance can move to the channel's receivable — `CHECKOUT {otaCredit:true}`, allowed only for a
 *  booking from a connected channel. Returns the checkbox panel and whether it is ticked (call it before any early return; the check-out button adds the flag). */
export function useOtaCredit(b:Booking|undefined,mobile?:boolean){
  const {s}=useHcc();const [on,setOn]=useState(false);
  const ch=b?.otaChannel?chnSlice(s).channels.find(c=>c.name===b.otaChannel):undefined,t=b?totals(b):undefined;
  const eligible=!!b&&!!ch&&ch.status==='Connected'&&b.status==='In house'&&!!t&&t.balance>0;
  const node=eligible?<div className={'info-note folio-ota'+(mobile?' mm-cc':'')}><Wallet size={mobile?18:16} aria-hidden/><div><strong>Paid by the channel · {ch!.name}</strong>
    <p>{ch!.name} pays the hotel later, net of its commission, against a statement. Ticking this moves the {money(t!.balance)} balance to the channel’s receivable when you confirm check-out; the payout is recorded under OTA channels › Payouts.</p>
    <label className="checkbox-line"><input type="checkbox" checked={on} onChange={e=>setOn(e.target.checked)}/> Move the balance to {ch!.name}’s receivable at check-out</label></div></div>:null;
  return {on:eligible&&on,node};
}
export function ApplyDepositPanel({b,mobile}:{b:Booking;mobile?:boolean}){
  const {s,run,busy,permission,user}=useHcc();const t=totals(b);const [src,setSrc]=useState<'receipts'|'opening'>('receipts');const lead=user.role==='Owner'||user.role==='Manager';
  const max=Math.min(t.deposit,Math.max(t.balance,0));const [amount,setAmount]=useState(fromPaise(max));
  if(b.status!=='In house'||t.deposit<=0||t.balance<=0)return null;
  const paise=toPaise(amount),gstAdv=cmpSlice(s).profile?.gstOnAdvances===true;
  const why=!permission('payments')?'Your role cannot apply a deposit — reception or a cashier does.':!s.shiftOpen?'A cash shift must be open to apply a deposit.':paise<=0?'Enter an amount.':paise>max?`At most ${money(max)} (the smaller of the deposit held and the balance due).`:'';
  return <div className={'info-note folio-deposit'+(mobile?' mm-cc':'')}><Wallet size={mobile?18:16} aria-hidden/><div>
    <strong>Apply the deposit</strong>
    <p>{money(t.deposit)} deposit is held for this stay and {money(t.balance)} is due. Applying it moves the amount from advance deposits to the guest’s bill{gstAdv?' and reverses the GST booked on the advance':''}; no money changes hands.</p>
    <div className={mobile?'grp-link grp-link-m':'grp-link'}>{lead&&<label>Deposit taken from<select value={src} onChange={e=>setSrc(e.target.value==='opening'?'opening':'receipts')}><option value="receipts">This stay’s own receipts</option><option value="opening">Opening balance carried in (Manager / Owner)</option></select></label>}
    <label>Amount to apply (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)}/></label>
      <button type="button" className={mobile?'btn':'btn small-btn'} disabled={busy||!!why} onClick={()=>void run({type:'DEPOSIT_APPLY',booking:b.id,amount:paise,...(src==='opening'?{source:'opening' as const}:{}),key:`depapply:${b.id}:${b.version}:${paise}:${src}`} satisfies DepositApply,`${money(paise)} of the deposit applied`)}>Apply deposit</button></div>
    {why&&<small className="muted" role="status">{why}</small>}</div></div>;
}
