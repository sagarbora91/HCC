// Sync runtime (W4-0) — framework-free, so tests drive it with a fake fetch (tests/_sync-harness.mjs). Marker: hcc-sync-layer.
// • State: empty skeleton (empty.ts) + slices from the area loaders + the signed-in user overlay (capabilities.ts). Nothing else.
// • Loads: after sign-in, after each command (its reload slices), on window focus / page visible (throttled), manual refresh.
// • Commands: ctx.run(cmd) → mapper of cmd.type → HTTP command(s) with a stable Idempotency-Key → reload → result.
//   Unmapped → refused "Not available on the server yet: <type>". Server unreachable → refused (read-only); nothing queued.
// • Read cache: last loaded slices in IndexedDB (cache.ts) with their "as of" time, shown while the server is unreachable.
import type {State,Command,Permission} from '../../services/mock';
import {can} from '../../services/mock';
import {HTTP_ADAPTER_ID,createHttpTransport,createHttpReservationsClient,createHttpSystemClient,createHttpMeClient,type FetchLike,type HttpTransport} from '../http';
import {createConnection,type Connection} from '../connection';
import {HccClientError,errorText} from '../errors';
import {IDEMPOTENCY_KEY_MAX} from '../wire';
import type {HccClients} from '../types';
import type {SrvCapabilities} from '../server';
import type {SyncArea,SliceKey,SliceLoader,CommandMapper,SyncApi,SyncMe,LoaderCtx,CommandCtx} from './types';
import {createIdDirectory,type IdDirectory} from './map';
import {meFromCapabilities,overlayMe} from './capabilities';
import {createMemoryCache,type ReadCache,type CachedSlice} from './cache';
import {cookieAuth,type AuthAdapter,type SignInInput} from './auth';
import {emptyState} from './empty';
import {CACHE_FORMAT} from './cache';

export const SYNC_MARKER='hcc-sync-layer';
export const NOT_ON_SERVER='Not available on the server yet';
export const READ_ONLY_REFUSAL='Server unavailable — read-only. Nothing was sent and nothing is saved on this device';
/** Focus / visibility refreshes at most this often. */
export const FOCUS_REFRESH_MS=30_000;

export type SyncStatus='booting'|'signed-out'|'loading'|'ready';
export type SyncSnapshot={
  status:SyncStatus;
  state:State;
  me:SyncMe|null;
  /** ISO UTC time of the OLDEST loaded slice shown (the data is at least this fresh); undefined before the first load. */
  asOf?:string;
  /** True while showing cached data that has not been confirmed by the server since this start. */
  fromCache:boolean;
  loading:boolean;
  readOnly:boolean;
  /** Slice → last load error text (cleared by the next successful load of that slice). */
  errors:Partial<Record<SliceKey,string>>;
  /** Shown on the sign-in screen (wrong PIN, session ended, server unreachable). */
  signInMessage?:string;
};
export type RunResult={ok:true;result?:unknown}|{ok:false;text:string;error?:unknown;
  /** 'unmapped' = no area maps this command; 'read-only' = server unreachable; 'precheck' = refused on the client; 'server' = sent and refused / failed. */
  reason:'unmapped'|'read-only'|'precheck'|'server'|'signed-out'};

export type SyncRuntimeOptions={
  areas:SyncArea[];
  baseUrl?:string;fetch?:FetchLike;cache?:ReadCache;auth?:AuthAdapter;
  now?:()=>Date;timeoutMs?:number;commandRetries?:number;focusRefreshMs?:number;
  /** Override the skeleton (tests). */
  skeleton?:()=>State;
};

/** Registry rules: one owner per slice, patches/needs only on owned slices, one mapper per command type. */
export function validateAreas(areas:SyncArea[]):string[]{
  const problems:string[]=[],owners=new Map<string,string>(),types=new Map<string,string>(),ids=new Set<string>();
  for(const a of areas){
    if(!a.id||ids.has(a.id))problems.push(`Area id "${a.id}" is missing or used twice.`);ids.add(a.id);
    for(const l of a.loaders)if((l.kind??'own')==='own'){if(owners.has(l.slice))problems.push(`Slice "${l.slice}" has two owners: ${owners.get(l.slice)} and ${a.id}.`);else owners.set(l.slice,a.id);}
    for(const c of a.commands){if(types.has(c.type))problems.push(`Command "${c.type}" is mapped by ${types.get(c.type)} and ${a.id}.`);else types.set(c.type,a.id);}
  }
  for(const a of areas)for(const l of a.loaders){
    if(l.kind==='patch'&&!owners.has(l.slice))problems.push(`${a.id}: patch loader on "${l.slice}" but no area owns it.`);
    for(const n of l.needs??[])if(!owners.has(n))problems.push(`${a.id}: loader "${l.slice}" needs "${n}" but no area owns it.`);
  }
  for(const a of areas)for(const c of a.commands)for(const r of c.reload)if(!owners.has(r))problems.push(`${a.id}: ${c.type} reloads "${r}" but no area owns it.`);
  return problems;
}

/** FNV-1a 32-bit, hex — short stable digest for idempotency keys. */
export function digest(s:string){let h=0x811c9dc5;for(let i=0;i<s.length;i++){h^=s.charCodeAt(i);h=Math.imul(h,0x01000193)>>>0;}return h.toString(16).padStart(8,'0');}
/** Visible ASCII, at most 80 characters (longer keys keep a prefix + digest of the whole key). */
export function fitKey(raw:string){
  const k=String(raw).replace(/[^\x21-\x7e]/g,'_')||'cmd';
  return k.length<=IDEMPOTENCY_KEY_MAX?k:k.slice(0,IDEMPOTENCY_KEY_MAX-9)+'~'+digest(k);
}
const stable=(v:unknown):string=>Array.isArray(v)?'['+v.map(stable).join(',')+']':v&&typeof v==='object'?'{'+Object.keys(v as object).sort().filter(k=>(v as Record<string,unknown>)[k]!==undefined).map(k=>JSON.stringify(k)+':'+stable((v as Record<string,unknown>)[k])).join(',')+'}':JSON.stringify(v)??'null';

export function createSyncRuntime(o:SyncRuntimeOptions){
  const problems=validateAreas(o.areas);
  if(problems.length)throw new Error('Sync registry: '+problems.join(' '));
  const now=o.now??(()=>new Date());
  const cache=o.cache??createMemoryCache();
  const auth=o.auth??cookieAuth();
  const connection:Connection=createConnection();
  const transport:HttpTransport=createHttpTransport({baseUrl:o.baseUrl,fetch:o.fetch,connection,headers:()=>auth.headers(),timeoutMs:o.timeoutMs,commandRetries:o.commandRetries});
  const clients:HccClients&{adapter:typeof HTTP_ADAPTER_ID}={kind:'http',adapter:HTTP_ADAPTER_ID,reservations:createHttpReservationsClient(transport),system:createHttpSystemClient(transport),me:createHttpMeClient(transport)};
  const skeleton=(o.skeleton??emptyState)();
  const loaders:{area:string;l:SliceLoader;order:number}[]=[];
  const mappers=new Map<string,CommandMapper>();
  o.areas.forEach(a=>{a.loaders.forEach(l=>loaders.push({area:a.id,l,order:loaders.length}));a.commands.forEach(c=>mappers.set(c.type,c));});
  const owned=new Set(loaders.filter(x=>(x.l.kind??'own')==='own').map(x=>x.l.slice));

  let values:Partial<Record<SliceKey,unknown>>={},asOf:Partial<Record<SliceKey,string>>={},errors:SyncSnapshot['errors']={};
  let me:SyncMe|null=null,ids:IdDirectory=createIdDirectory(),status:SyncStatus='booting',fromCache=false,loading=0,signInMessage:string|undefined;
  let nonce=newNonce(),generation=0,lastFull=0,chain:Promise<void>=Promise.resolve();
  const subs=new Set<()=>void>();
  let snap:SyncSnapshot=build();

  function newNonce(){return digest(String(Math.random())+String(Date.now())).slice(0,6);}
  function assemble():State{return overlayMe({...skeleton,...values} as State,me);}
  function build():SyncSnapshot{
    const shown=Object.keys(values).filter(k=>owned.has(k as SliceKey)).map(k=>asOf[k as SliceKey]).filter(Boolean) as string[];
    return {status,state:assemble(),me,asOf:shown.length?shown.sort()[0]:undefined,fromCache,loading:loading>0,readOnly:connection.readOnly,errors:{...errors},signInMessage};
  }
  function emit(){snap=build();subs.forEach(f=>f());}
  connection.subscribe(()=>{snap={...snap,readOnly:connection.readOnly};subs.forEach(f=>f());});
  const canMe=(p:Permission|readonly Permission[])=>!!me&&can(snap.state,me.uid,p as Permission|Permission[]);

  async function persist(fresh:Record<string,CachedSlice>){
    if(!me)return;
    await cache.save({format:CACHE_FORMAT,me,asOf:snap.asOf??now().toISOString(),ids:ids.snapshot()},fresh);
  }

  /** Loads the given slices (owners, then patches, in `needs` order). Never throws. */
  async function runBatch(slices:Set<SliceKey>):Promise<void>{
    if(!me)return;
    const batch=loaders.filter(x=>slices.has(x.l.slice));
    if(!batch.length)return;
    loading++;emit();
    const fresh:Partial<Record<SliceKey,unknown>>={},done=new Set<typeof batch[number]>(),stamp=now().toISOString();
    const loadedOk=new Set<SliceKey>();let sessionLost=false;
    const sliceDone=(k:SliceKey)=>batch.filter(x=>x.l.slice===k).every(x=>done.has(x));
    const ctx:LoaderCtx={
      api:apiFor(undefined),
      current:<K extends SliceKey>(k:K)=>(k in fresh?fresh[k]:k in values?values[k]:assemble()[k]) as State[K],
      can:p=>canMe(p),get me(){return me!;},ids,
    };
    const runOne=async(x:typeof batch[number])=>{
      const k=x.l.slice,own=(x.l.kind??'own')==='own';
      if(x.l.requires&&!canMe(x.l.requires)){if(own){fresh[k]=skeleton[k];loadedOk.add(k);}return;}
      try{fresh[k]=await x.l.load(ctx);loadedOk.add(k);delete errors[k];}
      catch(e){
        const w=e instanceof HccClientError?e.wireCode:undefined;
        if(w==='UNAUTHENTICATED'){sessionLost=true;return;}
        if(w==='FORBIDDEN'&&own){fresh[k]=skeleton[k];loadedOk.add(k);delete errors[k];return;}
        if(w!=='UNAVAILABLE')errors[k]=`${x.area}: ${errorText(e)}`;
      }
    };
    let pending=[...batch];
    while(pending.length){
      const wave=pending.filter(x=>{
        if((x.l.needs??[]).some(n=>slices.has(n)&&!sliceDone(n)))return false;
        if(x.l.kind==='patch')return batch.every(y=>y===x||y.l.slice!==x.l.slice||done.has(y)||(y.l.kind==='patch'&&y.order>x.order));
        return true;
      });
      if(!wave.length){pending.forEach(x=>{errors[x.l.slice]=`${x.area}: loader order cannot be resolved (needs cycle).`;});break;}
      await Promise.all(wave.map(runOne));
      wave.forEach(x=>done.add(x));pending=pending.filter(x=>!done.has(x));
    }
    loading--;
    if(sessionLost){endSession('Your session has ended. Sign in again.');return;}
    const cached:Record<string,CachedSlice>={};
    for(const k of loadedOk){values[k]=fresh[k];asOf[k]=stamp;cached[k]={value:fresh[k],asOf:stamp};}
    if(loadedOk.size)fromCache=false;
    emit();
    if(loadedOk.size)await persist(cached);
  }
  /** Queue a load (serialised, so a reload after a command never races an older full load). */
  function reload(slices:Iterable<SliceKey>):Promise<void>{
    const set=new Set(slices);
    chain=chain.then(()=>runBatch(set)).catch(()=>undefined);
    return chain;
  }
  const allSlices=()=>new Set(loaders.map(x=>x.l.slice));

  async function fetchMe():Promise<'ok'|'signed-out'|'unreachable'>{
    try{
      const c=await transport.get<SrvCapabilities>('/me/capabilities');
      const next=meFromCapabilities(c);
      if(me&&me.userId!==next.userId)forget();
      me=next;signInMessage=undefined;return 'ok';
    }catch(e){
      const w=e instanceof HccClientError?e.wireCode:undefined;
      if(w==='UNAUTHENTICATED')return 'signed-out';
      if(w==='UNAVAILABLE')return 'unreachable';
      signInMessage=errorText(e);return 'unreachable';
    }
  }
  /** Re-reads capabilities (session check, role changes) then every slice. */
  async function loadAll():Promise<void>{
    const r=await fetchMe();
    if(r==='signed-out'){endSession(me?'Your session has ended. Sign in again.':undefined);return;}
    if(r==='unreachable'){emit();return;}
    status='ready';lastFull=now().getTime();
    await reload(allSlices());
  }
  function forget(){values={};asOf={};errors={};ids=createIdDirectory();void cache.clear();}
  function endSession(message?:string){forget();me=null;status='signed-out';signInMessage=message;fromCache=false;nonce=newNonce();generation=0;emit();}

  function apiFor(key:string|undefined):SyncApi{
    let n=0;
    return {transport,clients,get:(p,q)=>transport.get(p,q),
      command(name,body,opts={}){
        if(key===undefined)return Promise.reject(new HccClientError('VALIDATION','Loaders cannot send commands.'));
        n++;const k=opts.step?fitKey(`${key}:${opts.step}`):n===1?key:fitKey(`${key}:${n}`);
        return transport.command(name,body,{idempotencyKey:k,expectedVersion:opts.expectedVersion});
      }};
  }
  /** Stable per user action: the dialog's key when the screen gave one (same on every retry of that dialog), otherwise
   *  type + digest of the command + this session's nonce + the count of commands committed so far — so a retry after a lost
   *  response reuses the key, and an identical second action after a success gets a new one. */
  function actionKey(cmd:Command){
    if(typeof cmd.key==='string'&&cmd.key)return fitKey(cmd.key);
    const {key:_k,...body}=cmd;void _k;
    return fitKey(`${cmd.type}:${nonce}:${generation}:${digest(stable(body))}`);
  }

  async function run(cmd:Command):Promise<RunResult>{
    if(!me)return {ok:false,reason:'signed-out',text:'Sign in again: your session has ended.'};
    const m=mappers.get(cmd.type);
    if(!m)return {ok:false,reason:'unmapped',text:`${NOT_ON_SERVER}: ${cmd.type}`};
    if(connection.readOnly){await clients.system.health().catch(()=>undefined);
      if(connection.readOnly)return {ok:false,reason:'read-only',text:`${READ_ONLY_REFUSAL} (${cmd.type}).`};}
    const state=snap.state;
    const pre=m.precheck?.(cmd,state,me);
    if(pre){const e=new HccClientError('VALIDATION',pre);return {ok:false,reason:'precheck',text:errorText(e),error:e};}
    const key=actionKey(cmd);
    const c:CommandCtx={cmd,state,api:apiFor(key),ids,me,key,
      expectedVersion:(kind,id)=>cmd.version!==undefined?ids.version(kind,id):undefined};
    let out;
    try{out=await m.send(c);}
    catch(e){
      if(e instanceof HccClientError&&e.wireCode==='UNAUTHENTICATED'){endSession('Your session has ended. Sign in again.');}
      return {ok:false,reason:'server',text:errorText(e),error:e};
    }
    generation++;
    await reload([...m.reload,...(out?.reload??[])]);
    return {ok:true,result:out?.result};
  }

  return {
    marker:SYNC_MARKER,connection,transport,clients,
    get:()=>snap,
    subscribe(f:()=>void){subs.add(f);return ()=>{subs.delete(f);};},
    /** Start: read the cache, check the session, then load everything. The cached slices are shown (with their "as of") only
     *  once the session is confirmed (same user) or the server is unreachable (read-only) — never after a 401. */
    async boot(){
      const c=await cache.load().catch(()=>null);
      if(c){me=c.meta.me;ids=createIdDirectory(c.meta.ids);
        for(const [k,v] of Object.entries(c.slices)){values[k as SliceKey]=v.value;asOf[k as SliceKey]=v.asOf;}
        fromCache=true;}
      const r=await fetchMe();
      if(r==='signed-out'){endSession();return;}
      if(r==='unreachable'){
        if(c)status='ready';
        else{status='signed-out';signInMessage=signInMessage??'The hotel server cannot be reached. Check the network and try again.';}
        emit();return;}
      status='ready';lastFull=now().getTime();emit();
      await reload(allSlices());
    },
    async signIn(input:SignInInput):Promise<{ok:boolean;text?:string}>{
      try{await auth.signIn(transport,input);}
      catch(e){signInMessage=errorText(e);emit();return {ok:false,text:signInMessage};}
      nonce=newNonce();generation=0;status='loading';emit();
      await loadAll();
      return snap.status==='ready'?{ok:true}:{ok:false,text:snap.signInMessage};
    },
    async signOut(){await auth.signOut(transport).catch(()=>undefined);endSession();},
    /** Manual refresh / reconnect: health check while read-only, then capabilities + every slice. */
    async refresh(){
      if(connection.readOnly){await clients.system.health().catch(()=>undefined);if(connection.readOnly){emit();return;}}
      await loadAll();
    },
    /** Window focus / page visible: refresh unless one ran in the last FOCUS_REFRESH_MS. */
    focusRefresh(){
      if(status!=='ready'||now().getTime()-lastFull<(o.focusRefreshMs??FOCUS_REFRESH_MS))return Promise.resolve();
      return this.refresh();
    },
    reload,run,
    /** Typed-client flow finished (see CommandMapper.track): remember ids, then reload that command's slices. */
    async flowDone(type:string,result:unknown){const m=mappers.get(type);if(!m)return;m.track?.(result,ids);generation++;await reload(m.reload);},
    mapped:(type:string)=>mappers.has(type),
    get ids(){return ids;},
    /** Browser wiring: focus + visibilitychange → focusRefresh. Returns the detach function. */
    attachWindow(w:Pick<Window,'addEventListener'|'removeEventListener'>&{document?:Pick<Document,'addEventListener'|'removeEventListener'|'visibilityState'>}){
      const onFocus=()=>{void this.focusRefresh();};
      const onVis=()=>{if(w.document?.visibilityState==='visible')void this.focusRefresh();};
      w.addEventListener('focus',onFocus);w.document?.addEventListener('visibilitychange',onVis);
      return ()=>{w.removeEventListener('focus',onFocus);w.document?.removeEventListener('visibilitychange',onVis);};
    },
  };
}
export type SyncRuntime=ReturnType<typeof createSyncRuntime>;
