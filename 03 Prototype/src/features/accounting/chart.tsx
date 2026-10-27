// ACC-1 · acc1-ui — Chart of accounts (group tree of the 28 Tally groups + custom sub-groups, ledger rows, Owner add / edit / retire, history)
// and Parties (list grouped by control ledger with balances). Contract §1, §2, §3, §7.
import React,{useMemo,useState} from 'react';
import {Plus,Pencil,Search,ChevronRight,Users} from 'lucide-react';
import {useHcc} from '../../app/context';
import {clockTime,Empty} from '../../components/ui';
import {accChart,accGroups,accChartHistory,accPartyLedgers,accTrialBalance6} from '../../services/mock';
import type {ACCGroupDef,ACCLedger} from '../../services/mock';
import {ListCard,ChipFilter} from '../../app/mobile/components';
import {useAccPerms,accUserName} from './parts';
import {drcr} from './shared';

type Filter='active'|'retired'|'all';
const depthOf=(g:ACCGroupDef,byId:Map<string,ACCGroupDef>)=>{let d=0,p=g.parent;while(p&&d<6){d++;p=byId.get(p)?.parent;}return d;};
/** Groups in tree order (parent, then its children) with their depth. */
export function groupTree(groups:ACCGroupDef[]):{g:ACCGroupDef;depth:number}[]{
  const byId=new Map(groups.map(g=>[g.id,g])),out:{g:ACCGroupDef;depth:number}[]=[];
  const add=(parent?:string)=>groups.filter(g=>(g.parent||undefined)===parent).forEach(g=>{out.push({g,depth:depthOf(g,byId)});add(g.id);});
  add(undefined);return out;
}

export function ChartView({phone}:{phone?:boolean}){
  const {s,open}=useHcc();const {canChart}=useAccPerms();
  const [q,setQ]=useState('');const [f,setF]=useState<Filter>('active');const [hideEmpty,setHideEmpty]=useState(true);
  const chart=useMemo(()=>accChart(s),[s]),groups=useMemo(()=>accGroups(s),[s]),hist=useMemo(()=>accChartHistory(s).slice(0,20),[s]);
  const byId=useMemo(()=>new Map(groups.map(g=>[g.id,g])),[groups]);
  const t=q.trim().toLowerCase();
  const rows=chart.filter(l=>(f==='all'||(f==='active')===l.active)&&(!t||l.code.includes(t)||l.name.toLowerCase().includes(t)));
  const own=(id:string)=>rows.filter(l=>l.group===id);
  const full=useMemo(()=>groupTree(groups),[groups]);
  const hasRows=(id:string):boolean=>own(id).length>0||full.some(x=>x.g.parent===id&&hasRows(x.g.id));
  const tree=full.filter(({g})=>(!hideEmpty&&!t)||hasRows(g.id)||(!t&&!!g.custom));
  const counts={active:chart.filter(l=>l.active).length,retired:chart.filter(l=>!l.active).length,all:chart.length};
  const natureOf=(l:ACCLedger)=>byId.get(l.group)?.nature??'Dr';
  const edit=(code:string)=>open({type:'acc-ledger',id:code}),addLedger=(g?:string)=>open({type:'acc-ledger',id:g?'new:'+g:'new'});
  const addGroup=(parent?:string)=>open({type:'acc-group',id:parent?'new:'+parent:'new'});
  const tools=<>
    <label className="acc-search"><Search size={16} aria-hidden/><input type="search" aria-label="Search ledgers" placeholder="Search code or name" value={q} onChange={e=>setQ(e.target.value)}/></label>
    {phone?<ChipFilter<Filter> label="Status" value={f} onChange={setF} options={(['active','retired','all'] as Filter[]).map(x=>({id:x,label:x==='active'?'Active':x==='retired'?'Retired':'All',count:counts[x]}))}/>
      :<div className="filters" role="group" aria-label="Status">{(['active','retired','all'] as Filter[]).map(x=><button type="button" key={x} className={f===x?'active':''} aria-pressed={f===x} onClick={()=>setF(x)}>{x==='active'?'Active':x==='retired'?'Retired':'All'} ({counts[x]})</button>)}</div>}
    <label className="acc-check"><input type="checkbox" checked={hideEmpty} onChange={e=>setHideEmpty(e.target.checked)}/> Hide empty groups</label></>;
  const note=<p className="muted acc-chart-note" role="note">{canChart?'The 28 Tally groups are fixed; you can add sub-groups and ledgers. A ledger can be retired only when it (and its parties) has a zero balance and no mapping points at it.':'Only the Owner can change the chart of accounts. You can read it and see its history.'}</p>;
  const history=<details className="acc-hist-box"><summary>History of changes ({hist.length})</summary>
    {hist.length?<ul className="acc-hist">{hist.map(h=><li key={h.id}><strong>{h.kind==='group'?'Group':'Ledger'} {h.code}</strong> · {h.field}: {h.before||'none'} → {h.after||'none'} · {accUserName(s,h.actor)} · {clockTime(h.at)}</li>)}</ul>:<p className="muted">No change yet. The built-in chart is as shipped.</p>}</details>;
  if(phone)return <>
    {note}{tools}
    {canChart&&<button type="button" className="btn full" onClick={()=>addGroup()}><Plus size={16}/> Add sub-group</button>}
    {tree.map(({g,depth})=><section key={g.id} className="acc-m-group">
      <h2 className="m-group">{g.name.toUpperCase()}<small className="acc-nat"> {g.nature} · {g.side==='BS'?'Balance sheet':'P&L'}{g.custom?' · custom':''}</small></h2>
      {own(g.id).map(l=><ListCard key={l.code} title={`${l.code} · ${l.name}`} sub={`${natureOf(l)} balance${l.control?' · control ledger (parties)':''}`} meta={l.active?(l.system?'Built-in':'Owner-added'):'Retired'}
        right={l.active?undefined:<span className="pill acc-st-closed">Retired</span>} onClick={()=>edit(l.code)}/>)}
      {depth>=0&&!own(g.id).length&&<p className="muted acc-m-note">No ledgers shown.</p>}
      {g.custom&&canChart&&<button type="button" className="btn full" onClick={()=>open({type:'acc-group',id:g.id})}><Pencil size={16}/> Edit sub-group</button>}
    </section>)}
    {history}
    {canChart&&<div className="m-sticky-footer"><button type="button" className="btn primary" onClick={()=>addLedger()}><Plus size={16}/> Add ledger</button></div>}</>;
  return <div className="padded acc-chart">
    <div className="acc-subbar acc-chart-tools">{tools}{canChart&&<span className="inline"><button type="button" className="btn" onClick={()=>addGroup()}><Plus size={16}/> Add sub-group</button><button type="button" className="btn primary" onClick={()=>addLedger()}><Plus size={16}/> Add ledger</button></span>}</div>
    {note}
    {rows.length||tree.length?<div className="table-wrap"><table className="acc-table"><thead><tr><th>Code</th><th>Ledger</th><th>Group</th><th>Nature</th><th>Control</th><th>Active</th><th/></tr></thead>
      <tbody>{tree.map(({g,depth})=><React.Fragment key={g.id}>
        <tr className="acc-grp-row"><td colSpan={5}><span className="acc-grp-name" style={{paddingLeft:depth*18}}><ChevronRight size={13} aria-hidden/> <strong>{g.name}</strong> <small className="muted">{g.nature} · {g.side==='BS'?'Balance sheet':'P&L'}{g.custom?' · custom sub-group':''}</small></span></td><td/>
          <td className="acc-num">{canChart&&<span className="inline"><button type="button" className="text-btn" onClick={()=>addLedger(g.id)}>Add ledger</button><button type="button" className="text-btn" onClick={()=>addGroup(g.id)}>Add sub-group</button>{g.custom&&<button type="button" className="text-btn" onClick={()=>open({type:'acc-group',id:g.id})}>Edit</button>}</span>}</td></tr>
        {own(g.id).map(l=><tr key={l.code} className={l.active?'':'acc-retired'}><td className="number">{l.code}</td><td><strong>{l.name}</strong>{l.note&&<small className="acc-sub">{l.note}</small>}</td><td>{g.name}</td><td>{natureOf(l)}</td>
          <td>{l.control?<span className="pill blue"><Users size={12} aria-hidden/>Parties</span>:'—'}</td><td>{l.active?<span className="pill green">Active</span>:<span className="pill acc-st-closed">Retired</span>}</td>
          <td className="acc-num"><button type="button" className="btn small-btn" onClick={()=>edit(l.code)}>{canChart?'Edit':'View'}</button></td></tr>)}</React.Fragment>)}</tbody></table></div>
      :<Empty title="No ledger matches" body="Change the search or the status filter."/>}
    {history}</div>;
}

// ── Parties ──
export function PartiesView({phone,onStatement}:{phone?:boolean;onStatement:(account:string)=>void}){
  const {s}=useHcc();const [q,setQ]=useState('');
  const chart=useMemo(()=>accChart(s),[s]),parties=useMemo(()=>accPartyLedgers(s),[s]);
  const tb=useMemo(()=>accTrialBalance6(s,s.date,s.date,true),[s]);
  const bal=useMemo(()=>new Map(tb.rows.map(r=>[r.account,r.closingDebit-r.closingCredit])),[tb]);
  const t=q.trim().toLowerCase();
  const controls=chart.filter(l=>l.control);
  const sum=(c:string)=>parties.filter(p=>p.control===c).reduce((a,p)=>a+(bal.get(p.id)??0),0)+(bal.get(c)??0);
  const search=<label className="acc-search"><Search size={16} aria-hidden/><input type="search" aria-label="Search parties" placeholder="Search party or code" value={q} onChange={e=>setQ(e.target.value)}/></label>;
  const block=(c:ACCLedger)=>{
    const ps=parties.filter(p=>p.control===c.code&&(!t||p.name.toLowerCase().includes(t)||p.partyId.toLowerCase().includes(t)));
    const unassigned=bal.get(c.code)??0;
    if(t&&!ps.length)return null;
    return <section key={c.code} className="acc-m-group">
      <h2 className={phone?'m-group':'acc-h3'}>{c.code} · {c.name}<small className="acc-nat"> · {ps.length} {ps.length===1?'party':'parties'} · {drcr(sum(c.code))}</small></h2>
      {ps.length?phone?ps.map(p=><ListCard key={p.id} title={p.name} sub={p.id} meta={p.active?undefined:'Retired'} right={<strong className="number">{drcr(bal.get(p.id)??0)}</strong>} onClick={()=>onStatement(p.id)}/>)
        :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Ledger id</th><th>Party</th><th>Status</th><th className="acc-num">Balance</th><th/></tr></thead><tbody>{ps.map(p=><tr key={p.id}><td className="number">{p.id}</td><td><strong>{p.name}</strong></td>
          <td>{p.active?<span className="pill green">Active</span>:<span className="pill acc-st-closed">Retired</span>}</td><td className="acc-num number">{drcr(bal.get(p.id)??0)}</td><td className="acc-num"><button type="button" className="btn small-btn" onClick={()=>onStatement(p.id)}>Statement</button></td></tr>)}</tbody></table></div>
        :<p className="muted acc-m-note">No party yet — they come from the masters.</p>}
      {unassigned!==0&&<p className="muted acc-m-note">Posted to the control ledger without a party: {drcr(unassigned)} (see Exceptions › no party).</p>}
    </section>;};
  return <div className={phone?'':'padded'}>{search}<p className="muted acc-chart-note">Parties come from the masters (companies, channels, staff, bank accounts, vendors); balances are as of the business date.</p>
    {controls.map(block)}</div>;
}
