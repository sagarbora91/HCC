// MB-07/09/10 (stream ops): phone housekeeping/maintenance logic, tab focus, server-rendered phone page (no money for
// housekeeper/maintenance, ≥ 56 px action classes), walkthrough steps and the inline web app manifest/icons.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {inflateSync} from 'node:zlib';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/mobile-ops-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,assignedScopeRoles} from '../../src/services/mock';
import {MoHousekeeping} from '../../src/mobile/ops/Housekeeping';
import {MoAssignSheet} from '../../src/mobile/ops/sheets';
export * from '../../src/mobile/ops/logic';
export {announceTab,takeTabFocus,tabsFor,roleTabs} from '../../src/app/mobile/tabs';
export {guideSteps} from '../../src/app/demo/DemoModals';
export {registration} from '../../src/mobile/ops/register';
function ctx(s:any,uid:string){const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),
    taskList:s.tasks.filter((t:any)=>!assignedScopeRoles.includes(user.role)||t.assignee===uid),display:{isMobile:true}} as any;}
export function renderPage(s:any,uid:string){return renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid)}><MoHousekeeping/></HccContext.Provider>);}
export function renderAssign(s:any,uid:string,id:string){return renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid)}><MoAssignSheet id={id}/></HccContext.Provider>);}
`);
  await build({entryPoints:['tests/.generated/mobile-ops-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mobile-ops.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __moReq} from 'node:module';const require=__moReq(import.meta.url);"}});
  const X=await import('./.generated/mobile-ops.mjs');
  const Mf=await import('../src/mobile/ops/manifest.mjs');
  const money=/₹|\brate\b|balance|folio|invoice/i;

  test('[MO] View per role: housekeeper My tasks, maintenance My repairs, others board',()=>{
    assert.equal(X.moViewFor('Housekeeper'),'mytasks');assert.equal(X.moViewFor('Maintenance'),'repairs');
    for(const r of ['Supervisor','Manager','Owner'])assert.equal(X.moViewFor(r),'board');
  });
  test('[MO] Tab/URL focus: Inspect tab or #inspect opens the Inspect segment; Report repair tab opens report mode',()=>{
    assert.deepEqual(X.moFocusFrom('inspect'),{segment:'inspect'});assert.deepEqual(X.moFocusFrom('','#inspect'),{segment:'inspect'});
    assert.deepEqual(X.moFocusFrom('board'),{segment:'todo'});assert.deepEqual(X.moFocusFrom('hkrepair'),{report:true});assert.deepEqual(X.moFocusFrom('rooms'),{});
    X.announceTab('inspect');assert.equal(X.takeTabFocus(),'inspect');assert.equal(X.takeTabFocus(),'',"focus is consumed once");
    const hk=X.roleTabs.Housekeeper.map(t=>t.key);assert.deepEqual(hk,['mytasks','hkrepair']);
    assert.ok(X.roleTabs.Supervisor.some(t=>t.key==='inspect'&&t.page==='housekeeping'));
  });
  test('[MO] My tasks: own tasks only, active work first; no other assignee leaks',()=>{
    const s=M.seed(),mine=X.moMyTasks(s,'housekeeper');
    assert.ok(mine.length>0&&mine.every(t=>t.assignee==='housekeeper'));
    assert.equal(mine[0].status,'In progress');assert.equal(mine[0].room,'305');
    assert.ok(!mine.some(t=>t.room==='204'||t.room==='303'));
  });
  test('[MO] Supervisor segments partition all tasks; Inspect holds room 303',()=>{
    const s=M.seed(),c=X.moSegmentCounts(s.tasks);
    assert.equal(c.todo+c.cleaning+c.inspect+c.done,s.tasks.length);
    assert.deepEqual(X.moSegmentTasks(s,s.tasks,'inspect').map(t=>t.room),['303']);
    assert.equal(X.moSegments.map(x=>x.label).join('/'),'To do/Cleaning/Inspect/Done');
  });
  test('[MO] Assign picker lists only enabled staff of the right role (repair → maintenance)',()=>{
    let s=M.seed();const clean=s.tasks.find(t=>t.id==='task-0'),rep=s.tasks.find(t=>t.kind==='repair');
    assert.deepEqual(X.moAssignPool(s,clean).map(u=>u.id).sort(),['housekeeper','housekeeper2']);
    assert.deepEqual(X.moAssignPool(s,rep).map(u=>u.id),['maint']);
    s=M.execute(s,'owner',{type:'USER',id:'housekeeper2'});
    assert.deepEqual(X.moAssignPool(s,clean).map(u=>u.id),['housekeeper']);
    s=M.execute(s,'supervisor',{type:'TASK',id:'task-1',action:'assign',assignee:'housekeeper'});
    assert.equal(s.tasks.find(t=>t.id==='task-1').assignee,'housekeeper');
    const html=X.renderAssign(s,'supervisor','task-0');assert.match(html,/Ravi/);assert.ok(!/Kavya/.test(html),'disabled housekeeper not offered');
  });
  test('[MO] Report-repair rooms: own open rooms; an open repair is flagged; REPAIR_REQUEST scoped',()=>{
    let s=M.seed();assert.deepEqual(X.moRepairRooms(s,'housekeeper').map(r=>r.room),['108','208','305']);
    s=M.execute(s,'housekeeper',{type:'REPAIR_REQUEST',room:'108',reason:'Tap leaking'});
    assert.equal(X.moRepairRooms(s,'housekeeper').find(r=>r.room==='108').open.note,'Tap leaking');
    assert.throws(()=>M.execute(s,'housekeeper',{type:'REPAIR_REQUEST',room:'204',reason:'Not mine'}),/assigned to you/);
  });
  test('[MO] AC-M07 phone flow: housekeeper cleans Room 105 after checkout, supervisor passes inspection',()=>{
    let s=M.seed();s=M.execute(s,'owner',{type:'BOOK',guest:'Phone Flow (fictional)',room:'105',arrival:s.date,departure:'2026-10-25',people:1});const id=s.bookings.at(-1).id;
    s=M.execute(s,'owner',{type:'REGISTER',id});s=M.execute(s,'owner',{type:'CHECKIN',id});s=M.execute(s,'owner',{type:'POST_NIGHTS',id});
    const bal=M.totals(s.bookings.find(b=>b.id===id)).balance;s=M.execute(s,'cashier',{type:'PAYMENT',id,amount:bal,method:'Cash',kind:'payment'});
    s=M.execute(s,'owner',{type:'CHECKOUT',id});
    const t=s.tasks.find(x=>x.room==='105'&&x.status==='To do');assert.ok(t,'checkout created a task');
    const who=t.assignee;assert.ok(X.moMyTasks(s,who).some(x=>x.id===t.id));
    let html=X.renderPage(s,who);assert.match(html,/Room 105/);assert.match(html,/Start cleaning/);assert.ok(!money.test(html),'no money on My tasks');
    s=M.execute(s,who,{type:'TASK',id:t.id,action:'start'});
    html=X.renderPage(s,who);assert.equal((html.match(/mo-box/g)||[]).length>=3,true);assert.match(html,/mo-big[^>]*disabled/);
    for(let i=0;i<3;i++)s=M.execute(s,who,{type:'TASK',id:t.id,action:'check',index:i});
    s=M.execute(s,who,{type:'TASK',id:t.id,action:'complete'});
    assert.ok(X.moSegmentTasks(s,s.tasks,'inspect').some(x=>x.id===t.id));
    X.announceTab('inspect');html=X.renderPage(s,'supervisor');assert.match(html,/Inspect rooms/);assert.match(html,/Pass/);assert.match(html,/Reject/);
    s=M.execute(s,'supervisor',{type:'TASK',id:t.id,action:'inspect'});assert.equal(s.rooms.find(r=>r.id==='105').hk,'Ready');
  });
  test('[MO] Phone page shows no financial data to housekeeper or maintenance; maintenance sees block info',()=>{
    const s=M.seed();
    for(const uid of ['housekeeper','housekeeper2','maint']){const html=X.renderPage(s,uid);assert.ok(!money.test(html),uid+' sees money');}
    const m=X.renderPage(s,'maint');assert.match(m,/My repairs/);assert.match(m,/Room 308/);assert.match(m,/Room blocked/);assert.match(m,/Start repair/);
    X.announceTab('hkrepair');const r=X.renderPage(s,'housekeeper');assert.match(r,/Report a repair/);assert.ok((r.match(/Report repair/g)||[]).length>=3);
  });
  test('[MO] Registration: phone page + sheets registered with allowed IDs',()=>{
    assert.ok(X.registration.mobile.pages.housekeeping);
    assert.deepEqual(Object.keys(X.registration.mobile.modals).sort(),['hk-reject','mo-assign','room-repair']);
  });
  test('[MO] Walkthrough steps: S0 has 15 steps with valid actors and a housekeeping step for Ravi',()=>{
    const s=M.seed(),ids=s.users.map(u=>u.id);
    assert.equal(X.guideSteps.length,15);assert.ok(X.guideSteps.every(x=>ids.includes(x.actor)&&x.page));
    assert.ok(X.guideSteps.some(x=>x.actor==='housekeeper'&&x.page==='housekeeping'));
  });
  test('[MO] Manifest: names, colours, standalone, PNG icons decode to a maroon square with a white H',()=>{
    const m=Mf.moManifest();
    assert.equal(m.name,'Hotel Control Centre');assert.equal(m.short_name,'HCC');assert.equal(m.theme_color,'#7a1f2b');assert.equal(m.background_color,'#faf6f0');assert.equal(m.display,'standalone');
    assert.deepEqual(m.icons.map(i=>i.sizes),['192x192','512x512','any']);
    const png=Mf.moIconPng(48);assert.deepEqual([...png.subarray(0,8)],[137,80,78,71,13,10,26,10]);assert.equal(png.readUInt32BE(16),48);
    const idat=png.indexOf('IDAT'),len=png.readUInt32BE(idat-4),raw=inflateSync(png.subarray(idat+4,idat+4+len)),px=(x,y)=>[...raw.subarray(y*(48*3+1)+1+x*3,y*(48*3+1)+4+x*3)];
    assert.deepEqual(px(2,2),[0x7a,0x1f,0x2b]);assert.deepEqual(px(17,24),[255,255,255]);assert.deepEqual(px(24,16),[0x7a,0x1f,0x2b]);
    const head=Mf.moHeadTags();assert.match(head,/rel="manifest" href="data:application\/manifest\+json,/);assert.match(head,/rel="apple-touch-icon"/);assert.ok(!/https?:\/\//.test(head.replace(/http:\/\/www\.w3\.org\/2000\/svg|http%3A%2F%2Fwww\.w3\.org%2F2000%2Fsvg/g,'')),'no external URLs');
  });
}
