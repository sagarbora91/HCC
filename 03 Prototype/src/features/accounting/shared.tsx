// ACC-1 · acc1-ui — pieces shared by the desktop tabs, the phone views and the dialogs: Dr / Cr display, date range bar, ledger / party picker.
// Display helpers only; the figures come from the contract selectors (chart, parties, opening, statements lanes).
import React,{useMemo,useState} from 'react';
import {Search} from 'lucide-react';
import {money,addDays,accChart,accPartyLedgers,type State} from '../../services/mock';
import {ChipFilter} from '../../app/mobile/components';

/** Dr / Cr display: never a negative number (rulebook §7.2). Positive = Dr, negative = Cr. Zero → a dash. */
export const drcr=(n:number)=>n===0?'—':money(Math.abs(n))+(n>0?' Dr':' Cr');
export const accAmt=(n:number)=>n?money(n):'—';
/** Business year (1 April) of a date, for the opening entry's "as of" and the FY-to-date range. */
export const fyStart=(d:string)=>{const y=+d.slice(0,4),m=+d.slice(5,7);return (m>=4?y:y-1)+'-04-01';};

// ── date range ──
export type AccRange={from:string;to:string};
export type RangePreset='today'|'week'|'month'|'fy';
export const rangeOf=(date:string,p:RangePreset):AccRange=>p==='today'?{from:date,to:date}:p==='week'?{from:addDays(date,-6),to:date}:p==='month'?{from:date.slice(0,8)+'01',to:date}:{from:fyStart(date),to:date};
export function useAccRange(date:string){const [r,setR]=useState<AccRange>(()=>rangeOf(date,'month'));return [r,setR] as const;}
/** From / to date inputs with preset chips (phone: 48 px chips and date inputs through z-touch). */
export function RangeBar({s,range,setRange,phone}:{s:State;range:AccRange;setRange:(r:AccRange)=>void;phone?:boolean}){
  const preset=(['today','week','month','fy'] as RangePreset[]).find(p=>{const r=rangeOf(s.date,p);return r.from===range.from&&r.to===range.to;});
  const labels:Record<RangePreset,string>={today:'Today',week:'Last 7 days',month:'This month',fy:'Financial year'};
  const inputs=<div className="acc-range"><label>From<input type="date" value={range.from} max={range.to} onChange={e=>e.target.value&&setRange({...range,from:e.target.value})}/></label>
    <label>To<input type="date" value={range.to} min={range.from} max={s.date} onChange={e=>e.target.value&&setRange({...range,to:e.target.value})}/></label></div>;
  const chips=(['today','week','month','fy'] as RangePreset[]).map(p=>({id:p,label:labels[p]}));
  return <div className="acc-rangebar">
    {phone?<ChipFilter<RangePreset> label="Date range" value={preset??'month'} onChange={p=>setRange(rangeOf(s.date,p))} options={chips}/>
      :<div className="filters" role="group" aria-label="Date range">{chips.map(c=><button type="button" key={c.id} className={preset===c.id?'active':''} aria-pressed={preset===c.id} onClick={()=>setRange(rangeOf(s.date,c.id))}>{c.label}</button>)}</div>}
    {inputs}</div>;
}

// ── ledger / party picker ──
export type LedgerOpt={id:string;label:string;control?:string};
/** Options for a picker. postable = ledgers that take entries (no control ledger itself, no 1400 / 9999) plus the party ledgers; otherwise every ledger (a control sums its parties) plus the parties. */
export function ledgerOptions(s:State,postable:boolean):LedgerOpt[]{
  const chart=accChart(s),parties=accPartyLedgers(s),ctl=new Map(chart.map(l=>[l.code,l]));
  const lead=chart.filter(l=>postable?(l.active&&!l.control&&l.code!=='1400'&&l.code!=='9999'):true).map(l=>({id:l.code,label:`${l.code} · ${l.name}${l.control?' (all parties)':''}${l.active?'':' · retired'}`}));
  const ps=parties.filter(p=>!postable||p.active).map(p=>({id:p.id,control:p.control,label:`${p.id} · ${p.name} (${ctl.get(p.control)?.name??p.control})`}));
  return [...lead,...ps].sort((a,b)=>a.id<b.id?-1:a.id>b.id?1:0);
}
export function LedgerPicker({options,value,onChange,label='Ledger or party',disabled}:{options:LedgerOpt[];value:string;onChange:(v:string)=>void;label?:string;disabled?:boolean}){
  const [q,setQ]=useState('');
  const shown=useMemo(()=>{const t=q.trim().toLowerCase();const hit=options.filter(o=>!t||o.label.toLowerCase().includes(t));const cur=options.find(o=>o.id===value);return cur&&!hit.includes(cur)?[cur,...hit]:hit;},[options,q,value]);
  return <div className="acc-picker"><label className="acc-search"><Search size={16} aria-hidden/><input type="search" aria-label={`Search ${label.toLowerCase()}`} placeholder="Search code or name" value={q} onChange={e=>setQ(e.target.value)} disabled={disabled}/></label>
    <label>{label}<select value={value} disabled={disabled} onChange={e=>onChange(e.target.value)}>
      <option value="">Choose…</option>{shown.map(o=><option key={o.id} value={o.id}>{o.label}</option>)}</select></label>
    {q&&<small className="muted">{shown.length} match{shown.length===1?'':'es'}</small>}</div>;
}
/** Plain amount entry (rupees) → paise; empty → 0. */
export const toPaise=(v:string)=>{const n=Number(v.replace(/,/g,''));return Number.isFinite(n)&&n>0?Math.round(n*100):0;};
export const fromPaise=(p:number)=>p?String(p/100):'';

// ── ACC-2 pickers (cash / bank, banks, loans, debtors) ──
import {accLoans} from './_contract-stubs-2';
const byId=(a:LedgerOpt,b:LedgerOpt)=>a.id<b.id?-1:a.id>b.id?1:0;
/** Cash and bank ledgers that take entries: tills (1000/…), petty cash, UPI / card clearing, bank accounts (1030/…). */
export function tenderOptions(s:State):LedgerOpt[]{
  const chart=accChart(s),ctl=new Map(chart.map(l=>[l.code,l]));
  const lead=chart.filter(l=>l.active&&!l.control&&(l.group==='cash'||l.group==='bank')).map(l=>({id:l.code,label:`${l.code} · ${l.name}`}));
  const ps=accPartyLedgers(s).filter(p=>p.active&&(p.control==='1000'||p.control==='1030')).map(p=>({id:p.id,control:p.control,label:`${p.id} · ${p.name} (${ctl.get(p.control)?.name??p.control})`}));
  return [...lead,...ps].sort(byId);
}
/** Bank accounts only (1030/<bank>). */
export const bankOptions=(s:State):LedgerOpt[]=>accPartyLedgers(s).filter(p=>p.active&&p.control==='1030').map(p=>({id:p.id,control:p.control,label:`${p.name} (${p.id})`})).sort(byId);
/** Owner-added loans (2400/<LN-code>). */
export const loanOptions=(s:State):LedgerOpt[]=>accLoans(s).filter(l=>l.active).map(l=>({id:l.id,control:'2400',label:`${l.name}${l.lender?' · '+l.lender:''} (${l.id})`}));
/** Receivables a bad debt can be written off against: company (1110/…) and Udhaar customer (1120/…) party ledgers. */
export const debtorOptions=(s:State):LedgerOpt[]=>accPartyLedgers(s).filter(p=>p.control==='1110'||p.control==='1120').map(p=>({id:p.id,control:p.control,label:`${p.name} (${p.id})`})).sort(byId);
/** Month list for the month pickers: the business month and the eleven before it, newest first. */
export function monthChoices(date:string,n=12):string[]{
  const out:string[]=[];let y=+date.slice(0,4),m=+date.slice(5,7);
  for(let i=0;i<n;i++){out.push(`${y}-${String(m).padStart(2,'0')}`);m--;if(m===0){m=12;y--;}}return out;
}
export const monthLabel=(ym:string)=>new Date(ym+'-15T12:00:00').toLocaleDateString('en-IN',{month:'long',year:'numeric'});
import {mnyNames,mnyIsCredit,mnyIsDrawer} from '../../services/mock';
/** Payment methods a remittance / vendor payment can be made with: not "pay later" credit. [value, label, isDrawerCash]. */
export const payMethods=(s:State):[string,string,boolean][]=>mnyNames(s,'expenses').filter(m=>!mnyIsCredit(s,m)).map(m=>[m,mnyIsDrawer(s,m)?'Drawer cash':m,mnyIsDrawer(s,m)]);
/** Bank-type methods only (challans and bank remittances need a reference). */
export const bankMethods=(s:State)=>payMethods(s).filter(m=>!m[2]);
