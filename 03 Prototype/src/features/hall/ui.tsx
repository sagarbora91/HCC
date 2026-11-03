// F6-H3 · small shared pieces of the hall screens: rupee input, status pills, the "no prices" note.
import React,{useState} from 'react';
import {money} from '../../domain/money';
import {Pill} from '../../components/ui';

/** "12,500.50" → paise; empty / invalid → undefined. */
export function toPaise(text:string):number|undefined{
  const t=text.replace(/[,₹\s]/g,'');if(!/^\d+(\.\d{0,2})?$/.test(t))return undefined;return Math.round(Number(t)*100);
}
export const rupees=(p:number)=>p%100?(p/100).toFixed(2):String(p/100);
/** Rupee amount field: edits as text, commits paise on blur (so typing "1," or "1.5" never jumps). */
export function RupeeInput({value,onChange,label,disabled,min=0}:{value:number;onChange:(p:number)=>void;label:string;disabled?:boolean;min?:number}){
  const [text,setText]=useState(rupees(value)),[focus,setFocus]=useState(false);
  const shown=focus?text:rupees(value);
  return <input className="hall-rupee" inputMode="decimal" aria-label={label} disabled={disabled} value={shown}
    onFocus={()=>{setText(rupees(value));setFocus(true);}} onChange={e=>{setText(e.target.value);const p=toPaise(e.target.value);if(p!==undefined&&p>=min)onChange(p);}} onBlur={()=>setFocus(false)}/>;
}
export const Money=({p}:{p:number})=><span className="num">{money(p)}</span>;
export const NoPrices=()=><p className="muted hall-noprice" role="note">Prices are not shown to this role.</p>;
export const StatusPill=({tone,children}:{tone:string;children:React.ReactNode})=><Pill tone={tone}>{children}</Pill>;
export function Field({label,children,wide}:{label:string;children:React.ReactNode;wide?:boolean}){return <label className={'hall-field'+(wide?' wide':'')}><span>{label}</span>{children}</label>;}
