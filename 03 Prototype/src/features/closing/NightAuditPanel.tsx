// F8-QW-NA · Night audit panel: first panel of Day closing › Preflight. Show only: it adds no blocker (QWQ-1). The checklist HOVT-NA-PRE can be
// ticked from the passing lines through the existing HOV_ITEM command with the note "auto".
import React from 'react';
import {ArrowUpRight} from 'lucide-react';
import {naChecklist,naAutoTicks,naTicksToSend,NA_TEMPLATE} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill} from '../../components/ui';

export function NightAuditPanel(){
  const {s,go,run,busy,permission}=useHcc();
  const lines=naChecklist(s),ticks=naAutoTicks(s),todo=naTicksToSend(s);
  const ops=lines.filter(l=>l.group==='operational'),meas=lines.filter(l=>l.group==='measurement');
  const row=(l:typeof lines[number])=><li key={l.key} className={l.ok?'ok':'bad'}>
    <div><strong>{l.ok?'✓':'✗'} {l.label}</strong><small>{l.detail}{!l.ok&&l.blocking?' · blocks the close':''}</small></div>
    {l.link&&!l.ok&&<button className="btn small-btn" onClick={()=>go(l.link!)}>Open <ArrowUpRight size={13}/></button>}</li>;
  const tickAll=async()=>{for(const item of todo){if(!(await run({type:'HOV_ITEM',template:NA_TEMPLATE,item,done:true,note:'auto'}))) break;}};
  return <section className="f-night-audit" aria-label="Night audit">
    <h3>Night audit <Pill tone={lines.every(l=>l.ok)?'green':'orange'}>{lines.filter(l=>!l.ok).length?`${lines.filter(l=>!l.ok).length} to look at`:'All clear'}</Pill></h3>
    <ul className="f-checks">{ops.map(row)}</ul>
    {!!meas.length&&<><h4>Measurement exceptions</h4><ul className="f-checks">{meas.map(row)}</ul></>}
    {!!ticks.length&&<p className="muted">Checklist “Night audit pre-checks”: {ticks.filter(t=>t.done).length} of {ticks.length} automatic items ticked.
      {!!todo.length&&permission('close')&&<> <button className="btn small-btn" disabled={busy} onClick={tickAll}>Tick {todo.length} passing item{todo.length===1?'':'s'}</button></>}</p>}
  </section>;
}
