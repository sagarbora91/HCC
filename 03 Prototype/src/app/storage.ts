// Browser adapter for versioned local persistence. Per browser only; never shared across devices (DEC-022).
// Plan H-05: IndexedDB (database "hcc", store "state") because the FY26 state is several MB; small states fall back to
// localStorage when IndexedDB is unavailable. Works in Chrome and in the Capacitor WebView. Writes are debounced.
//
// Store format (audit P-03): one record per top-level object slice ("slice:bookings", "slice:posX", …) plus a "manifest"
// record (format, schema, key order, scalar fields). A save writes only the slices whose object identity changed since the
// last successful save (execute() shares untouched slices) plus the manifest, all in ONE readwrite transaction, so a crash
// or a failed write can never leave a mix of old and new slices. The older single-blob format (key "current", and the
// localStorage key `hcc-prototype-v2`) is still read and migrated (persistence.ts SCHEMA rules); the first save after such
// a load rewrites every slice and deletes "current" in the same transaction.
import {State} from '../services/mock';
import {inspectSaved,loadScenario,STORAGE_KEY,LEGACY_KEYS,Restored,DEFAULT_SCENARIO} from '../services/mock';
import {knownJson} from './jsonCache';
import {generateScenario,type GenProgress} from './scenarioGen';
export const IDB_NAME='hcc',IDB_STORE='state',IDB_KEY='current';
export const MANIFEST_KEY='manifest',SLICE_PREFIX='slice:',MANIFEST_FORMAT='hcc-slices-1';
/** localStorage is only used as a fallback, and only for states below this many characters. */
export const LS_LIMIT=4_000_000;
export const SAVE_DELAY_MS=500;
export type Manifest={format:string;schema:number;order:string[];slices:string[];scalars:Record<string,unknown>};
export type SaveStats={slices:string[];bytes:number;full:boolean};
export type StorageDeps={idb?:IDBFactory;ls?:Storage;delay?:number;setTimer?:(fn:()=>void,ms:number)=>unknown;clearTimer?:(h:unknown)=>void;
  /** Builds the fallback scenario when nothing is saved (the app passes the worker-backed generator). */
  generate?:(id:string,onProgress?:(p:GenProgress)=>void)=>Promise<State>};
export type StateStorage={load(onProgress?:(p:GenProgress)=>void):Promise<Restored>;save(s:State):boolean;flush():Promise<void>;onError(cb:()=>void):void;
  /** What the last successful IndexedDB save wrote (diagnostics and tests). */
  lastSave():SaveStats|null};
type Loaded={obj:Record<string,unknown>;schema:number}|{blob:string}|{corrupt:true}|null;

const isObj=(v:unknown):v is object=>v!==null&&typeof v==='object';

export function createStateStorage(deps:StorageDeps):StateStorage{
  const {idb,ls}=deps,delay=deps.delay??SAVE_DELAY_MS,generate=deps.generate??(async(id:string)=>loadScenario(id));
  const setT=deps.setTimer??((fn:()=>void,ms:number)=>setTimeout(fn,ms)),clearT=deps.clearTimer??((h:unknown)=>clearTimeout(h as ReturnType<typeof setTimeout>));
  let dbp:Promise<IDBDatabase>|null=null,latest:State|null=null,timer:unknown,queue:Promise<void>=Promise.resolve(),broken=false,onErr:()=>void=()=>{};
  /** Slice objects as of the last successful IndexedDB save; null = unknown, so the next save rewrites everything. */
  let saved:Map<string,unknown>|null=null,stats:SaveStats|null=null;

  const db=():Promise<IDBDatabase>=>{
    if(!idb)return Promise.reject(new Error('IndexedDB unavailable'));
    return dbp??=new Promise<IDBDatabase>((res,rej)=>{
      const req=idb.open(IDB_NAME,1);
      req.onupgradeneeded=()=>{req.result.createObjectStore(IDB_STORE);};
      req.onsuccess=()=>res(req.result);
      req.onerror=()=>{dbp=null;rej(req.error);};
      req.onblocked=()=>{dbp=null;rej(new Error('IndexedDB blocked'));};
    });
  };
  /** Reads the manifest and its slices (or the old single blob) in one readonly transaction. */
  const idbRead=async():Promise<Loaded>=>{
    const d=await db();
    return new Promise<Loaded>((res,rej)=>{
      const tx=d.transaction(IDB_STORE,'readonly'),st=tx.objectStore(IDB_STORE);
      let man:Manifest|null=null,blob:string|null=null,badManifest=false;const parts:Record<string,string>={};
      const m=st.get(MANIFEST_KEY);
      m.onsuccess=()=>{
        try{man=typeof m.result==='string'?JSON.parse(m.result) as Manifest:null;}catch{man=null;}
        if(man&&(man.format!==MANIFEST_FORMAT||!Array.isArray(man.order)||!Array.isArray(man.slices)||!man.scalars))man=null;
        if(!man){
          badManifest=m.result!==undefined;
          const r=st.get(IDB_KEY);r.onsuccess=()=>{if(typeof r.result==='string')blob=r.result;};return;}
        for(const k of man.slices){const r=st.get(SLICE_PREFIX+k);r.onsuccess=()=>{if(typeof r.result==='string')parts[k]=r.result;};}
      };
      tx.oncomplete=()=>{
        if(!man){res(blob!==null?{blob}:badManifest?{corrupt:true}:null);return;}
        const mf:Manifest=man;
        if(mf.slices.some(k=>!(k in parts))){res({corrupt:true});return;}
        try{
          const obj:Record<string,unknown>={},have=new Set(mf.slices);
          for(const k of mf.order)obj[k]=have.has(k)?JSON.parse(parts[k]):mf.scalars[k];
          res({obj,schema:mf.schema});
        }catch{res({corrupt:true});}
      };
      tx.onerror=()=>rej(tx.error);tx.onabort=()=>rej(tx.error);
    });
  };
  /** One readwrite transaction: changed slices + manifest; deletes slices that no longer exist (and, on a full write, the
   *  old single blob and any stray slice record). Resolves only when the transaction has committed. */
  const idbWrite=async(puts:[string,string][],dels:string[],full:boolean,keep:Set<string>):Promise<void>=>{
    const d=await db();
    return new Promise((res,rej)=>{
      const tx=d.transaction(IDB_STORE,'readwrite'),st=tx.objectStore(IDB_STORE);
      for(const [k,v] of puts)st.put(v,k);
      for(const k of dels)st.delete(k);
      if(full){const r=st.getAllKeys();r.onsuccess=()=>{for(const k of r.result as IDBValidKey[]){const key=String(k);
        if(key===IDB_KEY||key.startsWith(SLICE_PREFIX)&&!keep.has(key))st.delete(k);}};}
      tx.oncomplete=()=>res();tx.onerror=()=>rej(tx.error);tx.onabort=()=>rej(tx.error);
    });
  };
  const lsGet=(k:string)=>{try{return ls?.getItem(k)??null;}catch{return null;}};

  async function load(onProgress?:(p:GenProgress)=>void):Promise<Restored>{
    let got:Loaded=null;
    try{got=await idbRead();}catch{/* fall through to localStorage */}
    let raw:string|Record<string,unknown>|null=null;
    if(got&&'obj' in got)raw=got.obj;
    else if(got&&'blob' in got)raw=got.blob;
    else if(got&&'corrupt' in got)raw='{unreadable';
    else raw=lsGet(STORAGE_KEY);
    const r=inspectSaved(raw,LEGACY_KEYS.some(k=>lsGet(k)!==null),DEFAULT_SCENARIO);
    if(r.state){
      // Slices just read are what IndexedDB holds, so the next save writes only what changes — unless a migration ran.
      if(got&&'obj' in got&&!r.migrated){const st=r.state as unknown as Record<string,unknown>;saved=new Map(Object.keys(st).filter(k=>isObj(st[k])).map(k=>[k,st[k]]));}
      return {state:r.state,reset:false};
    }
    const state=await generate(DEFAULT_SCENARIO,onProgress);
    // First run (nothing saved at all): store the generated data now so the next start does not generate it again.
    // A reset of unreadable or old data is only written by the next command, as before.
    if(raw===null&&!r.reset)save(state);
    return {state,reset:r.reset,...(r.reason?{reason:r.reason}:{})};
  }
  function write(){
    const s=latest;if(!s)return queue;// nothing new: wait for a write already in progress
    latest=null;
    queue=queue.then(async()=>{
      const st=s as unknown as Record<string,unknown>,full=saved===null;
      const man:Manifest={format:MANIFEST_FORMAT,schema:s.schema,order:[],slices:[],scalars:{}};
      const puts:[string,string][]=[],written:string[]=[],next=new Map<string,unknown>();
      let bytes=0;
      for(const k of Object.keys(st)){
        const v=st[k];if(v===undefined||typeof v==='function')continue;// not representable in JSON (as before)
        man.order.push(k);
        if(!isObj(v)){man.scalars[k]=v;continue;}
        man.slices.push(k);next.set(k,v);
        if(!full&&saved!.get(k)===v)continue;
        const json=knownJson(v)??JSON.stringify(v);puts.push([SLICE_PREFIX+k,json]);written.push(k);bytes+=json.length;
      }
      const manJson=JSON.stringify(man);puts.push([MANIFEST_KEY,manJson]);bytes+=manJson.length;
      const dels=full?[]:[...saved!.keys()].filter(k=>!next.has(k)).map(k=>SLICE_PREFIX+k);
      try{
        await idbWrite(puts,dels,full,new Set(man.slices.map(k=>SLICE_PREFIX+k)));
        saved=next;stats={slices:written,bytes,full};
        try{ls?.removeItem(STORAGE_KEY);LEGACY_KEYS.forEach(k=>ls?.removeItem(k));}catch{/* ignore */}
        broken=false;
      }catch{
        saved=null;// unknown what IndexedDB holds now: the next successful save rewrites every slice
        try{const json=JSON.stringify(s);if(!ls||json.length>=LS_LIMIT)throw new Error('too large');ls.setItem(STORAGE_KEY,json);broken=false;}
        catch{broken=true;onErr();}
      }
    });
    return queue;
  }
  function save(s:State){latest=s;if(timer!==undefined)clearT(timer);timer=setT(()=>{timer=undefined;void write();},delay);return !broken;}
  return {
    load,save,
    flush(){if(timer!==undefined){clearT(timer);timer=undefined;}return write();},
    onError(cb){onErr=cb;},
    lastSave:()=>stats,
  };
}

let shared:StateStorage|null=null;
function instance():StateStorage{
  if(shared)return shared;
  let idb:IDBFactory|undefined,ls:Storage|undefined;
  try{idb=typeof indexedDB!=='undefined'?indexedDB:undefined;}catch{/* blocked */}
  try{ls=typeof localStorage!=='undefined'?localStorage:undefined;}catch{/* blocked */}
  shared=createStateStorage({idb,ls,generate:generateScenario});
  // Do not lose the last 500 ms of changes when the page is hidden or closed.
  try{
    window.addEventListener('pagehide',()=>{void shared!.flush();});
    document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='hidden')void shared!.flush();});
  }catch{/* not a browser */}
  return shared;
}
export function loadStored(onProgress?:(p:GenProgress)=>void):Promise<Restored>{return instance().load(onProgress);}
export function saveStored(s:State):boolean{return instance().save(s);}
/** Called when neither IndexedDB nor localStorage could store the state. */
export function onStorageError(cb:()=>void){instance().onError(cb);}
