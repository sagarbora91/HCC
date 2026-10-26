// DEC-077 (resolves DEC-028) · the Owner reopens a closed business day (mock handlers mirror Hcc.Domain.Closing.ReopenRules on the server).
// Names prefixed [REOPEN]. Day 24 Oct is closed through the real commands (BASE fixture), 25 Oct is open.
import {build} from 'esbuild';
import {tillClear} from './_till.mjs';

const D23='2026-10-23',D24='2026-10-24',D25='2026-10-25',D26='2026-10-26';

/** BASE: every arrival checked in, the pending expense approved, nights posted, cash counted exactly by the cashier, 24 Oct closed by the
 *  Manager and 25 Oct opened. */
function closedDay(M){
  let x=M.seed();
  for(const b of x.bookings.filter(b=>b.status==='Confirmed')){x=M.execute(x,'owner',{type:'REGISTER',id:b.id});x=M.execute(x,'owner',{type:'CHECKIN',id:b.id});}
  x=M.execute(x,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});x=M.execute(x,'owner',{type:'NIGHT_POST'});
  x=M.execute(x,'cashier',{type:'SHIFT_SUBMIT',counted:M.drawer(x)});x=tillClear(M,x);
  x=M.execute(x,'manager',{type:'DAY_CLOSE'});return M.execute(x,'manager',{type:'NEXT_DAY'});
}
const code=c=>e=>e.code===c;
const says=(c,re)=>e=>e.code===c&&re.test(e.message);

export async function register({test,M,assert}){
  const s0=closedDay(M);
  const reopen=(s,uid='owner',date=D24,reason='Room night posted to the wrong folio')=>M.execute(s,uid,{type:'DAY_REOPEN',date,reason});
  // An in-house stay that covers 24 Oct with its 24 Oct room line (posted by the close's night step).
  const stay=s0.bookings.find(b=>b.status==='In house'&&b.arrival<=D24&&D24<b.departure&&b.lines.some(l=>l.kind==='room'&&l.date===D24));
  const night=stay.lines.find(l=>l.kind==='room'&&l.date===D24);

  test('[REOPEN] Setup: 24 Oct is closed with its record, 25 Oct is open, nothing is reopened',()=>{
    assert.equal(s0.date,D25);assert.ok(s0.closedDates.includes(D24));assert.ok(s0.closings[D24]);assert.equal(M.closingReopened(s0),undefined);
    assert.ok(stay&&night,'a stay with a 24 Oct night');});

  test('[REOPEN] Owner only, with a reason; refused for the open day, a day without a record, a second reopen and a day locked in Accounting',()=>{
    for(const u of ['manager','accountant','cashier','reception'])assert.throws(()=>reopen(s0,u),code('FORBIDDEN'),u);
    assert.throws(()=>reopen(s0,'owner',D24,'   '),says('VALIDATION',/reason is required/));
    assert.throws(()=>reopen(s0,'owner',D25),says('INVALID_TRANSITION',/not closed/));
    assert.throws(()=>reopen(s0,'owner',D23),says('INVALID_TRANSITION',/no closing record/));
    const locked=M.execute(s0,'accountant',{type:'ACC_LOCK',date:D24});
    assert.throws(()=>reopen(locked),says('INVALID_TRANSITION',/locked in Accounting/));
    const s=reopen(M.execute(locked,'owner',{type:'ACC_UNLOCK',date:D24,reason:'Owner reopens 24 Oct'}));
    assert.equal(M.closingReopened(s).date,D24);
    assert.throws(()=>reopen(s),says('INVALID_TRANSITION',/already reopened/));
    assert.match(s.audit[0].detail,/Business day 2026-10-24 reopened/);
    assert.equal(M.closingReopenRefusal(s,D24,false),'Business day 2026-10-24 is already reopened.');
    assert.match(M.closingReopenRefusal({...s,date:'2026-10-26',closedDates:[...s.closedDates,D25],closings:{...s.closings,[D25]:s.closings[D24]}},D25,false),/2026-10-24 is already reopened\. Close it again/);});

  test('[REOPEN] While reopened: today waits, corrections and missed charges post ON 24 Oct, closing again replaces the record and keeps the old one',()=>{
    let s=reopen(s0);const before=s0.closings[D24];
    assert.equal(s.date,D25,'the business date does not move');
    assert.equal(M.closingPreflight(s).find(c=>c.id==='reopened').items.length,1);
    assert.throws(()=>M.execute(s,'manager',{type:'DAY_CLOSE'}),says('INVALID_TRANSITION',/reopened day closed again/));
    // A missed laundry charge dated 24 Oct: ₹300 at the laundry rate (18 %); 23 Oct stays closed, 26 Oct is ahead.
    s=M.execute(s,'reception',{type:'CHARGE',id:stay.id,label:'Laundry missed on the day',amount:30000,gst:'laundry',date:D24});
    const charge=s.bookings.find(b=>b.id===stay.id).lines.at(-1);
    assert.deepEqual([charge.date,charge.amount,charge.taxBps],[D24,30000,1800]);
    assert.throws(()=>M.execute(s,'reception',{type:'CHARGE',id:stay.id,label:'x',amount:100,date:D23}),says('DAY_CLOSED',/2026-10-23 is closed/));
    assert.throws(()=>M.execute(s,'reception',{type:'CHARGE',id:stay.id,label:'x',amount:100,date:D26}),code('VALIDATION'));
    // The correction of the 24 Oct night: pending, it blocks closing 24 Oct again; approved, its reversal posts on 24 Oct.
    s=M.execute(s,'reception',{type:'CORRECTION_REQUEST',id:stay.id,lineId:night.id,reason:'Night belongs to another guest'});
    const apr=s.approvals.at(-1).id;
    assert.deepEqual(M.closingPendingCorrections(s,D24).map(a=>a.id),[apr]);
    assert.throws(()=>M.execute(s,'manager',{type:'DAY_RECLOSE',date:D24}),says('INVALID_TRANSITION',/corrections of 2026-10-24 decided/));
    s=M.execute(s,'owner',{type:'APPROVAL',id:apr,approve:true});
    const rev=s.bookings.find(b=>b.id===stay.id).lines.find(l=>l.reversalOf===night.id);
    assert.deepEqual([rev.date,rev.amount],[D24,-night.amount]);
    // Closed again by the Manager: net revenue = before + ₹300 − the night; the old record is kept unchanged as history.
    s=M.execute(s,'manager',{type:'DAY_RECLOSE',date:D24});
    const after=s.closings[D24];
    assert.equal(after.revenue.net,before.revenue.net+30000-night.amount);
    assert.equal(after.closedBy,'manager');
    assert.equal(after.replaced.length,1);
    assert.deepEqual(after.replaced[0],before,'the replaced record is the first one, unchanged');
    assert.deepEqual([after.cash,after.deposits.heldAtClose,after.nightAudit],[before.cash,before.deposits.heldAtClose,before.nightAudit],'point-in-time values of the first close stay');
    assert.ok(after.exceptions.some(e=>/^Reopened .* \(DEC-077\): Room night posted to the wrong folio · closed again/.test(e)),after.exceptions.join(' | '));
    assert.ok(after.exceptions.some(e=>e.startsWith('Reversal ')));
    assert.equal(after.reopens[0].reclosedBy,'manager');assert.equal(M.closingReopened(s),undefined);
    assert.equal(s.date,D25);
    assert.equal(M.closingPreflight(s).find(c=>c.id==='reopened').items.length,0);
    // Closed again = adjustment-only again: a charge on 24 Oct is refused, a later reversal of a 24 Oct line posts on 25 Oct.
    assert.throws(()=>M.execute(s,'reception',{type:'CHARGE',id:stay.id,label:'x',amount:100,date:D24}),code('DAY_CLOSED'));
    s=M.execute(s,'reception',{type:'CORRECTION_REQUEST',id:stay.id,lineId:charge.id,reason:'Not used'});
    s=M.execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    assert.equal(s.bookings.find(b=>b.id===stay.id).lines.find(l=>l.reversalOf===charge.id).date,D25);
    // A second reopen and re-close keeps both earlier records.
    s=M.execute(reopen(s,'owner',D24,'Second look'),'owner',{type:'DAY_RECLOSE',date:D24});
    assert.equal(s.closings[D24].replaced.length,2);assert.equal(s.closings[D24].reopens.length,2);});

  test('[REOPEN] Closing again needs the close permission; only the reopened date can be closed again',()=>{
    const s=reopen(s0);
    for(const u of ['accountant','cashier','reception'])assert.throws(()=>M.execute(s,u,{type:'DAY_RECLOSE',date:D24}),code('FORBIDDEN'),u);
    assert.throws(()=>M.execute(s,'manager',{type:'DAY_RECLOSE',date:D23}),says('INVALID_TRANSITION',/not reopened/));
    assert.throws(()=>M.execute(s0,'manager',{type:'DAY_RECLOSE',date:D24}),says('INVALID_TRANSITION',/not reopened/));});

  test('[REOPEN] Invoices never change: the stay checked out (invoiced) on 25 Oct takes no charge on the reopened day; a stay arriving later is outside it',()=>{
    // A one-night stay 25 → 26 Oct: checked in, its night posted, paid by UPI and checked out on 25 Oct (invoice issued at checkout).
    let s=M.execute(s0,'owner',{type:'BOOK',guest:'Invoiced Guest (fictional)',room:'105',arrival:D25,departure:D26,people:1});const oid=s.bookings.at(-1).id;
    s=M.execute(s,'owner',{type:'REGISTER',id:oid});s=M.execute(s,'owner',{type:'CHECKIN',id:oid});s=M.execute(s,'owner',{type:'NIGHT_POST'});
    const out=s.bookings.find(b=>b.id===oid),t=M.totals(out);
    if(t.deposit)s=M.execute(s,'cashier',{type:'PAYMENT',id:out.id,amount:t.deposit,method:'Cash',kind:'deposit-return'});
    const bal=M.totals(s.bookings.find(b=>b.id===out.id)).balance;
    if(bal>0)s=M.execute(s,'cashier',{type:'PAYMENT',id:out.id,amount:bal,method:'UPI',kind:'payment',reference:'DEMO-UPI-OUT'});
    s=M.execute(s,'owner',{type:'CHECKOUT',id:out.id});
    const invoiced=s.bookings.find(b=>b.id===out.id);assert.equal(invoiced.status,'Departed');assert.ok(invoiced.invoiceNo);
    s=reopen(s);
    assert.throws(()=>M.execute(s,'reception',{type:'CHARGE',id:out.id,label:'x',amount:100,date:D24}),says('INVALID_TRANSITION',/no longer open/));
    s=M.execute(s,'manager',{type:'DAY_RECLOSE',date:D24});
    const after=s.bookings.find(b=>b.id===out.id);
    assert.deepEqual([after.invoiceNo,M.totals(after)],[invoiced.invoiceNo,M.totals(invoiced)],'invoice number and totals unchanged');
    let x=M.execute(s0,'owner',{type:'BOOK',guest:'Later Guest (fictional)',room:'106',arrival:D25,departure:D26,people:1});const id=x.bookings.at(-1).id;
    x=M.execute(x,'owner',{type:'REGISTER',id});x=M.execute(reopen(M.execute(x,'owner',{type:'CHECKIN',id})),'owner',{type:'CHARGE',id:stay.id,label:'ok',amount:100,date:D24});
    assert.throws(()=>M.execute(x,'reception',{type:'CHARGE',id,label:'x',amount:100,date:D24}),says('INVALID_TRANSITION',/outside the stay/));});

  test('[REOPEN] The cash-book lock (DEC-065) stays: reopen says so, a charge touching the day waits for the Owner to reopen the cash book',()=>{
    const locked=M.execute(s0,'accountant',{type:'CB_LOCK',date:D24});
    let s=reopen(locked);
    assert.equal(s.closings[D24].reopens[0].cashBookLocked,true);
    assert.match(s.audit[0].detail,/The cash book of 2026-10-24 stays locked \(DEC-065\): reopening the day does not unlock it/);
    assert.equal(M.cbIsLocked(s,D24),true);
    assert.throws(()=>M.execute(s,'reception',{type:'CHARGE',id:stay.id,label:'Laundry',amount:30000,date:D24}),says('DAY_CLOSED',/Cash book 2026-10-24 is locked/));
    s=M.execute(s,'owner',{type:'CB_REOPEN',date:D24,reason:'Charge on the reopened day'});
    assert.equal(M.execute(s,'reception',{type:'CHARGE',id:stay.id,label:'Laundry',amount:30000,date:D24}).bookings.find(b=>b.id===stay.id).lines.at(-1).date,D24);
    assert.match(reopen(s0).audit[0].detail,/does not change cash-book locks/);});

  test('[REOPEN] Accounting: the reopened day is Open (not lockable) until closed again',()=>{
    const s=reopen(s0);
    assert.equal(M.accDayStatus(s0,D24),'Closed');assert.equal(M.accDayStatus(s,D24),'Open');
    assert.throws(()=>M.execute(s,'accountant',{type:'ACC_LOCK',date:D24}),says('INVALID_TRANSITION',/Close the business day/));
    assert.equal(M.accDayStatus(M.execute(s,'manager',{type:'DAY_RECLOSE',date:D24}),D24),'Closed');});

  test('[REOPEN] Tally: an exported day that is reopened is flagged and carried again (provisional while reopened) until an export after it is closed again',()=>{
    assert.throws(()=>M.execute(s0,'manager',{type:'ACC_TALLY_EXPORT',from:D24,to:D24}),code('FORBIDDEN'));
    let s=M.execute(s0,'accountant',{type:'ACC_TALLY_EXPORT',from:D24,to:D24});
    assert.deepEqual(s.accountingX.tallyExports.map(e=>e.dates),[[D24]]);
    // A day never exported is not flagged.
    assert.equal(reopen(s0).closings[D24].reopens[0].tallyReexport,false);
    s=reopen(s);
    assert.equal(s.closings[D24].reopens[0].tallyReexport,true);assert.ok(s.accountingX.reexport[D24]);
    assert.match(s.audit[0].detail,/exported to Tally: its vouchers are marked changed/);
    let p=M.accTallyXml(s,D25,D25);
    assert.deepEqual([p.reexport,p.provisional],[[D24],[D24]]);
    assert.match(p.xml,/RE-EXPORT · 2026-10-24 changed after it was exported \(day reopened: Room night posted to the wrong folio\)/);
    assert.match(p.xml,/PROVISIONAL · 2026-10-24/);assert.match(p.xml,/<DATE>20261024<\/DATE>/);
    s=M.execute(s,'owner',{type:'ACC_TALLY_EXPORT',from:D25,to:D25});
    assert.ok(s.accountingX.reexport[D24],'still reopened: the flag stays');
    s=M.execute(s,'manager',{type:'DAY_RECLOSE',date:D24});
    p=M.accTallyXml(s,D25,D25);assert.deepEqual([p.reexport,p.provisional],[[D24],[]]);
    s=M.execute(s,'accountant',{type:'ACC_TALLY_EXPORT',from:D25,to:D25});
    assert.equal(s.accountingX.reexport[D24],undefined);
    assert.deepEqual(s.accountingX.tallyExports.at(-1).reexport,[D24]);
    p=M.accTallyXml(s,D25,D25);assert.deepEqual(p.reexport,[]);assert.doesNotMatch(p.xml,/20261024/);
    assert.equal(M.accTallyNote('a -- b <c>'),'<!-- a - - b &lt;c&gt; -->');});

  // Screens: the Owner's reopen form, the reopened-day panel (desktop and phone) and the earlier records list.
  const dir=(process.env.HCC_SYNC_TEST_DIR||'tests/.generated');
  await (await import('node:fs/promises')).writeFile(dir+'/reopen-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {ReopenDay} from '../../src/features/closing/ReopenDay';
import {DayClosing} from '../../src/features/closing/DayClosing';
import {MaClosing} from '../../src/mobile/admin/Closing';
export function render(s:any,uid:string,what:'panel'|'desktop'|'phone',phone=false){
  const user=s.users.find((u:any)=>u.id===uid);const noop=()=>{};
  const ctx:any={s,uid,user,m:metrics(s),page:'closing',go:noop,open:noop,booking:noop,busy:false,run:async()=>false,toast:noop,permission:(p:any)=>can(s,uid,p),
    display:{isMobile:phone,mode:phone?'mobile':'desktop',setMode:noop}};
  const el=what==='panel'?<ReopenDay phone={phone}/>:what==='desktop'?<DayClosing/>:<MaClosing/>;
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{el}</HccContext.Provider>);
}`);
  await build({entryPoints:[dir+'/reopen-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:dir+'/reopen-render.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __rq} from 'node:module';const require=__rq(import.meta.url);"}});
  const R=await import(new URL('../'+dir+'/reopen-render.mjs',import.meta.url).href);
  test('[REOPEN] Screens: the Owner sees the reopen form (others do not); a reopened day shows its panel with Close again on desktop and phone',()=>{
    assert.match(R.render(s0,'owner','panel'),/Reopen a closed day/);
    assert.equal(R.render(s0,'manager','panel'),'');
    const s=reopen(M.execute(s0,'accountant',{type:'CB_LOCK',date:D24}));
    for(const phone of [false,true]){const h=R.render(s,'manager','panel',phone);
      assert.match(h,/is reopened/);assert.match(h,/Close 24 Oct again/);assert.match(h,/stays locked/);assert.match(h,/Post a missed charge on 24 Oct/);
      if(phone)assert.match(h,/ma-card ma-reopen/);}
    const desk=R.render(s,'manager','desktop'),phone=R.render(s,'manager','phone',true);
    for(const h of [desk,phone]){assert.match(h,/is reopened/);assert.match(h,/Reopened day closed again|reopened by the Owner/);}
    const done=M.execute(s,'manager',{type:'DAY_RECLOSE',date:D24});
    assert.match(R.render(done,'owner','desktop'),/Closed again/);assert.match(R.render(done,'owner','phone',true),/closed again/);});
}
