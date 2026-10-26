// Wave C3 · System health (audit A09-05 + fix wave A "Left open"), prototype side: the demo's parked event and backup results, the mock
// SYS_RETRY_PARKED (same permission, refusals and audit text as the server's RetryParkedEvent), the accounting "stuck — see System health"
// exception (word for word the server's), and the desktop + phone page in both builds (the production build never shows demo values).
// The production mapping (GET /admin/system-health, RetryParkedEvent) is tested in sync-syshealth.mjs. Names prefixed [SYS].
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus} from '../../src/services/mock';
import {navItems} from '../../src/app/nav'; // same import order as App.tsx (nav before registry)
import {extraPages,mobilePages} from '../../src/app/registry';
void navItems;
function ctx(s:any,uid:string,mobile:boolean,over:any={}):any{
  const user=s.users.find((u:any)=>u.id===uid)??s.users[0];
  const noop=()=>{};
  return {s,uid,user,m:metrics(s),page:'syshealth',go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'exceptions',setTab:noop,
    arrivals:[],departures:[],pending:0,taskList:[],roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop,...over};
}
const html=(s:any,uid:string,mobile:boolean,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
export const page=(s:any,uid:string,mobile=false)=>{const P=(mobile?mobilePages:extraPages).syshealth;return html(s,uid,mobile,<P/>);};
export const accountingPhone=(s:any,uid:string)=>{const P=mobilePages.accounting;return html(s,uid,true,<P/>);};
`;

const ACC_EVENT={consumer:'accounting-vouchers',label:'Accounting vouchers',outboxId:'90311',eventType:'ChargePosted',attempts:5,errorType:'InvalidOperationException',
  firstFailedAt:'2026-10-24T08:10',lastFailedAt:'2026-10-24T08:12',parkedAt:'2026-10-24T08:12'};

export async function register({test,M,assert}){
  await mkdir('tests/.generated',{recursive:true});
  await writeFile('tests/.generated/sys-entry.tsx',ENTRY);
  const load=async mode=>{
    await build({entryPoints:['tests/.generated/sys-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:`tests/.generated/sys-${mode}.mjs`,logLevel:'error',jsx:'automatic',
      define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':JSON.stringify(mode)},
      banner:{js:"import {createRequire as __sysReq} from 'node:module';const require=__sysReq(import.meta.url);"}});
    return import(new URL(`./.generated/sys-${mode}.mjs`,import.meta.url).href);
  };
  const prod=await load('production'),demo=await load('prototype');
  const s=M.seed();
  const code=c=>e=>e.code===c;

  test('[SYS] Demo view: one fictional parked guest-message event from the simulated clock, the simulated backup history and last Sunday\'s drill; no Event Log',()=>{
    const v=M.sysHealthView(s),p=v.parked[0];
    assert.equal(v.source,'demo');assert.equal(v.parked.length,1);
    assert.deepEqual([p.consumer,p.label,p.outboxId,p.eventType,p.attempts,p.errorType],['messaging-triggers','Guest messages','48213','ReservationConfirmed',5,'SqlException 547']);
    assert.equal(p.lastFailedAt,M.addMinutes(s.clock,-35));assert.equal(p.firstFailedAt,M.addMinutes(s.clock,-43));
    const b=M.hkBackupStatus(s);
    assert.deepEqual([v.lastBackup.kind,v.lastBackup.ok,v.lastBackup.at],[b.latest.kind,b.latest.result==='Succeeded',b.latest.at]);
    assert.equal(v.lastOkBackupAt,M.hkLastBackup(s).at);
    assert.equal(v.lastDrill.kind,'RestoreDrill');assert.ok(v.lastDrill.ok);assert.equal(new Date(v.lastDrill.at.slice(0,10)+'T12:00:00Z').getUTCDay(),0,'a Sunday');
    assert.ok(v.lastDrill.at<=s.clock);assert.equal(v.eventLog,'unavailable');
    assert.equal(M.sysParkedAccounting(s),0,'the demo event is not an accounting one: the demo books stay lockable');});

  const sx=M.execute(s,'techadmin',{type:'SYS_RETRY_PARKED',consumer:'messaging-triggers',outboxId:'48213',key:'sys-retry:messaging-triggers/48213/5'});
  test('[SYS] SYS_RETRY_PARKED (Tech admin): the event leaves the list, audited with the server\'s text; the same key is a replay; again = NOT_FOUND',()=>{
    assert.deepEqual(M.sysHealthView(sx).parked,[]);assert.equal(sx.sysHealthX.source,'demo');
    const a=sx.audit[0];assert.equal(a.action,'SYS RETRY PARKED');
    assert.equal(a.detail,'Parked event 48213 (ReservationConfirmed) of Guest messages [messaging-triggers] released for one more delivery attempt after 5 failed attempt(s) (SqlException 547)');
    const again=M.execute(sx,'techadmin',{type:'SYS_RETRY_PARKED',consumer:'messaging-triggers',outboxId:'48213',key:'sys-retry:messaging-triggers/48213/5'});
    assert.equal(again.audit.length,sx.audit.length,'replayed key: no second effect');
    assert.throws(()=>M.execute(sx,'owner',{type:'SYS_RETRY_PARKED',consumer:'messaging-triggers',outboxId:'48213'}),
      e=>e.code==='NOT_FOUND'&&/may have been retried or delivered already/.test(e.message));
    assert.ok(M.sysHealthView(M.execute(s,'owner',{type:'SYS_RETRY_PARKED',consumer:'messaging-triggers',outboxId:'48213'})).parked.length===0,'the Owner may retry too');});
  test('[SYS] SYS_RETRY_PARKED: Manager, Accountant, Cashier, Reception, Housekeeping are refused FORBIDDEN; bad input VALIDATION; another consumer NOT_FOUND',()=>{
    for(const u of ['manager','accountant','cashier','reception','supervisor','housekeeper','maintenance'])
      assert.throws(()=>M.execute(s,u,{type:'SYS_RETRY_PARKED',consumer:'messaging-triggers',outboxId:'48213'}),code('FORBIDDEN'),u);
    assert.throws(()=>M.execute(s,'owner',{type:'SYS_RETRY_PARKED',consumer:' ',outboxId:'48213'}),code('VALIDATION'));
    assert.throws(()=>M.execute(s,'owner',{type:'SYS_RETRY_PARKED',consumer:'messaging-triggers',outboxId:'x1'}),code('VALIDATION'));
    assert.throws(()=>M.execute(s,'owner',{type:'SYS_RETRY_PARKED',consumer:'accounting-vouchers',outboxId:'48213'}),code('NOT_FOUND'));});

  // A parked ACCOUNTING event (as the server reports it) blocks every unlocked accounting date with the server's words.
  const date=M.accDates(s).find(d=>M.accDayStatus(s,d)==='Closed')??M.accDates(s)[0];
  const stuck={...structuredClone(s),sysHealthX:{source:'demo',parked:[ACC_EVENT]}};
  test('[SYS] Accounting: a parked accounting event is "Accounting events stuck — see System health" (server text word for word) and blocks the lock; retried, it is gone',()=>{
    const x=M.accExceptions(stuck,date).find(e=>e.kind==='stuck');
    assert.ok(x,'stuck exception');assert.equal(x.severity,'error');assert.equal(x.id,'stuck');
    assert.equal(x.title,'Accounting events stuck — see System health');
    assert.equal(x.detail,'1 posting event(s) for this property could not be booked after repeated attempts and were set aside. The Owner or the Tech admin can retry them in Admin & Setup › System health; this date cannot be locked until they are booked.');
    assert.match(M.accLockBlocker(stuck,date),/stuck — see System health/);
    assert.ok(!M.accExceptions(s,date).some(e=>e.kind==='stuck'));
    const cs=readFileSync(new URL('../../08 Production/src/Hcc.Domain/Accounting/Vouchers.cs',import.meta.url),'utf8');
    assert.ok(cs.includes(`StuckTitle = "${M.SYS_STUCK_TITLE}"`),'server title differs');
    assert.ok(cs.includes('$"{parkedEvents} '+M.sysStuckDetail(1).slice(2)+'"'),'server detail differs');
    const done=M.execute(stuck,'owner',{type:'SYS_RETRY_PARKED',consumer:'accounting-vouchers',outboxId:'90311'});
    assert.ok(!M.accExceptions(done,date).some(e=>e.kind==='stuck'));});

  const own=demo.page(s,'owner'),tad=demo.page(s,'techadmin'),phone=demo.page(s,'techadmin',true),mgr=demo.page(s,'manager'),mgrPhone=demo.page(s,'manager',true);
  test('[SYS] Demo page (desktop): parked table with Retry, backups, last good backup, drill and Event Log rows; Demo data pill; no guest data',()=>{
    for(const h of [own,tad]){
      for(const t of ['System health','Parked events','1 parked','Guest messages','ReservationConfirmed','No. 48213','SqlException 547','Retry','Last backup','Last good backup','Last restore drill','Windows Event Log','Demo data'])
        assert.ok(h.includes(t),'missing '+t);
      assert.ok(!/Ravi|guest name|phone/i.test(h.replace(/Guest messages|guest message/gi,'')),'no guest data');}
    assert.ok(mgr.includes('System health is for the Owner and the Tech admin')&&!mgr.includes('Retry'));});
  test('[SYS] Demo page (phone): the same parked card with a Retry button, the backup card, no table; the Manager sees the notice',()=>{
    for(const t of ['Parked events','Guest messages','ReservationConfirmed · No. 48213','5 attempts','Retry','Last backup','Last restore drill','Windows Event Log'])assert.ok(phone.includes(t),'missing '+t);
    assert.ok(!phone.includes('<table'));assert.ok(mgrPhone.includes('System health is for the Owner and the Tech admin'));
    assert.ok(demo.page(sx,'owner',true).includes('Nothing parked'));});

  const server={source:'server',checkedAt:'2026-10-24T10:05',eventLog:'off',parked:[ACC_EVENT],
    lastBackup:{kind:'Differential',ok:false,at:'2026-10-24T08:30',reason:'sqlcmd failed (exit 1): Msg 3201, Msg 3013'},lastOkBackupAt:'2026-10-24T02:31',
    lastDrill:{kind:'RestoreDrill',ok:true,at:'2026-10-20T03:34',file:'Hcc_FULL_20261019T210000Z.bak.hccenc'}};
  const ps={...structuredClone(s),sysHealthX:server};
  const prodPage=prod.page(ps,'owner'),prodPhone=prod.page(ps,'owner',true),prodEmpty=prod.page(s,'owner'),prodEmptyPhone=prod.page(s,'owner',true);
  test('[SYS] Production page: the server\'s parked events and backup results; failed backup with its reason; Event Log off with the fix; no demo values',()=>{
    for(const t of ['Accounting vouchers','ChargePosted','No. 90311','InvalidOperationException','Failed','Changes backup (differential)','Reason: sqlcmd failed (exit 1): Msg 3201, Msg 3013',
      'Passed','Restore drill','Install-HccService.ps1 -EventSourceOnly','Checked 10:05 IST','A voucher is missing'])assert.ok(prodPage.includes(t),'missing '+t);
    assert.ok(!prodPage.includes('Demo data')&&!prodPage.includes('48213'));
    assert.ok(prodPhone.includes('Accounting vouchers')&&prodPhone.includes('Retry')&&prodPhone.includes('Msg 3201'));});
  test('[SYS] Production page without the server answer (not loaded / no PERM-SYS-001): empty, never the demo event or the simulated backups',()=>{
    for(const h of [prodEmpty,prodEmptyPhone]){assert.ok(h.includes('Nothing parked'));assert.ok(h.includes('None recorded yet'));assert.ok(!h.includes('48213')&&!h.includes('simulated'));}});
  test('[SYS] Accounting (phone): the stuck exception card opens System health for the Owner; for the Manager it is a plain card (no System health page)',()=>{
    const card=/<(button|div)[^>]*class="m-list-card acc-m-ex-error"[^>]*><div class="m-lc-main"><strong>Accounting events stuck — see System health<\/strong>/;
    assert.equal(demo.accountingPhone(stuck,'owner').match(card)?.[1],'button');
    assert.equal(demo.accountingPhone(stuck,'manager').match(card)?.[1],'div');
    assert.ok(!demo.accountingPhone(s,'owner').includes('Accounting events stuck'));});
  test('[SYS] Navigation: System health is an Admin & Setup page after Backup & recovery, gated "admin" (Owner, Tech admin), searchable',()=>{
    const nm=readFileSync(new URL('../src/app/navmap.ts',import.meta.url),'utf8'),reg=readFileSync(new URL('../src/features/admin/register.tsx',import.meta.url),'utf8');
    assert.match(nm,/syshealth:'admin'/);assert.match(nm,/syshealth:'Stuck background work and the last backups'/);assert.match(nm,/syshealth:\['system health',/);
    assert.match(reg,/\{id:'syshealth',label:'System health',icon:Activity,p:'admin'\},after:'backup'\}/);
    assert.deepEqual(['owner','techadmin','manager','accountant','cashier'].map(u=>M.can(s,u,'admin')),[true,true,false,false,false]);});
}
