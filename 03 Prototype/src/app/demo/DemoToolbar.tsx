// Demo-only controls, deliberately separate from staff actions (Prototype Spec SCR-20). Removable for production.
import React from 'react';
import {FlaskConical,Users,RotateCcw,BookOpen,Layers,Zap} from 'lucide-react';
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
