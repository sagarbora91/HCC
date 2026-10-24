import React,{useState} from 'react';

/** Compare against the effective value, including an inherited hotel default. Blank is not zero. */
export function changedPolicyLimit(raw:string,unit:'percent'|'money',current?:number):number|undefined{
  if(!raw.trim())return undefined;
  const value=Number(raw),scaled=unit==='percent'?value:value*100;
  const result=Math.round(scaled);
  if(!Number.isFinite(value)||value<0||!Number.isSafeInteger(result)||Math.abs(scaled-result)>1e-7||
    (unit==='percent'&&result>100)||result===current)return undefined;
  return result;
}

export function PolicyLimitEditor({unit,current,disabled,onSave}:{unit:'percent'|'money';current?:number;disabled:boolean;onSave:(value:number)=>void}){
  const [raw,setRaw]=useState(current===undefined?'':String(unit==='percent'?current:current/100));
  const changed=changedPolicyLimit(raw,unit,current);
  return <form className="adm-limit" onSubmit={e=>{e.preventDefault();if(!disabled&&changed!==undefined)onSave(changed);}}>
    <label>{unit==='percent'?'Limit %':'Limit ₹'}<input name="limit" type="number" min="0" max={unit==='percent'?100:undefined} step={unit==='percent'?1:0.01} disabled={disabled} value={raw} onChange={e=>setRaw(e.target.value)} placeholder="not set"/></label>
    <button type="submit" className="btn small-btn" disabled={disabled||changed===undefined}>Save limit</button>
  </form>;
}
