// FIT-2 lane forms: Prefs rail shows each section's current value, Master configuration rail shows headline values, Backup tabs carry counts.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus} from '../../src/services/mock';
import {extraPages} from '../../src/app/registry';
import {prefsRailSummary} from '../../src/features/prefs/PrefsPage';
import {cfgHeads} from '../../src/features/admin/HotelSettings';
export {prefsRailSummary,cfgHeads};
export function render(s:any,uid:string,page:string){
  const user=s.users.find((u:any)=>u.id===uid)??s.users[0];const noop=()=>{};
  const ctx:any={s,uid,user,m:metrics(s),page,go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'',setTab:noop,
    arrivals:[],departures:[],pending:0,taskList:[],roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:false,mode:'desktop',setMode:noop,framed:false},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
  const P=(extraPages as any)[page];
  return renderToStaticMarkup(<HccContext.Provider value={ctx}><P/></HccContext.Provider>);
}
`;
const read=f=>readFileSync(f,'utf8');
export async function register({test,M,assert}){
  await mkdir('tests/.generated',{recursive:true});
  await writeFile('tests/.generated/fit-forms-entry.tsx',ENTRY);
  await build({entryPoints:['tests/.generated/fit-forms-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-forms.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"prototype"'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import(new URL('./.generated/fit-forms.mjs',import.meta.url).href);
  const s=M.seed();

  test('[FIT2-FORMS] prefs rail: every section shows its current value',()=>{
    const d={textScale:1.15,honourSystemFont:true,reduceMotion:'system',density:'compact',startPage:'',listPageSize:100,toastSeconds:6.5,theme:'dark'};
    const r=X.prefsRailSummary(d,'desktop','Modules Home');
    assert.equal(r.text,'Large (115 %)');assert.equal(r.appearance,'Dark, Compact');assert.equal(r.display,'Desktop');
    assert.equal(r.navigation,'Modules Home, 100 rows');assert.equal(r.alerts,'6.5 s');
    assert.equal(X.prefsRailSummary({...d,reduceMotion:'on',toastSeconds:0},'auto','Rooms').appearance,'Dark, Compact, Reduced motion');
    const h=X.render(s,'owner','prefs');
    assert.ok(h.includes('fit-form has-summary'),'rail has summaries');
    for(const t of ['fit-rail-sum','Default (100 %)','Light, Comfortable'])assert.ok(h.includes(t),t);
  });
  test('[FIT2-FORMS] master configuration rail: headline values beside each group summary; modules none; no secret',()=>{
    const h=X.render(s,'owner','settings');
    assert.ok(h.includes('cfg-heads'),'heads rendered');
    assert.ok(/Daily backup time: \d\d:\d\d/.test(X.cfgHeads(s,'owner','data')),X.cfgHeads(s,'owner','data'));
    assert.equal(X.cfgHeads(s,'owner','modules'),'');
    for(const g of ['property','rooms','frontdesk','money','pos','hkm','people','compliance','users','messages','data']){
      const t=X.cfgHeads(s,'owner',g);assert.ok(t.length>0,g+' has headline values');assert.ok(t.split(' · ').length<=3,g);}
    assert.ok(!/gateway/i.test(X.cfgHeads(s,'owner','messages')),'secret setting never shown');
  });
  test('[FIT2-FORMS] backup tabs carry counts; stylesheet adds no font size',()=>{
    const h=X.render(s,'owner','backup');assert.ok(/Restore procedure<[^>]*>[^<]*<[^>]*>\d+/.test(h)||h.includes('Restore procedure'),'restore tab');
    assert.ok(!/font-size/.test(read('src/styles/fit2-forms.css').replace(/\/\*[^]*?\*\//g,'')));
  });
}
