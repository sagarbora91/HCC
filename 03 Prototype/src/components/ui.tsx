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
/** Focusable controls inside a dialog (visible, enabled). Used by the Tab trap. */
export const FOCUSABLE='button:not([disabled]),input:not([disabled]):not([type="hidden"]),select:not([disabled]),textarea:not([disabled]),a[href],summary,[tabindex]:not([tabindex="-1"])';
/** Keeps Tab / Shift+Tab inside `root`. Returns true when it handled the event. */
export function trapTab(e:{key:string;shiftKey:boolean;preventDefault:()=>void},root:HTMLElement):boolean{
  if(e.key!=='Tab')return false;
  const els=Array.from(root.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(el=>el.offsetWidth>0||el.offsetHeight>0||el===document.activeElement);
  if(!els.length){e.preventDefault();root.focus();return true;}
  const first=els[0],last=els[els.length-1],a=document.activeElement;
  if(e.shiftKey&&(a===first||a===root||!root.contains(a))){e.preventDefault();last.focus();return true;}
  if(!e.shiftKey&&(a===last||!root.contains(a))){e.preventDefault();first.focus();return true;}
  return false;
}
/** Remembers the element focused when the component first renders and gives focus back to it on unmount (WCAG 2.4.3). */
export function useReturnFocus(){
  const opener=React.useRef<HTMLElement|null>(null);
  if(opener.current===null&&typeof document!=='undefined'){const a=document.activeElement as HTMLElement|null;opener.current=a&&a!==document.body?a:null;}
  React.useEffect(()=>()=>{const el=opener.current;if(el&&el.isConnected&&typeof el.focus==='function')el.focus();},[]);
}
/** Exit confirmation (phone back button on the first screen): focus moves in, Tab is trapped, Escape = Stay, focus returns on close. */
export function ExitAsk({onStay,onExit}:{onStay:()=>void;onExit:()=>void}){
  useReturnFocus();
  const ref=React.useRef<HTMLDivElement>(null);
  React.useEffect(()=>{ref.current?.querySelector<HTMLElement>('button')?.focus();},[]);
  return <div className="exit-ask" role="alertdialog" aria-modal="true" aria-labelledby="exit-ask-title" aria-describedby="exit-ask-body" ref={ref} onKeyDown={e=>{if(e.key==='Escape'){e.preventDefault();onStay();}else if(ref.current)trapTab(e,ref.current);}}><div className="exit-card"><h2 id="exit-ask-title">Exit Hotel Control Centre?</h2><p id="exit-ask-body">Your demo data stays saved on this device.</p><div className="exit-actions"><button type="button" className="btn" onClick={onStay}>Stay</button><button type="button" className="btn primary" onClick={onExit}>Exit</button></div></div></div>;
}
/**
 * Decorative lucide icons and table header scopes, applied once to the live DOM (A-14, A-15):
 * every `svg.lucide` without its own name is hidden from assistive technology, every `<th>` in a `<thead>` gets `scope="col"`.
 */
export function enhanceA11y(root:ParentNode=document){
  root.querySelectorAll('svg.lucide:not([aria-hidden]):not([aria-label]):not([role])').forEach(n=>n.setAttribute('aria-hidden','true'));
  root.querySelectorAll('thead th:not([scope])').forEach(n=>n.setAttribute('scope','col'));
}
export function installA11yEnhancer(){
  if(typeof document==='undefined'||typeof MutationObserver==='undefined')return;
  let queued=false;
  const run=()=>{queued=false;enhanceA11y();};
  new MutationObserver(()=>{if(!queued){queued=true;(window.requestAnimationFrame||setTimeout)(run);}}).observe(document.body,{childList:true,subtree:true});
  enhanceA11y();
}
