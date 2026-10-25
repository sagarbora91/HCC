// Wave 0.5 · W05-C shared UI pieces for the compliance calendar (desktop, phone, dashboard card).
import React from 'react';
import {useHcc} from '../../app/context';
import {cmpStatusText} from '../../services/mock';
import type {CMPItem,State} from '../../services/mock';
import {Pill} from '../../components/ui';

/** Unique idempotency key per user action. */
export const cmpActionKey=(s:State,...parts:(string|number|undefined)[])=>['cmp',...parts,s.clock,s.audit.length].join(':');
export const cmpName=(s:State,id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
export const CMP_TONE:Record<CMPItem['status'],string>={Overdue:'cmp-overdue','Due today':'cmp-overdue','Due soon':'orange',Upcoming:'blue',Filed:'green','Not applicable':'neutral'};
/** Colour AND text, never colour alone. */
export function CmpStatusPill({item}:{item:CMPItem}){return <Pill tone={CMP_TONE[item.status]}>{cmpStatusText(item)}</Pill>;}
export function useCmpFilingText(){const {s}=useHcc();
  return (i:CMPItem)=>i.filing?(i.filing.status==='Filed'?`Filed by ${cmpName(s,i.filing.by)} · ${i.filing.at.slice(0,10)} · ref ${i.filing.reference}`:`Not applicable · ${i.filing.reason} · ${cmpName(s,i.filing.by)}`):'';}
export type CmpFilter='open'|'overdue'|'filed'|'all';
export const CMP_FILTERS:{id:CmpFilter;label:string}[]=[{id:'open',label:'Open'},{id:'overdue',label:'Overdue'},{id:'filed',label:'Filed / not applicable'},{id:'all',label:'All'}];
export function cmpApplyFilter(list:CMPItem[],f:CmpFilter,kind:string){
  const k=list.filter(i=>kind==='all'||i.obligation.kind===kind);
  if(f==='open')return k.filter(i=>i.status!=='Filed'&&i.status!=='Not applicable');
  if(f==='overdue')return k.filter(i=>i.status==='Overdue');
  if(f==='filed')return k.filter(i=>i.status==='Filed'||i.status==='Not applicable').reverse();
  return k;}
