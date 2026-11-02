// LF-UI · phone version of 'counter-bills' (A7): one column, 48 px targets, cards instead of a table, a sticky total + Issue button (LF contract; phone rules in CLAUDE.md).
import React,{useState} from 'react';
import {Plus,Trash2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,ctrState,ctrTotals,ctrLineFrom,CTR_SETTLEMENTS,type CTRLine} from '../../services/mock';
import {Pill,Empty,shortDate} from '../../components/ui';
import {StickyFooter} from '../../app/mobile/components';

const MODE_LABEL={paid:'Paid now',udhaar:'Udhaar credit',company:'Company credit'} as const;
type Row={label:string;qty:string;unit:string;gstCategory:string};
const blank=():Row=>({label:'',qty:'1',unit:'',gstCategory:'other'});
const T48:React.CSSProperties={minHeight:48};

function NewBillPhone({onDone}:{onDone:()=>void}){
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
  return <form id="ctr-new-bill-m" className="form padded" onSubmit={e=>{e.preventDefault();void run({type:'CTR_BILL_ISSUE',party:p,lines:inputs,...(d>0?{discount:{amount:d,reason:disc.reason}}:{}),
      settlement:{mode,method,reference,companyId}},'Counter bill issued.',{after:onDone});}}>
    <label>Customer name<input style={T48} value={p.name} required onChange={e=>setP({...p,name:e.target.value})}/></label>
    <label>Phone<input style={T48} inputMode="tel" value={p.phone} onChange={e=>setP({...p,phone:e.target.value})}/></label>
    <label>GSTIN (optional)<input style={T48} value={p.gstin} maxLength={15} onChange={e=>setP({...p,gstin:e.target.value})}/></label>
    {rows.map((r,i)=><div className="dk-card" key={i}>
      <label>Item<input style={T48} value={r.label} onChange={e=>set(i,{label:e.target.value})}/></label>
      <label>Qty<input style={T48} type="number" inputMode="numeric" min={1} max={99} value={r.qty} onChange={e=>set(i,{qty:e.target.value})}/></label>
      <label>Unit price (₹)<input style={T48} type="number" inputMode="decimal" min={0} step="0.01" value={r.unit} onChange={e=>set(i,{unit:e.target.value})}/></label>
      <label>GST<select style={T48} value={r.gstCategory} onChange={e=>set(i,{gstCategory:e.target.value})}>{['food','laundry','banquet','other'].map(c=><option key={c}>{c}</option>)}</select></label>
      {rows.length>1&&<button type="button" className="btn" style={T48} onClick={()=>setRows(x=>x.filter((_,j)=>j!==i))}><Trash2 size={15}/> Remove line</button>}</div>)}
    <button type="button" className="btn" style={T48} onClick={()=>setRows(r=>[...r,blank()])}><Plus size={15}/> Add line</button>
    {err&&<p className="muted" role="status">{err.error}</p>}
    <label>Discount (₹)<input style={T48} type="number" inputMode="decimal" min={0} step="0.01" value={disc.amount} onChange={e=>setDisc({...disc,amount:e.target.value})}/></label>
    <label>Discount reason<input style={T48} value={disc.reason} onChange={e=>setDisc({...disc,reason:e.target.value})}/></label>
    <label>Settlement<select style={T48} value={mode} onChange={e=>setMode(e.target.value as typeof mode)}>{CTR_SETTLEMENTS.map(m=><option key={m} value={m}>{MODE_LABEL[m]}</option>)}</select></label>
    {mode==='paid'&&<><label>Method<input style={T48} value={method} onChange={e=>setMethod(e.target.value)}/></label><label>Reference<input style={T48} value={reference} onChange={e=>setReference(e.target.value)}/></label></>}
    {mode==='company'&&<label>Company id<input style={T48} value={companyId} onChange={e=>setCompanyId(e.target.value)}/></label>}
    <StickyFooter><div className="inline" style={{width:'100%',justifyContent:'space-between',alignItems:'center'}}><span><strong>{money(t.total)}</strong><small className="muted"> GST {money(t.tax)}</small></span>
      <button type="submit" className="btn primary" style={T48} disabled={busy||!!err||!good.length}>Issue bill</button></div></StickyFooter>
  </form>;
}

function BillsPhone(){
  const {s,run,busy}=useHcc();const bills=[...ctrState(s).bills].reverse();const [ask,setAsk]=useState('');const [reason,setReason]=useState('');
  if(!bills.length)return <Empty title="No counter bills yet" body="Issue one from the New bill tab."/>;
  return <div className="dk-cards">{bills.map(b=><div className="dk-card" key={b.id}>
    <strong>{b.no}</strong> <Pill tone={b.status==='Issued'?'green':'neutral'}>{b.status==='Credited'?'Credited '+(b.creditNoteNo??''):b.status}</Pill>
    <p style={{margin:'4px 0'}}>{b.party.name}</p><small className="muted">{shortDate(b.date)} · {MODE_LABEL[b.settlement.mode]} · {money(b.total)}</small>
    {b.status==='Issued'&&<div style={{marginTop:8}}><button type="button" className="btn" style={T48} onClick={()=>setAsk(ask===b.id?'':b.id)}>Credit note…</button></div>}
    {ask===b.id&&<div className="form" style={{marginTop:8}}><label>Reason (required)<input style={T48} value={reason} maxLength={200} onChange={e=>setReason(e.target.value)}/></label>
      <button type="button" className="btn danger" style={T48} disabled={busy||!reason.trim()} onClick={()=>void run({type:'CTR_BILL_VOID_REQUEST',id:b.id,reason},'Credit note sent to the Owner.',{after:()=>{setAsk('');setReason('');}})}>Request credit note</button></div>}
  </div>)}</div>;
}

export function MfCounterBills(){
  const [tab,setTab]=useState<'new-bill'|'bills'>('new-bill');
  return <div className="mf-page">
    <div className="mf-tabs" role="tablist" aria-label="Counter bills">{([['new-bill','New bill'],['bills','Bills']] as const).map(([k,l])=><button key={k} type="button" role="tab" aria-selected={tab===k} className={tab===k?'active':''} onClick={()=>setTab(k)}>{l}</button>)}</div>
    {tab==='new-bill'?<NewBillPhone onDone={()=>setTab('bills')}/>:<BillsPhone/>}</div>;
}
