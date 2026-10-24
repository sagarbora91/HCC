// Module 14 · shared UI bits (desktop + phone): status/credit pills, figures, labels.
import React from 'react';
import {money,grpStatus,grpCredit,GRP_CREDIT_LABEL,grpDaysToCutoff} from '../../services/mock';
import type {State,GRPBlock,GRPCompany,GRPCreditLevel} from '../../services/mock';
import {shortDate} from '../../components/ui';

export const grpStatusTone=(st:string)=>st==='Open'?'green':st==='Released'?'blue':'neutral';
export function GrpStatusPill({s,blk}:{s:State;blk:GRPBlock}){const st=grpStatus(s,blk);return <span className={'pill '+grpStatusTone(st)}>{st}{st==='Released'&&blk.status==='Open'?' · at cut-off':''}</span>;}
const creditTone:Record<GRPCreditLevel,string>={ok:'green',near:'grp-amber',over:'grp-red',none:'neutral'};
export function GrpCreditPill({level}:{level:GRPCreditLevel}){return <span className={'pill '+creditTone[level]}>{GRP_CREDIT_LABEL[level]}</span>;}
export function GrpCreditBar({s,c}:{s:State;c:GRPCompany}){
  const k=grpCredit(s,c),w=Math.min(100,k.limit?Math.round(k.exposure/k.limit*100):k.exposure?100:0);
  return <div className="grp-bar" role="img" aria-label={`Credit used ${k.limit?k.pct+'%':'— no limit'}: ${money(k.exposure)} of ${money(k.limit)}`}><span className={'grp-bar-fill '+k.level} style={{width:w+'%'}}/></div>;
}
export function grpCutoffText(s:State,blk:GRPBlock){
  const st=grpStatus(s,blk);if(st==='Cancelled')return 'Cancelled';
  if(st==='Released')return blk.status==='Open'?`Cut-off ${shortDate(blk.cutoff)} passed — auto-release due`:`Released ${blk.releasedBy==='auto'?'automatically at cut-off':'manually'}`;
  const d=grpDaysToCutoff(s,blk);return `Cut-off ${shortDate(blk.cutoff)} · ${d===0?'today':d===1?'tomorrow':d+' days'}`;
}
export const grpRupees=(paise:number)=>paise?String(paise/100):'';
export const grpPaise=(v:string|FormDataEntryValue|null)=>{const t=String(v??'').replace(/[,\s₹]/g,'');return t===''?undefined:Math.round(Number(t)*100);};
export const grpUserName=(s:State,id?:string)=>id==='auto'?'System (auto)':s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
export const grpSimNote='Simulated: GSTINs are fictional placeholders; no GST validation, no company invoices or AR postings — exposure is an estimate.';
