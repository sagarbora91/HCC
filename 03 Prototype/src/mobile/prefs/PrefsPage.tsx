// CP-1 · Phone "Appearance & display": the same settings as the desktop page as stacked cards (48 px targets, 16 px text, no tables).
import React from 'react';
import {RotateCcw,Smartphone} from 'lucide-react';
import {useHcc} from '../../app/context';
import {DisplayToggle,displayModes} from '../../app/display';
import {isNative} from '../../app/native';
import {usePrefs,useResolvedTheme,PAGE_SIZES,TOAST_SECONDS,toastLabel,textScaleLabel,resolvePageSize} from '../../app/prefs';
import {Seg,StartPageSelect,textScaleOptions,DEVICE_ONLY,ThemeSeg,ThemePreview,themeHelp} from '../../features/prefs/parts';
import {MOTION_OPTIONS,DENSITY_OPTIONS,SAMPLE_LINE} from '../../features/prefs/PrefsPage';
import {applyHonourSystemFont} from '../../features/prefs/prefsNative';

function MCard({title,children}:{title:string;children:React.ReactNode}){
  return <section className="pf-m-card" aria-label={title}><h2>{title}</h2><p className="pf-m-scope">{DEVICE_ONLY}</p>{children}</section>;
}
function Field({label,help,children}:{label:string;help?:string;children:React.ReactNode}){
  return <div className="pf-m-field"><span className="pf-m-label">{label}</span>{children}{help&&<small>{help}</small>}</div>;
}

export function MPrefsPage(){
  const {display,toast}=useHcc();
  const {prefs,save,reset}=usePrefs();
  const native=isNative();const shown=useResolvedTheme();
  const cur=displayModes.find(d=>d.id===display.mode);
  const honour=(v:boolean)=>{save({honourSystemFont:v});void applyHonourSystemFont(v);};
  return <div className="pf-m-page">
    <MCard title="Text size">
      <Field label={`Text size · ${textScaleLabel(prefs.textScale)}`}><Seg label="Text size" value={prefs.textScale} options={textScaleOptions} onChange={v=>save({textScale:v})} className="pf-seg-text"/></Field>
      <p className="pf-sample" aria-live="polite">{SAMPLE_LINE}</p>
      {native&&<label className="pf-m-toggle"><input type="checkbox" className="pf-check" checked={prefs.honourSystemFont} onChange={e=>honour(e.target.checked)}/>
        <span><strong>Honour the phone's system font size</strong><small>On: the Android font size still applies and the size above adds to it.</small></span></label>}
    </MCard>
    <MCard title="Appearance">
      <Field label="Theme" help={themeHelp(prefs.theme,shown)+'. Printed documents always stay light.'}><ThemeSeg/></Field>
      <ThemePreview/>
      <Field label="Reduce motion" help={MOTION_OPTIONS.find(o=>o.id===prefs.reduceMotion)?.help}><Seg label="Reduce motion" value={prefs.reduceMotion} options={MOTION_OPTIONS} onChange={v=>save({reduceMotion:v})}/></Field>
      <Field label="Density" help="Affects the desktop and tablet layout; phone buttons always stay 48 px."><Seg label="Density" value={prefs.density} options={DENSITY_OPTIONS} onChange={v=>save({density:v})}/></Field>
    </MCard>
    <MCard title="Display mode">
      <Field label="Layout" help={cur?`${cur.label}: ${cur.help}.`:undefined}><DisplayToggle display={display}/></Field>
    </MCard>
    <MCard title="Start & navigation">
      <label className="pf-m-field" htmlFor="pf-m-start"><span className="pf-m-label">Start page</span><StartPageSelect id="pf-m-start"/><small>Opens after sign-in. If this user may not open it, Modules Home opens instead.</small></label>
      <Field label="List page size" help={`Long lists on this phone show ${resolvePageSize(prefs.listPageSize,true)} at a time (desktop ${prefs.listPageSize}).`}>
        <Seg label="List page size" value={prefs.listPageSize} options={PAGE_SIZES.map(n=>({id:n,label:String(n)}))} onChange={v=>save({listPageSize:v})}/></Field>
    </MCard>
    <MCard title="Alerts">
      <Field label="Message duration"><Seg label="Message duration" value={prefs.toastSeconds} options={TOAST_SECONDS.map(t=>({id:t,label:toastLabel(t)}))} onChange={v=>save({toastSeconds:v})}/></Field>
      <button type="button" className="btn full" onClick={()=>toast('Sample message: preferences are saved on this device only.')}>Show a sample message</button>
    </MCard>
    <MCard title="Reset">
      <p className="pf-m-note"><Smartphone size={16} aria-hidden="true"/>Every setting above goes back to its default on this phone. The display mode is kept.</p>
      <button type="button" className="btn full" onClick={()=>{reset();if(native)void applyHonourSystemFont(true);toast('Appearance & display reset to the defaults on this device.');}}><RotateCcw size={18}/>Reset this device</button>
    </MCard>
  </div>;
}
