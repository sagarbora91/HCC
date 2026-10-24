// Shared presentation components and small form helpers.
import React from 'react';
import {CheckCircle2,Search} from 'lucide-react';
export const dateLabel=(d:string)=>new Date(d+'T12:00:00').toLocaleDateString('en-IN',{day:'numeric',month:'short',year:'numeric'});
export const shortDate=(d:string)=>new Date(d+'T12:00:00').toLocaleDateString('en-IN',{day:'numeric',month:'short'});
/** Simulated clock "YYYY-MM-DDTHH:mm" → "08:14". */
export const clockTime=(c:string)=>c.slice(11,16);
export function Pill({children,tone='neutral'}:{children:React.ReactNode;tone?:string}){return <span className={'pill '+tone}>{children}</span>}
export function Empty({title,body}:{title:string;body?:string}){return <div className="empty"><CheckCircle2 size={30}/><h3>{title}</h3><p>{body||'There are no records to show here.'}</p></div>}
export function PageTitle({title,sub,action}:{title:string;sub:string;action?:React.ReactNode}){return <div className="page-title"><div><h1>{title}</h1><p>{sub}</p></div>{action}</div>}
export function SearchBox({value,onChange,placeholder}:{value:string;onChange:(v:string)=>void;placeholder:string}){return <label className="search"><Search size={17}/><input aria-label={placeholder} value={value} placeholder={placeholder} onChange={e=>onChange(e.target.value)}/></label>}
export function Stat({title,value,sub}:{title:string;value:string;sub:string}){return <div className="stat"><div className="stat-label">{title}</div><strong className="money-stat">{value}</strong><small>{sub}</small></div>}
export const field=(f:FormData,k:string)=>String(f.get(k)||'');
/** Rupee input → integer paise. */
export const amount=(f:FormData,k='amount')=>Math.round(Number(f.get(k))*100);
export function submit(e:React.FormEvent<HTMLFormElement>,fn:(f:FormData)=>void){e.preventDefault();fn(new FormData(e.currentTarget));}
