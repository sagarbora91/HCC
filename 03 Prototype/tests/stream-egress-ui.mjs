// F5-U1 · internet access screens (EGRESS-CONTRACT.md §5, §6): Master configuration › Internet group (Owner changes with a reason, Manager reads, nobody else
// sees it), System health Egress block, banners ("Internet off — messages queue", OTA last sync), held-queue badge, APK allowNavigation.
// Names prefixed [EGRESS-UI].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/egress-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {InternetPanel,NetSetModal,EgressBlock,NetBanner,netBannerText,netHeldOf,netRoleSees,netRoleEdits} from '../../src/features/admin/InternetPanel';
import {CfgGroupDetail} from '../../src/features/admin/HotelSettings';
import {SystemHealth} from '../../src/features/admin/SystemHealth';
import {MsgMessagesPage,MsgMessagesMobile} from '../../src/features/messaging/Messages';
import {ChannelsPage} from '../../src/features/channels/Channels';
import {registration} from '../../src/features/admin/register';
import {cfgListRows} from '../../src/features/admin/config/logic';
export {cfgListRows,netBannerText,netHeldOf,netRoleSees,netRoleEdits,registration};
const C:any={InternetPanel,NetSetModal,EgressBlock,NetBanner,CfgGroupDetail,SystemHealth,MsgMessagesPage,MsgMessagesMobile,ChannelsPage};
export function render(name:string,props:any,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'settings',go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],props)}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/egress-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/egress-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __auReq} from 'node:module';const require=__auReq(import.meta.url);"}});
  const X=await import('./.generated/egress-ui.mjs');const {render}=X;
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const T='[EGRESS-UI] ',ALL=['owner','manager','accountant','cashier','reception','housekeeper','maint','techadmin','supervisor'];

  test(T+'Internet group: 13th group, Owner changes (buttons with a reason prompt), Manager reads, Reception / Housekeeping / Maintenance / Restaurant never see it',()=>{
    assert.ok(M.CFG_GROUPS.some(g=>g.id==='internet'&&g.section==='Software and security'));
    const s=M.seed();
    const own=text(render('CfgGroupDetail',{g:'internet',q:''},s,'owner'));
    assert.match(own,/Internet access/);assert.match(own,/Internet \(master switch\)/);assert.match(own,/Guest messaging/);assert.match(own,/OTA channel manager/);assert.match(own,/ID scan \(OCR\)/);
    assert.match(own,/Payment links/);assert.match(own,/E-mail/);assert.match(own,/Update check/);assert.match(own,/Allowed time window/);assert.match(own,/Switch on/);assert.match(own,/Change window/);
    const mgr=text(render('CfgGroupDetail',{g:'internet',q:''},s,'manager'));assert.match(mgr,/Read only/);assert.doesNotMatch(mgr,/Switch on|Change window/);
    for(const u of ['reception','housekeeper','maint','cashier','techadmin'])assert.match(text(render('InternetPanel',{},s,u)),/Not available for your role/,u);
    assert.deepEqual(ALL.filter(u=>X.netRoleSees(s.users.find(x=>x.id===u)?.role)),['owner','manager']);assert.deepEqual(ALL.filter(u=>X.netRoleEdits(s.users.find(x=>x.id===u)?.role)),['owner']);
  });
  test(T+'the group list (search palette side of the hub) hides the group from every role but Owner and Manager; searching the group finds the switches',()=>{
    const s=M.seed();
    for(const u of ALL){const has=X.cfgListRows(s,u,'').some(r=>r.id==='internet');assert.equal(has,u==='owner'||u==='manager',u);}
    assert.equal(M.cfgSearch(s,'').length,14);/* F12-SETTINGS: + measure */assert.ok(M.cfgSearch(s,'ocr').some(h=>h.group.id==='internet'));
    assert.equal(M.cfgGroupAccess(s,'owner','internet').readOnly,false);assert.equal(M.cfgGroupAccess(s,'manager','internet').readOnly,true);
    for(const d of M.cfgDefs(s).filter(d=>d.group==='internet'))assert.equal(d.editable,false,d.key);
  });
  test(T+'CFG_NET_SET from the dialog: Owner with a reason; the Manager and everyone else refused; the reason is required',()=>{
    let s=M.seed();
    assert.throws(()=>M.execute(s,'manager',{type:'CFG_NET_SET',setting:'net.internetEnabled',value:true,reason:'test'}),/Only the Owner/);
    assert.throws(()=>M.execute(s,'owner',{type:'CFG_NET_SET',setting:'net.internetEnabled',value:true,reason:''}),/reason/);
    s=M.execute(s,'owner',{type:'CFG_NET_SET',setting:'net.internetEnabled',value:true,reason:'Open for the OTA test'});
    const own=text(render('InternetPanel',{},s,'owner'));assert.match(own,/Switch off/);assert.match(own,/Open for the OTA test/);
    const dlg=text(render('NetSetModal',{id:'net.ota',room:'on'},s,'owner'));assert.match(dlg,/Switch on · OTA channel manager/);assert.match(dlg,/Reason \(3–120 characters, required\)/);
    assert.match(text(render('NetSetModal',{id:'net.window',room:'window'},s,'owner')),/Allowed time window/);
    assert.ok(X.registration.modals['net-set']);
  });
  test(T+'System health Egress block: state per switch with the reason, window, last change, held counts; Owner and Manager only',()=>{
    let s=M.seed();
    const off=text(render('EgressBlock',{},s,'owner'));assert.match(off,/Internet \(egress\)/);assert.match(off,/No internet call allowed now/);assert.match(off,/Off — master switch is off/);assert.match(off,/None yet \(all off\)/);
    s=M.execute(s,'owner',{type:'CFG_NET_SET',setting:'net.internetEnabled',value:true,reason:'Go live test'});s=M.execute(s,'owner',{type:'CFG_NET_SET',setting:'net.messaging',value:true,reason:'Messages on'});
    const on=text(render('EgressBlock',{},s,'manager'));assert.match(on,/Some services allowed now/);assert.match(on,/Messages on/);assert.match(on,/Off — this switch is off/);assert.doesNotMatch(on,/Owner Demo|Sagar/);
    for(const u of ['reception','housekeeper','maint','cashier','techadmin','accountant'])assert.equal(render('EgressBlock',{},s,u),'',u);
    assert.doesNotMatch(render('EgressBlock',{phone:true},s,'owner',true),/<table/);
    assert.match(text(render('SystemHealth',{},s,'owner')),/Internet \(egress\)/);
  });
  test(T+'banners and held badge: "Internet off — messages queue" with the held count; OTA "not being received — last sync"; gone when the switch is on and nothing is held',()=>{
    let s=structuredClone(M.seed());
    assert.match(X.netBannerText(s,'messaging'),/^Internet off — messages queue · /);assert.match(X.netBannerText(s,'ota'),/^OTA bookings are not being received — last sync /);
    M.netEnqueue(s,'messaging','messaging','m1');M.netEnqueue(s,'messaging','messaging','m2');
    assert.equal(X.netHeldOf(s,'messaging'),2);assert.match(X.netBannerText(s,'messaging'),/Internet off — messages queue · 2 held · /);
    assert.match(text(render('NetBanner',{kind:'messaging'},s,'owner')),/Internet off — messages queue.*2 held/);assert.match(text(render('NetBanner',{kind:'ota'},s,'owner')),/OTA bookings are not being received — last sync/);
    assert.match(render('MsgMessagesPage',{},s,'owner'),/Internet off — messages queue · 2 held/);assert.match(text(render('MsgMessagesMobile',{},s,'reception',true)),/Internet off — messages queue/);
    assert.match(render('ChannelsPage',{},s,'owner'),/OTA bookings are not being received — last sync/);
    s=M.execute(s,'owner',{type:'CFG_NET_SET',setting:'net.internetEnabled',value:true,reason:'Turn on'});s=M.execute(s,'owner',{type:'CFG_NET_SET',setting:'net.messaging',value:true,reason:'Turn on'});s=M.execute(s,'owner',{type:'CFG_NET_SET',setting:'net.ota',value:true,reason:'Turn on'});
    assert.equal(X.netHeldOf(s,'messaging'),0,'replayed in order');assert.equal(X.netBannerText(s,'messaging'),'');assert.equal(X.netBannerText(s,'ota'),'');assert.equal(render('NetBanner',{kind:'messaging'},s,'owner'),'');
  });
  test(T+'APK: capacitor server.allowNavigation is the app origin only (the server address is typed on the device); the README says so',()=>{
    const cfg=JSON.parse(readFileSync(new URL('../../04 Android/capacitor.config.json',import.meta.url),'utf8'));
    assert.deepEqual(cfg.server.allowNavigation,['localhost']);assert.equal(cfg.server.androidScheme,'https');assert.equal(cfg.android.allowMixedContent,false);
    assert.match(readFileSync(new URL('../../04 Android/README.md',import.meta.url),'utf8'),/allowNavigation/);
  });
}
