// MA-0 · MasterList (desktop / tablet): one master's rows beside the selected row's MasterForm. Search, Active / Retired / All
// filter, "used in" counts, reorder with arrow buttons (no drag), Add. Phone version: mobile/masters/MasterListPhone.tsx.
// MstListsBlock = the "Lists" block a Master configuration group shows for the masters registered in it (HotelSettings.tsx);
// MstLink = the "Manage list…" button module pages show instead of their own add forms.
import React,{useState} from 'react';
import {ArrowUp,ArrowDown,Plus,Search,ListChecks,ExternalLink,ChevronRight} from 'lucide-react';
import {mstSpec,mstRows,mstUsage,mstMove,mstCanEdit,mstSpecsOfGroup,mstSummary,mstSearch,type MasterRow,type CfgGroupId} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty} from '../../components/ui';
import {cfgNav} from '../admin/config/logic';
import {MasterForm} from './MasterForm';
import {mstNav} from './nav';

export type MstFilter='active'|'retired'|'all';
/** Rows after search + status filter (shared by desktop and phone). */
export function mstFilterRows(rows:MasterRow[],q:string,f:MstFilter){
  const t=q.trim().toLowerCase();
  return rows.filter(r=>(f==='all'||(f==='active')===r.active)&&(!t||r.code.toLowerCase().includes(t)||r.name.toLowerCase().includes(t)));
}
/** Props of MasterList: spec = master id (registerMaster id); select = row to open first. */
export type MasterListProps={spec:string;select?:string};
export function MasterList({spec:specId,select}:MasterListProps){
  const {s,uid,run,busy}=useHcc();
  const spec=mstSpec(specId);
  const [q,setQ]=useState('');const [f,setF]=useState<MstFilter>('active');
  const [sel,setSel]=useState<string|undefined>(select);const [adding,setAdding]=useState(false);
  if(!spec)return <Empty title="List not found"/>;
  const all=mstRows(s,specId),rows=mstFilterRows(all,q,f);
  const can=mstCanEdit(s,uid,spec);
  const move=(id:string,dir:-1|1)=>{const ids=mstMove(all,id,dir);void run({type:'MST_MASTER_REORDER',master:specId,ids,key:`mst-order:${specId}:${id}:${dir}:${s.sequence}`},'Order saved');};
  const current=adding?undefined:sel&&all.some(r=>r.id===sel)?sel:undefined;
  const counts={active:all.filter(r=>r.active).length,retired:all.filter(r=>!r.active).length,all:all.length};
  return <div className="mst-list" aria-label={spec.label}>
    <div className="mst-list-head"><div><h3>{spec.label}</h3>{spec.help&&<p className="muted">{spec.help}</p>}</div>
      {can.ok&&<button type="button" className="btn primary" onClick={()=>{setAdding(true);setSel(undefined);}}><Plus size={16}/> Add {spec.singular}</button>}</div>
    {!can.ok&&<p className="mst-lock" role="note">{can.reason}</p>}
    <div className="mst-tools">
      <label className="cfg-search mst-search"><Search size={16}/><input type="search" aria-label={`Search ${spec.label.toLowerCase()}`} placeholder="Search code or name" value={q} onChange={e=>setQ(e.target.value)}/></label>
      <div className="filters" role="group" aria-label="Status">{(['active','retired','all'] as const).map(x=><button type="button" key={x} className={f===x?'active':''} aria-pressed={f===x} onClick={()=>setF(x)}>{x==='active'?'Active':x==='retired'?'Retired':'All'} ({counts[x]})</button>)}</div>
    </div>
    <div className="mst-split">
      <ul className="mst-rows">{rows.length?rows.map(r=>{const u=mstUsage(s,spec,r);const i=all.indexOf(r);
        return <li key={r.id} className={'mst-row'+(r.id===current?' on':'')+(r.active?'':' retired')}>
          <button type="button" className="mst-row-main" aria-current={r.id===current?'true':undefined} onClick={()=>{setSel(r.id);setAdding(false);}}>
            <span className="mst-code">{r.code}</span><span className="mst-name"><strong>{r.name}</strong><small>Used in {u.count}{r.active?'':' · retired'}</small></span><ChevronRight size={15} aria-hidden="true"/></button>
          {can.ok&&f!=='retired'&&!q&&<span className="mst-order">
            <button type="button" className="icon-btn" aria-label={`Move ${r.name} up`} disabled={busy||i===0} onClick={()=>move(r.id,-1)}><ArrowUp size={15}/></button>
            <button type="button" className="icon-btn" aria-label={`Move ${r.name} down`} disabled={busy||i===all.length-1} onClick={()=>move(r.id,1)}><ArrowDown size={15}/></button></span>}
        </li>;}):<li className="muted mst-none">No {f==='retired'?'retired ':''}{spec.label.toLowerCase()}{q?` match “${q}”`:''}.</li>}</ul>
      <div className="mst-detail">{adding?<MasterForm key={'new:'+s.sequence} specId={specId} onDone={()=>setAdding(false)} onCancel={()=>setAdding(false)}/>
        :current?<MasterForm key={current+':'+s.sequence} specId={specId} rowId={current} onDone={id=>setSel(id)} onCancel={()=>setSel(undefined)}/>
        :<p className="muted mst-pick">Select a {spec.singular} to see it, its use and its history{can.ok?`, or add a new one`:''}.</p>}</div>
    </div>
  </div>;
}

/** "Lists" block of a Master configuration group (desktop): the masters registered in the group → MasterList. */
export function MstListsBlock({g,q=''}:{g:CfgGroupId;q?:string}){
  const {s}=useHcc();
  const hits=q.trim()?new Set(mstSearch(q).map(x=>x.id)):undefined;
  const specs=mstSpecsOfGroup(g).filter(x=>!hits||hits.has(x.id));
  const [open,setOpen]=useState<string|undefined>(()=>{const x=mstNav.spec;if(x&&specs.some(p=>p.id===x)){mstNav.spec=undefined;return x;}return specs.length===1?specs[0].id:undefined;});
  if(!specs.length)return null;
  return <section className="mst-block" aria-label="Lists"><h3 className="mst-block-title"><ListChecks size={16}/> Lists</h3>
    <ul className="mst-specs">{specs.map(p=><li key={p.id}><button type="button" className={'mst-spec'+(open===p.id?' on':'')} aria-expanded={open===p.id} onClick={()=>setOpen(open===p.id?undefined:p.id)}>
      <strong>{p.label}</strong><small>{mstSummary(s,p.id)}</small>{p.money&&<Pill tone="orange">Owner only</Pill>}</button></li>)}</ul>
    {open&&<MasterList key={open} spec={open}/>}
  </section>;
}

/** "Manage list…" for module pages: opens Master configuration at the list (Owner / Tech admin, or anyone granted `masters`). */
export function MstLink({spec,label}:{spec:string;label?:string}){
  const {go,permission}=useHcc();
  const sp=mstSpec(spec);
  if(!sp||!permission(sp.money||sp.permission==='settings'?'admin':['masters']))return null;
  return <button type="button" className="text-btn mst-link" onClick={()=>{cfgNav.group=sp.group;mstNav.spec=sp.id;go('settings');}}><ExternalLink size={13}/> {label??`Manage list… · ${sp.label}`}</button>;
}
