// Read cache of the last server data (W4-0): one record per loaded slice + a meta record, in its OWN IndexedDB database
// ("hcc-server-cache") — never the prototype's demo store ("hcc", app/storage.ts). It is only a cache for read-only mode
// (server unreachable: screens keep showing the last loaded data with its "as of" time). Nothing is ever queued here:
// commands are refused while the server is unreachable. Cleared on sign-out and when another user signs in on the device.
import type {SyncMe} from './types';
import type {IdSnapshot} from './map';

export const CACHE_DB='hcc-server-cache',CACHE_STORE='slices',CACHE_META='meta',CACHE_FORMAT='hcc-server-cache-1';
export type CachedSlice={value:unknown;asOf:string};
export type CacheMeta={format:string;me:SyncMe;asOf:string;ids:IdSnapshot};
export type CacheContents={meta:CacheMeta;slices:Record<string,CachedSlice>};
export interface ReadCache{
  load():Promise<CacheContents|null>;
  /** Writes the given slices and the meta record in one transaction. */
  save(meta:CacheMeta,slices:Record<string,CachedSlice>):Promise<void>;
  clear():Promise<void>;
}

/** Tests and browsers without IndexedDB. */
export function createMemoryCache():ReadCache&{dump():CacheContents|null}{
  let meta:CacheMeta|null=null;let slices:Record<string,CachedSlice>={};
  const copy=<T,>(x:T):T=>x===undefined?x:JSON.parse(JSON.stringify(x));
  return {
    async load(){return meta?{meta:copy(meta),slices:copy(slices)}:null;},
    async save(m,s){meta=copy(m);slices={...slices,...copy(s)};},
    async clear(){meta=null;slices={};},
    dump:()=>meta?{meta:copy(meta),slices:copy(slices)}:null,
  };
}

export function createIdbCache(idb:IDBFactory):ReadCache{
  let dbp:Promise<IDBDatabase>|null=null;
  const db=()=>dbp??=new Promise<IDBDatabase>((res,rej)=>{
    const r=idb.open(CACHE_DB,1);
    r.onupgradeneeded=()=>{r.result.createObjectStore(CACHE_STORE);};
    r.onsuccess=()=>res(r.result);r.onerror=()=>{dbp=null;rej(r.error);};r.onblocked=()=>{dbp=null;rej(new Error('IndexedDB blocked'));};
  });
  const tx=async(mode:IDBTransactionMode,fn:(st:IDBObjectStore)=>void)=>{const d=await db();
    return new Promise<void>((res,rej)=>{const t=d.transaction(CACHE_STORE,mode);fn(t.objectStore(CACHE_STORE));t.oncomplete=()=>res();t.onerror=()=>rej(t.error);t.onabort=()=>rej(t.error);});};
  return {
    async load(){
      let meta:CacheMeta|null=null;const slices:Record<string,CachedSlice>={};
      let keys:IDBValidKey[]=[],values:unknown[]=[];
      try{await tx('readonly',st=>{const r=st.getAll(),k=st.getAllKeys();r.onsuccess=()=>{values=r.result;};k.onsuccess=()=>{keys=k.result;};});}
      catch{return null;}
      keys.forEach((key,i)=>{try{const v=JSON.parse(String(values[i]));if(key===CACHE_META)meta=v;else slices[String(key)]=v;}catch{/* unreadable record: ignored */}});
      const m=meta as CacheMeta|null;
      return m&&m.format===CACHE_FORMAT?{meta:m,slices}:null;
    },
    async save(meta,slices){
      try{await tx('readwrite',st=>{for(const [k,v] of Object.entries(slices))st.put(JSON.stringify(v),k);st.put(JSON.stringify(meta),CACHE_META);});}
      catch{/* cache only: a failed write is not an error for the user */}
    },
    async clear(){try{await tx('readwrite',st=>{st.clear();});}catch{/* ignore */}},
  };
}

export function defaultCache():ReadCache{
  let idb:IDBFactory|undefined;try{idb=typeof indexedDB!=='undefined'?indexedDB:undefined;}catch{/* blocked */}
  return idb?createIdbCache(idb):createMemoryCache();
}
