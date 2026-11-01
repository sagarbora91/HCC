// Demo-only controls, deliberately separate from staff actions (Prototype Spec SCR-20). Removable for production.
import React from 'react';
import {FlaskConical,Users,RotateCcw,BookOpen,Layers,Zap,Wifi,ChevronDown} from 'lucide-react';
import {scenarios} from '../../services/mock';
import {faultModes,FaultMode} from '../../services/mock/faults';
import {useHcc} from '../context';
import {DisplayToggle} from '../display';
export function DemoToolbar(){
  const {s,uid,switchUser,fault,setFault,open,busy,display}=useHcc();
  const scen=scenarios.find(x=>x.id===s.scenario);
  return <div className="demo-toolbar" role="region" aria-label="Demo controls">
    <span className="demo-label"><FlaskConical size={14}/>DEMO CONTROLS</span>
    <button className="demo-btn" onClick={()=>open({type:'scenario'})} title="Choose a fixture scenario"><Layers size={14}/>Scenario: <strong>{s.scenario}</strong><span className="demo-sub">{scen?.name}</span></button>
    <label className="demo-field"><Users size={14}/><span>Preview as</span><select aria-label="Preview as demo user" value={uid} onChange={e=>switchUser(e.target.value)}>{s.users.map(u=><option key={u.id} value={u.id}>{u.name}{!u.enabled?' (disabled)':''}</option>)}</select></label>
    <label className={'demo-field '+(fault!=='none'?'armed':'')}><Zap size={14}/><span>Next action</span><select aria-label="Simulate a fault on the next action" value={fault} onChange={e=>setFault(e.target.value as FaultMode)}>{faultModes.map(f=><option key={f.id} value={f.id}>{f.label}</option>)}</select></label>
    {busy&&<span className="demo-busy" role="status">Waiting for simulated response…</span>}
    <span className="demo-spacer"/>
    <DisplayToggle display={display} compact/>
    <button className="demo-btn" onClick={()=>open({type:'reset'})}><RotateCcw size={14}/>Reset</button>
    <button className="demo-btn" onClick={()=>open({type:'guide'})}><BookOpen size={14}/>Walkthrough</button>
    <span className="demo-note">Role switching is a demo tool, not sign-in.</span>
  </div>;
}

/** FIT §3 (prototype build only): the demo controls collapse into one amber "DEMO" pill in the top bar. It opens a popover holding the
 *  DemoToolbar above unchanged (scenario, preview as, next action fault, display mode, reset, walkthrough) plus the "fictional data" and
 *  "local mock service" notes that used to sit in the top bar. Escape or a click outside closes it; focus returns to the pill. */
export function DemoPill(){
  const {s,fault,busy}=useHcc();
  const [open,setOpen]=React.useState(false);
  const wrap=React.useRef<HTMLDivElement>(null),btn=React.useRef<HTMLButtonElement>(null);
  React.useEffect(()=>{if(!open)return;
    const away=(e:Event)=>{if(wrap.current&&!wrap.current.contains(e.target as Node))setOpen(false);};
    const key=(e:KeyboardEvent)=>{if(e.key==='Escape'&&!e.defaultPrevented){e.preventDefault();setOpen(false);btn.current?.focus();}};
    document.addEventListener('mousedown',away);document.addEventListener('touchstart',away);document.addEventListener('keydown',key);
    return ()=>{document.removeEventListener('mousedown',away);document.removeEventListener('touchstart',away);document.removeEventListener('keydown',key);};},[open]);
  // A dialog the demo controls open (scenario, reset, walkthrough) closes the popover so it never sits over the dialog.
  const onClick=(e:React.MouseEvent)=>{if((e.target as HTMLElement).closest('.demo-btn'))setOpen(false);};
  return <div className="fit-demo" ref={wrap}>
    <button type="button" ref={btn} className={'fit-demo-pill'+(fault!=='none'?' armed':'')} aria-expanded={open} aria-controls="fit-demo-pop"
      title={`Demo controls · scenario ${s.scenario} · fictional data`} onClick={()=>setOpen(v=>!v)}><FlaskConical size={14}/>DEMO{busy?'…':''}<ChevronDown size={14}/></button>
    {open&&<div className="fit-demo-pop" id="fit-demo-pop" role="dialog" aria-label="Demo controls" onClick={onClick}>
      <div className="fit-demo-notes"><span className="demo-badge">DEMO — FICTIONAL DATA</span>
        <span className="conn-chip" title="All data comes from the in-browser mock service"><Wifi size={13}/>Local mock service</span></div>
      <DemoToolbar/>
    </div>}
  </div>;
}
