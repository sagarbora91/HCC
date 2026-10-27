// Native (Capacitor APK) hooks, used only when `window.Capacitor` exists — no npm packages in the prototype (plan D1).
// Plugins are reached through the bridge the APK injects: Capacitor.registerPlugin('App' | 'StatusBar' | 'Filesystem' | 'Share').
// In a normal browser every helper falls back to the existing behaviour (window.print, a download link).

import {useEffect} from 'react';

type Plugin=Record<string,(...a:any[])=>any>;
type CapacitorLike={isNativePlatform?:()=>boolean;registerPlugin?:(name:string)=>Plugin;Plugins?:Record<string,Plugin>};
const cap=():CapacitorLike|undefined=>(globalThis as {Capacitor?:CapacitorLike}).Capacitor;
export const isNative=()=>{try{return !!cap()?.isNativePlatform?.();}catch{return false;}};
const cache=new Map<string,Plugin|undefined>();
export function nativePlugin(name:string):Plugin|undefined{
  if(!isNative())return undefined;
  if(cache.has(name))return cache.get(name);
  let p:Plugin|undefined;
  try{p=cap()!.registerPlugin?.(name)??cap()!.Plugins?.[name];}catch{p=cap()?.Plugins?.[name];}
  cache.set(name,p);return p;
}
/** True when the APK really contains the native plugin (registerPlugin returns a proxy for any name). */
export function nativePluginAvailable(name:string):boolean{
  if(!isNative())return false;
  try{const c=cap() as CapacitorLike&{isPluginAvailable?:(n:string)=>boolean};return c?.isPluginAvailable?c.isPluginAvailable(name):!!c?.Plugins?.[name];}catch{return false;}
}
/** Test hook: forget cached plugin proxies. */
export const resetNativeCache=()=>cache.clear();

/** Inside the APK, Auto means the phone layout only on phone-sized screens: the device's shorter side is under 600 CSS px (Android's own tablet cut-off).
 *  A tablet such as the Galaxy Tab S7 (≈ 670 px short side, any rotation) gets the desktop layout (owner decision, plan D6). */
export const PHONE_SHORT_SIDE=600;
export const nativePhoneLayout=(native:boolean,shortSide:number)=>native&&shortSide<PHONE_SHORT_SIDE;
export const screenShortSide=()=>{try{return Math.min(screen.width,screen.height);}catch{return 0;}};

// ── Android back button ──
export type BackState={sheetOpen:boolean;stepBack:boolean;depth:number;atLanding:boolean};
export type BackAction='close-sheet'|'step-back'|'history-back'|'go-landing'|'confirm-exit';
/** What the hardware back button does: close the sheet → previous wizard step → previous page → landing page → ask before exiting. */
export function decideBack(s:BackState):BackAction{
  if(s.sheetOpen)return 'close-sheet';
  if(s.stepBack)return 'step-back';
  if(s.depth>0)return 'history-back';
  if(!s.atLanding)return 'go-landing';
  return 'confirm-exit';
}
// A sheet with steps registers its "previous step" handler while it is on a step after the first.
const stepHandlers:(()=>void)[]=[];
export function pushStepBack(fn:()=>void){stepHandlers.push(fn);return ()=>{const i=stepHandlers.lastIndexOf(fn);if(i>=0)stepHandlers.splice(i,1);};}
/** While `active`, the Android back button steps this sheet back instead of closing it. */
export function useStepBack(active:boolean,fn:()=>void){useEffect(()=>active?pushStepBack(fn):undefined,[active]);} // eslint-disable-line react-hooks/exhaustive-deps
export const hasStepBack=()=>stepHandlers.length>0;
export const runStepBack=()=>{const fn=stepHandlers[stepHandlers.length-1];if(fn)fn();};
/** Registers the back-button listener (native only). Returns an unregister function. */
export function listenBackButton(onBack:()=>void):()=>void{
  const app=nativePlugin('App');if(!app?.addListener)return ()=>{};
  let handle:{remove?:()=>void}|undefined,dead=false;
  Promise.resolve(app.addListener('backButton',()=>onBack())).then(h=>{if(dead)h?.remove?.();else handle=h;}).catch(()=>{});
  return ()=>{dead=true;handle?.remove?.();};
}
export function exitApp(){try{void nativePlugin('App')?.exitApp?.();}catch{/* ignore */}}
/** CP-5: status bar colour per theme = the phone app-bar fill (--primary-fill in src/styles/themes.css); light icons in every theme. */
export const STATUS_BAR_COLOURS:Readonly<Record<'light'|'dark'|'hc',string>>=Object.freeze({light:'#7a1f2b',dark:'#ae4155',hc:'#5e1620'});
/** The theme <html data-theme> shows now (light when unknown). */
const currentTheme=():'light'|'dark'|'hc'=>{try{const t=(globalThis as {document?:{documentElement?:{getAttribute:(k:string)=>string|null}}}).document?.documentElement?.getAttribute('data-theme');return t==='dark'||t==='hc'?t:'light';}catch{return 'light';}};
/** The page surface colour of the theme on screen (the bottom tab bar), as #rrggbb, or undefined when it cannot be read. */
const surfaceColour=():string|undefined=>{try{const d=(globalThis as {document?:{documentElement?:unknown}}).document?.documentElement as Element|undefined;if(!d)return undefined;const v=(globalThis as unknown as {getComputedStyle:(e:Element)=>CSSStyleDeclaration}).getComputedStyle(d).getPropertyValue('--surface').trim();return /^#[0-9a-f]{6}$/i.test(v)?v:undefined;}catch{return undefined;}};
/** Status bar in the theme's app-bar colour with light icons (native only; no-op in a browser). Without an argument: the theme on screen.
 *  V14-2: on Android 15+ (edge-to-edge) StatusBar.setBackgroundColor is ignored, so the APK's own StatusBarColor plugin
 *  (04 Android … StatusBarColorPlugin.java) also paints the status-bar band (top) and the strip behind the gesture bar (bottom = theme surface).
 *  It does nothing below Android 15, where the StatusBar plugin still works; an older APK without it just skips that call. */
export function styleStatusBar(theme:'light'|'dark'|'hc'=currentTheme()){
  const sb=nativePlugin('StatusBar');if(!sb)return;
  const color=STATUS_BAR_COLOURS[theme]??STATUS_BAR_COLOURS.light;
  try{void sb.setOverlaysWebView?.({overlay:false});void sb.setBackgroundColor?.({color});void sb.setStyle?.({style:'DARK'});}catch{/* ignore */}
  try{if(nativePluginAvailable('StatusBarColor')){const bottom=surfaceColour();void Promise.resolve(nativePlugin('StatusBarColor')?.setColors?.(bottom?{top:color,bottom}:{top:color})).catch(()=>{});}}catch{/* ignore */}
}
type MediaLike={addEventListener?:(t:'change',fn:()=>void)=>void;removeEventListener?:(t:'change',fn:()=>void)=>void};
type WinLike={addEventListener:(t:string,fn:()=>void)=>void;removeEventListener:(t:string,fn:()=>void)=>void;setTimeout:(fn:()=>void,ms:number)=>unknown;clearTimeout:(id:unknown)=>void;matchMedia?:(q:string)=>MediaLike|null};
/** V15-1: on Android 15+ a rotation re-lays out the decor view and the StatusBarColor paint falls back to the manifest colour (maroon) while the
 *  theme stays dark, so the APK paints the theme colours again a moment after every resize / orientation change (debounced: the inset padding
 *  settles after the layout pass). V16-2: the same happens when the system night mode flips while the app theme is fixed (uiMode is in the
 *  activity's configChanges, so nothing resizes; Follow device was already repainted by prefs.ts), so a prefers-color-scheme change repaints too.
 *  Native only (a no-op in a browser). Returns the unregister function. */
export const STATUS_BAR_RESTYLE_MS=150;
export const DARK_SCHEME_QUERY='(prefers-color-scheme: dark)';
export function listenStatusBarRestyle(delayMs=STATUS_BAR_RESTYLE_MS,win:WinLike|undefined=globalThis as unknown as WinLike):()=>void{
  if(!isNative()||typeof win?.addEventListener!=='function')return ()=>{};
  let timer:unknown;
  const fire=()=>{if(timer!==undefined)win.clearTimeout(timer);timer=win.setTimeout(()=>{timer=undefined;styleStatusBar();},delayMs);};
  win.addEventListener('resize',fire);win.addEventListener('orientationchange',fire);
  let media:MediaLike|null=null;
  try{media=win.matchMedia?.(DARK_SCHEME_QUERY)??null;media?.addEventListener?.('change',fire);}catch{media=null;}
  return ()=>{if(timer!==undefined)win.clearTimeout(timer);timer=undefined;win.removeEventListener('resize',fire);win.removeEventListener('orientationchange',fire);try{media?.removeEventListener?.('change',fire);}catch{/* ignore */}};
}

// ── Files: CSV export and printing ──
/** `HCC-<slug>-<yyyymmdd>.<ext>`; only letters, digits, dot, dash and underscore survive. */
export function nativeFileName(title:string,ext:string,date=''):string{
  const slug=title.replace(/\.[a-z0-9]{2,4}$/i,'').replace(/[^A-Za-z0-9._-]+/g,'-').replace(/^-+|-+$/g,'').slice(0,60)||'document';
  const withHcc=/^HCC-/i.test(slug)?slug:'HCC-'+slug;
  return withHcc+(date?'-'+date.split('-').join(''):'')+'.'+ext.replace(/^\./,'');
}
export type SaveResult='downloaded'|'shared'|'failed';
const toBase64=(text:string)=>{const bytes=new TextEncoder().encode(text);let bin='';for(let i=0;i<bytes.length;i+=0x8000)bin+=String.fromCharCode(...bytes.subarray(i,i+0x8000));return btoa(bin);};
/** Exports are written to the app's private cache (`exports/<name>`), never to the public Documents folder: the next user of a shared tablet
 *  cannot browse them, the OS may reclaim them, and clearExports() deletes them on sign-out (audit A10-01). */
export const EXPORT_DIR='exports';
const EXPORT_STORE='CACHE';
/** Deletes every export this app wrote to its cache. Native only (a no-op in a browser); a missing folder is not an error. Never throws. */
export async function clearExports():Promise<void>{
  const fs=nativePlugin('Filesystem');if(!fs)return;
  try{await fs.rmdir({path:EXPORT_DIR,directory:EXPORT_STORE,recursive:true});}catch{/* nothing to delete, or already gone */}
}
/** Wave B1 (DEC-073): the camera app writes a slip photo taken through <input capture> to the app's external files folder `Pictures/`
 *  (Capacitor 8 BridgeWebChromeClient, shared through the FileProvider entry hcc_slip_capture). Once the photo is compressed and kept in
 *  memory the original is deleted, so no full-size slip photo stays on the device. Native only; a missing folder is not an error; never throws. */
export const CAPTURE_DIR='Pictures';
export async function clearCapturedPhotos():Promise<void>{
  const fs=nativePlugin('Filesystem');if(!fs)return;
  try{await fs.rmdir({path:CAPTURE_DIR,directory:'EXTERNAL',recursive:true});}catch{/* nothing to delete, or already gone */}
}
/** Saves text to the app cache (`exports/<name>`) and opens the share sheet (native), or downloads it (browser). */
export async function saveTextFile(name:string,text:string,mime:string,bom=false):Promise<SaveResult>{
  const fs=nativePlugin('Filesystem'),share=nativePlugin('Share');
  if(fs&&share){
    try{
      const path=EXPORT_DIR+'/'+name;
      await fs.writeFile({path,data:toBase64((bom?'﻿':'')+text),directory:EXPORT_STORE,recursive:true});
      const {uri}=await fs.getUri({path,directory:EXPORT_STORE});
      await share.share({title:name,text:name,url:uri,dialogTitle:'Save or share '+name});
      return 'shared';
    }catch{return 'failed';}
  }
  try{
    const url=URL.createObjectURL(new Blob([(bom?'﻿':'')+text],{type:mime+';charset=utf-8'}));
    const a=document.createElement('a');a.href=url;a.download=name;a.click();URL.revokeObjectURL(url);return 'downloaded';
  }catch{return 'failed';}
}
export const saveCsv=(name:string,csv:string)=>saveTextFile(name,csv,'text/csv',true);
const bytesToBase64=(bytes:Uint8Array)=>{let bin='';for(let i=0;i<bytes.length;i+=0x8000)bin+=String.fromCharCode(...bytes.subarray(i,i+0x8000));return btoa(bin);};
/** Bytes per native write (a multiple of 3, so each base64 piece is whole). */
export const NATIVE_CHUNK=3*1024*1024;
/** Binary twin of saveTextFile (e.g. .xlsx): cache exports/<name> + share sheet (native), or a download (browser). */
export async function saveBinaryFile(name:string,bytes:Uint8Array,mime:string):Promise<SaveResult>{
  const fs=nativePlugin('Filesystem'),share=nativePlugin('Share');
  if(fs&&share){
    try{
      const path=EXPORT_DIR+'/'+name;
      // W05 review F6: written in base64 chunks (writeFile + appendFile) so a large workbook never becomes one huge string on the bridge.
      for(let i=0;i<Math.max(1,bytes.length);i+=NATIVE_CHUNK){const data=bytesToBase64(bytes.subarray(i,i+NATIVE_CHUNK));
        if(i===0)await fs.writeFile({path,data,directory:EXPORT_STORE,recursive:true});else await fs.appendFile({path,data,directory:EXPORT_STORE});}
      const {uri}=await fs.getUri({path,directory:EXPORT_STORE});
      await share.share({title:name,text:name,url:uri,dialogTitle:'Save or share '+name});
      return 'shared';
    }catch{return 'failed';}
  }
  try{
    const url=URL.createObjectURL(new Blob([bytes as BlobPart],{type:mime}));
    const a=document.createElement('a');a.href=url;a.download=name;a.click();URL.revokeObjectURL(url);return 'downloaded';
  }catch{return 'failed';}
}

/** Toast text for a save result (W05 review F11): a failed save is reported as failed, never as saved. */
export function saveMessage(r:SaveResult,okText:string,what:string):{text:string;error:boolean}{
  return r==='failed'?{text:`Could not save ${what}. Nothing was written — try again, or choose a shorter date range.`,error:true}:{text:okText,error:false};
}
/** Awaits a save and shows the right toast. Returns the result. */
export async function saveAndReport(save:Promise<SaveResult>,toast:(text:string,error?:boolean)=>void,okText:string,what:string):Promise<SaveResult>{
  let r:SaveResult;try{r=await save;}catch{r='failed';}
  const m=saveMessage(r,okText,what);toast(m.text,m.error);return r;
}

/** Standalone printable page from a fragment of the screen. */
export function printablePage(title:string,bodyHtml:string,css:string):string{
  const esc=(t:string)=>t.replace(/&/g,'&amp;').replace(/</g,'&lt;');
  return `<!doctype html><html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${esc(title)}</title><style>${css}</style><style>html,body{background:#fff!important;height:auto!important;min-height:0!important}body{margin:0;padding:16px}.no-print,.modal-actions{display:none!important}@page{size:A4;margin:14mm}@media print{body{padding:0}.invoice,.f-print-doc,.print-doc,.table-wrap{overflow:visible!important;max-height:none!important}thead{display:table-header-group}tr{break-inside:avoid}}</style></head><body>${bodyHtml}</body></html>`;
}
/** The part of the screen worth printing: the open sheet's document, else the page. */
function printTarget():{title:string;html:string}{
  const dlg=document.querySelector<HTMLElement>('[role=dialog]');
  const root=(dlg?.querySelector<HTMLElement>('.invoice,.f-print-doc,.print-doc')??dlg??document.querySelector<HTMLElement>('main')??document.body).cloneNode(true) as HTMLElement;
  root.querySelectorAll('.no-print,button,select,input[type=date]').forEach(n=>n.remove());
  const heading=(dlg??document).querySelector('h1,h2')?.textContent?.trim()||document.title;
  return {title:heading,html:root.outerHTML};
}
/** Opens Android's print service with a printable page (@capgo/capacitor-printer); if that is unavailable or fails, shares the page as an
 *  HTML file (Filesystem + Share) so it can be printed from Chrome or a printer app (owner decision OWNER-2). */
export async function printPage(title:string,page:string):Promise<SaveResult|'printed'>{
  const printer=nativePlugin('Printer');
  if(printer?.printHtml){try{await printer.printHtml({name:title,html:page});return 'printed';}catch{/* fall back to sharing */}}
  return saveTextFile(nativeFileName(title,'html'),page,'text/html');
}
/** Print button: `window.print()` in a browser; in the APK the on-screen document goes through printPage(). */
export async function printDocument():Promise<SaveResult|'printed'>{
  if(!isNative()){try{window.print();return 'printed';}catch{return 'failed';}}
  const {title,html}=printTarget();
  const css=Array.from(document.querySelectorAll('style')).map(s=>s.textContent??'').join('\n');
  return printPage(title,printablePage(title,html,css));
}

// ── Android system font size switch (CP-4) ──
// The APK's TextZoom plugin (04 Android … TextZoomPlugin.java) decides whether the web view follows Android's own "Font size" setting.
// Everything below is a no-op / undefined outside the APK, or in an older APK that has no TextZoom plugin. Never throws.
/** The native TextZoom plugin (`getTextZoom()` → {zoom, systemScale, honour}; `setHonourSystem({honour})`), or undefined in a browser / an APK without it. */
export function textZoomPlugin():Plugin|undefined{
  if(!isNative()||!nativePluginAvailable('TextZoom'))return undefined;
  return nativePlugin('TextZoom');
}
/** Switches "follow the Android font size" on or off (stored on the device and applied at every start). Resolves true when the APK applied it. */
export async function setHonourSystemFont(flag:boolean):Promise<boolean>{
  const p=textZoomPlugin();if(!p?.setHonourSystem)return false;
  try{await p.setHonourSystem({honour:!!flag});return true;}catch{return false;}
}
/** The Android font-size setting as a multiplier (1 = normal, 1.3 ≈ "Largest"), or undefined when not running in the APK / the value is unusable. */
export async function readSystemFontScale():Promise<number|undefined>{
  const p=textZoomPlugin();if(!p?.getTextZoom)return undefined;
  try{const r=await p.getTextZoom();const s=Number(r?.systemScale);return Number.isFinite(s)&&s>0?s:undefined;}catch{return undefined;}
}

// ── Screen privacy while a full guest ID number is on screen (audit A07-04) ──
// The APK's SecureScreen plugin (04 Android … SecureScreenPlugin.java) sets Android's FLAG_SECURE on the window: no screenshots, no screen
// recording or casting, and a blank recent-apps thumbnail. Each caller holds the flag while its content is shown; it is cleared when the last
// holder lets go (the plugin clears it only once the app is in the foreground again). No-op in a browser or an older APK without the plugin. Never throws.
/** The native SecureScreen plugin (`setSecure({secure})`), or undefined in a browser / an APK without it. */
export function secureScreenPlugin():Plugin|undefined{
  if(!isNative()||!nativePluginAvailable('SecureScreen'))return undefined;
  return nativePlugin('SecureScreen');
}
let secureHolds=0;
function sendSecure(secure:boolean){
  const p=secureScreenPlugin();if(!p?.setSecure)return;
  try{void Promise.resolve(p.setSecure({secure})).catch(()=>{/* an older plugin or a bridge error: nothing more to do */});}catch{/* ignore */}
}
/** Turns FLAG_SECURE on (first holder) and returns the release function (idempotent); the flag goes off when the last holder releases. */
export function holdSecureScreen():()=>void{
  secureHolds++;if(secureHolds===1)sendSecure(true);
  let released=false;
  return ()=>{if(released)return;released=true;secureHolds=Math.max(0,secureHolds-1);if(secureHolds===0)sendSecure(false);};
}
/** How many holders keep the screen secure now (tests and diagnostics). */
export const secureScreenHolds=()=>secureHolds;
/** While `active`, the APK window is FLAG_SECURE (React hook; released on unmount). */
export function useSecureScreen(active:boolean){useEffect(()=>active?holdSecureScreen():undefined,[active]);}
