// ACC-2 · acc2-ui — purchase fields of the expense form (contract §3): GST kind intra / inter (IGST), reverse charge (category flag, editable rate), capital
// purchase (asset ledger and register row), the DEC-143 ITC guard, the TDS held back (§5) and the cash-guard note (§7). Shared by the desktop dialog and the
// phone sheet (both render ExpenseSccFields). Screens only: the flags come from the category / vendor masters, the posting from the purchase lane.
import React from 'react';
import {Info,Landmark,Repeat} from 'lucide-react';
import {money,mnyCategory,mnyVendor,mnyIsDrawer,accLedgerName} from '../../services/mock';
import type {State} from '../../services/mock';
import {useHcc} from '../../app/context';
import {expPurchaseItcGuard,tdsForBill,accCashGuard} from '../accounting/_contract-stubs-2';
import {CashGuardNote} from '../accounting/cashguard';

export type PurchaseForm={category:string;vendor:string;method:string;date:string;itc:'allowed'|'disallowed';gstKind:'intra'|'inter';rcmBps:string};
/** What the purchase fields add to the add-expense command (nothing for an ordinary expense, so old records and payloads stay as they were). */
export function purchaseCommand(s:State,f:PurchaseForm,gstPaise:number){
  const c=mnyCategory(s,f.category);const out:Record<string,unknown>={};
  if(gstPaise>0&&!c?.rcm)out.gstKind=f.gstKind;
  if(c?.rcm){const b=Math.round(Number(f.rcmBps||(c.rcmBps??0)/100)*100);if(b>0)out.rcmBps=b;}
  return out;
}
/** First problem of the purchase fields ('' = none): the DEC-143 ITC guard. */
export function purchaseProblem(s:State,f:PurchaseForm,gstPaise:number,gstBps:number):string{
  return gstPaise>0?expPurchaseItcGuard(s,{category:f.category,itc:f.itc,gstBps:gstBps||undefined}):'';
}
/** Default GST kind for a vendor: inter-state when its two-letter state code is not MH. */
export const vendorGstKind=(s:State,vendor:string):'intra'|'inter'=>{const st=mnyVendor(s,vendor)?.state;return st&&st!=='MH'?'inter':'intra';};

export function ExpensePurchaseFields({f,setF,paise,gst}:{f:PurchaseForm&Record<string,unknown>;setF:(v:never)=>void;paise:number;gst:number}){
  const {s}=useHcc();const c=mnyCategory(s,f.category),v=f.vendor?mnyVendor(s,f.vendor):undefined;
  const set=(p:Partial<PurchaseForm>)=>setF({...f,...p} as never);
  const hasGst=Number.isFinite(gst)&&gst>0;
  const tds=v?.tds&&paise>0?tdsForBill(s,{vendor:f.vendor,amount:paise,gstPaise:hasGst?gst:0,date:f.date}):0;
  const guard=f.vendor.trim()&&mnyIsDrawer(s,f.method)&&paise>0?accCashGuard(s,{kind:'payment',party:f.vendor.trim(),amount:paise,date:f.date}):undefined;
  const itcMsg=hasGst?expPurchaseItcGuard(s,{category:f.category,itc:f.itc,gstBps:undefined}):'';
  return <>
    {c?.rcm&&<div className="info-note exp-purchase" role="status"><Repeat size={16} aria-hidden/><div><strong>Reverse charge (category “{f.category}”).</strong>
      <p>The bill is posted without the vendor’s GST; the hotel adds its own GST to “GST payable under reverse charge” (2140), paid by a GST payment — never by set-off.</p>
      <label>Reverse-charge GST rate (%)<input inputMode="decimal" value={f.rcmBps} placeholder={String((c.rcmBps??0)/100)} onChange={e=>set({rcmBps:e.target.value})}/></label></div></div>}
    {!c?.rcm&&hasGst&&<label>Where is the vendor?<select value={f.gstKind} onChange={e=>set({gstKind:e.target.value==='inter'?'inter':'intra'})}>
      <option value="intra">Within Maharashtra — CGST + SGST on the bill</option><option value="inter">Outside Maharashtra — IGST on the bill</option></select>
      {v?.state&&<small className="muted">Vendor state on file: {v.state}.</small>}
      {f.gstKind==='inter'&&<small className="muted">The whole GST goes to Input IGST (1330) when ITC is allowed.</small>}</label>}
    {c?.capital&&<div className="info-note exp-purchase" role="status"><Landmark size={16} aria-hidden/><div><strong>Fixed-asset purchase.</strong>
      <p>The cost goes to {c.ledger?`${c.ledger} · ${accLedgerName(c.ledger)}`:'the category’s asset ledger'}, not to expenses, reports or budgets. A fixed-asset register row (FA-year-number) is created when it posts; depreciation is worked in Tally.</p></div></div>}
    {c?.itcBlocked&&<div className="info-note" role="status"><Info size={16} aria-hidden/><span>Input credit is blocked on this category (section 17(5), DEC-143): keep ITC disallowed.</span></div>}
    {itcMsg&&<p className="f-why" role="alert">{itcMsg}</p>}
    {v?.tds&&<div className="info-note" role="status"><Info size={16} aria-hidden/><span>TDS {v.tdsSection?`(${v.tdsSection})`:''} applies to {f.vendor}: the vendor is credited net of TDS{tds>0?` — about ${money(tds)} held back`:''}; the rate and threshold come from Accounting › TDS.</span></div>}
    {guard&&<CashGuardNote verdict={guard}/>}
  </>;
}
