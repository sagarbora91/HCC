// Module 16 · shared UI bits: quantity input (NumberStepper on phones), status pill, names.
import React from 'react';
import {useHcc} from '../../app/context';
import {NumberStepper} from '../../app/mobile/components';
import {Pill} from '../../components/ui';
import {INVItem,invClean,invMoneyRole} from '../../services/mock';

/** Whole-number quantity. Phone: NumberStepper (48 px targets). Desktop: numeric input. */
export function InvQty({label,value,onChange,min=0,max=9999}:{label:string;value:number;onChange:(v:number)=>void;min?:number;max?:number}){
  const {display}=useHcc();
  if(display.isMobile)return <div className="inv-stepper"><NumberStepper label={label} value={value} onChange={onChange} min={min} max={max}/></div>;
  return <label className="inv-qty">{label}<input type="number" inputMode="numeric" enterKeyHint="next" min={min} max={max} step={1} value={value}
    onChange={e=>{const n=Math.floor(Number(e.target.value));onChange(Number.isFinite(n)?Math.max(min,Math.min(max,n)):min);}}/></label>;
}
export function InvStatus({it}:{it:INVItem}){const c=invClean(it);
  return c<it.min?<Pill tone="inv-low">Below minimum</Pill>:c<it.min*1.2?<Pill tone="orange">Near minimum</Pill>:<Pill tone="green">OK</Pill>;}
export function useInv(){const h=useHcc();return {...h,money:invMoneyRole(h.s,h.uid)};}
export const invUserName=(s:{users:{id:string;name:string}[]},id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
