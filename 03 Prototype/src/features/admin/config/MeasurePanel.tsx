// F12 MEAS · meas-settings: Master configuration › Measurement reports panel (MEAS-CONTRACT §1). Same markup on desktop (a section of the
// settings FitPage; lists scroll in their panel) and phone (stacked cfg- cards, 48 px buttons, 16 px text, no sideways scroll). Classes cfg-
// (styles/m-config.css), colours from tokens only. Every change goes through SETTING_SET, so history, export / import and DEC-058 apply.
import React,{useState} from 'react';
import {Power,Lock,ChevronDown,Save} from 'lucide-react';
import {MEAS_PACKS,MEAS_TARGETS,measPackItems,measPackOn,measPackCount,measOn,measOffReason,measIsCore,measTarget,
  cfgDef,cfgCanChange,type MeasTargetDef} from '../../../services/mock';
import {useHcc} from '../../../app/context';
import {Pill} from '../../../components/ui';

const rupees=(p:number)=>(p/100).toLocaleString('en-IN',{maximumFractionDigits:2});
const showTarget=(t:MeasTargetDef,v:number|undefined)=>v===undefined?'—':t.kind==='money'?'₹'+rupees(v):t.kind==='percent'?`${v} %`:t.kind==='minutes'?`${v} min`:String(v);

export function MeasurePanel(){
  const {s,uid,run,busy}=useHcc();
  const [open,setOpen]=useState<string|null>(null);
  const packDef=cfgDef(s,MEAS_PACKS[0].key);const dec=packDef?cfgCanChange(s,uid,packDef):undefined;
  const readOnly=!dec||dec.mode!=='allow';
  return <div className="cfg-panel">
    <p className="muted">Switch a pack or a single report off: it disappears from the report hub, search, phone tabs, scheduled runs and the Owner digest, and its capture forms refuse new entries. Data already captured is kept. Core items stay on so the day close never breaks.</p>
    {readOnly&&<p className="adm-why">{dec?.reason??'Only the Owner changes measurement settings (PERM-MEAS-001). You can review them here.'}</p>}
    <ul className="cfg-mods">{MEAS_PACKS.map(p=>{const on=measPackOn(s,p.pack);const n=measPackCount(s,p.pack);const items=measPackItems(p.pack);const isOpen=open===p.pack;
      return <li key={p.pack} className={'cfg-mod'+(on?' on':'')}>
        <div><strong>{p.label}</strong><small>{items.length} items · governed by {p.perm}</small></div>
        <Pill tone={on?'green':'neutral'}>{`${n.on} of ${n.total} on`}</Pill>
        <span className="cfg-mod-act">
          <button type="button" className="btn small-btn" disabled={readOnly||busy} aria-label={`${on?'Switch off':'Switch on'} ${p.label}`}
            onClick={()=>run({type:'SETTING_SET',setting:p.key,value:!on,key:`meas-pack:${p.pack}:${s.sequence}`},on?`${p.label} switched off · core items stay on · data kept`:`${p.label} switched on`)}><Power size={15}/> {on?'Switch off':'Switch on'}</button>
          <button type="button" className="btn small-btn" aria-expanded={isOpen} aria-label={`${isOpen?'Hide':'Show'} the items of ${p.label}`} onClick={()=>setOpen(isOpen?null:p.pack)}><ChevronDown size={15}/> Items</button>
        </span>
        {isOpen&&<ul className="cfg-mods" aria-label={`${p.label} items`}>{items.map(i=>{
          const iOn=measOn(s,i.key);const core=measIsCore(s,i.key);const why=measOffReason(s,i.key);
          const block=core?'Core — always on':!on?'Switch the pack on first':why==='module'?`Needs the ${i.module} module, which is off`:readOnly?'Owner only':undefined;
          const own=s.configX?.values?.[i.key]!==false;
          return <li key={i.key} className={'cfg-mod cfg-feature'+(iOn?' on':'')+(core?' core':'')}>
            <div><strong>{i.label}</strong>
              <small>{i.report?`Report ${i.report}`:'Measure (no report of its own)'} · {p.perm}</small>
              {i.capture&&<small>Capture needed: {i.capture}</small>}</div>
            <Pill tone={iOn?'green':'neutral'}>{core?'Always on':iOn?'On':'Off'}</Pill>
            <span className="cfg-mod-act">
              {!block&&<button type="button" className="btn small-btn" disabled={busy} aria-label={`${own?'Switch off':'Switch on'} ${i.label}`}
                onClick={()=>run({type:'SETTING_SET',setting:i.key,value:!own,key:`meas-item:${i.key}:${s.sequence}`},own?`${i.label} switched off · data kept`:`${i.label} switched on`)}><Power size={15}/> {own?'Switch off':'Switch on'}</button>}
              {block&&<small className="adm-why">{block}</small>}</span>
          </li>;})}</ul>}
      </li>;})}</ul>
    <div className="cfg-core"><Lock size={15}/><span><strong>Core — always on:</strong> Manager&apos;s flash, Complimentary / house-use / out-of-order, and High-balance while company credit is on.</span></div>
    <MeasureTargets readOnly={readOnly}/>
  </div>;
}

/** Targets editor (meas.target.*, M-9 defaults) with the FY 2025-26 actual as a read-only hint. Money is entered in rupees, stored in paise. */
export function MeasureTargets({readOnly}:{readOnly:boolean}){
  const {s,run,busy}=useHcc();
  const [edit,setEdit]=useState<Record<string,string>>({});
  return <section className="cfg-features" aria-label="Measurement targets">
    <h3>Targets</h3>
    <p className="muted">Red rows in the measurement reports and the Owner digest use these targets.</p>
    <ul className="cfg-mods">{MEAS_TARGETS.map(t=>{const v=measTarget(s,t.key);const draft=edit[t.key];
      const parsed=draft===undefined||draft.trim()===''?undefined:t.kind==='money'?Math.round(Number(draft)*100):Number(draft);
      const ok=parsed!==undefined&&Number.isSafeInteger(parsed)&&parsed!==v;
      return <li key={t.key} className="cfg-mod on">
        <div><strong>{t.label}</strong><small>Now {showTarget(t,v)}{t.baseline?` · FY 2025-26 actual ${t.baseline}`:''}</small></div>
        {!readOnly&&<span className="cfg-mod-act">
          <label className="sr-only" htmlFor={'mt-'+t.key}>{t.label}{t.kind==='money'?' in rupees':''}</label>
          <input id={'mt-'+t.key} inputMode="decimal" value={draft??''} placeholder={t.kind==='money'?rupees(v??0):String(v??'')} onChange={e=>setEdit({...edit,[t.key]:e.target.value})}/>
          <button type="button" className="btn small-btn" disabled={!ok||busy} aria-label={`Save ${t.label}`}
            onClick={()=>{run({type:'SETTING_SET',setting:t.key,value:parsed,key:`meas-target:${t.key}:${s.sequence}`},`${t.label} saved`);setEdit({...edit,[t.key]:''});}}><Save size={15}/> Save</button>
        </span>}
      </li>;})}</ul>
  </section>;
}
