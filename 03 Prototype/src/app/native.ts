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
/** Maroon status bar with light icons (native only). */
export function styleStatusBar(){
  const sb=nativePlugin('StatusBar');if(!sb)return;
  try{void sb.setOverlaysWebView?.({overlay:false});void sb.setBackgroundColor?.({color:'#7a1f2b'});void sb.setStyle?.({style:'DARK'});}catch{/* ignore */}
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
/** Saves text to `Documents/HCC/<name>` and opens the share sheet (native), or downloads it (browser). */
export async function saveTextFile(name:string,text:string,mime:string,bom=false):Promise<SaveResult>{
  const fs=nativePlugin('Filesystem'),share=nativePlugin('Share');
  if(fs&&share){
    try{
      const path='HCC/'+name;
      await fs.writeFile({path,data:toBase64((bom?'﻿':'')+text),directory:'DOCUMENTS',recursive:true});
      const {uri}=await fs.getUri({path,directory:'DOCUMENTS'});
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
/** Binary twin of saveTextFile (e.g. .xlsx): Documents/HCC/<name> + share sheet (native), or a download (browser). */
export async function saveBinaryFile(name:string,bytes:Uint8Array,mime:string):Promise<SaveResult>{
  const fs=nativePlugin('Filesystem'),share=nativePlugin('Share');
  if(fs&&share){
    try{
      const path='HCC/'+name;
      // W05 review F6: written in base64 chunks (writeFile + appendFile) so a large workbook never becomes one huge string on the bridge.
      for(let i=0;i<Math.max(1,bytes.length);i+=NATIVE_CHUNK){const data=bytesToBase64(bytes.subarray(i,i+NATIVE_CHUNK));
        if(i===0)await fs.writeFile({path,data,directory:'DOCUMENTS',recursive:true});else await fs.appendFile({path,data,directory:'DOCUMENTS'});}
      const {uri}=await fs.getUri({path,directory:'DOCUMENTS'});
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
