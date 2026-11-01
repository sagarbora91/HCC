// CP-1 · "Appearance & display" (page id 'prefs', every role). Personal settings for THIS device only (DEC-044 extended):
// text size, motion, density, display mode, start page, list page size, message duration, reset. Changes apply at once; no Save.
import React from 'react';
import {RotateCcw,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {DisplaySection} from '../../app/display';
import {isNative} from '../../app/native';
import {FitPage,FormSections,FitNote,useFitTab} from '../../components/fit';
import {usePrefs,useResolvedTheme,PAGE_SIZES,TOAST_SECONDS,toastLabel,textScaleLabel,resolvePageSize,type ReduceMotion,type Density} from '../../app/prefs';
import {Seg,StartPageSelect,textScaleOptions,DEVICE_ONLY,ThemeSeg,ThemePreview,themeHelp} from './parts';
import {applyHonourSystemFont} from './prefsNative';

export const MOTION_OPTIONS:{id:ReduceMotion;label:string;help:string}[]=[
  {id:'system',label:'Follow device',help:'Animations follow the setting of this phone or PC'},
  {id:'on',label:'Reduce',help:'Turn animations and smooth scrolling off in this app'},
  {id:'off',label:'Don’t reduce',help:'Normal animations (if the device itself asks for less motion, that still applies)'},
];
export const DENSITY_OPTIONS:{id:Density;label:string;help:string}[]=[
  {id:'comfortable',label:'Comfortable',help:'Standard spacing'},
  {id:'compact',label:'Compact',help:'Tighter table rows and card padding; touch buttons never below 44 px'},
];
/** FIT (contract §6, form): one section of the page at a time; ids are the sub-screens (SUB_SCREENS, lane block FIT-ADMIN). */
export const PREFS_SECTIONS=[{id:'text',label:'Text size'},{id:'appearance',label:'Appearance'},{id:'display',label:'Device & display'},{id:'navigation',label:'Start & navigation'},{id:'alerts',label:'Alerts'}] as const;
type PrefsSection=typeof PREFS_SECTIONS[number]['id'];
export const SAMPLE_LINE='Room 105 · Arrival 26 Oct 2026 · Balance ₹4,265.00';

function Card({title,children,id}:{title:string;children:React.ReactNode;id?:string}){
  return <section className="card pf-card" id={id} aria-labelledby={id?id+'-h':undefined}><div className="section-title"><div><h2 id={id?id+'-h':undefined}>{title}</h2><p>{DEVICE_ONLY}</p></div></div><div className="padded pf-body">{children}</div></section>;
}
function Row({label,help,children,htmlFor}:{label:string;help?:string;children:React.ReactNode;htmlFor?:string}){
  return <div className="pf-row"><div className="pf-row-label">{htmlFor?<label htmlFor={htmlFor}>{label}</label>:<span>{label}</span>}{help&&<small>{help}</small>}</div><div className="pf-row-control">{children}</div></div>;
}

export function PrefsPage(){
  const {display,toast}=useHcc();
  const {prefs,save,reset}=usePrefs();
  const [sec,setSec]=useFitTab<PrefsSection>('prefs',['text','appearance','display','navigation','alerts'],'text');
  const native=isNative();const shown=useResolvedTheme();
  const honour=(v:boolean)=>{save({honourSystemFont:v});void applyHonourSystemFont(v);};
  const sections={
    text:<Card title="Text size" id="pf-text">
      <Row label="Text size" help={`${textScaleLabel(prefs.textScale)} · ${Math.round(prefs.textScale*100)} %`}>
        <Seg label="Text size" value={prefs.textScale} options={textScaleOptions} onChange={v=>save({textScale:v})} className="pf-seg-text"/>
      </Row>
      <p className="pf-sample" aria-live="polite">{SAMPLE_LINE}</p>
      {native&&<Row label="Honour the phone's system font size" help="On: the Android font size setting still applies and the size above adds to it. Off: only the size above counts." htmlFor="pf-honour">
        <input id="pf-honour" type="checkbox" className="pf-check" checked={prefs.honourSystemFont} onChange={e=>honour(e.target.checked)}/></Row>}
    </Card>,
    appearance:<Card title="Appearance" id="pf-look">
      <Row label="Theme" help={themeHelp(prefs.theme,shown)+'. Printed documents always stay light.'}><ThemeSeg/></Row>
      <ThemePreview/>
      <Row label="Reduce motion" help={MOTION_OPTIONS.find(o=>o.id===prefs.reduceMotion)?.help}><Seg label="Reduce motion" value={prefs.reduceMotion} options={MOTION_OPTIONS} onChange={v=>save({reduceMotion:v})}/></Row>
      <Row label="Density" help={DENSITY_OPTIONS.find(o=>o.id===prefs.density)?.help}><Seg label="Density" value={prefs.density} options={DENSITY_OPTIONS} onChange={v=>save({density:v})}/></Row>
    </Card>,
    display:<DisplaySection display={display}/>,
    navigation:<Card title="Start & navigation" id="pf-nav">
      <Row label="Start page" help="Opens after sign-in. If this user may not open it, Modules Home opens instead." htmlFor="pf-start"><StartPageSelect id="pf-start"/></Row>
      <Row label="List page size" help={`Rows shown before "Show more" on long lists. Phones show ${resolvePageSize(prefs.listPageSize,true)}.`}>
        <Seg label="List page size" value={prefs.listPageSize} options={PAGE_SIZES.map(n=>({id:n,label:String(n)}))} onChange={v=>save({listPageSize:v})}/></Row>
    </Card>,
    alerts:<Card title="Alerts" id="pf-alerts">
      <Row label="Message duration" help="How long confirmation and error messages stay on screen.">
        <Seg label="Message duration" value={prefs.toastSeconds} options={TOAST_SECONDS.map(t=>({id:t,label:toastLabel(t)}))} onChange={v=>save({toastSeconds:v})}/></Row>
      <div><button type="button" className="btn" onClick={()=>toast('Sample message: preferences are saved on this device only.')}>Show a sample message</button></div>
    </Card>,
  };
  // The Reset card became the sticky action bar of the form (always visible, whichever section is open): same note, same button, same handler.
  return <FitPage title="Appearance & display" sub="How the app looks and behaves on this device. Changes apply at once. Printed documents are never affected." layout="form">
    <FormSections value={sec} onChange={id=>setSec(id as PrefsSection)} sections={PREFS_SECTIONS.map(x=>({id:x.id,label:x.label}))}
      actions={<><div className="pf-note"><FitNote lines={1} text={<><Info size={16} aria-hidden="true"/> Puts every setting on this page back to its default on this device. The display mode above is kept. {DEVICE_ONLY}</>}/></div>
        <button type="button" className="btn" onClick={()=>{reset();if(native)void applyHonourSystemFont(true);toast('Appearance & display reset to the defaults on this device.');}}><RotateCcw size={16}/>Reset this device</button></>}>
      <div className="pf-page">{sections[sec]}</div>
    </FormSections>
  </FitPage>;
}
