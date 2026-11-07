// CP-1 · Personal preferences for THIS device (plan Customisation Part 1, DEC-044 extended, DEC-CP-1).
// Saved in localStorage under one versioned key; never hotel data, never audited, never sent to the server.
// Pure helpers (sanitize / resolve*) are unit-tested without a DOM (tests/stream-prefs.mjs); the hook keeps every tab in sync.
import {useSyncExternalStore,useCallback,useEffect} from 'react';
import {HOME_PAGE} from './navmap';
import {styleStatusBar,STATUS_BAR_COLOURS} from './native';

export const PREFS_KEY='hcc-prefs-1';
/** Bump when a stored value changes meaning; an older (or newer) stored version is ignored and the defaults apply. */
export const PREFS_VERSION=1;

export type TextScale=0.9|1|1.15|1.3;
export type ReduceMotion='system'|'on'|'off';
export type Density='comfortable'|'compact';
export type ListPageSize=25|50|100|200;
export type ToastSeconds=3|6.5|10|0;
/** CP-5: 'system' follows the device's light/dark setting (prefers-color-scheme), live. */
export type Theme='light'|'dark'|'hc'|'system';
/** The theme actually on screen (<html data-theme>). */
export type ResolvedTheme='light'|'dark'|'hc';
export type Prefs={
  /** Text size multiplier, set on <html> as --hcc-text (CP-2 makes the CSS honour it). */
  textScale:TextScale;
  /** APK only: keep following the Android system font size (stacks with textScale). */
  honourSystemFont:boolean;
  /** 'system' follows the device setting, 'on' always reduces motion, 'off' never forces it (the device setting still applies). */
  reduceMotion:ReduceMotion;
  density:Density;
  /** Page ID to open after sign-in; '' = the role default (Modules Home). */
  startPage:string;
  /** Rows per page for long desktop lists (phones are capped, see resolvePageSize). */
  listPageSize:ListPageSize;
  /** Seconds a message stays on screen; 0 = until dismissed. */
  toastSeconds:ToastSeconds;
  /** CP-5 colour theme: Light (Maroon & Sand), Dark, High contrast, or follow the device. */
  theme:Theme;
};

export const TEXT_SCALES:readonly TextScale[]=[0.9,1,1.15,1.3];
export const TEXT_SCALE_LABEL:Record<string,string>={'0.9':'Small','1':'Default','1.15':'Large','1.3':'Extra large'};
export const REDUCE_MOTION:readonly ReduceMotion[]=['system','on','off'];
export const DENSITIES:readonly Density[]=['comfortable','compact'];
export const PAGE_SIZES:readonly ListPageSize[]=[25,50,100,200];
export const TOAST_SECONDS:readonly ToastSeconds[]=[3,6.5,10,0];
export const THEMES:readonly Theme[]=['light','dark','hc','system'];
export const THEME_LABEL:Record<Theme,string>={light:'Light',dark:'Dark',hc:'High contrast',system:'Follow device'};

export const PREFS_DEFAULTS:Readonly<Prefs>=Object.freeze({textScale:1,honourSystemFont:true,reduceMotion:'system',density:'comfortable',startPage:'',listPageSize:100,toastSeconds:6.5,theme:'light'});

const pick=<T>(allowed:readonly T[],v:unknown,fallback:T):T=>allowed.includes(v as T)?v as T:fallback;
const PAGE_ID=/^[a-z0-9][a-z0-9:-]{0,63}$/;
/** Keeps only known fields with allowed values; anything else falls back to the default for that field. */
export function sanitizePrefs(raw:unknown):Prefs{
  const r=(raw&&typeof raw==='object'?raw:{}) as Record<string,unknown>,d=PREFS_DEFAULTS;
  return {
    textScale:pick(TEXT_SCALES,r.textScale,d.textScale),
    honourSystemFont:typeof r.honourSystemFont==='boolean'?r.honourSystemFont:d.honourSystemFont,
    reduceMotion:pick(REDUCE_MOTION,r.reduceMotion,d.reduceMotion),
    density:pick(DENSITIES,r.density,d.density),
    startPage:typeof r.startPage==='string'&&(r.startPage===''||PAGE_ID.test(r.startPage))?r.startPage:d.startPage,
    listPageSize:pick(PAGE_SIZES,r.listPageSize,d.listPageSize),
    toastSeconds:pick(TOAST_SECONDS,r.toastSeconds,d.toastSeconds),
    theme:pick(THEMES,r.theme,d.theme),
  };
}
/** Stored text → preferences. Missing, unreadable or other-version data gives the defaults. */
export function parsePrefs(text:string|null|undefined):Prefs{
  if(!text)return {...PREFS_DEFAULTS};
  try{const o=JSON.parse(text) as {v?:unknown;p?:unknown};return o&&o.v===PREFS_VERSION?sanitizePrefs(o.p):{...PREFS_DEFAULTS};}
  catch{return {...PREFS_DEFAULTS};}
}
export const serializePrefs=(p:Prefs)=>JSON.stringify({v:PREFS_VERSION,p:sanitizePrefs(p)});

// ── Pure resolvers ──
/** Start page after sign-in: the chosen page when this user may open it, else the role default (today every role starts on Modules Home;
 *  the per-role `landing` table in nav.ts is informational). `allowed` = the page IDs the user may open (App's allowedIds). */
export function resolveStartPage(_role:string,startPage:string,allowed:readonly string[]):string{
  if(startPage&&(startPage===HOME_PAGE||allowed.includes(startPage)))return startPage;
  return HOME_PAGE;
}
/** Rows per page: desktop uses the preference; phones show 25 → 25, 50 or 100 → 50 (the phone default), 200 → 100 (cap for speed). */
export const PHONE_PAGE_CAP=100;
export function resolvePageSize(pref:ListPageSize,phone:boolean):number{
  if(!phone)return pref;
  return pref<=25?25:pref<=100?50:PHONE_PAGE_CAP;
}
/** Toast lifetime in ms, or null = stays until dismissed. */
export const toastMs=(seconds:ToastSeconds):number|null=>seconds>0?Math.round(seconds*1000):null;
/** ADM-UX: a message that must be read (configuration changes) lasts at least `minMs`; the personal 'until dismissed' choice stays as it is. */
export const toastDuration=(seconds:ToastSeconds,minMs?:number):number|null=>{const base=toastMs(seconds);return base===null||!minMs?base:Math.max(base,minMs);};
/** Touch tokens per density (DEC-060). Compact tightens the tablet primary target; nothing goes below 44 px on touch. */
export const TOUCH_MIN=44;
export function densityTokens(d:Density):{dense:number;primary:number}{
  return d==='compact'?{dense:TOUCH_MIN,primary:TOUCH_MIN}:{dense:44,primary:48};
}
/** <html data-motion>: 'off' = animations off (forced), 'on' = not forced (device setting still applies), 'system' = device decides. */
export const motionAttr=(m:ReduceMotion):'off'|'on'|'system'=>m==='on'?'off':m==='off'?'on':'system';
/** A− / A+ quick control: the next step in TEXT_SCALES (stays at the ends). */
export function stepTextScale(cur:TextScale,dir:1|-1):TextScale{
  const i=TEXT_SCALES.indexOf(cur),j=Math.min(TEXT_SCALES.length-1,Math.max(0,(i<0?1:i)+dir));
  return TEXT_SCALES[j];
}
export const textScaleLabel=(t:TextScale)=>TEXT_SCALE_LABEL[String(t)]??'Default';
export const toastLabel=(t:ToastSeconds)=>t===0?'Until dismissed':`${t} s`;
/** CP-5: the theme on screen. 'system' = dark when the device asks for a dark colour scheme, else light. */
export function resolveTheme(pref:Theme,systemDark:boolean):ResolvedTheme{return pref==='system'?(systemDark?'dark':'light'):pref;}
/** Status bar and <meta name="theme-color"> per theme: the phone app-bar fill (light icons in every theme). */
export const THEME_CHROME:Readonly<Record<ResolvedTheme,string>>=STATUS_BAR_COLOURS;
export const themeChrome=(t:ResolvedTheme):string=>THEME_CHROME[t]??THEME_CHROME.light;
const DARK_QUERY='(prefers-color-scheme: dark)';
type MediaLike={matches:boolean;addEventListener?:(t:'change',f:()=>void)=>void;removeEventListener?:(t:'change',f:()=>void)=>void};
const darkMedia=():MediaLike|null=>{try{return (globalThis as {matchMedia?:(q:string)=>MediaLike}).matchMedia?.(DARK_QUERY)??null;}catch{return null;}};
/** Does this device currently ask for a dark colour scheme? (false when unknown) */
export const systemPrefersDark=():boolean=>!!darkMedia()?.matches;

// ── Storage + store (one in-memory copy shared by every hook; the storage event brings changes from other tabs) ──
type DocLike={documentElement:{style:{setProperty:(k:string,v:string)=>void};setAttribute:(k:string,v:string)=>void};querySelector?:(sel:string)=>{setAttribute:(k:string,v:string)=>void}|null};
const storage=():Storage|null=>{try{return globalThis.localStorage??null;}catch{return null;}};
export function readPrefs():Prefs{try{return parsePrefs(storage()?.getItem(PREFS_KEY));}catch{return {...PREFS_DEFAULTS};}}
let cache:Prefs|null=null;
const listeners=new Set<()=>void>();
const emit=()=>{for(const l of [...listeners])l();};
const snapshot=():Prefs=>cache??=readPrefs();
const serverSnapshot=():Prefs=>PREFS_DEFAULTS as Prefs;
/** Merges a change into the saved preferences. Returns the new preferences (kept in memory even when storage is unavailable). */
export function savePrefs(partial:Partial<Prefs>):Prefs{
  const next=sanitizePrefs({...snapshot(),...partial});
  try{storage()?.setItem(PREFS_KEY,serializePrefs(next));}catch{/* storage full or blocked: this session only */}
  cache=next;emit();return next;
}
/** Back to the defaults on this device. */
export function resetPrefs():Prefs{
  try{storage()?.removeItem(PREFS_KEY);}catch{/* ignore */}
  cache={...PREFS_DEFAULTS};emit();return cache;
}
let storageHooked=false;
function onStorage(e:StorageEvent){if(e.key===PREFS_KEY||e.key===null){cache=readPrefs();emit();}}
export function subscribePrefs(fn:()=>void):()=>void{
  listeners.add(fn);
  if(!storageHooked){try{window.addEventListener('storage',onStorage);storageHooked=true;}catch{/* no window (tests) */}}
  return ()=>{listeners.delete(fn);};
}
/** Current preferences (the same copy every usePrefs() hook sees). */
export const getPrefs=():Prefs=>snapshot();
/** Test hook: forget the in-memory copy so the next read comes from storage. */
export const resetPrefsCache=()=>{cache=null;};

/** Writes the preferences onto <html>: --hcc-text, data-density, data-motion, data-theme (CP-5; also <meta theme-color> and the APK
 *  status bar). Printing ignores them (prefs.css; themes.css keeps dark / high contrast screen-only). Returns the theme on screen. */
export function applyPrefsToDocument(p:Prefs,doc:DocLike|undefined=(globalThis as {document?:DocLike}).document,systemDark:boolean=systemPrefersDark()):ResolvedTheme{
  const theme=resolveTheme(p.theme,systemDark);
  if(!doc)return theme;
  try{
    const el=doc.documentElement;
    el.style.setProperty('--hcc-text',String(p.textScale));
    el.setAttribute('data-density',p.density);
    el.setAttribute('data-motion',motionAttr(p.reduceMotion));
    el.setAttribute('data-theme',theme);
    doc.querySelector?.('meta[name="theme-color"]')?.setAttribute('content',themeChrome(theme));
  }catch{/* ignore */}
  styleStatusBar(theme);
  return theme;
}
// Before React renders (this module loads with App): put the saved theme on <html> so a dark device never flashes light.
try{if((globalThis as {document?:unknown}).document)applyPrefsToDocument(readPrefs());}catch{/* ignore */}

export type PrefsApi={prefs:Prefs;save:(p:Partial<Prefs>)=>void;reset:()=>void};
/** Live preferences; re-renders on any change in this tab or another tab on the same device. */
export function usePrefs():PrefsApi{
  const prefs=useSyncExternalStore(subscribePrefs,snapshot,serverSnapshot);
  const save=useCallback((p:Partial<Prefs>)=>{savePrefs(p);},[]);
  const reset=useCallback(()=>{resetPrefs();},[]);
  return {prefs,save,reset};
}
/** Keeps <html> in step with the preferences (App mounts it once at boot). */
export function useApplyPrefs(){
  const {prefs}=usePrefs();
  useEffect(()=>{applyPrefsToDocument(prefs);},[prefs]);
  // Follow device: re-apply when the device switches between light and dark while the app is open.
  useEffect(()=>{
    if(prefs.theme!=='system')return undefined;
    const mq=darkMedia();if(!mq?.addEventListener)return undefined;
    const on=()=>{applyPrefsToDocument(prefs);};
    mq.addEventListener('change',on);return ()=>mq.removeEventListener?.('change',on);
  },[prefs]);
}
/** The theme on screen now (resolves Follow device; re-renders when the device switches). */
export function useResolvedTheme():ResolvedTheme{
  const {prefs}=usePrefs();
  const dark=useSyncExternalStore(fn=>{const mq=darkMedia();if(!mq?.addEventListener)return ()=>{};mq.addEventListener('change',fn);return ()=>mq.removeEventListener?.('change',fn);},systemPrefersDark,()=>false);
  return resolveTheme(prefs.theme,dark);
}
