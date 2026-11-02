// LF-UI · page 'counter-bills' (Cash & billing › Counter bills, Alt+B): a bill for someone who is not staying (A7). Tabs New bill · Bills.
import React,{useState} from 'react';
import {Plus,Trash2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,ctrState,ctrTotals,ctrLineFrom,CTR_SETTLEMENTS,type CTRLine} from '../../services/mock';
import {FitPage,FitTabs,ScrollPanel,useFitTab} from '../../components/fit';
import {Pill,Empty,shortDate} from '../../components/ui';

const TABS=['new-bill','bills'] as const;type Tab=typeof TABS[number];
const MODE_LABEL={paid:'Paid now',udhaar:'Udhaar credit',company:'Company credit'} as const;
type Row={label:string;qty:string;unit:string;gstCategory:string};
const blank=():Row=>({label:'',qty:'1',unit:'',gstCategory:'other'});

function NewBill({onDone}:{onDone:()=>void}){
  const {s,run,busy}=useHcc();
  const [p,setP]=useState({name:'',phone:'',gstin:''});const [rows,setRows]=useState<Row[]>([blank()]);
  const [mode,setMode]=useState<typeof CTR_SETTLEMENTS[number]>('paid');const [method,setMethod]=useState('Cash');const [reference,setReference]=useState('');
  const [companyId,setCompanyId]=useState('');const [disc,setDisc]=useState({amount:'',reason:''});
  const inputs=rows.filter(r=>r.label.trim()||r.unit).map(r=>({label:r.label,qty:Number(r.qty),unit:Math.round(Number(r.unit)*100),gstCategory:r.gstCategory}));
  const built=inputs.map((x,i)=>ctrLineFrom(s,x,s.date,'L'+(i+1)));
  const err=built.find(l=>'error' in l) as {error:string}|undefined;
  const good=built.filter(l=>!('error' in l)) as CTRLine[];
  const d=Math.round(Number(disc.amount||0)*100);const t=ctrTotals(good,d>0&&d<=good.reduce((a,l)=>a+l.amount,0)?d:0);
  const set=(i:number,x:Partial<Row>)=>setRows(r=>r.map((y,j)=>j===i?{...y,...x}:y));
  return <ScrollPanel title="New counter bill" label="New counter bill" footer={<div className="inline"><strong>Total {money(t.total)}</strong><span className="muted">net {money(t.net)} · GST {money(t.tax)}</span>
    <button type="submit" form="ctr-new-bill" className="btn primary" disabled={busy||!!err||!good.length}>Issue counter bill</button></div>}>
    <form id="ctr-new-bill" className="form padded" onSubmit={e=>{e.preventDefault();void run({type:'CTR_BILL_ISSUE',party:p,lines:inputs,...(d>0?{discount:{amount:d,reason:disc.reason}}:{}),
      settlement:{mode,method,reference,companyId}},'Counter bill issued.',{after:onDone});}}>
      <div className="form-row"><label>Customer name<input value={p.name} required onChange={e=>setP({...p,name:e.target.value})}/></label>
        <label>Phone<input value={p.phone} onChange={e=>setP({...p,phone:e.target.value})}/></label><label>GSTIN (optional)<input value={p.gstin} maxLength={15} onChange={e=>setP({...p,gstin:e.target.value})}/></label></div>
      {rows.map((r,i)=><div className="form-row" key={i}><label>Item<input value={r.label} onChange={e=>set(i,{label:e.target.value})}/></label>
        <label>Qty<input type="number" min={1} max={99} value={r.qty} onChange={e=>set(i,{qty:e.target.value})}/></label>
        <label>Unit price (₹)<input type="number" min={0} step="0.01" value={r.unit} onChange={e=>set(i,{unit:e.target.value})}/></label>
        <label>GST<select value={r.gstCategory} onChange={e=>set(i,{gstCategory:e.target.value})}>{['food','laundry','banquet','other'].map(c=><option key={c}>{c}</option>)}</select></label>
        {rows.length>1&&<button type="button" className="btn" aria-label="Remove line" onClick={()=>setRows(x=>x.filter((_,j)=>j!==i))}><Trash2 size={15}/></button>}</div>)}
      <button type="button" className="btn" onClick={()=>setRows(r=>[...r,blank()])}><Plus size={15}/> Add line</button>
      {err&&<p className="muted" role="status">{err.error}</p>}
      <div className="form-row"><label>Discount (₹)<input type="number" min={0} step="0.01" value={disc.amount} onChange={e=>setDisc({...disc,amount:e.target.value})}/></label>
        <label>Discount reason<input value={disc.reason} onChange={e=>setDisc({...disc,reason:e.target.value})}/></label></div>
      <div className="form-row"><label>Settlement<select value={mode} onChange={e=>setMode(e.target.value as typeof mode)}>{CTR_SETTLEMENTS.map(m=><option key={m} value={m}>{MODE_LABEL[m]}</option>)}</select></label>
        {mode==='paid'&&<><label>Method<input value={method} onChange={e=>setMethod(e.target.value)}/></label><label>Reference<input value={reference} onChange={e=>setReference(e.target.value)}/></label></>}
        {mode==='company'&&<label>Company id<input value={companyId} onChange={e=>setCompanyId(e.target.value)}/></label>}</div>
    </form></ScrollPanel>;
}

function Bills(){
  const {s,run,busy}=useHcc();const bills=[...ctrState(s).bills].reverse();const [ask,setAsk]=useState('');const [reason,setReason]=useState('');
  return <ScrollPanel title="Counter bills" label="Counter bills" actions={<span className="muted">{bills.length}</span>}>
    {bills.length?<div className="table-wrap"><table><thead><tr><th>No.</th><th>Date</th><th>Customer</th><th>Settled</th><th>Total</th><th>Status</th><th></th></tr></thead><tbody>
      {bills.map(b=><React.Fragment key={b.id}><tr><td><strong>{b.no}</strong></td><td>{shortDate(b.date)}</td><td>{b.party.name}</td><td>{MODE_LABEL[b.settlement.mode]}</td><td>{money(b.total)}</td>
        <td><Pill tone={b.status==='Issued'?'green':'neutral'}>{b.status==='Credited'?'Credited '+(b.creditNoteNo??''):b.status}</Pill></td>
        <td>{b.status==='Issued'&&<button type="button" className="text-btn" onClick={()=>setAsk(ask===b.id?'':b.id)}>Credit note…</button>}</td></tr>
        {ask===b.id&&<tr><td colSpan={7}><div className="inline"><input aria-label="Reason" placeholder="Reason (required)" value={reason} maxLength={200} onChange={e=>setReason(e.target.value)}/>
          <button type="button" className="btn danger" disabled={busy||!reason.trim()} onClick={()=>void run({type:'CTR_BILL_VOID_REQUEST',id:b.id,reason},'Credit note sent to the Owner.',{after:()=>{setAsk('');setReason('');}})}>Request credit note</button></div></td></tr>}</React.Fragment>)}</tbody></table></div>
      :<Empty title="No counter bills yet" body="Issue one from the New bill tab."/>}
  </ScrollPanel>;
}

export function CounterBillsPage(){
  const [tab,setTab]=useFitTab<Tab>('counter-bills',TABS,'new-bill');
  return <FitPage title="Counter bills" sub="Bills for people who are not staying: paid now, on Udhaar credit or on company credit" layout="fill"
    tabs={<FitTabs<Tab> page="counter-bills" tabs={[{id:'new-bill',label:'New bill'},{id:'bills',label:'Bills'}]} value={tab} onChange={setTab}/>}>
    {tab==='new-bill'?<NewBill onDone={()=>setTab('bills')}/>:<Bills/>}
  </FitPage>;
}
