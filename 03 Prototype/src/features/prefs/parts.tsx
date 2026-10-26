// CP-1 · Pieces shared by the desktop and phone "Appearance & display" pages and the quick controls (top bar, phone user menu).
import React from 'react';
import {AArrowDown,AArrowUp,SlidersHorizontal,Sun,Moon,Contrast,MonitorSmartphone} from 'lucide-react';
import {useHcc} from '../../app/context';
import {navItems} from '../../app/nav';
import {navFilterModules,NAV_CATEGORIES,PAGE_CATEGORY,DASHBOARD} from '../../app/navmap';
import {usePrefs,stepTextScale,textScaleLabel,TEXT_SCALES,THEME_LABEL,useResolvedTheme,type TextScale,type Theme,type ResolvedTheme} from '../../app/prefs';

export const PREFS_PAGE='prefs';
export const DEVICE_ONLY='This device only — saved in this browser or app, not hotel data, not audited.';

/** Segmented control (≤ 4 options): a radiogroup of buttons. */
export function Seg<T extends string|number>({label,value,options,onChange,className}:{label:string;value:T;options:{id:T;label:React.ReactNode;help?:string}[];onChange:(v:T)=>void;className?:string}){
  return <div className={'pf-seg'+(className?' '+className:'')} role="radiogroup" aria-label={label}>
    {options.map(o=><button key={String(o.id)} type="button" role="radio" aria-checked={value===o.id} className={value===o.id?'on':''} title={o.help} onClick={()=>onChange(o.id)}>{o.label}</button>)}
  </div>;
}

/** Text size steps shown as growing "A" letters, each with its name. */
export const textScaleOptions=TEXT_SCALES.map(t=>({id:t,label:<span className="pf-a"><b style={{fontSize:Math.round(14*t)+'px'}} aria-hidden="true">A</b><small>{textScaleLabel(t)}</small></span>,help:textScaleLabel(t)+' text ('+Math.round(t*100)+' %)'}));

// ── CP-5 theme choice ──
const THEME_ICON:Record<Theme,React.ReactNode>={light:<Sun size={16} aria-hidden="true"/>,dark:<Moon size={16} aria-hidden="true"/>,hc:<Contrast size={16} aria-hidden="true"/>,system:<MonitorSmartphone size={16} aria-hidden="true"/>};
export const THEME_OPTIONS:{id:Theme;label:React.ReactNode;help:string}[]=[
  {id:'light',label:<>{THEME_ICON.light}Light</>,help:'Maroon & Sand on light surfaces'},
  {id:'dark',label:<>{THEME_ICON.dark}Dark</>,help:'Warm dark surfaces, lighter maroon; easier on the eyes at night'},
  {id:'hc',label:<>{THEME_ICON.hc}High contrast</>,help:'Black-on-white text (AAA), thicker control borders, strong focus ring, no tints'},
  {id:'system',label:<>{THEME_ICON.system}Follow device</>,help:'Light or Dark, following the setting of this phone or PC'},
];
/** Help line for the Theme row: what the choice does, and what Follow device shows right now. */
export function themeHelp(pref:Theme,shown:ResolvedTheme):string{
  const base=THEME_OPTIONS.find(o=>o.id===pref)?.help??'';
  return pref==='system'?`${base} (now: ${THEME_LABEL[shown]})`:base;
}
/** Theme segmented control. */
export function ThemeSeg(){
  const {prefs,save}=usePrefs();
  return <Seg label="Theme" value={prefs.theme} options={THEME_OPTIONS} onChange={v=>save({theme:v})} className="pf-seg-theme"/>;
}
/** Live preview strip: the same small card in each theme (status chips keep their text labels). Picking a card picks that theme. */
export function ThemePreview(){
  const {prefs,save}=usePrefs();const shown=useResolvedTheme();
  return <div className="pf-theme-strip" role="group" aria-label="Theme preview">
    {(['light','dark','hc'] as const).map(t=><button key={t} type="button" data-theme={t} className={'pf-theme-card'+(shown===t?' on':'')} aria-pressed={prefs.theme===t}
      aria-label={`Use the ${THEME_LABEL[t]} theme`} onClick={()=>save({theme:t})}>
      <span className="pf-tp-head"><b>Room 105</b><small>{THEME_LABEL[t]}</small></span>
      <span className="pf-tp-muted">Arrival today · 2 guests</span>
      <span className="pf-tp-chips"><i className="pf-tp-ready">Ready</i><i className="pf-tp-dirty">Dirty</i><i className="pf-tp-occ">Occupied</i></span>
      <span className="pf-tp-btn">Check in</span>
    </button>)}
  </div>;
}

/** Pages this user may choose as the start page, grouped by module category (same filter as the menus). */
export function useStartPageChoices():{group:string;pages:{id:string;label:string}[]}[]{
  const {s,uid,permission}=useHcc();
  const allowed=navFilterModules(s,navItems,uid).filter(n=>permission(n.p));
  const groups:{group:string;pages:{id:string;label:string}[]}[]=[];
  const dash=allowed.find(n=>n.id===DASHBOARD.id);
  if(dash)groups.push({group:'Dashboard',pages:[{id:dash.id,label:DASHBOARD.label}]});
  for(const c of NAV_CATEGORIES){const pages=allowed.filter(n=>PAGE_CATEGORY[n.id]===c.id).map(n=>({id:n.id,label:n.label}));if(pages.length)groups.push({group:c.label,pages});}
  return groups;
}
export function StartPageSelect({id}:{id?:string}){
  const {prefs,save}=usePrefs();const groups=useStartPageChoices();
  const known=!prefs.startPage||groups.some(g=>g.pages.some(p=>p.id===prefs.startPage));
  return <select id={id} value={known?prefs.startPage:''} onChange={e=>save({startPage:e.target.value})}>
    <option value="">Modules Home (default)</option>
    {groups.map(g=><optgroup key={g.group} label={g.group}>{g.pages.map(p=><option key={p.id} value={p.id}>{p.label}</option>)}</optgroup>)}
  </select>;
}

/** A− / A+ quick control plus the link to the full page (desktop top bar). */
export function TextSizeQuick({showLink=true}:{showLink?:boolean}){
  const {prefs,save}=usePrefs();const {go}=useHcc();
  const set=(t:TextScale)=>save({textScale:t});
  const i=TEXT_SCALES.indexOf(prefs.textScale);
  return <div className="pf-quick" role="group" aria-label={'Text size: '+textScaleLabel(prefs.textScale)+' (this device only)'}>
    <button type="button" aria-label="Smaller text" title="Smaller text" disabled={i<=0} onClick={()=>set(stepTextScale(prefs.textScale,-1))}><AArrowDown size={16}/></button>
    <button type="button" aria-label="Larger text" title="Larger text" disabled={i>=TEXT_SCALES.length-1} onClick={()=>set(stepTextScale(prefs.textScale,1))}><AArrowUp size={16}/></button>
    {showLink&&<button type="button" aria-label="Appearance & display" title="Appearance & display (this device)" onClick={()=>go(PREFS_PAGE)}><SlidersHorizontal size={16}/></button>}
  </div>;
}
