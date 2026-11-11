// FX merge-ui · Customers 360 › Possible duplicates (training finding F18). Desktop: list of pairs + merge requests on the left, side-by-side compare / confirm on the right
// (the list scrolls in its panel, DEC-213). The phone version is MergeMobile.tsx. Data: domain/streams/customer-merge.ts (demo: derived; production: the server's lists).
// Merging is maker-checker like the server: "Merge into this one" sends a request, a different Owner / Manager approves. Nothing here is undone from the screen.
import React,{useEffect,useMemo,useState} from 'react';
import {Lock,Users,Check,X,GitMerge} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Pill,Empty} from '../../components/ui';
import {ScrollPanel,SplitView,FitNote} from '../../components/fit';
import {getLegacyLookup} from './legacy-source';
import {ensurePairProfiles,getPairProfile,clearPairProfiles} from './profile-source';
import {cmxView,cmxRows,cmxMoves,cmxReasonText,CMX_REASON_MIN,CMX_REASON_MAX,CMX_UNDO_TEXT,CMX_APPROVE_UNDO_TEXT,CMX_LIST_NOTE,cmxApproveToast,type CMXPair,type CMXMember,type CMXMerge,type CMXView} from '../../domain/streams/customer-merge';

/** What the user is confirming: a new request (keep = survivor), or a decision on a request. */
export type MergeAsk={kind:'propose';pair:CMXPair;keep:string}|{kind:'decide';merge:CMXMerge;approve:boolean};

export function useMergeView():CMXView{
  const {s,uid}=useHcc();
  return useMemo(()=>cmxView(s,uid,getLegacyLookup()),[s.bookings,s.guests,s.customerMergeX,s.date,uid]); // eslint-disable-line react-hooks/exhaustive-deps
}
/** Production: the opened pair with preferences, notes and tags read on demand (two profile reads, remembered). Demo: the pair as it is. */
export function usePairProfiles(pair:CMXPair|undefined,server:boolean):CMXPair|undefined{
  const [,bump]=useState(0);
  useEffect(()=>{if(!pair||!server)return;let live=true;void ensurePairProfiles([pair.a.key,pair.b.key]).then(ch=>{if(live&&ch)bump(n=>n+1);});return ()=>{live=false;};},[pair?.id,server]); // eslint-disable-line react-hooks/exhaustive-deps
  if(!pair||!server)return pair;
  const fill=(m:CMXMember):CMXMember=>{const p=getPairProfile(m.key);return p?{...m,preferences:p.preferences,notes:p.notes,tags:[...new Set([...m.tags,...p.tags])]}:m;};
  return {...pair,a:fill(pair.a),b:fill(pair.b)};
}
export const sideOf=(p:CMXPair,key:string):{keep:CMXMember;drop:CMXMember}=>p.a.key===key?{keep:p.a,drop:p.b}:{keep:p.b,drop:p.a};
const blank=(key:string,name:string):CMXMember=>({key,name,phone:'',email:'',stays:0,nights:0,tags:[]});

/** The confirm body, shared by desktop and phone: what moves, that it cannot be undone from here, a reason / note field and the buttons. */
export function MergeConfirm({ask,onDone}:{ask:MergeAsk;onDone:()=>void}){
  const {run,busy,uid,s}=useHcc();
  const server=!!s.customerMergeX?.server;
  const [text,setText]=useState(ask.kind==='propose'?cmxReasonText[ask.pair.reason]:'');
  const [err,setErr]=useState('');
  if(ask.kind==='propose'){
    const {keep,drop}=sideOf(ask.pair,ask.keep);
    const send=async()=>{
      const r=text.trim();
      if(r.length<CMX_REASON_MIN||r.length>CMX_REASON_MAX){setErr(`Give a reason of ${CMX_REASON_MIN}-${CMX_REASON_MAX} characters.`);return;}
      const ok=await run({type:'CUSTMERGE_PROPOSE',source:drop.key,target:keep.key,reason:r},`Merge of ${drop.name} into ${keep.name} sent for approval`);
      if(ok)onDone();
    };
    return <div className="cmx-confirm" role="region" aria-label="Confirm merge">
      <h3>Merge {drop.name} into {keep.name}?</h3>
      <ul className="cmx-moves">{cmxMoves(keep,drop).map(t=><li key={t}>{t}</li>)}</ul>
      <p className="cmx-warn" role="note">{CMX_UNDO_TEXT}</p>
      <label className="cmx-field">Reason<input value={text} maxLength={CMX_REASON_MAX} onChange={e=>{setText(e.target.value);setErr('');}} aria-invalid={!!err}/></label>
      {err&&<p className="cmx-err" role="alert">{err}</p>}
      <div className="inline cmx-actions"><button type="button" className="btn primary" disabled={busy} onClick={send}><GitMerge size={16}/>Send merge for approval</button>
        <button type="button" className="btn" onClick={onDone}>Cancel</button></div></div>;
  }
  const m=ask.merge,mine=m.proposedBy===uid;
  const decide=async()=>{
    const ok=await run({type:'CUSTMERGE_DECIDE',id:m.id,approve:ask.approve,note:text.trim()||undefined,version:m.version},
      ask.approve?cmxApproveToast(server,m):'Merge rejected');
    if(ok){clearPairProfiles();onDone();}
  };
  return <div className="cmx-confirm" role="region" aria-label={ask.approve?'Approve merge':'Reject merge'}>
    <h3>{ask.approve?'Approve':'Reject'} merge of {m.sourceName} into {m.targetName}?</h3>
    <p className="muted">Reason given: {m.reason}</p>
    {ask.approve&&server&&<p className="muted" role="note">{CMX_LIST_NOTE}</p>}
    {ask.approve&&<><ul className="cmx-moves">{cmxMoves(blank(m.target,m.targetName),blank(m.source,m.sourceName)).slice(1).map(t=><li key={t}>{t}</li>)}</ul>
      <p className="cmx-warn" role="note">{CMX_APPROVE_UNDO_TEXT} All stays of {m.sourceName} will show under {m.targetName}.</p></>}
    {mine&&<p className="cmx-err" role="alert">You sent this request. A different Owner or Manager must decide it.</p>}
    <label className="cmx-field">Note (optional)<input value={text} maxLength={CMX_REASON_MAX} onChange={e=>setText(e.target.value)}/></label>
    <div className="inline cmx-actions"><button type="button" className={'btn '+(ask.approve?'primary':'')} disabled={busy||mine} onClick={decide}>{ask.approve?<><Check size={16}/>Approve and merge</>:<><X size={16}/>Reject</>}</button>
      <button type="button" className="btn" onClick={onDone}>Cancel</button></div></div>;
}

/** Side-by-side table of two customers; differing rows are marked. */
export function CompareTable({pair,showMoney}:{pair:CMXPair;showMoney:boolean}){
  const rows=cmxRows(pair,showMoney);
  const head=(m:CMXMember)=><th scope="col">{m.name}{pair.suggested===m.key&&<span className="cmx-sug"> · suggested to keep</span>}</th>;
  return <table className="cmx-table" aria-label="Compare the two customers"><thead><tr><th scope="col"><span className="sr-only">Field</span></th>{head(pair.a)}{head(pair.b)}</tr></thead>
    <tbody>{rows.map(r=><tr key={r.label} className={r.differs?'differs':''}><th scope="row">{r.label}</th><td>{r.a}</td><td>{r.b}</td></tr>)}</tbody></table>;
}

export function PendingCard({m,uid,onAsk}:{m:CMXMerge;uid:string;onAsk:(a:MergeAsk)=>void}){
  const mine=m.proposedBy===uid;
  return <li className="cmx-pending"><div className="cmx-pending-main"><strong>{m.sourceName} → {m.targetName}</strong><small>{m.reason}</small></div>
    <div className="inline">{mine?<Pill tone="orange">Waiting for another approver</Pill>:<>
      <button type="button" className="btn primary" onClick={()=>onAsk({kind:'decide',merge:m,approve:true})}>Approve</button>
      <button type="button" className="btn" onClick={()=>onAsk({kind:'decide',merge:m,approve:false})}>Reject</button></>}</div></li>;
}
export const inHouseNote=(p:CMXPair)=>(p.a.inHouse||p.b.inHouse)?<p className="cmx-warn" role="note"><Lock size={13}/> One of these customers is in house now.</p>:null;

export function MergePanel(){
  const {uid,run,permission,busy}=useHcc();
  const v=useMergeView();
  const money=permission('billing');
  const [sel,setSel]=useState('');
  const [ask,setAsk]=useState<MergeAsk|null>(null);
  const pair=usePairProfiles(v.pairs.find(p=>p.id===sel),v.server);
  if(!v.canMerge)return <section className="card cust-empty"><Empty title="Not available" body="Only an Owner or Manager can merge customers."/></section>;
  const dismiss=async(p:CMXPair)=>{if(await run({type:'CUSTMERGE_DISMISS',a:p.a.key,b:p.b.key},'Marked as not the same person. It will not be suggested again.'))setSel('');};
  const note=v.server?'Pairs come from the server: same e-mail address, or the same name with no mobile number. A merge needs a second approver and keeps all history. '+CMX_LIST_NOTE
    :'Pairs: same e-mail address, or the same name with no mobile number. A merge needs a second approver and keeps all history. All records are fictional.';
  const list=<ScrollPanel title="Possible duplicates" label="Possible duplicates list" actions={<span className="muted">{v.pairs.length} pair{v.pairs.length===1?'':'s'}</span>} footer={<FitNote text={note}/>}>
    {v.pending.length>0&&<><h3 className="gs-sub">Waiting for approval ({v.pending.length})</h3><ul className="cmx-list">{v.pending.map(m=><PendingCard key={m.id} m={m} uid={uid} onAsk={a=>{setAsk(a);setSel('');}}/>)}</ul></>}
    {v.pairs.length?<ul className="cust-list cmx-pairs">{v.pairs.map(p=><li key={p.id}><button type="button" className={'cust-item'+(sel===p.id?' active':'')} aria-pressed={sel===p.id} onClick={()=>{setSel(p.id);setAsk(null);}}>
      <span className="avatar" aria-hidden="true"><Users size={16}/></span>
      <span className="cust-main"><strong>{p.a.name} · {p.b.name}</strong><small>{cmxReasonText[p.reason]}</small></span></button></li>)}</ul>
      :<Empty title="No possible duplicates" body="No two customers share an e-mail address or a name without a mobile number."/>}
    {v.recent.length>0&&<><h3 className="gs-sub">Recent decisions</h3><ul className="cmx-list">{v.recent.map(m=><li key={m.id} className="cmx-pending"><div className="cmx-pending-main"><strong>{m.sourceName} → {m.targetName}</strong>
      <small>{m.reason}{m.note?' · '+m.note:''}</small></div><Pill tone={m.status==='Approved'?'green':'neutral'}>{m.status}</Pill></li>)}</ul>{v.server&&v.recent.some(m=>m.status==='Approved')&&<p className="muted" role="note">{CMX_LIST_NOTE}</p>}</>}
  </ScrollPanel>;
  const detail=ask?<section className="card cmx-detail"><MergeConfirm key={ask.kind==='propose'?ask.pair.id+ask.keep:ask.merge.id+String(ask.approve)} ask={ask} onDone={()=>setAsk(null)}/></section>
    :pair?<section className="card cmx-detail" aria-label="Compare customers">
      <h2>Are these the same person?</h2><p className="muted">{cmxReasonText[pair.reason]}. Mobile numbers stay masked for roles that cannot see them.</p>
      <CompareTable pair={pair} showMoney={money}/>{inHouseNote(pair)}
      <div className="inline cmx-actions">{[pair.a,pair.b].map(m=><button key={m.key} type="button" className={'btn'+(pair.suggested===m.key?' primary':'')} disabled={busy} onClick={()=>setAsk({kind:'propose',pair,keep:m.key})}>Merge into {m.name}</button>)}
        {!v.server&&<button type="button" className="btn" disabled={busy} onClick={()=>dismiss(pair)}>Not the same person</button>}</div>
    </section>:null;
  return <SplitView label="Possible duplicates" ratio="2fr 3fr" onCloseDetail={()=>{setSel('');setAsk(null);}} list={list} detail={detail}
    empty={<section className="card cust-empty"><Empty title="Choose a pair" body="Compare the two customers side by side, then merge them or mark them as different people."/></section>}/>;
}
export const mergeCount=(v:CMXView)=>v.pairs.length+v.pending.length;
