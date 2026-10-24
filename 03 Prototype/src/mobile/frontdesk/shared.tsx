// MB-05 shared phone bits for the front desk: status pill, page header, collapsible section card, page-permission check.
import React,{useEffect} from 'react';
import {ChevronDown} from 'lucide-react';
import {useHcc} from '../../app/context';
import {navItems} from '../../app/nav';
import {Pill} from '../../components/ui';

export const mfTone=(st:string)=>st==='In house'?'green':st==='Confirmed'?'blue':st==='Held'?'orange':st==='Departed'?'neutral':'neutral';
export function MfStatus({status}:{status:string}){return <Pill tone={mfTone(status)}>{status}</Pill>;}

/** Page IDs the current user may open (same rules as the nav). */
export function useMfPages(){const {permission}=useHcc();return navItems.filter(n=>permission(n.p)).map(n=>n.id);}

export function MfHeader({title,sub,children}:{title:string;sub?:string;children?:React.ReactNode}){
  return <div className="mf-head"><div><h2>{title}</h2>{sub&&<p>{sub}</p>}</div>{children}</div>;
}

/** Collapsible card section (native details/summary: keyboard and screen-reader friendly, no hover). */
export function MfSection({title,meta,open,children}:{title:string;meta?:React.ReactNode;open?:boolean;children:React.ReactNode}){
  return <details className="mf-section" open={open}><summary><span>{title}</span>{meta!==undefined&&<small>{meta}</small>}<ChevronDown size={18} aria-hidden/></summary><div className="mf-section-body">{children}</div></details>;
}

/** Label/value rows (phone-friendly summary list). */
export function MfRows({rows}:{rows:[string,React.ReactNode][]}){
  return <dl className="mf-rows">{rows.map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v||'—'}</dd></div>)}</dl>;
}

/** Search input sized for phones. */
export function MfSearch({value,onChange,placeholder}:{value:string;onChange:(v:string)=>void;placeholder:string}){
  return <input className="mf-search" type="search" enterKeyHint="search" aria-label={placeholder} placeholder={placeholder} value={value} onChange={e=>onChange(e.target.value)}/>;
}

/** Phone pages open at the top (the shell keeps the window scroll between pages). */
export function useMfTop(){useEffect(()=>{try{window.scrollTo(0,0);}catch{/* ignore */}},[]);}
