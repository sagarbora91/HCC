// Mobile sheets for the demo controls (M24, collapsed DEMO pill) and the user menu (profile: change my password / PIN, display mode,
// sign out; demo: switch user). The user menu is also the desktop profile dialog (top-bar name chip).
import React from 'react';
import {Layers,Users,RotateCcw,BookOpen,LogOut,Smartphone,FlaskConical,ArrowRight,KeyRound,Keyboard} from 'lucide-react';
import {useHcc} from '../context';
import {scenarios} from '../../services/mock';
import {faultModes,FaultMode} from '../../services/mock/faults';
import {DisplayToggle} from '../display';
import {guideSteps,type GuideStep} from '../demo/DemoModals';
import {useOwnCredentialKind,wordOf} from '../OwnCredential';
import {PrefsQuickSheet} from '../../features/prefs/MenuPrefs'; // CP-1 text size + Appearance & display link
function UserSelect(){const {s,uid,switchUser}=useHcc();
  return <label><span>Preview as (demo user)</span><select value={uid} onChange={e=>switchUser(e.target.value)}>{s.users.map(u=><option key={u.id} value={u.id}>{u.name}{!u.enabled?' (disabled)':''}</option>)}</select></label>;}
export function DemoSheet(){
  const {s,fault,setFault,open,display,switchUser,go}=useHcc();const scen=scenarios.find(x=>x.id===s.scenario);
  const help=faultModes.find(f=>f.id===fault)?.help;
  const goThere=(x:GuideStep)=>{switchUser(x.actor);go(x.page);};
  const who=(id:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id;
  return <><div className="modal-heading"><FlaskConical size={25}/><h2>Demo controls</h2><p>Demo tools are not staff features. All records are fictional.</p></div>
    <div className="info-note mo-device-note" role="note"><Smartphone size={17}/><span>Demo data is stored <strong>on this device only</strong> — two phones (or a phone and a PC) do not share bookings, tasks or cash.</span></div>
    <div className="form m-sheet-form">
      <button type="button" className="btn full mo-btn" onClick={()=>open({type:'scenario'})}><Layers size={18}/>Scenario: {s.scenario} · {scen?.name}</button>
      <UserSelect/>
      <label><span>Next action (simulate a fault once)</span><select value={fault} onChange={e=>setFault(e.target.value as FaultMode)}>{faultModes.map(f=><option key={f.id} value={f.id}>{f.label}</option>)}</select>{fault!=='none'&&<small className="mo-fault-help">{help}</small>}</label>
      <div><span className="m-field-label">Display mode (this device)</span><DisplayToggle display={display}/></div>
    </div>
    <section className="mo-demo-steps" aria-label="Walkthrough steps">
      <h3 className="m-group">WALKTHROUGH · S0 · ROOM 105</h3>
      {s.scenario!=='S0'&&<p className="mo-hint">Expected figures assume scenario S0 — load it from Walkthrough.</p>}
      <ol>{guideSteps.map(x=><li key={x.n}><span className="mo-step-no">{x.n}</span><div><strong>{x.title}</strong><small>{x.date} · {who(x.actor)}</small></div>
        <button type="button" className="btn mo-btn-sm" aria-label={`Go to step ${x.n}: ${x.title}`} onClick={()=>goThere(x)}>Go there<ArrowRight size={15}/></button></li>)}</ol>
    </section>
    <div className="modal-actions"><button className="btn" onClick={()=>open({type:'reset'})}><RotateCcw size={18}/>Reset</button><button className="btn primary" onClick={()=>open({type:'guide'})}><BookOpen size={18}/>Walkthrough</button></div></>;
}
export function UserMenuSheet(){
  const {user,display,signOut,open}=useHcc();const word=wordOf(useOwnCredentialKind());
  const prod=process.env.HCC_BUILD==='production';
  return <><div className="modal-heading"><Users size={25}/><h2>{user.name.split(' · ')[0]}</h2><p>{user.role}. {prod?'Sign out returns to the sign-in screen.':'Sign out returns to the staff picker; Preview as is a demo tool.'}</p></div>
    <div className="form m-sheet-form">
      <button type="button" className="btn full mo-btn" onClick={()=>open({type:'own-credential'})}><KeyRound size={18}/>Change my {word}</button>
      {!prod&&<UserSelect/>}
      <div><span className="m-field-label"><Smartphone size={15}/> Display mode (this device only)</span><DisplayToggle display={display}/></div>
      <PrefsQuickSheet/>
      {!display.isMobile&&<button type="button" className="btn full mo-btn" onClick={()=>open({type:'shortcuts'})}><Keyboard size={18}/>Keyboard shortcuts</button>/* LF-ALERTS · A8 */}</div>
    <div className="modal-actions"><button className="btn" onClick={signOut}><LogOut size={18}/>Sign out</button></div></>;
}
