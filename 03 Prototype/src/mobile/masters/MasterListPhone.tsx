// MA-0 · Phone masters (mobile Master configuration group page): "Lists" cards → one master's rows as cards (search, status chips,
// reorder by arrow buttons, Add) → the 'mst-row' sheet (MasterSheet). Cards, not tables; 48 px targets; no sideways scroll at 360 px.
import React,{useState} from 'react';
import {ArrowUp,ArrowDown,Plus,Search,ListChecks,ChevronDown,ChevronUp} from 'lucide-react';
import {mstSpec,mstRows,mstUsage,mstMove,mstCanEdit,mstSpecsOfGroup,mstSummary,mstSearch,type CfgGroupId} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ChipFilter,ListCard} from '../../app/mobile/components';
import {mstFilterRows,type MstFilter} from '../../features/masters/MasterList';
import {mstNav} from '../../features/masters/nav';
import {mstTools} from '../../features/masters/tools';

export function MasterListPhone({spec:specId}:{spec:string}){
  const {s,uid,run,busy,open}=useHcc();
  const spec=mstSpec(specId);
  const [q,setQ]=useState('');const [f,setF]=useState<MstFilter>('active');
  if(!spec)return null;
  const all=mstRows(s,specId),rows=mstFilterRows(all,q,f),can=mstCanEdit(s,uid,spec),ext=mstTools[specId]; // MA-1 tools
  const move=(id:string,dir:-1|1)=>void run({type:'MST_MASTER_REORDER',master:specId,ids:mstMove(all,id,dir),key:`mst-order:${specId}:${id}:${dir}:${s.sequence}`},'Order saved');
  return <div className="mst-m-list" aria-label={spec.label}>
    {spec.help&&<p className="ma-intro">{spec.help}</p>}
    {!can.ok&&<p className="mst-lock" role="note">{can.reason}</p>}
    {ext?.Tools&&<ext.Tools specId={specId} phone/>}
    {can.ok&&!ext?.hideAdd&&<button type="button" className="btn primary mst-m-add" onClick={()=>open({type:'mst-row',id:specId+':'})}><Plus size={18}/> Add {spec.singular}</button>}
    <label className="cfg-search cfg-m-search"><Search size={18}/><input type="search" aria-label={`Search ${spec.label.toLowerCase()}`} placeholder="Search code or name" value={q} onChange={e=>setQ(e.target.value)} enterKeyHint="search"/></label>
    <ChipFilter label="Status" value={f} onChange={setF} options={[{id:'active',label:'Active',count:all.filter(r=>r.active).length},{id:'retired',label:'Retired',count:all.filter(r=>!r.active).length},{id:'all',label:'All',count:all.length}]}/>
    {rows.length?rows.map(r=>{const i=all.indexOf(r);const u=mstUsage(s,spec,r);
      return <div key={r.id} className={'mst-m-row'+(r.active?'':' retired')}>
        <ListCard title={r.name} sub={`${r.code} · used in ${u.count}`} meta={r.active?undefined:'Retired'} onClick={()=>open({type:'mst-row',id:`${specId}:${r.id}`})}/>
        {can.ok&&!ext?.noReorder&&f!=='retired'&&!q&&<div className="mst-m-order">
          <button type="button" className="btn" aria-label={`Move ${r.name} up`} disabled={busy||i===0} onClick={()=>move(r.id,-1)}><ArrowUp size={18}/></button>
          <button type="button" className="btn" aria-label={`Move ${r.name} down`} disabled={busy||i===all.length-1} onClick={()=>move(r.id,1)}><ArrowDown size={18}/></button></div>}
      </div>;}):<p className="muted">No {f==='retired'?'retired ':''}{spec.label.toLowerCase()}{q?` match “${q}”`:''}.</p>}
  </div>;
}

/** Phone "Lists" block of a Master configuration group: one card per master, expanding to its list. */
export function MstListsBlockPhone({g,q=''}:{g:CfgGroupId;q?:string}){
  const {s}=useHcc();
  const hits=q.trim()?new Set(mstSearch(q).map(x=>x.id)):undefined;
  const specs=mstSpecsOfGroup(g).filter(x=>!hits||hits.has(x.id));
  const [openId,setOpenId]=useState<string|undefined>(()=>{const x=mstNav.spec;if(x&&specs.some(p=>p.id===x)){mstNav.spec=undefined;return x;}return undefined;});
  if(!specs.length)return null;
  return <section className="mst-m-block" aria-label="Lists"><h2 className="ma-h2"><ListChecks size={18}/> Lists</h2>
    {specs.map(p=><div key={p.id} className="mst-m-spec">
      <button type="button" className="m-list-card" aria-expanded={openId===p.id} onClick={()=>setOpenId(openId===p.id?undefined:p.id)}>
        <div className="m-lc-main"><strong>{p.label}</strong><span>{mstSummary(s,p.id)}{p.money?' · Owner only':''}</span></div>{openId===p.id?<ChevronUp size={18} aria-hidden/>:<ChevronDown size={18} aria-hidden/>}</button>
      {openId===p.id&&<MasterListPhone spec={p.id}/>}
    </div>)}
  </section>;
}
