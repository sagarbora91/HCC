// W05-A · Master configuration group panels used on desktop/tablet and phone: modules on/off, data and system (export/import,
// device display, invariants, reset), users link. Classes cfg- (styles/m-config.css). Phone = same markup, stacked cards.
import React from 'react';
import {Power,Download,Upload,ShieldCheck,RotateCcw,ExternalLink,Lock} from 'lucide-react';
import {CFG_MODULES,CFG_CORE_MODULES,cfgModuleOn,cfgDef,cfgCanChange,cfgExport,cfgPendingRequests,cfgPageOn,cfgGroup,type CfgGroupId} from '../../../services/mock';
import {useHcc} from '../../../app/context';
import {DisplaySection} from '../../../app/display';
import {saveTextFile,nativeFileName} from '../../../app/native';
import {Pill} from '../../../components/ui';
import {CFG_INVARIANTS,cfgNav} from './logic';

export function CfgModulesPanel(){
  const {s,uid,run,busy}=useHcc();
  return <div className="cfg-panel">
    <p className="muted">Turning a module off hides its tiles, menu items, search entries, reminders and phone tabs, and its commands are refused. Its data and history are kept — switch it back on at any time.</p>
    <ul className="cfg-mods">{CFG_MODULES.map(m=>{const on=cfgModuleOn(s,m.id);const def=cfgDef(s,'module.'+m.id)!;const d=cfgCanChange(s,uid,def);const pend=cfgPendingRequests(s,def.key).length>0;
      const block=d.mode==='deny'?d.reason:pend?'Change waiting for approval':undefined;
      return <li key={m.id} className={'cfg-mod'+(on?' on':'')}>
        <div><strong>{m.label}</strong><small>{m.note}</small></div>
        <Pill tone={on?'green':'neutral'}>{on?'On':'Off'}</Pill>
        <span className="cfg-mod-act"><button type="button" className="btn small-btn" disabled={!!block||busy} aria-label={`${on?'Switch off':'Switch on'} ${m.label}`}
          onClick={()=>run({type:'SETTING_SET',setting:def.key,value:!on,key:`cfg-mod:${m.id}:${s.sequence}`},on?`${m.label} switched off · data kept`:`${m.label} switched on`)}><Power size={15}/> {on?'Switch off':'Switch on'}</button>
          {block&&<small className="adm-why">{block}</small>}</span>
      </li>;})}</ul>
    <div className="cfg-core"><Lock size={15}/><span><strong>Core modules — always on:</strong> {CFG_CORE_MODULES.join(', ')}.</span></div>
  </div>;
}

export function CfgDataPanel(){
  const {s,uid,open,display,toast}=useHcc();
  const role=s.users.find(u=>u.id===uid)?.role;
  const doExport=async()=>{const r=await saveTextFile(nativeFileName('Master-configuration','json',s.date),JSON.stringify(cfgExport(s),null,2),'application/json');
    toast(r==='failed'?'Export failed — try again':'Configuration exported (no secrets)',r==='failed');};
  return <div className="cfg-panel">
    <section className="cfg-box"><h3>Configuration export and import</h3>
      <p className="muted">JSON file for backup or for setting up a second property. Secrets (gateway keys), users, access policy and guest data are never included.</p>
      <div className="cfg-actions"><button type="button" className="btn" onClick={()=>void doExport()}><Download size={16}/> Export configuration</button>
        <button type="button" className="btn" disabled={role!=='Owner'&&role!=='Tech admin'} onClick={()=>open({type:'cfg-import'})}><Upload size={16}/> Import…</button></div></section>
    <DisplaySection display={display}/>
    <section className="cfg-box"><h3><ShieldCheck size={17}/> Not configurable (safety invariants)</h3>
      <ul className="cfg-inv">{CFG_INVARIANTS.map(([k,v])=><li key={k}><span><strong>{k}</strong> — {v}</span><Pill tone="green">Always on</Pill></li>)}</ul></section>
    {process.env.HCC_BUILD==='production'?null:<section className="cfg-box"><h3>Prototype boundaries</h3>
      <p className="muted">Local and fictional. Data is stored in this browser; another device has its own data. No SQL Server, bank gateway, real authentication or live GST validation. Browser storage is not backup.</p>
      <button type="button" className="btn danger" onClick={()=>open({type:'reset'})}><RotateCcw size={16}/> Reset demo data ({s.scenario})</button></section>}{/* demo-only (Reset): compiled out of the production build */}
  </div>;
}

/** "Open <module page>" link shown in a group (hidden when the page is not available to this user or its module is off). */
export function CfgGroupLink({g}:{g:CfgGroupId}){
  const {s,go,permission}=useHcc();const grp=cfgGroup(g)!;
  if(!grp.link||!cfgPageOn(s,grp.link.page))return null;
  const need:Record<string,Parameters<typeof permission>[0]>={yield:'reservations',pos:'billing',staff:['approve','reports'],access:'admin',messages:['reservations','checkin']};
  if(need[grp.link.page]&&!permission(need[grp.link.page]))return null;
  return <button type="button" className="text-btn cfg-link" onClick={()=>go(grp.link!.page)}><ExternalLink size={14}/> {grp.link.label}</button>;
}

/** Small link old module pages show next to their own settings (thin wrappers keep working there). */
export function CfgHubLink({what,group}:{what:string;group?:CfgGroupId}){
  const {go,permission}=useHcc();
  if(!permission('admin'))return null;
  return <button type="button" className="text-btn cfg-hublink" onClick={()=>{cfgNav.group=group;go('settings');}}><ExternalLink size={13}/> {what} are also in Master configuration</button>;
}
