// ADM-UX · usability fixes for Administration › Modules & features (tester findings after the owner thought the switches did not work):
// the On/Off pill is a real switch (role=switch) that does what the button does, fixed rows show a lock + plain reason and are not toggles,
// a parent switched back on says its dependants stay off, config toasts last ~8 s without touching the personal toast length,
// and the phone menu has an Admin & Setup entry for Owner and Tech admin only. Names prefixed [ADM-UX].
// No DOM library in the repo: the build swaps react/jsx-runtime for a spy that records the onClick of every rendered element, so a click is replayed
// on the real handler (the browser itself turns Space / Enter on a <button> into that click).
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';

const SPY=`
import * as R from '../../node_modules/react/jsx-runtime.js';
const wrap=(f:any)=>(type:any,props:any,key:any)=>{if(typeof type==='string'&&props&&typeof props.onClick==='function'){((globalThis as any).__spy||((globalThis as any).__spy=[])).push({type,props});}return f(type,props,key);};
export const jsx=wrap((R as any).jsx),jsxs=wrap((R as any).jsxs),Fragment=(R as any).Fragment;
`;
const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {CfgModulesPanel,CfgFeaturesTree} from '../../src/features/admin/config/panels';
import {NavDrawer} from '../../src/app/NavDrawer';
import {toastDuration} from '../../src/app/prefs';
import {cfgSwitchOnNote,CFG_TOAST_MS} from '../../src/features/admin/config/logic';
export {toastDuration,cfgSwitchOnNote,CFG_TOAST_MS};
const C:any={CfgModulesPanel,CfgFeaturesTree,NavDrawer};
export function render(name:string,s:any,uid:string,runs:any[]=[],mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);(globalThis as any).__spy=[];
  const ctx:any={s,uid,user,m:metrics(s),page:'home',go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,
    run:async(cmd:any,msg:string,opts:any)=>{runs.push({cmd,msg,opts});return true;},toast:noop,permission:(p:any)=>can(s,uid,p),
    display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,signOut:noop,taskList:[]};
  const html=renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],{onClose:noop})}</HccContext.Provider>);
  return {html,spy:(globalThis as any).__spy as any[]};
}`;

export async function register({test,M,assert}){
  await writeFile('tests/.generated/adm-ux-jsx.ts',SPY);
  await writeFile('tests/.generated/adm-ux-entry.tsx',ENTRY);
  await build({entryPoints:['tests/.generated/adm-ux-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/adm-ux.mjs',logLevel:'error',jsx:'automatic',
    alias:{'react/jsx-runtime':'./tests/.generated/adm-ux-jsx.ts'},
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import(new URL('./.generated/adm-ux.mjs',import.meta.url).href);
  const base=M.seed();
  const key=id=>M.featureKey(id);
  const withOff=ids=>{let s=structuredClone(base);for(const id of ids)s=M.execute(s,'owner',{type:'SETTING_SET',setting:key(id),value:false});return s;};
  const label=id=>M.featureById(id).feature.label;
  const esc=t=>t.replace(/&/g,'&amp;');
  /** The recorded click handler of the element with this aria-label (button or switch). */
  const handler=(spy,aria)=>{const e=spy.find(x=>x.props['aria-label']===aria);assert.ok(e,'no clickable element labelled '+aria);return e;};
  const confirmYes=fn=>{const g=globalThis,had='window' in g,prev=g.window;g.window={confirm:()=>true};try{return fn();}finally{if(had)g.window=prev;else delete g.window;}};

  test('[ADM-UX] pill is a real switch: <button role="switch" aria-checked> for every changeable module and sub-function, next to the unchanged "Switch off X" button',()=>{
    const r=X.render('CfgModulesPanel',base,'owner');
    const sw=r.spy.filter(x=>x.props.role==='switch');
    assert.ok(sw.length>=M.CFG_MODULES.length+10,'switches '+sw.length);
    assert.ok(sw.every(x=>x.type==='button'),'a native button, so Space and Enter work');
    assert.ok(r.html.includes('role="switch" aria-checked="true" aria-label="Messages is on"')||r.html.includes('aria-label="Messages is on"'));
    assert.ok(r.html.includes(`aria-label="Switch off ${esc(label('messaging.messages'))}"`),'old button label kept');
    assert.ok(new RegExp(`aria-label="Switch (on|off) ${esc(M.CFG_MODULES[0].label)}"`).test(r.html));
  });
  test('[ADM-UX] clicking the pill sends the same SETTING_SET as the button (module and sub-function), 8 s toast option, same confirm for dependants',()=>{
    const id='messaging.messages',deps=M.featureClosure(id).slice(1);assert.ok(deps.length>0,'Messages has dependants');
    const runs=[];const r=X.render('CfgFeaturesTree',base,'owner',runs);
    const L=label(id);
    let declined=true;{const g=globalThis,had='window' in g,prev=g.window;g.window={confirm:()=>false};try{handler(r.spy,`${L} is on`).props.onClick({target:{closest:()=>null}});}finally{if(had)g.window=prev;else delete g.window;}}
    assert.equal(runs.length,0,'declined confirm sends nothing');void declined;
    confirmYes(()=>{handler(r.spy,`${L} is on`).props.onClick({});handler(r.spy,`Switch off ${L}`).props.onClick({});});
    assert.equal(runs.length,2);
    assert.deepEqual(runs[0].cmd,runs[1].cmd);assert.equal(runs[0].msg,runs[1].msg);
    assert.equal(runs[0].cmd.type,'SETTING_SET');assert.equal(runs[0].cmd.setting,key(id));assert.equal(runs[0].cmd.value,false);
    assert.equal(runs[0].opts.toastMs,8000);assert.equal(X.CFG_TOAST_MS,8000);
    const m=M.CFG_MODULES.find(x=>x.id!=='x');const mr=[];const rm=X.render('CfgModulesPanel',base,'owner',mr);
    handler(rm.spy,`${m.label} is on`).props.onClick({});handler(rm.spy,`Switch off ${m.label}`).props.onClick({});
    assert.equal(mr.length,2);assert.deepEqual(mr[0].cmd,mr[1].cmd);assert.equal(mr[0].cmd.setting,'module.'+m.id);assert.equal(mr[0].opts.toastMs,8000);
  });
  test('[ADM-UX] the row itself is tappable where allowed (click outside a button acts, a click on a button inside is left to the button)',()=>{
    const runs=[];const r=X.render('CfgModulesPanel',base,'owner',runs);
    const m=M.CFG_MODULES[0];
    const row=r.spy.find(x=>x.type==='li'&&x.props.className.includes('tappable'));assert.ok(row,'a tappable row');
    row.props.onClick({target:{closest:sel=>sel==='button'?{}:null}});assert.equal(runs.length,0,'button click is not doubled');
    row.props.onClick({target:{closest:()=>null}});assert.equal(runs.length,1);assert.equal(runs[0].cmd.type,'SETTING_SET');void m;
  });
  test('[ADM-UX] fixed rows (core sub-function, dependency off, no right to change) are not toggles, look locked and say why in one plain sentence',()=>{
    const core=M.featureAll().find(r=>r.feature.core);
    const r=X.render('CfgFeaturesTree',base,'owner');
    assert.ok(r.html.includes('Always on — the hotel cannot run without it.'));
    assert.ok(!r.spy.some(x=>x.props['aria-label']===`${core.feature.label} is on`||x.props['aria-label']===`Switch off ${core.feature.label}`),'core row has no switch or button');
    assert.ok(r.html.includes('class="cfg-lock"')&&r.html.includes('cfg-mod cfg-feature on core locked'));
    // dependency off: Messages off (cascade) then switch the templates' own flag back on would not be possible; module Messages off -> hint
    const s2=M.execute(structuredClone(base),'owner',{type:'SETTING_SET',setting:'module.messaging',value:false});
    const r2=X.render('CfgFeaturesTree',s2,'owner');assert.ok(r2.html.includes('Switch the module on first.')&&!r2.spy.some(x=>x.props['aria-label']==='Messages is off'));
    // Manager cannot change configuration: no switch at all, a reason is shown
    const mgr=base.users.find(u=>u.role==='Manager').id;
    const r3=X.render('CfgModulesPanel',base,mgr);
    assert.equal(r3.spy.filter(x=>x.props.role==='switch').length,0,'Manager sees no switch');
    assert.ok(r3.html.includes('class="cfg-lock"'),'reason visible');assert.ok(!r3.html.includes('tappable'));
  });
  test('[ADM-UX] dependants stay off: switching a parent on lists the dependants still off; their rows keep "Off with it" / a "Switch on" button',()=>{
    const parent='messaging.messages',deps=M.featureClosure(parent).slice(1).map(label);
    const s=withOff([parent]);const off=M.cfgFeaturesOff(s);
    assert.ok(M.featureClosure(parent).every(id=>off.includes(id)),'cascade switched the dependants off');
    const runs=[];const r=X.render('CfgFeaturesTree',s,'owner',runs);
    handler(r.spy,`Switch on ${label(parent)}`).props.onClick({});
    assert.equal(runs.length,1);assert.equal(runs[0].cmd.value,true);
    for(const d of deps)assert.ok(runs[0].msg.includes(d),'message names '+d);
    assert.match(runs[0].msg,/stays? off — switch (it|them) on separately/);
    assert.equal(runs[0].msg,X.cfgSwitchOnNote(label(parent),deps));
    // applied: parent on, dependants remain off, their rows carry the note and a Switch on button
    const s2=M.execute(structuredClone(s),'owner',runs[0].cmd);
    assert.ok(M.cfgFeatureOn(s2,parent));for(const id of M.featureClosure(parent).slice(1))assert.ok(!M.cfgFeatureOn(s2,id),id+' stays off');
    const r2=X.render('CfgFeaturesTree',s2,'owner');
    for(const id of M.featureClosure(parent).slice(1)){assert.ok(r2.spy.some(x=>x.props['aria-label']===`Switch on ${label(id)}`),'Switch on '+label(id));}
    assert.ok(r2.html.includes('Stayed off when the part it needs was switched back on'));
    assert.equal(X.cfgSwitchOnNote('Messages',[]),'Messages switched on');
    assert.equal(X.cfgSwitchOnNote('Messages',['Templates & rules']),'Messages switched on. Templates & rules stays off — switch it on separately.');
  });
  test('[ADM-UX] config toast lasts about 8 s but never shortens the personal setting; "until dismissed" stays; other toasts keep the preference',()=>{
    assert.equal(X.toastDuration(3,8000),8000);assert.equal(X.toastDuration(6.5,8000),8000);assert.equal(X.toastDuration(10,8000),10000);
    assert.equal(X.toastDuration(0,8000),null,'sticky preference stays sticky');
    assert.equal(X.toastDuration(3),3000);assert.equal(X.toastDuration(6.5),6500);
    const app=readFileSync('src/app/App.tsx','utf8');
    assert.ok(app.includes('toastDuration(prefsRef.current.toastSeconds,minMs)')&&app.includes('opts.toastMs'));
    assert.ok(readFileSync('src/app/context.tsx','utf8').includes('toastMs?:number'));
  });
  test('[ADM-UX] phone menu: Admin & Setup entry for Owner and Tech admin, not for Manager; tile caption says modules on/off',()=>{
    for(const uid of ['owner','techadmin']){const r=X.render('NavDrawer',base,uid,[],true);
      assert.ok(r.html.includes('data-page="admin-setup"')&&r.html.includes('Admin &amp; Setup')&&r.html.includes('Modules on/off'),uid);}
    const mgr=base.users.find(u=>u.role==='Manager').id;
    assert.ok(!X.render('NavDrawer',base,mgr,[],true).html.includes('data-page="admin-setup"'),'Manager has no entry');
    assert.ok(readFileSync('src/app/navmap.ts','utf8').includes("blurb:'Modules on/off, users, roles, audit, backup'"));
  });
}
