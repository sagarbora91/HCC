// W4 sync-layer test harness (W4-0). Area tests (tests/sync-<area>.mjs) use it to run the REAL sync runtime against a fake
// server: fake routes answer like the .NET API, every request is recorded (method, path, query, headers, body, key), and
// the runtime's assembled state is what the screens would read. Not a test file itself (the leading "_" keeps run.mjs off it).
//
//   import {loadSync,fakeServer,startRuntime,reply,settle} from './_sync-harness.mjs';
//   export async function register({test,assert,M}){
//     const S=await loadSync();                                     // bundled src/clients/sync/index.ts (Node ESM)
//     const srv=fakeServer({'GET /rooms/status':()=>({rooms:[…]})}); // + defaults: /system/health, /me/capabilities (Owner), empty /reservations + /guests
//     const rt=await startRuntime(S,srv);                           // boot: capabilities → every registered loader
//     const r=await rt.run({type:'TASK',id:'12',action:'start'});   // ctx.run in production = rt.run (+ toast/busy in App)
//     test('[W4-2] …',()=>{assert.equal(rt.get().state.rooms.length,24);assert.deepEqual(srv.commands('TransitionHousekeepingTask')[0].body,{…});});
//   }
// test() is synchronous: do every await first, assert on the captured results (as tests/stream-clients.mjs does).
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';

const OUT=(process.env.HCC_SYNC_TEST_DIR||'tests/.generated')+'/sync.mjs';
let loaded;
/** Bundles src/clients/sync/index.ts once per test run and imports it. */
export function loadSync(){
  return loaded??=(async()=>{
    await mkdir(OUT.replace(/\/[^/]+$/,''),{recursive:true});
    await build({entryPoints:['src/clients/sync/index.ts'],bundle:true,platform:'node',format:'esm',outfile:OUT,logLevel:'error',define:{'process.env.HCC_BUILD':'"production"'}});
    return import(new URL('../'+OUT,import.meta.url).href);
  })();
}

export const settle=async f=>{try{return {ok:true,value:await f()};}catch(error){return {ok:false,error};}};
const REPLY=Symbol('reply');
/** A handler result with an explicit status: reply(409,{code:'STALE_VERSION',message:'…'}). */
export const reply=(status,body,headers)=>({[REPLY]:true,status,body,headers});
/** A handler result that simulates a lost request / server down (fetch rejects). */
export const netDown=()=>new TypeError('Failed to fetch');

/** The Owner template's PERM ids as the real server lists them (tests/_role-reads.json, D5: module ids included, so the read gates of
 *  sync/reads.ts let an Owner-like test user send every read), plus every id the capabilities mapping knows; mode A, scope P. */
const OWNER_PERMS=JSON.parse(readFileSync(new URL('./_role-reads.json',import.meta.url),'utf8')).roles.OWN.permissions.map(p=>p[0]);
export function capabilities({userId=1,displayName='Test Owner',roles=['OWN'],grants,fieldMasks={}}={}){
  const all0=['PERM-RES-001','PERM-RES-002','PERM-RES-003','PERM-RES-004','PERM-STY-001','PERM-STY-002','PERM-STY-003','PERM-GST-001','PERM-GST-002','PERM-GST-003',
    'PERM-FOL-001','PERM-FOL-002','PERM-FOL-003','PERM-FOL-004','PERM-FOL-005','PERM-PAY-001','PERM-PAY-002','PERM-PAY-003','PERM-PAY-004','PERM-PAY-005',
    'PERM-SHF-001','PERM-SHF-002','PERM-HK-001','PERM-HK-002','PERM-HK-003','PERM-HK-004','PERM-ROOM-001','PERM-ROOM-002','PERM-EXP-001','PERM-EXP-002','PERM-EXP-003',
    'PERM-CLS-001','PERM-CLS-002','PERM-CLS-003','PERM-CLS-004','PERM-APR-001','PERM-RPT-001','PERM-RPT-002','PERM-RPT-003','PERM-ADM-001','PERM-ADM-002','PERM-ADM-003','PERM-ADM-004','PERM-ADM-005'];
  // PERM-INV-002 (issue invoice) stays opt-in as before D5: with it, CHECKOUT also issues the invoice (tests that cover that flow pass it).
  const all=[...new Set([...all0,...OWNER_PERMS])].filter(p=>p!=='PERM-INV-002');
  const list=grants??all.map(p=>[p,'A']);
  return {userId,propertyId:1,displayName,credentialKind:'PASSWORD',roles,
    permissions:list.map(([permissionId,mode='A'])=>({permissionId,mode,scope:'P',limit:null,limitUnit:null})),fieldMasks,
    session:{idleExpiresUtc:'2026-10-24T05:00:00.000Z',absoluteExpiresUtc:'2026-10-24T16:00:00.000Z',idleTimeoutSeconds:1800}};
}

/**
 * Fake .NET API. `routes`: {'METHOD /path/:param': handler}. Paths are without /api/v1. A handler gets
 * {method,path,params,query,headers,body,key} and returns: a value (200 JSON), reply(status,body), undefined (204), or an Error
 * (thrown by fetch = network down). Defaults: GET /system/health and GET /me/capabilities (capabilities()); override them.
 * Unknown routes answer 404 NOT_FOUND and are recorded in `unrouted`.
 */
export function fakeServer(routes={}){
  const table={'GET /system/health':()=>({status:'ok',database:'ok',schemaVersion:2,serverTimeUtc:'2026-10-24T04:30:00.000Z'}),
    'GET /me/capabilities':()=>capabilities(),
    // Empty front-desk lists (W4-1): every runtime with the full registry reloads bookings / guests; area tests override them.
    'GET /reservations':()=>({businessDate:'2026-10-24',total:0,page:1,pageSize:500,items:[]}),'GET /guests':()=>({total:0,page:1,pageSize:500,items:[]}),...routes};
  const calls=[],unrouted=[];
  const compiled=()=>Object.entries(table).map(([spec,h])=>{const [m,p]=spec.split(' ');const names=[];
    const re=new RegExp('^'+p.replace(/:[A-Za-z]+/g,x=>{names.push(x.slice(1));return '([^/]+)';})+'$');return {m,re,names,h};});
  const srv={
    calls,unrouted,
    /** Set true to make every request fail as if the LAN were down. */
    down:false,
    /** Add / replace a route. */
    on(spec,h){table[spec]=h;return srv;},
    /** Recorded POST /commands/<name> calls. */
    commands:name=>calls.filter(c=>c.method==='POST'&&c.path==='/commands/'+name),
    gets:path=>calls.filter(c=>c.method==='GET'&&(path===undefined||c.path===path)),
    reset(){calls.length=0;unrouted.length=0;},
    async fetch(url,init={}){
      const u=new URL(url,'http://hcc.test');const path=u.pathname.replace(/^\/api\/v1/,'');const method=init.method??'GET';
      const headers={...(init.headers??{})};const body=init.body===undefined?undefined:JSON.parse(init.body);
      const call={method,path,query:Object.fromEntries(u.searchParams),headers,body,key:headers['Idempotency-Key'],url:String(url)};
      calls.push(call);
      if(srv.down)throw netDown();
      const hit=compiled().map(r=>r.m===method&&r.re.exec(path)?{r,m:r.re.exec(path)}:null).find(Boolean);
      let res;
      if(!hit){unrouted.push(`${method} ${path}`);res=reply(404,{code:'NOT_FOUND',message:`No fake route for ${method} ${path}`});}
      else{const params=Object.fromEntries(hit.r.names.map((n,i)=>[n,decodeURIComponent(hit.m[i+1])]));res=await hit.r.h({...call,params});}
      if(res instanceof Error)throw res;
      const status=res&&res[REPLY]?res.status:res===undefined?204:200;const payload=res&&res[REPLY]?res.body:res;
      const text=payload===undefined?'':typeof payload==='string'?payload:JSON.stringify(payload);
      const rh=res&&res[REPLY]?res.headers??{}:{};
      return {status,ok:status<300,text:async()=>text,headers:{get:n=>{const k=Object.keys(rh).find(x=>x.toLowerCase()===n.toLowerCase());return k?String(rh[k]):null;}}};
    },
  };
  return srv;
}

/** The read cache's resume key lives outside the cache (F-H01): one key store per cache = one device across restarts. */
const keyStores=new WeakMap();
export const keyStoreFor=(S,cache)=>{let k=keyStores.get(cache);if(!k){k=S.memoryTokenStore();keyStores.set(cache,k);}return k;};
/** Creates the runtime on the fake server (registry AREAS unless `areas` is given), boots it and waits for the loads. */
export async function startRuntime(S,srv,{areas,cache,now,focusRefreshMs,recoveryMs,boot=true,keyStore,auth,onWipe}={}){
  const c=cache??S.createMemoryCache();
  const rt=S.createSyncRuntime({areas:areas??S.AREAS,fetch:srv.fetch,cache:c,keyStore:keyStore??keyStoreFor(S,c),cacheIterations:1000,auth,onWipe,now,focusRefreshMs,recoveryMs,commandRetries:0});
  if(boot)await rt.boot();
  return rt;
}

/** Load errors of the given area ids only (other areas in the registry have no fake routes in an area test). */
export function ownErrors(errors,...areaIds){return Object.fromEntries(Object.entries(errors).filter(([,m])=>areaIds.some(a=>String(m).startsWith(a+": "))));}

// ── Screen smoke render ──
// Renders every desktop page (and every phone page with mobile=true) of the PRODUCTION build over a given state with the
// app's context, server-side (react-dom/server; effects do not run). Returns {pageId: error message} for pages that throw.
// Area tests call it on the state their loaders produced: the screens must render on server data (and on the empty skeleton).
let pages;
export function loadPages(){
  return pages??=(async()=>{
    const dir=OUT.replace(/\/[^/]+$/,'');await mkdir(dir,{recursive:true});
    await writeFile(dir+'/sync-pages-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus,addDays,assignedScopeRoles} from '../../src/services/mock';
import {navItems} from '../../src/app/nav'; // same import order as App.tsx (nav before registry)
import {extraPages,mobilePages} from '../../src/app/registry';
import {ModulesHome} from '../../src/app/ModulesHome';
import {Overview} from '../../src/features/overview/Overview';
import {RoomBoard} from '../../src/features/rooms/RoomBoard';
import {Reservations} from '../../src/features/reservations/Reservations';
import {Housekeeping} from '../../src/features/housekeeping/Housekeeping';
import {Expenses} from '../../src/features/expenses/Expenses';
import {DayClosing} from '../../src/features/closing/DayClosing';
import {Reports} from '../../src/features/reports/Reports';
import {AccessControl} from '../../src/features/admin/AccessControl';
import {HotelSettings} from '../../src/features/admin/HotelSettings';
const core:Record<string,()=>any>={home:()=><ModulesHome/>,overview:()=><Overview/>,rooms:()=><RoomBoard/>,reservations:()=><Reservations/>,folios:()=><Reservations folio/>,housekeeping:()=><Housekeeping/>,
  expenses:()=><Expenses/>,closing:()=><DayClosing/>,reports:()=><Reports/>,access:()=><AccessControl/>,settings:()=><HotelSettings/>};
function ctx(s:any,uid:string,mobile:boolean,page:string):any{
  const user=s.users.find((u:any)=>u.id===uid)??s.users[0];
  const noop=()=>{};
  return {s,uid,user,m:metrics(s),page,go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'Arrivals',setTab:noop,
    arrivals:s.bookings.filter((b:any)=>b.status==='Confirmed'&&b.arrival<=s.date),departures:s.bookings.filter((b:any)=>b.status==='In house'&&b.departure<=s.date),
    pending:s.approvals.filter((a:any)=>a.status==='Pending').length+s.expenses.filter((e:any)=>e.status==='Pending').length,
    taskList:s.tasks.filter((t:any)=>(!assignedScopeRoles.includes(user?.role)||t.assignee===uid)&&!(t.status==='Done'&&t.date&&t.date<addDays(s.date,-2))),
    roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
}
export function renderPages(s:any,uid:string,mobile=false){
  const all:Record<string,()=>any>=mobile?Object.fromEntries(Object.entries(mobilePages).map(([k,P]:any)=>[k,()=><P/>])):{...core,...Object.fromEntries(Object.entries(extraPages).map(([k,P]:any)=>[k,()=><P/>]))};
  void navItems;const errors:Record<string,string>={};let rendered=0;
  for(const [id,el] of Object.entries(all)){
    try{renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile,id)}>{el()}</HccContext.Provider>);rendered++;}
    catch(e:any){errors[id]=String(e?.message??e).slice(0,300);}
  }
  return {rendered,errors};
}
`);
    await build({entryPoints:[dir+'/sync-pages-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:dir+'/sync-pages.mjs',logLevel:'error',jsx:'automatic',
      define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"production"'},
      banner:{js:"import {createRequire as __spReq} from 'node:module';const require=__spReq(import.meta.url);"}});
    return import(new URL('../'+dir+'/sync-pages.mjs',import.meta.url).href);
  })();
}
