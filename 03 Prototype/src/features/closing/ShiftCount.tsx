// Cash count entry (SCR-11): total or denomination grid, live difference, reason when it differs. Submits SHIFT_SUBMIT.
import React,{useState} from 'react';
import {Info,Send} from 'lucide-react';
import {money,shiftExpected,shiftDenominationTotal,SHIFT_DENOMINATIONS,SHIFT_MANAGER_DIFF_LIMIT} from '../../services/mock';
import {useHcc} from '../../app/context';

export function ShiftCountForm({after}:{after?:()=>void}){
  const {s,run,busy,permission}=useHcc();
  const [total,setTotal]=useState('');const [grid,setGrid]=useState(false);
  const [den,setDen]=useState<Record<string,string>>({});const [reason,setReason]=useState('');
  const ex=shiftExpected(s);
  if(!permission('payments'))return <p className="muted padded">Counting the drawer needs the payments permission (cashier or reception).</p>;
  const nums:Record<string,number>=Object.fromEntries([...SHIFT_DENOMINATIONS.map(String),'coins'].map(k=>[k,Number(den[k]||0)]));
  const gridBad=Object.values(nums).some(v=>!Number.isFinite(v)||v<0)||SHIFT_DENOMINATIONS.some(n=>!Number.isInteger(nums[n]));
  const totalBad=total.trim()===''||!Number.isFinite(Number(total))||Number(total)<0;
  const counted=grid?shiftDenominationTotal(nums):Math.round(Number(total)*100);
  const entered=grid?!gridBad&&Object.values(den).some(v=>v!==''):!totalBad;
  const diff=entered?counted-ex.expected:0;
  const needReason=entered&&diff!==0&&!reason.trim();
  const blocked=!entered?(grid?'Enter note counts (whole numbers) to see the total.':'Enter the physical cash counted.'):needReason?'A reason is required because the count differs from expected cash.':'';
  function submit(e:React.FormEvent){e.preventDefault();if(blocked)return;
    run({type:'SHIFT_SUBMIT',counted,reason:reason.trim()||undefined,denominations:grid?nums:undefined},diff===0?'Cash matches exactly — shift closed':'Count submitted. The difference now needs approval by another user.',{after});}
  return <form className="form" onSubmit={submit} aria-label="Cash count">
    <div className="f-toggle-row"><span>Count by</span><div className="f-seg" role="group" aria-label="Count method">
      <button type="button" className={!grid?'on':''} aria-pressed={!grid} onClick={()=>setGrid(false)}>Total</button>
      <button type="button" className={grid?'on':''} aria-pressed={grid} onClick={()=>setGrid(true)}>Denominations</button></div></div>
    {!grid?<label>Physical cash counted (₹)<input inputMode="decimal" type="number" min="0" step="0.01" value={total} onChange={e=>setTotal(e.target.value)} placeholder="e.g. 10700"/></label>
      :<div className="f-den-grid">{SHIFT_DENOMINATIONS.map(n=><label key={n}>₹{n} notes<input type="number" min="0" step="1" value={den[n]??''} onChange={e=>setDen({...den,[n]:e.target.value})}/><small>{money((nums[n]||0)*n*100)}</small></label>)}
        <label>Coins (₹ total)<input type="number" min="0" step="0.01" value={den.coins??''} onChange={e=>setDen({...den,coins:e.target.value})}/><small>{money(Math.round((nums.coins||0)*100))}</small></label></div>}
    <div className="f-diff" role="status">
      <div><span>Expected</span><strong>{money(ex.expected)}</strong></div>
      <div><span>Counted</span><strong>{entered?money(counted):'—'}</strong></div>
      <div className={diff===0?'ok':'bad'}><span>Difference</span><strong>{!entered?'—':diff===0?'None · exact':(diff>0?'Over ':'Short ')+money(Math.abs(diff))}</strong></div>
    </div>
    {entered&&diff!==0&&<label>Reason for the difference (required)<textarea required maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. ₹100 note given as change twice"/></label>}
    {entered&&diff!==0&&<div className="info-note"><Info size={16}/>A difference needs approval by another user. A manager can accept up to {money(SHIFT_MANAGER_DIFF_LIMIT)}; above that, the Owner. Cash receipts are paused until it is decided.</div>}
    {blocked&&<p className="f-why" aria-live="polite">{blocked}</p>}
    <div className="modal-actions"><button className="btn primary" disabled={busy||!!blocked}><Send size={15}/> {diff===0?'Submit count & close shift':'Submit count for approval'}</button></div>
  </form>;
}
