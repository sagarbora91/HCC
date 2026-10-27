// MA-3 · Money masters on module screens (plan Part 2 §8 M15–M18). The lists themselves are edited in Master configuration ›
// Money and tax › Lists (MA-0 MasterList / phone sheet); this file has the pick-list pieces the money screens use:
//   ExpCategorySelect   expense category from the expenseCategories master ("needs a bill" hint)
//   ExpFundingOptions   funding sources = payment methods usable for expenses (desktop select options / phone chips)
//   ExpVendorInput      vendor from the expenseVendors master (datalist; DEC-MA-6 separate from maintenance vendors); optional, and a new
//                       name joins the list when the expense is saved (DEC-120, SCC auto-add)
//   MnySettlesTo        cash book / bank view: money in and out grouped by "settles to" account (till, bank accounts, credit)
// Phone and desktop share these (inputs and cards: 48 px targets via the form styles, no tables, no sideways scroll at 360 px).
import React,{useId,useMemo} from 'react';
import {Landmark,Wallet,CreditCard} from 'lucide-react';
import {money,mnyNames,mnyShortLabel,mnyIsDrawer,mnyIsCredit,mnyCategoryNames,mnyCategory,mnyVendor,mnyVendorNames,mnySettlement,mnyMasked,
  mnyAccounts,MNY_EV} from '../../services/mock';
import {useHcc} from '../../app/context';
import {MstLink} from '../masters/MasterList';

/** Funding sources for expenses: [stored value, label] (seed: Cash → Drawer cash, UPI, Bank transfer, Vendor credit (pay later)). */
export function mnyExpenseSources(s:Parameters<typeof mnyNames>[0],phone=false):[string,string][]{
  return mnyNames(s,'expenses').map(m=>[m,mnyIsDrawer(s,m)?'Drawer cash':mnyIsCredit(s,m)?(phone?m:`${m} (pay later)`):phone?mnyShortLabel(s,m):m]);
}
/** First funding source to preselect (the drawer method, else the first). */
export const mnyFirstSource=(s:Parameters<typeof mnyNames>[0])=>{const l=mnyNames(s,'expenses');return l.find(m=>mnyIsDrawer(s,m))??l[0]??'';};
/** "Funding with no drawer effect" note for a credit method. */
export function ExpCreditNote({method}:{method:string}){
  const {s}=useHcc();
  return mnyIsCredit(s,method)?<div className="info-note"><Wallet size={16}/>{method} records the expense without any cash drawer effect.</div>:null;
}

export function ExpCategorySelect({value,onChange}:{value:string;onChange:(v:string)=>void}){
  const {s}=useHcc();
  const names=mnyCategoryNames(s,value),cat=mnyCategory(s,value);
  return <label>Category<select value={value} onChange={e=>onChange(e.target.value)}>{!names.includes(value)&&value&&<option>{value}</option>}{names.map(x=><option key={x}>{x}</option>)}</select>
    {cat?.needsBill&&<small className="mst-help">{cat.name}: the bill / receipt number is required.</small>}</label>;
}

/** Vendor input: active vendors offered (datalist); optional (DEC-120, SCC "Vendor / payee — optional"); a new name joins the vendor list
 *  when the expense is saved (SCC auto-add); a retired vendor is flagged (the command refuses it). Shows the vendor's GSTIN (SCC hint). */
export function ExpVendorInput({value,onChange,phone=false}:{value:string;onChange:(v:string)=>void;phone?:boolean}){
  const {s}=useHcc();
  const listId=useId();
  const t=value.trim(),row=t?mnyVendor(s,t):undefined;
  return <label>Vendor / payee (optional)<input maxLength={60} list={listId} enterKeyHint="next" autoComplete="off" value={value} onChange={e=>onChange(e.target.value)} placeholder="Choose or type the vendor"
      aria-invalid={row&&!row.active?true:undefined}/>
    <datalist id={listId}>{mnyVendorNames(s).map(n=><option key={n} value={n}/>)}</datalist>
    {row&&!row.active&&<small className="mst-err" role="alert">{row.name} is retired in the vendor list — choose another vendor.</small>}
    {row&&row.active&&<small className="mst-help">In the vendor list{row.gstin?` · GSTIN ${row.gstin}`:''}{row.tds?' · TDS applies':''}</small>}
    {!row&&t&&<small className="mst-help">Not in the vendor list — it is added to the list when you save.</small>}
    {!phone&&<MstLink spec={MNY_EV} label="Manage vendors…"/>}</label>;
}

const icon=(k:string)=>k==='till'?<Wallet size={16} aria-hidden/>:k==='bank'?<Landmark size={16} aria-hidden/>:<CreditCard size={16} aria-hidden/>;
/** Cash book / bank view: every receipt, outlet bill and posted expense of the range grouped by the account its method settles to. */
export function MnySettlesTo({from,to}:{from:string;to:string}){
  const {s}=useHcc();
  const groups=useMemo(()=>mnySettlement(s,from,to),[s.bookings,s.posX,s.expenses,s.mastersX,from,to]),accounts=mnyAccounts(s); // eslint-disable-line react-hooks/exhaustive-deps
  return <section className="card mny-settle" aria-label="By account (settles to)">
    <div className="section-title"><h2>By account · settles to</h2><span className="muted">Folio receipts, outlet bills and posted expenses {from===to?`on ${from}`:`${from} – ${to}`}, grouped by where each payment method settles</span></div>
    {!groups.length?<p className="muted">No money moved in this range.</p>:<div className="mny-settle-grid">{groups.map(g=>{const acc=accounts.find(a=>a.code===g.account);
      return <article key={g.account} className="mny-settle-card">
        <h3>{icon(g.kind)} {g.label}{acc?.kind==='bank'&&<small> · {acc.bank} · {mnyMasked(acc.accountLast4)}</small>}</h3>
        <dl><div><dt>In</dt><dd>{money(g.in)}</dd></div><div><dt>Out</dt><dd>{money(g.out)}</dd></div><div><dt>Net</dt><dd><strong>{money(g.net)}</strong></dd></div></dl>
        <ul>{g.lines.map(l=><li key={l.method}><span>{l.method} · {l.count} record{l.count===1?'':'s'}</span><span>{l.in?'+ '+money(l.in):''}{l.in&&l.out?' / ':''}{l.out?'− '+money(l.out):''}</span></li>)}</ul>
      </article>;})}</div>}
    <p className="muted mny-settle-note">The till card is the drawer side of the cash book above (before banking and the count). Methods and accounts: <MstLink spec="paymentMethods" label="Payment methods…"/> <MstLink spec="bankAccounts" label="Bank accounts…"/></p>
  </section>;
}
