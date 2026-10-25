// Global search (Ctrl+K, the magnifier in the top bar / phone app bar): type a page or action, see the exact path, press Enter to go there.
// Pages and quick actions only (owner decision); booking-ID lookup lives on Arrivals & check-in. Results respect the signed-in role.
import React,{useEffect,useMemo,useRef,useState} from 'react';
import {Search,ChevronRight,CornerDownLeft,X,ArrowRight,Zap} from 'lucide-react';
import {useHcc} from './context';
import {navItems} from './nav';
import {buildSearchEntries,searchEntries,visibleEntries,categoryOf,PAGE_CATEGORY,navFilterModules,type SearchEntry} from './navmap';

const esc=(t:string)=>t.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
function Hl({text,terms}:{text:string;terms:string[]}){
  const ts=terms.filter(Boolean);if(!ts.length)return <>{text}</>;
  const parts=text.split(new RegExp('('+ts.map(esc).join('|')+')','ig'));
  return <>{parts.map((p,i)=>i%2?<mark key={i}>{p}</mark>:<span key={i}>{p}</span>)}</>;
}
export function SearchPalette({onClose}:{onClose:()=>void}){
  const {s,uid,go,open,permission}=useHcc();
  const [q,setQ]=useState(''),[at,setAt]=useState(0);
  const ref=useRef<HTMLInputElement>(null);
  const all=useMemo(()=>buildSearchEntries(navItems),[]);
  const allowed=new Set(navFilterModules(s,navItems,uid).filter(n=>permission(n.p)).map(n=>n.id));
  const visible=visibleEntries(all,allowed,p=>permission(p));
  const terms=q.trim().toLowerCase().split(/\s+/).filter(Boolean);
  const hits:SearchEntry[]=terms.length?searchEntries(visible,q).slice(0,12).map(h=>h.entry)
    :[...visible.filter(e=>e.kind==='action'),...visible.filter(e=>e.kind==='page').slice(0,6)];
  useEffect(()=>{ref.current?.focus();},[]);
  useEffect(()=>{setAt(0);},[q]);
  const choose=(e:SearchEntry)=>{onClose();go(e.page);if(e.modal){const m=e.modal;window.setTimeout(()=>open(m),60);}};
  const onKey=(e:React.KeyboardEvent)=>{
    if(e.key==='Escape'){e.preventDefault();onClose();}
    else if(e.key==='ArrowDown'){e.preventDefault();setAt(i=>Math.min(hits.length-1,i+1));}
    else if(e.key==='ArrowUp'){e.preventDefault();setAt(i=>Math.max(0,i-1));}
    else if(e.key==='Enter'&&hits[at]){e.preventDefault();choose(hits[at]);}
  };
  useEffect(()=>{document.getElementById('search-opt-'+at)?.scrollIntoView({block:'nearest'});},[at]);
  return <div className="search-overlay" role="presentation" onMouseDown={e=>{if(e.target===e.currentTarget)onClose();}}>
    <div className="search-box" role="dialog" aria-modal="true" aria-label="Search pages and actions" onKeyDown={onKey}>
      <div className="search-field"><Search size={20} aria-hidden="true"/>
        <input ref={ref} value={q} onChange={e=>setQ(e.target.value)} placeholder="Search a page or action — e.g. check in, expense, rates" autoComplete="off" spellCheck={false}
          role="combobox" aria-expanded="true" aria-controls="search-list" aria-activedescendant={hits[at]?'search-opt-'+at:undefined} aria-label="Search pages and actions"/>
        <button type="button" className="search-close" aria-label="Close search" onClick={onClose}><X size={20}/></button></div>
      {!terms.length&&<p className="search-hint">Quick actions and popular pages. Type to search every page you can open.</p>}
      <ul id="search-list" role="listbox" className="search-list" aria-label="Results">
        {hits.map((e,i)=>{const cat=e.category==='dashboard'?undefined:categoryOf(PAGE_CATEGORY[e.page]);const Icon=e.kind==='action'?Zap:cat?.icon??Search;
          return <li key={e.id} id={'search-opt-'+i} role="option" aria-selected={i===at} className={i===at?'on':''} onMouseEnter={()=>setAt(i)} onClick={()=>choose(e)}>
            <span className="search-ic" aria-hidden="true"><Icon size={18}/></span>
            <span className="search-main"><strong><Hl text={e.label} terms={terms}/></strong>
              <small className="search-path">{e.path.map((p,k)=><React.Fragment key={k}>{k>0&&<ChevronRight size={12} aria-hidden="true"/>}<span><Hl text={p} terms={terms}/></span></React.Fragment>)}</small></span>
            {e.kind==='action'?<span className="search-tag">Action</span>:<ArrowRight size={16} aria-hidden="true" className="search-go"/>}
          </li>;})}
        {terms.length>0&&!hits.length&&<li className="search-empty" role="presentation">Nothing matches “{q.trim()}”. Try a shorter word, or a page name like “reservations”, “cashier” or “reports”.</li>}
      </ul>
      <p className="search-foot"><kbd>↑</kbd><kbd>↓</kbd> move <kbd><CornerDownLeft size={11}/></kbd> open <kbd>Esc</kbd> close</p>
    </div>
  </div>;
}
