// Compliance calendar (SCC tax module in full, DEC-116) — shared UI pieces for the desktop page, the phone page, the dialogs and the dashboard card.
import {dobUrl} from '../../domain/streams/docobjects';
import React,{useMemo,useSyncExternalStore} from 'react';
import {useHcc} from '../../app/context';
import {cmpSlice,cmpPayableFeed,cmpRichSegments,cmpOpeningMonth,cmpWhen,type CMPPayableFeed,type CMPMonth,type CMPCatFilter,type CMPQuarterKey,type State,
  type CMPDaysTag} from '../../services/mock';
import {saveBinaryFile,saveAndReport} from '../../app/native';
import {useServerFetch,type Fetched} from '../../clients/sync/reportsHooks';
import {getSyncRuntime} from '../../clients/sync/instance';

/** Unique idempotency key per user action. */
export const cmpActionKey=(s:State,...parts:(string|number|undefined)[])=>['cmp',...parts,s.clock,s.audit.length].join(':');
export const cmpName=(s:State,id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';

/** SCC texts carry only <strong> / <em> markup (built-in cards and month notes): rendered as elements, never as raw HTML. */
export function CmpRich({text}:{text:string}){
  return <>{cmpRichSegments(text).map((p,n)=>p.b&&p.i?<strong key={n}><em>{p.t}</em></strong>:p.b?<strong key={n}>{p.t}</strong>:p.i?<em key={n}>{p.t}</em>:<React.Fragment key={n}>{p.t}</React.Fragment>)}</>;
}
/** A custom obligation's text is the user's own: shown as plain text (SCC escapes it). */
export function CmpText({text,custom}:{text:string;custom?:boolean}){return custom?<>{text}</>:<CmpRich text={text}/>;}
/** SCC days tag (colour + text). */
export function CmpDaysPill({tag}:{tag:CMPDaysTag|null}){return tag?<span className={'cmp-days cmp-days-'+tag.cls}>{tag.text}</span>:null;}
export const cmpWhenText=cmpWhen;

// ── Page state shared by the page and its dialogs (Action Center "Open →" jumps to a month and a card) ──
type UiState={view:'calendar'|'dashboard';month?:CMPMonth;filter:CMPCatFilter;quarter:CMPQuarterKey;focus?:string};
let ui:UiState={view:'calendar',filter:'all',quarter:'Q1'};
const listeners=new Set<()=>void>();
export function cmpUiSet(p:Partial<UiState>){ui={...ui,...p};listeners.forEach(l=>l());}
export function useCmpUi(today:string):UiState&{month:CMPMonth}{
  const st=useSyncExternalStore(l=>{listeners.add(l);return ()=>{listeners.delete(l);};},()=>ui,()=>ui);
  return {...st,month:st.month??cmpOpeningMonth(today)};
}

// ── "≈₹ payable" feed: computed from the demo's locked pay runs and invoices; production: the server's GET /compliance/payable ──
type SrvPayable={months:Record<string,{pfPaise:number;esicPaise:number;ptPaise:number;gstPaise:number;sources:string[];asOf:string}>;note:string};
const NO_FETCH:Fetched<CMPPayableFeed>={status:'idle'};
export function useCmpFeed():CMPPayableFeed|undefined{
  const {s}=useHcc();const fy=cmpSlice(s).fyStart;
  const srv=process.env.HCC_BUILD==='production'?useServerFetch(`cmp-payable|${fy}`,async api=>{ // eslint-disable-line react-hooks/rules-of-hooks
    const r=await api.get<SrvPayable>('/compliance/payable',{fy});
    return {note:r.note,months:Object.fromEntries(Object.entries(r.months??{}).map(([k,v])=>[k,{pf:v.pfPaise,esic:v.esicPaise,pt:v.ptPaise,gstEstimate:v.gstPaise,sources:v.sources??[],at:v.asOf}]))};}):NO_FETCH;
  const local=useMemo(()=>process.env.HCC_BUILD==='production'?undefined:cmpPayableFeed(s,fy),[s.staffX,s.bookings,s.posX,s.date,fy]); // eslint-disable-line react-hooks/exhaustive-deps
  return process.env.HCC_BUILD==='production'?srv.data:local;
}

// ── Evidence files: the demo keeps the content in s.docObjectsX (LF-docdb); production downloads it from the server ──
type SrvFile={fileName:string;contentType:string;base64:string};
const b64Bytes=(b64:string)=>{const bin=atob(b64),a=new Uint8Array(bin.length);for(let i=0;i<bin.length;i++)a[i]=bin.charCodeAt(i);return a;};
export const cmpDataUrlBytes=(dataUrl:string)=>{const i=dataUrl.indexOf(',');return b64Bytes(i<0?dataUrl:dataUrl.slice(i+1));};
/** The bytes of an evidence file. */
export async function cmpFileBytes(s:State,id:string):Promise<{bytes:Uint8Array;type:string;name:string}|null>{
  const f=cmpSlice(s).files.find(x=>x.id===id);if(!f)return null;
  if(process.env.HCC_BUILD==='production'){const r=await getSyncRuntime().api.get<SrvFile>(`/compliance/evidence/${encodeURIComponent(id)}`);return {bytes:b64Bytes(r.base64),type:r.contentType,name:r.fileName};}
  const d=dobUrl(s,f.objectRef);return d?{bytes:cmpDataUrlBytes(d),type:f.type,name:f.name}:null; // LF-docdb: the demo's content is in s.docObjectsX
}
/** SCC "View" (shell file viewer) → HCC saves or shares the file (browser download / Android share sheet). */
export function useCmpOpenFile(){
  const {s,toast}=useHcc();
  return async (id:string)=>{try{const f=await cmpFileBytes(s,id);if(!f){toast('The file is not available.',true);return;}
    await saveAndReport(saveBinaryFile(f.name,f.bytes,f.type||'application/octet-stream'),toast,`${f.name} saved`,'the evidence file');}
    catch(e){toast('The file could not be loaded: '+String((e as {message?:string})?.message??e),true);}};
}
/** Reads a picked file as a data URL (the command carries it to the store / the server). */
export const cmpReadFile=(f:File)=>new Promise<string>((res,rej)=>{const r=new FileReader();r.onload=()=>res(String(r.result));r.onerror=()=>rej(r.error);r.readAsDataURL(f);});
/** SCC evBytes (L2300). */
export const cmpBytes=(n:number)=>{n=+n||0;return n<1024?n+' B':n<1048576?Math.round(n/1024)+' KB':(n/1048576).toFixed(1)+' MB';};
