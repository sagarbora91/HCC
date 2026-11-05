// W05-A · Master configuration group panels used on desktop/tablet and phone: modules on/off, data and system (export/import,
// device display, invariants, reset), users link. Classes cfg- (styles/m-config.css). Phone = same markup, stacked cards.
import React from 'react';
import {Power,Download,Upload,ShieldCheck,RotateCcw,ExternalLink,Lock} from 'lucide-react';
import {CFG_MODULES,CFG_CORE_MODULES,CFG_CORE_REASON,cfgModuleOn,cfgFeatureOn,cfgFeatureOffReason,cfgFeaturesOff,featureAll,featureKey,featureClosure,featureById,
  cfgDef,cfgCanChange,cfgExport,cfgPendingRequests,cfgPageOn,cfgGroup,type CfgGroupId} from '../../../services/mock';
import {useHcc} from '../../../app/context';
import {DisplaySection} from '../../../app/display';
import {saveTextFile,nativeFileName} from '../../../app/native';
import {Pill} from '../../../components/ui';
import {CFG_INVARIANTS,cfgNav,CFG_TOAST_MS,CFG_CORE_PLAIN,cfgSwitchOnNote} from './logic';
import {MstIoPanel} from '../../masters/MastersIo'; // MA-8

/** ADM-UX item 2: the On/Off pill is itself the switch (role=switch, Space/Enter by the browser's own button behaviour) and does exactly what the
 *  "Switch on/off" button beside it does. A row that cannot change shows a plain pill (not a button) and never looks tappable. */
function CfgSwitch({on,label,onText,offText,canAct,busy,onToggle}:{on:boolean;label:string;onText:string;offText:string;canAct:boolean;busy:boolean;onToggle:()=>void}){
  const tone=on?'green':'neutral';
  if(!canAct)return <Pill tone={tone}>{on?onText:offText}</Pill>;
  return <button type="button" role="switch" aria-checked={on} aria-label={`${label} is ${on?'on':'off'}`} disabled={busy} className={'pill cfg-switch '+tone} onClick={onToggle}>{on?onText:offText}</button>;
}
/** Clicking empty space in a row acts like the switch (buttons inside keep their own click). */
const rowTap=(canAct:boolean,onToggle:()=>void)=>canAct?{onClick:(e:React.MouseEvent)=>{if((e.target as HTMLElement).closest('button'))return;onToggle();}}:{};
const Lockline=({text}:{text:string})=><small className="cfg-lock" role="note"><Lock size={14} aria-hidden="true"/><span>{text}</span></small>;

export function CfgModulesPanel(){
  const {s,uid,run,busy}=useHcc();
  return <div className="cfg-panel">
    <p className="muted">Turning a module off hides its tiles, menu items, search entries, reminders and phone tabs, and its commands are refused. Its data and history are kept — switch it back on at any time. Tap the On/Off switch or the button.</p>
    <ul className="cfg-mods">{CFG_MODULES.map(m=>{const on=cfgModuleOn(s,m.id);const def=cfgDef(s,'module.'+m.id)!;const d=cfgCanChange(s,uid,def);const pend=cfgPendingRequests(s,def.key).length>0;
      const block=d.mode==='deny'?d.reason:pend?'Change waiting for approval':undefined;
      const toggle=()=>{if(busy)return;void run({type:'SETTING_SET',setting:def.key,value:!on,key:`cfg-mod:${m.id}:${s.sequence}`},on?`${m.label} switched off · data kept`:`${m.label} switched on`,{toastMs:CFG_TOAST_MS});};
      return <li key={m.id} className={'cfg-mod'+(on?' on':'')+(block?' locked':' tappable')} {...rowTap(!block,toggle)}>
        <div><strong>{m.label}</strong><small>{m.note}</small></div>
        <CfgSwitch on={on} label={m.label} onText="On" offText="Off" canAct={!block} busy={busy} onToggle={toggle}/>
        <span className="cfg-mod-act"><button type="button" className="btn small-btn" disabled={!!block||busy} aria-label={`${on?'Switch off':'Switch on'} ${m.label}`} onClick={toggle}><Power size={15}/> {on?'Switch off':'Switch on'}</button>
          {block&&<Lockline text={block}/>}</span>
      </li>;})}</ul>
    <div className="cfg-core"><Lock size={15}/><span><strong>Core modules — always on, the hotel cannot run without them:</strong> {CFG_CORE_MODULES.join(', ')}.</span></div>
    <CfgFeaturesTree/>
  </div>;
}

/** ADM-1 (DEC-214 / DEC-215): sub-functions per module. Core rows show why they are fixed; a switch-off lists the dependants it takes down.
 *  ADM-UX: switching a parent back on never revives its dependants — the message and the dependant rows say so. */
export function CfgFeaturesTree(){
  const {s,uid,run,busy}=useHcc();
  const mods=[...new Map(featureAll().map(r=>[r.module.id,r.module])).values()];
  const off=cfgFeaturesOff(s);
  return <section className="cfg-features" aria-label="Sub-functions">
    <h3>Sub-functions</h3>
    <p className="muted">Switch a part of a module off without losing the rest: for example Front desk stays on while Reservations is off (walk-ins still check in). Core sub-functions cannot be switched off (DEC-215). Switching a part back on does not switch back on what went off with it.</p>
    {mods.map(m=>{const rows=featureAll().filter(r=>r.module.id===m.id);const modOn=!m.switch||cfgModuleOn(s,m.switch);
      return <div key={m.id} className={'cfg-fmod'+(modOn?'':' off')}>
        <h4>{m.label}{!modOn&&<small> · module switched off</small>}</h4>
        <ul className="cfg-mods">{rows.map(r=>{
          const on=cfgFeatureOn(s,r.id);const explicitOff=off.includes(r.id);
          const def=r.feature.core?undefined:cfgDef(s,featureKey(r.id));
          const d=def?cfgCanChange(s,uid,def):undefined;
          const pend=def?cfgPendingRequests(s,def.key).length>0:false;
          const needs=(r.feature.requires||[]).map(q=>featureById(q)).find(q=>q&&!cfgFeatureOn(s,q.feature.id));
          const why=r.feature.core?CFG_CORE_PLAIN:!modOn?'Switch the module on first.':d?.mode==='deny'?d.reason:pend?'Change waiting for approval':(!on&&!explicitOff)?(needs?`Switch ${needs.feature.label} on first — ${r.feature.label} needs it.`:cfgFeatureOffReason(s,r.id)):undefined;
          const depIds=featureClosure(r.id).slice(1);
          const deps=depIds.map(id=>featureById(id)?.feature.label).filter(Boolean) as string[];
          const stillOff=depIds.filter(id=>off.includes(id)).map(id=>featureById(id)!.feature.label);
          const parentOn=explicitOff&&(r.feature.requires||[]).length>0&&(r.feature.requires||[]).every(q=>cfgFeatureOn(s,q));
          const canAct=!!def&&!why;
          const toggle=()=>{if(!def||!canAct||busy)return;
            if(!explicitOff&&deps.length&&!window.confirm(`Switching off ${r.feature.label} also switches off: ${deps.join(', ')}. Continue?`))return;
            void run({type:'SETTING_SET',setting:def.key,value:explicitOff,key:`cfg-feat:${r.id}:${s.sequence}`},explicitOff?cfgSwitchOnNote(r.feature.label,stillOff):`${r.feature.label} switched off · data kept`,{toastMs:CFG_TOAST_MS});};
          return <li key={r.id} className={'cfg-mod cfg-feature'+(on?' on':'')+(r.feature.core?' core':'')+(canAct?' tappable':' locked')} {...rowTap(canAct,toggle)}>
            <div><strong>{r.feature.label}</strong><small>{r.feature.note}</small>{!on&&deps.length>0&&explicitOff&&<small>Off with it: {deps.join(', ')}</small>}
              {parentOn&&<small className="cfg-stays">Stayed off when the part it needs was switched back on — switch it on here.</small>}</div>
            <CfgSwitch on={on} label={r.feature.label} onText={r.feature.core?'Always on':'On'} offText="Off" canAct={canAct} busy={busy} onToggle={toggle}/>
            <span className="cfg-mod-act">
              {canAct&&<button type="button" className="btn small-btn" disabled={busy} aria-label={`${explicitOff?'Switch on':'Switch off'} ${r.feature.label}`} onClick={toggle}>
                <Power size={15}/> {explicitOff?'Switch on':'Switch off'}</button>}
              {why&&<Lockline text={why}/>}</span>
          </li>;})}</ul>
      </div>;})}
  </section>;
}

export function CfgDataPanel(){
  const {s,uid,open,display,toast,go}=useHcc();
  const role=s.users.find(u=>u.id===uid)?.role;
  const doExport=async()=>{const r=await saveTextFile(nativeFileName('Master-configuration','json',s.date),JSON.stringify(cfgExport(s),null,2),'application/json');
    toast(r==='failed'?'Export failed — try again':'Configuration exported (no secrets)',r==='failed');};
  return <div className="cfg-panel">
    <section className="cfg-box"><h3>Configuration export and import</h3>
      <p className="muted">JSON file for backup or for setting up a second property. Secrets (gateway keys), users, access policy and guest data are never included.</p>
      <div className="cfg-actions"><button type="button" className="btn" onClick={()=>void doExport()}><Download size={16}/> Export configuration</button>
        <button type="button" className="btn" disabled={role!=='Owner'&&role!=='Tech admin'} onClick={()=>open({type:'cfg-import'})}><Upload size={16}/> Import…</button></div></section>
    <MstIoPanel/>
    {role==='Owner'&&<section className="cfg-box"><h3>Legacy import</h3>
      <p className="muted">Load guests, stay history and opening balances extracted from the old Lodge software. Start with a dry run (Owner only).</p>
      <div className="cfg-actions"><button type="button" className="btn" onClick={()=>go('legacy-import')}><ExternalLink size={16}/> Open Legacy import</button></div></section>}
    <DisplaySection display={display}/>
    <section className="cfg-box"><h3>Appearance &amp; display (this device only)</h3>
      <p className="muted">Text size, reduce motion, density, start page, list size and alert timing — personal, saved on this device, never hotel data (CP-1).</p>
      <div className="cfg-actions"><button type="button" className="btn" onClick={()=>go('prefs')}><ExternalLink size={16}/> Open Appearance &amp; display</button></div></section>
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
