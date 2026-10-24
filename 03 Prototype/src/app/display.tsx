// MB-01 Display mode (DEC-044): Auto / Desktop / Mobile, stored per device in localStorage — never in hotel data, not audited.
import React,{useEffect,useState,useCallback} from 'react';
import {Monitor,Smartphone,Wand2} from 'lucide-react';
import {nativePhoneLayout,screenShortSide} from './native';
export type DisplayMode='auto'|'desktop'|'mobile';
export const DISPLAY_KEY='hcc-display-mode';
export const MOBILE_QUERY='(max-width: 767px)';
export const displayModes:{id:DisplayMode;label:string;icon:typeof Monitor;help:string}[]=[
  {id:'auto',label:'Auto',icon:Wand2,help:'Phone layout below 768 px width (also inside the Android app); tablets get the desktop layout'},
  {id:'desktop',label:'Desktop',icon:Monitor,help:'Always the desktop layout'},
  {id:'mobile',label:'Mobile',icon:Smartphone,help:'Always the phone layout (centred preview on wide screens)'},
];
export function readDisplayMode():DisplayMode{
  try{const v=localStorage.getItem(DISPLAY_KEY);return v==='desktop'||v==='mobile'||v==='auto'?v:'auto';}catch{return 'auto';}
}
export function saveDisplayMode(m:DisplayMode):boolean{try{localStorage.setItem(DISPLAY_KEY,m);return true;}catch{return false;}}
/** Inside a Capacitor APK, Auto means the phone layout on phone-sized screens (§12); tablets keep the desktop layout. */
export const isNativeApp=()=>{const c=(globalThis as {Capacitor?:{isNativePlatform?:()=>boolean}}).Capacitor;try{return !!c?.isNativePlatform?.();}catch{return false;}};
const narrow=()=>{try{return window.matchMedia(MOBILE_QUERY).matches;}catch{return false;}};
/** Pure resolver (unit-testable): which shell to show. */
export function resolveMobile(mode:DisplayMode,isNarrow:boolean,native:boolean){return mode==='mobile'||(mode==='auto'&&(isNarrow||native));}
export type Display={mode:DisplayMode;setMode:(m:DisplayMode)=>void;isMobile:boolean;framed:boolean};
export function useDisplayMode():Display{
  const [mode,setModeState]=useState<DisplayMode>(readDisplayMode);
  const [isNarrow,setNarrow]=useState(narrow);
  useEffect(()=>{let mq:MediaQueryList|undefined;try{mq=window.matchMedia(MOBILE_QUERY);}catch{return;}
    const on=()=>setNarrow(mq!.matches);mq.addEventListener?.('change',on);return ()=>mq!.removeEventListener?.('change',on);},[]);
  // Another tab on the same device changed the mode.
  useEffect(()=>{const on=(e:StorageEvent)=>{if(e.key===DISPLAY_KEY)setModeState(readDisplayMode());};window.addEventListener('storage',on);return ()=>window.removeEventListener('storage',on);},[]);
  const setMode=useCallback((m:DisplayMode)=>{saveDisplayMode(m);setModeState(m);},[]);
  const native=isNativeApp();
  // In the APK the screen size (not the window width) decides: phones get the phone layout in any rotation, tablets the desktop layout.
  const isMobile=resolveMobile(mode,native?false:isNarrow,nativePhoneLayout(native,screenShortSide()));
  return {mode,setMode,isMobile,framed:isMobile&&!isNarrow&&!native};
}
/** Segmented control used in the user menu, the demo sheet/toolbar and Hotel configuration → Device & display. */
export function DisplayToggle({display,compact}:{display:Display;compact?:boolean}){
  return <div className={'display-toggle'+(compact?' compact':'')} role="radiogroup" aria-label="Display mode (this device only)">
    {displayModes.map(d=><button key={d.id} type="button" role="radio" aria-checked={display.mode===d.id} className={display.mode===d.id?'on':''} title={d.help} onClick={()=>display.setMode(d.id)}><d.icon size={compact?14:16}/><span>{d.label}</span></button>)}
  </div>;
}
export function DisplaySection({display}:{display:Display}){
  const cur=displayModes.find(d=>d.id===display.mode)!;
  return <section className="card" id="device-display"><div className="section-title"><div><h2>Device &amp; display (this device only)</h2><p>Saved in this browser on this device — each phone or PC chooses its own. It is not hotel data and is not audited.</p></div></div>
    <div className="padded"><DisplayToggle display={display}/><p className="muted" style={{marginTop:12}}>{cur.label}: {cur.help}. Showing the <strong>{display.isMobile?'phone':'desktop'}</strong> layout now. Switching keeps the current page and open record.</p></div></section>;
}
