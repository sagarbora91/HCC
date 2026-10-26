// Sync runtime (W4-0) — framework-free, so tests drive it with a fake fetch (tests/_sync-harness.mjs). Marker: hcc-sync-layer.
// • State: empty skeleton (empty.ts) + slices from the area loaders + the signed-in user overlay (capabilities.ts). Nothing else.
// • Loads: after sign-in, after each command (its reload slices), on window focus / page visible (throttled), manual refresh.
// • Commands: ctx.run(cmd) → mapper of cmd.type → HTTP command(s) with a stable Idempotency-Key → reload → result.
//   Unmapped → refused "Not available on the server yet: <type>". Server unreachable → refused (read-only); nothing queued.
// • Read cache: last loaded slices in IndexedDB (cache.ts) with their "as of" time, shown while the server is unreachable —
//   encrypted, and only to a signed-in user (security review F-H01): a session the server confirms, a still-valid local session
//   (resume key in `keyStore`), or offline re-authentication (status 'locked' → unlock(secret)). Wiped on sign-out / expiry.
import type {State,Command,Permission} from '../../services/mock';
import {can} from '../../services/mock';
import {HTTP_ADAPTER_ID,createHttpTransport,createHttpReservationsClient,createHttpSystemClient,createHttpMeClient,type FetchLike,type HttpTransport} from '../http';
import {createConnection,type Connection} from '../connection';
import {HccClientError,errorText,signInText} from '../errors';
import {IDEMPOTENCY_KEY_MAX} from '../wire';
import type {HccClients} from '../types';
import type {SrvCapabilities} from '../server';
import type {SyncArea,SliceKey,SliceLoader,CommandMapper,SyncApi,SyncMe,LoaderCtx,CommandCtx} from './types';
import {createIdDirectory,type IdDirectory} from './map';
import {meFromCapabilities,overlayMe} from './capabilities';
import {createMemoryCache,type ReadCache,type CachedSlice,type CacheLock,type CacheMeta,CACHE_KDF_ITERATIONS,CACHE_MAX_UNLOCK_FAILURES,LOCK_RECORD,META_RECORD,SLICE_PREFIX,
  parseLock,lockExpired,createCacheKey,keyFromResume,keyFromSecret,rewrapResume,sealRecord,openRecord} from './cache';
import {cookieAuth,memoryTokenStore,type AuthAdapter,type SignInInput,type TokenStore} from './auth';
import {emptyState} from './empty';
import {mayRead} from './reads';

export const SYNC_MARKER='hcc-sync-layer';
export const NOT_ON_SERVER='Not available on the server yet';
export const READ_ONLY_REFUSAL='Server unavailable — read-only. Nothing was sent and nothing is saved on this device';
/** Focus / visibility refreshes at most this often. */
export const FOCUS_REFRESH_MS=30_000;
export const RECOVERY_MS=20_000;

/** 'locked' = server unreachable and an encrypted read cache exists, but no valid local session: re-authenticate (unlock). */
export type SyncStatus='booting'|'signed-out'|'loading'|'ready'|'locked';
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
  /** status 'locked': whose offline copy this is and which secret opens it. */
  locked?:{displayName:string;credentialKind:'password'|'pin'};
};
export type RunResult={ok:true;result?:unknown;/** Partial success: show this instead of the success message (CommandOutcome.warning). */warning?:string}|{ok:false;text:string;error?:unknown;
  /** 'unmapped' = no area maps this command; 'read-only' = server unreachable; 'precheck' = refused on the client; 'server' = sent and refused / failed. */
  reason:'unmapped'|'read-only'|'precheck'|'server'|'signed-out'};

export type SyncRuntimeOptions={
  areas:SyncArea[];
  baseUrl?:string;fetch?:FetchLike;cache?:ReadCache;auth?:AuthAdapter;
  /** Where the read cache's resume key lives — never the cache database itself (APK: sealed Preferences; browser: sessionStorage). */
  keyStore?:TokenStore;
  /** Called whenever the offline copy is wiped (sign-out, session loss, idle / absolute expiry, user change, too many wrong unlocks):
   *  the APK uses it to delete exported files from its cache (A10-01). Failures are ignored. */
  onWipe?:()=>void|Promise<void>;
  /** PBKDF2 iterations for the offline unlock key (tests use a small number). */
  cacheIterations?:number;
  now?:()=>Date;timeoutMs?:number;commandRetries?:number;focusRefreshMs?:number;recoveryMs?:number;
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

type SessionTimes={idleExpiresUtc?:string;absoluteExpiresUtc?:string;idleTimeoutSeconds?:number};
/** The server session's absolute end on THIS device's clock (ISO): the server's remaining lifetime (absolute − server now, server
 *  now = idleExpires − idleTimeout) added to the local time, so a tablet with a wrong clock neither keeps nor drops the copy early. */
export function localSessionEnd(s:SessionTimes|undefined,nowMs:number):string{
  const abs=Date.parse(s?.absoluteExpiresUtc??''),idle=Date.parse(s?.idleExpiresUtc??''),secs=s?.idleTimeoutSeconds??0;
  let left=12*3600_000;
  if(Number.isFinite(abs)&&Number.isFinite(idle))left=abs-(idle-secs*1000);else if(Number.isFinite(abs))left=abs-nowMs;
  return new Date(nowMs+Math.max(0,Math.min(left,24*3600_000))).toISOString();
}

export function createSyncRuntime(o:SyncRuntimeOptions){
  const problems=validateAreas(o.areas);
  if(problems.length)throw new Error('Sync registry: '+problems.join(' '));
  const now=o.now??(()=>new Date());
  const cache=o.cache??createMemoryCache();
  const auth=o.auth??cookieAuth();
  const keyStore=o.keyStore??memoryTokenStore();
  const connection:Connection=createConnection();
  const transport:HttpTransport=createHttpTransport({baseUrl:o.baseUrl,fetch:o.fetch,connection,headers:()=>auth.headers(),timeoutMs:o.timeoutMs,commandRetries:o.commandRetries});
  const clients:HccClients&{adapter:typeof HTTP_ADAPTER_ID}={kind:'http',adapter:HTTP_ADAPTER_ID,reservations:createHttpReservationsClient(transport),system:createHttpSystemClient(transport),me:createHttpMeClient(transport)};
  const skeleton=(o.skeleton??emptyState)();
  const loaders:{area:string;l:SliceLoader;order:number}[]=[];
  const mappers=new Map<string,CommandMapper>();
  o.areas.forEach(a=>{a.loaders.forEach(l=>loaders.push({area:a.id,l,order:loaders.length}));a.commands.forEach(c=>mappers.set(c.type,c));});
  const owned=new Set(loaders.filter(x=>(x.l.kind??'own')==='own').map(x=>x.l.slice));

  let values:Partial<Record<SliceKey,unknown>>={},asOf:Partial<Record<SliceKey,string>>={},errors:SyncSnapshot['errors']={};
  let recovering=false;let me:SyncMe|null=null,ids:IdDirectory=createIdDirectory(),status:SyncStatus='booting',fromCache=false,loading=0,signInMessage:string|undefined;
  let nonce=newNonce(),generation=0,lastFull=0,chain:Promise<void>=Promise.resolve();
  /** Read-cache data key (memory only) and the lock record it belongs to; the server's last session times (capabilities). */
  let cacheKey:CryptoKey|null=null,lock:CacheLock|null=null,srvSession:SessionTimes|undefined;
  const subs=new Set<()=>void>();
  let snap:SyncSnapshot=build();

  function newNonce(){return digest(String(Math.random())+String(Date.now())).slice(0,6);}
  function assemble():State{return overlayMe({...skeleton,...values} as State,me);}
  function build():SyncSnapshot{
    const shown=Object.keys(values).filter(k=>owned.has(k as SliceKey)).map(k=>asOf[k as SliceKey]).filter(Boolean) as string[];
    return {status,state:assemble(),me,asOf:shown.length?shown.sort()[0]:undefined,fromCache,loading:loading>0,readOnly:connection.readOnly,errors:{...errors},signInMessage,
      ...(status==='locked'&&lock?{locked:{displayName:lock.displayName,credentialKind:lock.credentialKind}}:{})};
  }
  function emit(){snap=build();subs.forEach(f=>f());}
  connection.subscribe(()=>{snap={...snap,readOnly:connection.readOnly};subs.forEach(f=>f());});
  const canMe=(p:Permission|readonly Permission[])=>!!me&&can(snap.state,me.uid,p as Permission|Permission[]);

  /** Seals the fresh slices + meta under the data key; also records server activity (idle expiry) in the lock record. */
  async function persist(fresh:Record<string,CachedSlice>){
    const k=cacheKey,l=lock;
    if(!me||!k||!l||me.userId!==l.userId)return;
    const next:CacheLock={...l,lastActivity:now().getTime()};lock=next;
    const out:Record<string,string>={};
    try{
      for(const [name,v] of Object.entries(fresh))out[SLICE_PREFIX+name]=await sealRecord(k,l.userId,SLICE_PREFIX+name,v);
      const meta:CacheMeta={me,asOf:snap.asOf??now().toISOString(),ids:ids.snapshot()};
      out[META_RECORD]=await sealRecord(k,l.userId,META_RECORD,meta);
    }catch{return;}
    if(cacheKey!==k||!lock)return; // signed out / wiped while sealing
    out[LOCK_RECORD]=JSON.stringify(lock);
    await cache.write(out).catch(()=>undefined);
  }
  /** Drops the data key and deletes the cache and its resume key. The in-memory part is synchronous. */
  async function wipeCache(){
    cacheKey=null;lock=null;
    await Promise.all([cache.clear().catch(()=>undefined),keyStore.clear().catch(()=>undefined),Promise.resolve().then(()=>o.onWipe?.()).catch(()=>undefined)]);
  }
  async function resumeKey():Promise<CryptoKey|null>{
    const l=lock;if(!l)return null;
    const v=await keyStore.load().catch(()=>null);if(!v)return null;
    let rec:{userId?:number;resume?:string};try{rec=JSON.parse(v);}catch{return null;}
    if(rec?.userId!==l.userId||typeof rec.resume!=='string')return null;
    return (await keyFromResume(l,rec.resume))?.key??null;
  }
  /** Decrypts the cached records into the state. keepMe: the server already confirmed the user (online start). */
  async function openCache(raw:Record<string,string>,k:CryptoKey,keepMe:boolean):Promise<boolean>{
    if(!lock)return false;const uid=lock.userId;
    const m=await openRecord<CacheMeta>(k,uid,META_RECORD,raw[META_RECORD]);
    if(!m||m.me?.userId!==uid)return false;
    const vals:Partial<Record<SliceKey,unknown>>={},at:Partial<Record<SliceKey,string>>={};
    for(const [name,sealed] of Object.entries(raw)){
      if(!name.startsWith(SLICE_PREFIX))continue;
      const v=await openRecord<CachedSlice>(k,uid,name,sealed);
      if(v){const key=name.slice(SLICE_PREFIX.length) as SliceKey;vals[key]=v.value;at[key]=v.asOf;}
    }
    if(!keepMe)me=m.me;
    ids=createIdDirectory(m.ids);values={...vals,...values};asOf={...at,...asOf};fromCache=true;
    return true;
  }
  /** New data key. With the sign-in secret the copy can also be opened offline by re-authentication; without it (a session
   *  the server confirmed after a restart) only by this session's resume key. */
  async function newCacheKey(u:{userId:number;displayName:string},kind:'password'|'pin',secret:string,session:SessionTimes|undefined){
    const t=now().getTime();
    const made=await createCacheKey({userId:u.userId,displayName:u.displayName??'',credentialKind:kind,secret,
      absoluteExpiresUtc:localSessionEnd(session,t),idleTimeoutSeconds:session?.idleTimeoutSeconds||1800,now:t,iterations:o.cacheIterations??CACHE_KDF_ITERATIONS}).catch(()=>null);
    if(!made)return;
    cacheKey=made.key;lock=made.lock;
    await keyStore.save(JSON.stringify({userId:made.lock.userId,resume:made.resume})).catch(()=>undefined);
  }
  /** Server confirmed `me`: keep the cache only for the same user with this session's resume key; otherwise wipe it and start
   *  a new (resume-only) copy for this session. */
  async function adoptOnline(raw?:Record<string,string>){
    if(!me)return;
    if(lock&&lock.userId===me.userId){
      if(!cacheKey){const k=await resumeKey();if(k){cacheKey=k;if(raw)await openCache(raw,k,true);}}
      if(cacheKey&&lock){lock={...lock,absoluteExpiresUtc:localSessionEnd(srvSession,now().getTime())};return;}
    }
    if(lock||cacheKey)await wipeCache();
    await newCacheKey(me,'password','',srvSession);
  }

  /** Loads the given slices (owners, then patches, in `needs` order). Never throws. */
  async function runBatch(slices:Set<SliceKey>):Promise<void>{
    if(!me)return;
    const batch=loaders.filter(x=>slices.has(x.l.slice));
    if(!batch.length)return;
    loading++;emit();
    const fresh:Partial<Record<SliceKey,unknown>>={},done=new Set<typeof batch[number]>(),stamp=now().toISOString();
    const loadedOk=new Set<SliceKey>(),patchFailed=new Set<SliceKey>();let sessionLost=false;
    const sliceDone=(k:SliceKey)=>batch.filter(x=>x.l.slice===k).every(x=>done.has(x));
    const ctx:LoaderCtx={
      api:apiFor(undefined),
      current:<K extends SliceKey>(k:K)=>(k in fresh?fresh[k]:k in values?values[k]:assemble()[k]) as State[K],
      can:p=>canMe(p),mayRead:(...r)=>mayRead(me,...r),get me(){return me!;},ids,
    };
    const runOne=async(x:typeof batch[number])=>{
      const k=x.l.slice,own=(x.l.kind??'own')==='own';
      if((x.l.requires&&!canMe(x.l.requires))||(x.l.reads&&!mayRead(me,...x.l.reads))){if(own){fresh[k]=skeleton[k];loadedOk.add(k);}return;}
      try{fresh[k]=await x.l.load(ctx);loadedOk.add(k);delete errors[k];}
      catch(e){
        const w=e instanceof HccClientError?e.wireCode:undefined;
        if(w==='UNAUTHENTICATED'){sessionLost=true;return;}
        if(w==='FORBIDDEN'&&own){fresh[k]=skeleton[k];loadedOk.add(k);delete errors[k];return;}
        // A failed patch (not a 403: the user may simply not see those fields) must not commit the owner's unpatched value,
        // e.g. bookings without their folio would show ₹0 balances; the previous value stays (see the commit below).
        if(!own&&w!=='FORBIDDEN')patchFailed.add(k);
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
    // Keep the previous value of a slice whose patch failed (its error stays listed); without one, the owner's value is shown.
    for(const k of patchFailed)if(k in values)loadedOk.delete(k);
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
      srvSession=c?.session;
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
    if(r==='signed-out'){endSession(me||lock?'Your session has ended. Sign in again.':undefined);return;}
    if(r==='unreachable'){emit();return;}
    await adoptOnline();
    status='ready';lastFull=now().getTime();
    await reload(allSlices());
  }
  function forget(){values={};asOf={};errors={};ids=createIdDirectory();void wipeCache();}
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
    return {ok:true,result:out?.result,...(out?.warning?{warning:out.warning}:{})};
  }

  return {
    marker:SYNC_MARKER,connection,transport,clients,
    get:()=>snap,
    subscribe(f:()=>void){subs.add(f);return ()=>{subs.delete(f);};},
    /** Start: read the (encrypted) cache, check the session, then load everything. Nothing cached is decrypted or shown before
     *  the session is confirmed (same user), or — server unreachable — before a valid local session or an offline unlock;
     *  never after a 401. An expired session's cache and an old unencrypted cache are deleted. */
    async boot(){
      const raw=await cache.readAll().catch(()=>({} as Record<string,string>));
      let l=parseLock(raw);
      if(!l&&Object.keys(raw).length)await wipeCache();
      if(l&&lockExpired(l,now().getTime())){await wipeCache();l=null;}
      lock=l;
      const r=await fetchMe();
      if(r==='signed-out'){endSession();return;}
      if(r==='unreachable'){
        if(!lock){status='signed-out';signInMessage=signInMessage??'The hotel server cannot be reached. Check the network and try again.';emit();return;}
        const k=await resumeKey();
        if(k&&await openCache(raw,k,false)){cacheKey=k;status='ready';}
        else if(lock.bySecret)status='locked';
        else{await wipeCache();status='signed-out';signInMessage=signInMessage??'The hotel server cannot be reached. Check the network and try again.';}
        emit();return;}
      await adoptOnline(raw);
      status='ready';lastFull=now().getTime();emit();
      await reload(allSlices());
    },
    /** status 'locked' (server unreachable): re-authenticate with the PIN / password of the cached user to see the read-only copy. */
    async unlock(secret:string):Promise<{ok:boolean;text?:string}>{
      const l=lock;
      if(status!=='locked'||!l)return {ok:false,text:'There is no offline copy to open.'};
      const kind=l.credentialKind==='pin'?'PIN':'password';
      if(!secret)return {ok:false,text:`Enter your ${kind}.`};
      const raw=await cache.readAll().catch(()=>({} as Record<string,string>));
      const u=await keyFromSecret(l,secret);
      if(u&&await openCache(raw,u.key,false)){
        cacheKey=u.key;
        const w=await rewrapResume(l,u.raw);const next:CacheLock={...w.lock,failures:0};lock=next;
        await keyStore.save(JSON.stringify({userId:next.userId,resume:w.resume})).catch(()=>undefined);
        await cache.write({[LOCK_RECORD]:JSON.stringify(next)}).catch(()=>undefined);
        status='ready';emit();return {ok:true};
      }
      const failures=l.failures+1;
      if(failures>=CACHE_MAX_UNLOCK_FAILURES){
        const text='Too many wrong attempts: the offline copy on this device was deleted. Sign in when the hotel server is reachable.';
        endSession(text);return {ok:false,text};
      }
      const next:CacheLock={...l,failures};lock=next;await cache.write({[LOCK_RECORD]:JSON.stringify(next)}).catch(()=>undefined);emit();
      const left=CACHE_MAX_UNLOCK_FAILURES-failures;
      return {ok:false,text:`Wrong ${kind}. ${left} more wrong attempt${left===1?'':'s'} delete${left===1?'s':''} the offline copy.`};
    },
    /** Idle / absolute session expiry by the lock record: signs out here and wipes the cache. True when it did. */
    checkExpiry():boolean{
      if(!lock||status==='signed-out'||!lockExpired(lock,now().getTime()))return false;
      endSession('You were signed out after a period without activity. Sign in again.');return true;
    },
    async signIn(input:SignInInput):Promise<{ok:boolean;text?:string}>{
      let res;
      try{res=await auth.signIn(transport,input);}
      catch(e){signInMessage=signInText(e);emit();return {ok:false,text:signInMessage};}
      // A sign-in always starts a new offline copy, keyed by this sign-in's secret (the secret itself is not kept).
      forget();me=null;await wipeCache();
      nonce=newNonce();generation=0;status='loading';emit();
      if(res&&typeof res.userId==='number')await newCacheKey(res,input.pin?'pin':'password',input.password??input.pin??'',res.session);
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
      if(status!=='ready')return Promise.resolve();
      // APK12-6: while the server is unreachable the focus / online signal always re-checks it (the throttle is for healthy refreshes).
      if(!connection.readOnly&&now().getTime()-lastFull<(o.focusRefreshMs??FOCUS_REFRESH_MS))return Promise.resolve();
      return this.refresh();
    },
    /** Read-only mode: one background health check; the banner clears and the slices reload when the server answers. Never overlaps itself. */
    recover(){
      if(status!=='ready'||!connection.readOnly||recovering)return Promise.resolve();
      recovering=true;
      return this.refresh().catch(()=>undefined).finally(()=>{recovering=false;});
    },
    reload,run,
    /** Typed-client flow finished (see CommandMapper.track): remember ids, then reload that command's slices. */
    async flowDone(type:string,result:unknown){const m=mappers.get(type);if(!m)return;m.track?.(result,ids);generation++;await reload(m.reload);},
    mapped:(type:string)=>mappers.has(type),
    /** Read-only server calls for screens that fetch outside the slice loaders (reports, W4 G3). Commands are refused. */
    get api(){return apiFor(undefined);},
    get ids(){return ids;},
    /** Browser wiring: focus + visibilitychange → focusRefresh. Returns the detach function. */
    attachWindow(w:Pick<Window,'addEventListener'|'removeEventListener'>&{document?:Pick<Document,'addEventListener'|'removeEventListener'|'visibilityState'>}){
      const onFocus=()=>{void this.focusRefresh();};
      const onOnline=()=>{void this.recover();};
      // APK12-6: while unavailable, try again on a timer (default 20 s) so the read-only banner clears without pressing Retry.
      const timer=setInterval(()=>{void this.recover();},o.recoveryMs??RECOVERY_MS);
      const onVis=()=>{if(w.document?.visibilityState==='visible')void this.focusRefresh();};
      w.addEventListener('focus',onFocus);w.addEventListener('online',onOnline);w.document?.addEventListener('visibilitychange',onVis);
      return ()=>{clearInterval(timer);w.removeEventListener('focus',onFocus);w.removeEventListener('online',onOnline);w.document?.removeEventListener('visibilitychange',onVis);};
    },
  };
}
export type SyncRuntime=ReturnType<typeof createSyncRuntime>;
