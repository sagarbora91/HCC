// FX merge-ui · Customers 360 › Possible duplicates on the phone (sheet opened from the Customers page). Cards, not tables; 48 px targets; no sideways scroll at 360 px.
// Same data, commands and wording as the desktop (Merge.tsx): pairs → two stacked customer cards → confirm that lists what moves; a second approver decides.
import React,{useState} from 'react';
import {ArrowLeft} from 'lucide-react';
import {useHcc} from '../../app/context';
import {ListCard} from '../../app/mobile/components';
import {Pill,Empty} from '../../components/ui';
import {cmxRows,cmxReasonText,type CMXPair,type CMXMerge} from '../../domain/streams/customer-merge';
import {useMergeView,MergeConfirm,inHouseNote,type MergeAsk} from './Merge';

function Cards({pair,showMoney}:{pair:CMXPair;showMoney:boolean}){
  const rows=cmxRows(pair,showMoney);
  return <>{[pair.a,pair.b].map((m,i)=><section key={m.key} className="card cmx-m-card" aria-label={'Customer '+m.name}>
    <h3>{m.name}{pair.suggested===m.key&&<span className="cmx-sug"> · suggested to keep</span>}</h3>
    <dl className="cmx-m-dl">{rows.filter(r=>r.label!=='Name').map(r=><div key={r.label} className={r.differs?'differs':''}><dt>{r.label}</dt><dd>{i===0?r.a:r.b}</dd></div>)}</dl></section>)}</>;
}

export function MergeMobile(){
  const {uid,run,permission,busy,closeModal}=useHcc();
  const v=useMergeView();
  const [sel,setSel]=useState('');
  const [ask,setAsk]=useState<MergeAsk|null>(null);
  const pair=v.pairs.find(p=>p.id===sel);
  if(!v.canMerge)return <div className="cmx-m"><Empty title="Not available" body="Only an Owner or Manager can merge customers."/></div>;
  const back=()=>{if(ask)setAsk(null);else if(sel)setSel('');else closeModal();};
  const pend=(m:CMXMerge)=>{const mine=m.proposedBy===uid;
    return <ListCard key={m.id} title={`${m.sourceName} → ${m.targetName}`} sub={m.reason} right={mine?<Pill tone="orange">Needs another approver</Pill>:undefined}
      onClick={mine?undefined:()=>setAsk({kind:'decide',merge:m,approve:true})}/>;};
  return <div className="cmx-m">
    <button type="button" className="btn cmx-m-back" onClick={back}><ArrowLeft size={16}/>{ask||sel?'Back':'Close'}</button>
    <h2>Possible duplicates</h2>
    {ask?<><MergeConfirm key={ask.kind==='propose'?ask.pair.id+ask.keep:ask.merge.id+String(ask.approve)} ask={ask} onDone={()=>setAsk(null)}/>
      {ask.kind==='decide'&&ask.merge.proposedBy!==uid&&<button type="button" className="btn cmx-m-switch" onClick={()=>setAsk({kind:'decide',merge:ask.merge,approve:!ask.approve})}>{ask.approve?'Reject this request instead':'Approve this request instead'}</button>}</>
    :pair?<>
      <p className="muted">{cmxReasonText[pair.reason]}.</p>
      <Cards pair={pair} showMoney={permission('billing')}/>{inHouseNote(pair)}
      <div className="cmx-m-actions">{[pair.a,pair.b].map(m=><button key={m.key} type="button" className={'btn'+(pair.suggested===m.key?' primary':'')} disabled={busy} onClick={()=>setAsk({kind:'propose',pair,keep:m.key})}>Merge into {m.name}</button>)}
        {!v.server&&<button type="button" className="btn" disabled={busy} onClick={async()=>{if(await run({type:'CUSTMERGE_DISMISS',a:pair.a.key,b:pair.b.key},'Marked as not the same person. It will not be suggested again.'))setSel('');}}>Not the same person</button>}</div></>
    :<>
      {v.pending.length>0&&<><h3>Waiting for approval ({v.pending.length})</h3>{v.pending.map(pend)}
        {v.pending.some(m=>m.proposedBy!==uid)&&<p className="muted">Tap a request to approve or reject it.</p>}</>}
      {v.pairs.length?v.pairs.map(p=><ListCard key={p.id} title={`${p.a.name} · ${p.b.name}`} sub={cmxReasonText[p.reason]} onClick={()=>setSel(p.id)}/>)
        :<Empty title="No possible duplicates" body="No two customers share an e-mail address or a name without a mobile number."/>}
      {v.recent.length>0&&<><h3>Recent decisions</h3>{v.recent.map(m=><ListCard key={m.id} title={`${m.sourceName} → ${m.targetName}`} sub={m.reason} right={<Pill tone={m.status==='Approved'?'green':'neutral'}>{m.status}</Pill>}/>)}</>}</>}
  </div>;
}
