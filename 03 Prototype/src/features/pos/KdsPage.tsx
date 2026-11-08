// F8 QW-KDS · kitchen display (tablet): station picker, outlet filter, open KOT cards oldest first, "Ready" / "Served" bump. No money on this page.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {FitPage,BoardColumns} from '../../components/fit';
import {Empty,clockTime} from '../../components/ui';
import {posOutletList,posStations,kdsCards,KDS_AMBER_MINUTES,KDS_RED_MINUTES} from '../../services/mock';
import type {KdsCard} from '../../services/mock';

function KdsTicket({c}:{c:KdsCard}){
  const {run,busy,permission}=useHcc();
  return <article className={'kds-card kds-'+c.level} aria-label={`${c.label}, ${c.spot}, ${c.minutes} minutes, ${c.levelText}`}>
    <header><strong>{c.label}</strong><span className="kds-spot">{c.spot}</span></header>
    <p className="kds-time"><b>{c.minutes} min</b> <span className={'kds-level kds-level-'+c.level}>{c.levelText}</span> <small>sent {clockTime(c.sentAt)}</small></p>
    <ul>{c.items.map((i,n)=><li key={n}><b>{i.qty}×</b> {i.name}</li>)}</ul>
    {c.next&&permission('billing')&&<button type="button" className="btn primary kds-bump" disabled={busy}
      onClick={()=>run({type:'POS_KOT_STATUS',id:c.orderId,kot:c.kotId,status:c.next},`${c.label} → ${c.next}`)}>{c.next==='Ready'?'Ready':'Served'}</button>}
  </article>;
}

export function KdsPage(){
  const {s}=useHcc();const outlets=posOutletList(s),stations=posStations(s);
  const [outlet,setOutlet]=useState('');const [station,setStation]=useState('');
  const out=outlets.some(o=>o.id===outlet)?outlet:'',st=stations.includes(station)?station:'';
  const cards=kdsCards(s,out,st);
  const seg=(label:string,items:{id:string;name:string}[],value:string,set:(v:string)=>void,all:string)=>
    <div className="pos-cats kds-seg" role="group" aria-label={label}>{[{id:'',name:all},...items].map(x=><button type="button" key={x.id||'all'} aria-pressed={value===x.id} className={value===x.id?'on':''} onClick={()=>set(x.id)}>{x.name}</button>)}</div>;
  return <FitPage title="Kitchen display" sub={`Open kitchen tickets, oldest first · amber after ${KDS_AMBER_MINUTES} min, red after ${KDS_RED_MINUTES} min · simulated clock ${clockTime(s.clock)}`} layout="fill"
    toolbar={<>{outlets.length>1&&seg('Outlet',outlets.map(o=>({id:o.id,name:o.name})),out,setOutlet,'All outlets')}
      {stations.length>0&&seg('Kitchen station',stations.map(x=>({id:x,name:x})),st,setStation,'All stations')}</>}>
    {!outlets.length?<div className="card"><Empty title="No outlets configured" body="Load a demo scenario or add an outlet in Master configuration."/></div>
      :<BoardColumns minColumn={260} columns={(['Preparing','Ready'] as const).map(c=>{const xs=cards.filter(x=>x.status===c);
        return {id:c,title:c==='Preparing'?'Preparing':'Ready to serve',count:xs.length,body:<>{xs.map(x=><KdsTicket key={x.kotId} c={x}/>)}{!xs.length&&<p className="muted">No tickets.</p>}</>};})}/>}
  </FitPage>;
}
