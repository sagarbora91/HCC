// Browser adapter for versioned local persistence. Per browser only; never shared across devices (DEC-022).
// Plan H-05: IndexedDB (database "hcc", store "state", key "current") because the FY26 state is several MB; small states fall
// back to localStorage when IndexedDB is unavailable. Works in Chrome and in the Capacitor WebView. Writes are debounced.
import {State} from '../services/mock';
import {restoreState,STORAGE_KEY,LEGACY_KEYS,Restored,DEFAULT_SCENARIO} from '../services/mock';
export const IDB_NAME='hcc',IDB_STORE='state',IDB_KEY='current';
/** localStorage is only used as a fallback, and only for states below this many characters. */
export const LS_LIMIT=4_000_000;
export const SAVE_DELAY_MS=500;
export type StorageDeps={idb?:IDBFactory;ls?:Storage;delay?:number;setTimer?:(fn:()=>void,ms:number)=>unknown;clearTimer?:(h:unknown)=>void};
export type StateStorage={load():Promise<Restored>;save(s:State):boolean;flush():Promise<void>;onError(cb:()=>void):void};

export function createStateStorage(deps:StorageDeps):StateStorage{
  const {idb,ls}=deps,delay=deps.delay??SAVE_DELAY_MS;
  const setT=deps.setTimer??((fn:()=>void,ms:number)=>setTimeout(fn,ms)),clearT=deps.clearTimer??((h:unknown)=>clearTimeout(h as ReturnType<typeof setTimeout>));
  let dbp:Promise<IDBDatabase>|null=null,latest:State|null=null,timer:unknown,queue:Promise<void>=Promise.resolve(),broken=false,onErr:()=>void=()=>{};

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
  const idbGet=async():Promise<string|null>=>{
    const d=await db();
    return new Promise((res,rej)=>{const r=d.transaction(IDB_STORE,'readonly').objectStore(IDB_STORE).get(IDB_KEY);r.onsuccess=()=>res(typeof r.result==='string'?r.result:null);r.onerror=()=>rej(r.error);});
  };
  const idbPut=async(json:string):Promise<void>=>{
    const d=await db();
    return new Promise((res,rej)=>{const tx=d.transaction(IDB_STORE,'readwrite');tx.objectStore(IDB_STORE).put(json,IDB_KEY);tx.oncomplete=()=>res();tx.onerror=()=>rej(tx.error);tx.onabort=()=>rej(tx.error);});
  };
  const lsGet=(k:string)=>{try{return ls?.getItem(k)??null;}catch{return null;}};

  async function load():Promise<Restored>{
    let raw:string|null=null;
    try{raw=await idbGet();}catch{/* fall through to localStorage */}
    if(raw===null)raw=lsGet(STORAGE_KEY);
    return restoreState(raw,LEGACY_KEYS.some(k=>lsGet(k)!==null),DEFAULT_SCENARIO);
  }
  function write(){
    const s=latest;if(!s)return Promise.resolve();
    latest=null;
    queue=queue.then(async()=>{
      const json=JSON.stringify(s);
      try{
        await idbPut(json);
        try{ls?.removeItem(STORAGE_KEY);LEGACY_KEYS.forEach(k=>ls?.removeItem(k));}catch{/* ignore */}
        broken=false;
      }catch{
        try{if(!ls||json.length>=LS_LIMIT)throw new Error('too large');ls.setItem(STORAGE_KEY,json);broken=false;}
        catch{broken=true;onErr();}
      }
    });
    return queue;
  }
  return {
    load,
    save(s){latest=s;if(timer!==undefined)clearT(timer);timer=setT(()=>{timer=undefined;void write();},delay);return !broken;},
    flush(){if(timer!==undefined){clearT(timer);timer=undefined;}return write();},
    onError(cb){onErr=cb;},
  };
}

let shared:StateStorage|null=null;
function instance():StateStorage{
  if(shared)return shared;
  let idb:IDBFactory|undefined,ls:Storage|undefined;
  try{idb=typeof indexedDB!=='undefined'?indexedDB:undefined;}catch{/* blocked */}
  try{ls=typeof localStorage!=='undefined'?localStorage:undefined;}catch{/* blocked */}
  shared=createStateStorage({idb,ls});
  // Do not lose the last 500 ms of changes when the page is hidden or closed.
  try{
    window.addEventListener('pagehide',()=>{void shared!.flush();});
    document.addEventListener('visibilitychange',()=>{if(document.visibilityState==='hidden')void shared!.flush();});
  }catch{/* not a browser */}
  return shared;
}
export function loadStored():Promise<Restored>{return instance().load();}
export function saveStored(s:State):boolean{return instance().save(s);}
/** Called when neither IndexedDB nor localStorage could store the state. */
export function onStorageError(cb:()=>void){instance().onError(cb);}
