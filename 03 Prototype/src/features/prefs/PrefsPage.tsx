// CP-1 · "Appearance & display" (page id 'prefs', every role). Personal settings for THIS device only (DEC-044 extended):
// text size, motion, density, display mode, start page, list page size, message duration, reset. Changes apply at once; no Save.
import React from 'react';
import {RotateCcw,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {DisplaySection} from '../../app/display';
import {isNative} from '../../app/native';
import {PageTitle} from '../../components/ui';
import {usePrefs,PAGE_SIZES,TOAST_SECONDS,toastLabel,textScaleLabel,resolvePageSize,type ReduceMotion,type Density} from '../../app/prefs';
import {Seg,StartPageSelect,textScaleOptions,DEVICE_ONLY} from './parts';
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
  const native=isNative();
  const honour=(v:boolean)=>{save({honourSystemFont:v});void applyHonourSystemFont(v);};
  return <div className="pf-page">
    <PageTitle title="Appearance & display" sub="How the app looks and behaves on this device. Changes apply at once. Printed documents are never affected."/>
    <Card title="Text size" id="pf-text">
      <Row label="Text size" help={`${textScaleLabel(prefs.textScale)} · ${Math.round(prefs.textScale*100)} %`}>
        <Seg label="Text size" value={prefs.textScale} options={textScaleOptions} onChange={v=>save({textScale:v})} className="pf-seg-text"/>
      </Row>
      <p className="pf-sample" aria-live="polite">{SAMPLE_LINE}</p>
      {native&&<Row label="Honour the phone's system font size" help="On: the Android font size setting still applies and the size above adds to it. Off: only the size above counts." htmlFor="pf-honour">
        <input id="pf-honour" type="checkbox" className="pf-check" checked={prefs.honourSystemFont} onChange={e=>honour(e.target.checked)}/></Row>}
    </Card>
    <Card title="Appearance" id="pf-look">
      <Row label="Reduce motion" help={MOTION_OPTIONS.find(o=>o.id===prefs.reduceMotion)?.help}><Seg label="Reduce motion" value={prefs.reduceMotion} options={MOTION_OPTIONS} onChange={v=>save({reduceMotion:v})}/></Row>
      <Row label="Density" help={DENSITY_OPTIONS.find(o=>o.id===prefs.density)?.help}><Seg label="Density" value={prefs.density} options={DENSITY_OPTIONS} onChange={v=>save({density:v})}/></Row>
      <Row label="Theme" help="A dark theme is planned for a later release."><span className="pf-static">Light · dark theme coming</span></Row>
    </Card>
    <DisplaySection display={display}/>
    <Card title="Start & navigation" id="pf-nav">
      <Row label="Start page" help="Opens after sign-in. If this user may not open it, Modules Home opens instead." htmlFor="pf-start"><StartPageSelect id="pf-start"/></Row>
      <Row label="List page size" help={`Rows shown before "Show more" on long lists. Phones show ${resolvePageSize(prefs.listPageSize,true)}.`}>
        <Seg label="List page size" value={prefs.listPageSize} options={PAGE_SIZES.map(n=>({id:n,label:String(n)}))} onChange={v=>save({listPageSize:v})}/></Row>
    </Card>
    <Card title="Alerts" id="pf-alerts">
      <Row label="Message duration" help="How long confirmation and error messages stay on screen.">
        <Seg label="Message duration" value={prefs.toastSeconds} options={TOAST_SECONDS.map(t=>({id:t,label:toastLabel(t)}))} onChange={v=>save({toastSeconds:v})}/></Row>
      <div><button type="button" className="btn" onClick={()=>toast('Sample message: preferences are saved on this device only.')}>Show a sample message</button></div>
    </Card>
    <Card title="Reset" id="pf-reset">
      <p className="pf-note"><Info size={16} aria-hidden="true"/>Puts every setting on this page back to its default on this device. The display mode above is kept.</p>
      <div><button type="button" className="btn" onClick={()=>{reset();if(native)void applyHonourSystemFont(true);toast('Appearance & display reset to the defaults on this device.');}}><RotateCcw size={16}/>Reset this device</button></div>
    </Card>
  </div>;
}
