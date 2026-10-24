// MB-04 Shared mobile components. Touch targets ≥ 48 px; text ≥ 14 px; styles in styles/mobile.css (m- prefix).
import React from 'react';
import {ChevronRight,Minus,Plus,Check} from 'lucide-react';
export function ListCard({title,sub,meta,right,onClick,tone}:{title:React.ReactNode;sub?:React.ReactNode;meta?:React.ReactNode;right?:React.ReactNode;onClick?:()=>void;tone?:string}){
  const body=<><div className="m-lc-main"><strong>{title}</strong>{sub&&<span>{sub}</span>}{meta&&<small>{meta}</small>}</div>{right&&<div className="m-lc-right">{right}</div>}{onClick&&<ChevronRight size={18} aria-hidden/>}</>;
  return onClick?<button type="button" className={'m-list-card '+(tone||'')} onClick={onClick}>{body}</button>:<div className={'m-list-card '+(tone||'')}>{body}</div>;
}
export function KpiCard({label,value,sub,onClick}:{label:string;value:React.ReactNode;sub?:string;onClick?:()=>void}){
  const inner=<><span className="m-kpi-label">{label}</span><strong className="m-kpi-value number">{value}</strong>{sub&&<small>{sub}</small>}</>;
  return onClick?<button type="button" className="m-kpi" onClick={onClick}>{inner}</button>:<div className="m-kpi">{inner}</div>;
}
export function ChipFilter<T extends string>({options,value,onChange,label}:{options:{id:T;label:string;count?:number}[];value:T;onChange:(v:T)=>void;label:string}){
  return <div className="m-chips" role="group" aria-label={label}>{options.map(o=><button type="button" key={o.id} aria-pressed={value===o.id} className={value===o.id?'on':''} onClick={()=>onChange(o.id)}>{o.label}{o.count!==undefined&&<b>{o.count}</b>}</button>)}</div>;
}
export function StickyFooter({children}:{children:React.ReactNode}){return <div className="m-sticky-footer">{children}</div>;}
export function NumberStepper({label,value,onChange,min=0,max=9999,step=1}:{label:string;value:number;onChange:(v:number)=>void;min?:number;max?:number;step?:number}){
  const set=(v:number)=>onChange(Math.max(min,Math.min(max,v)));
  return <div className="m-stepper-num"><span>{label}</span><button type="button" aria-label={'Decrease '+label} disabled={value<=min} onClick={()=>set(value-step)}><Minus size={18}/></button>
    <input inputMode="numeric" aria-label={label} value={value} onChange={e=>{const n=Number(e.target.value.replace(/\D/g,''));if(!Number.isNaN(n))set(n);}}/>
    <button type="button" aria-label={'Increase '+label} disabled={value>=max} onClick={()=>set(value+step)}><Plus size={18}/></button></div>;
}
/** Progress for wizards / closing: step labels with done / current state. */
export function Stepper({steps,current}:{steps:string[];current:number}){
  return <ol className="m-steps" aria-label={`Step ${current+1} of ${steps.length}`}>{steps.map((s,i)=><li key={s} className={i<current?'done':i===current?'current':''} aria-current={i===current?'step':undefined}><span>{i<current?<Check size={14}/>:i+1}</span><em>{s}</em></li>)}</ol>;
}
export function Fab({label,icon:Icon,onClick}:{label:string;icon:React.ComponentType<{size?:number}>;onClick:()=>void}){return <button type="button" className="m-fab" onClick={onClick}><Icon size={20}/><span>{label}</span></button>;}
