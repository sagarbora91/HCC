// F6-H3 · Hall booking UI state (features/hall/logic.ts, access.ts), navigation, roles, DEC-061 (no prices for housekeeping / maintenance), print = screen,
// room block (registerRoomHold), hall cash in the drawer, credit note. Names prefixed [HUI].
import {build} from 'esbuild';
import {writeFile,readFile} from 'node:fs/promises';
import {hallFixtures} from './_hall.mjs';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/hui-entry.ts',"export * from '../../src/features/hall/logic';\nexport * from '../../src/features/hall/access';\nexport {PAGE_CATEGORY,PAGE_BLURB,PAGE_KEYWORDS,navRoleOk} from '../../src/app/navmap';\nexport {SUB_SCREENS as SUBSCREENS,subScreenOpen} from '../../src/app/subscreens';");
  await build({entryPoints:['tests/.generated/hui-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/hui.mjs',logLevel:'error',jsx:'automatic',loader:{'.tsx':'tsx'}});
  const X=await import('./.generated/hui.mjs');
  const {seed,execute}=M;const {quote,at,ledger,goldenLines}=hallFixtures(M);
  const accepted=(s0=seed())=>{let [s,qid]=quote(s0);s=execute(s,'manager',{type:'QUO_ACCEPT',id:qid,version:1,advance:{amountPaise:2000000,method:'Cash',reference:''},key:'hui-acc'});return [s,qid,s.hallX.events.at(-1)];};

  test('[HUI] navigation: hall-booking in REVENUE & SALES with blurb and keywords; every staff role but the Tech admin may open it',()=>{
    assert.equal(X.PAGE_CATEGORY['hall-booking'],'revenue');assert.ok(X.PAGE_BLURB['hall-booking']);assert.ok(X.PAGE_KEYWORDS['hall-booking'].includes('wedding'));
    const s=seed();for(const u of ['owner','manager','reception','accountant','cashier','supervisor','housekeeper','maint'])assert.equal(X.navRoleOk(s,u,'hall-booking'),true,u);
    assert.equal(X.navRoleOk(s,'techadmin','hall-booking'),false);
  });
  const reg=await readFile('src/features/hall/register.tsx','utf8');
  test('[HUI] registration: page, phone page, six modals, nav item under REVENUE & SALES',()=>{
    assert.match(reg,/pages:\{'hall-booking':HallPage\}/);assert.match(reg,/mobile:\{pages:\{'hall-booking':HallMobile\}\}/);assert.match(reg,/group:'REVENUE & SALES'/);
    for(const m of ['hall-enquiry','hall-quote','hall-event','hall-print','hall-venue','hall-menu'])assert.ok(reg.includes("'"+m+"':"),m);
  });
  test('[HUI] sub-screens: the six tabs are listed; the palette hides Enquiries / Quotations / Menus / Templates from roles without them',()=>{
    assert.deepEqual(X.SUBSCREENS.filter(x=>x.page==='hall-booking').map(x=>x.id),['enquiries','quotations','calendar','events','menus','templates']);
    const s=seed();assert.equal(X.subScreenOpen(s,'owner','hall-booking','templates'),true);assert.equal(X.subScreenOpen(s,'housekeeper','hall-booking','quotations'),false);
    assert.equal(X.subScreenOpen(s,'housekeeper','hall-booking','calendar'),true);assert.equal(X.subScreenOpen(s,'accountant','hall-booking','enquiries'),false);
  });
  test('[HUI] tabs and buttons by role (spec §16): Owner all; Reception no template edit; Cashier advance + settle only; HK / maintenance / supervisor calendar + events without prices',()=>{
    const s=seed(),u=id=>X.hallUi(s,id);
    assert.deepEqual(X.hallTabsFor(s,'owner'),['enquiries','quotations','calendar','events','menus','templates']);
    assert.equal(u('owner').tnc,true);assert.equal(u('reception').tnc,false);assert.equal(u('reception').enquiry,true);assert.equal(u('reception').venues,false);assert.equal(u('manager').menus,true);assert.equal(u('reception').menus,false);
    assert.equal(u('cashier').advance,true);assert.equal(u('cashier').quote,false);assert.equal(u('cashier').enquiry,false);assert.equal(u('cashier').bill,true);
    assert.equal(u('accountant').quote,false);assert.equal(u('accountant').seesMoney,true);assert.equal(u('accountant').enquiry,false);
    for(const id of ['housekeeper','maint','supervisor']){assert.equal(u(id).seesMoney,false,id);assert.equal(u(id).bill,false);assert.deepEqual(X.hallTabsFor(s,id),['calendar','events']);}
    assert.equal(u('techadmin').view,false);
  });
  test('[HUI] enquiry board: follow-ups today first; columns by status; clash flagged on the card data',()=>{
    let s=seed();s=execute(s,'reception',{type:'HALL_ENQUIRY_ADD',name:'Asha (fictional)',phone:'9000000011',eventType:'BIRTHDAY',dates:['2026-12-10'],slot:'EVENING',pax:60,source:'PHONE',followUp:'2026-10-29'});
    s=execute(s,'reception',{type:'HALL_ENQUIRY_ADD',name:'Bhaskar (fictional)',phone:'9000000012',eventType:'WEDDING',dates:['2026-12-12'],slot:'EVENING',pax:200,source:'WALKIN',followUp:s.date});
    const b=X.hallBoard(s);assert.equal(b.columns.length,4);assert.equal(b.columns[0].items.length,2);assert.equal(b.columns[0].items[0].name,'Bhaskar (fictional)','today first');assert.equal(b.followUpsToday.length,1);
    assert.equal(b.isDue(b.columns[0].items[0]),true);assert.equal(X.hallAlerts(s).followUps,1);
  });
  test('[HUI] quotation draft: the hall line follows the venue × slot rate, menu plates follow guaranteed pax, discount limit per role',()=>{
    let s=seed();s=execute(s,'reception',{type:'HALL_ENQUIRY_ADD',name:'Chitra (fictional)',phone:'9000000013',eventType:'RECEPTION',dates:['2026-12-20'],slot:'EVENING',pax:120,venueId:'V-SAGAR',source:'REFERENCE',followUp:s.date});
    const enq=s.hallX.enquiries.at(-1);let d=X.hallDraftFromEnquiry(s,enq);
    assert.equal(d.lines[0].kind,'HALL');assert.equal(d.lines[0].ratePaise,1500000);assert.equal(d.header.venueId,'V-SAGAR');
    d=X.hallAddMenu(s,d,'MP-SILVER-VEG');assert.equal(d.lines.find(l=>l.kind==='MENU').qty,120);
    d=X.hallSyncLines(s,{...d,header:{...d.header,guaranteedPax:150}});assert.equal(d.lines.find(l=>l.kind==='MENU').qty,150);
    d=X.hallAddExtra(d,'Stage decor',1,1000000);assert.equal(d.lines.length,3);
    const t=X.hallQuoteTotals(s,d.lines);assert.equal(t.totalPaise,t.taxablePaise+t.cgstPaise+t.sgstPaise);
    const rate=d.lines[0].id;let e=X.hallSetRate(d,rate,1425000);
    assert.equal(X.hallDiscountState(s,'reception',e.lines).within,false,'Reception 0 %');assert.equal(X.hallDiscountState(s,'manager',e.lines).within,true,'Manager ≤ 10 %');
    const plan=X.hallDraftPlan(s,d);assert.equal(plan.length,3);assert.equal(plan.reduce((a,r)=>a+r.amountPaise,0),t.totalPaise,'balance row takes the rounding');
    d=X.hallSyncLines(s,{...e,header:{...e.header,slot:'EVENING'}});assert.equal(d.lines[0].ratePaise,1425000,'a discount survives a header change');
  });
  test('[HUI] calendar: accepted event shows Confirmed in the day grid, combined hall shows what blocks it, month cell counts, availability shows the clash',()=>{
    const [s,,e]=accepted();const g=X.hallDayGrid(s,e.date),row=id=>g.find(r=>r.venue.id===id);
    assert.equal(row('V-SAGAR').slots.find(c=>c.slot==='EVENING').state,'CONFIRMED');assert.equal(row('V-SAGAR').slots.find(c=>c.slot==='MORNING').state,'FREE');
    const comb=row('V-SAGAR-LAWN').slots.find(c=>c.slot==='EVENING');assert.equal(comb.state,'CONFIRMED');assert.equal(comb.via,'Sagar Hall');
    assert.equal(row('V-MOTI').slots.find(c=>c.slot==='EVENING').state,'FREE');
    const cell=X.hallMonthCells(s,e.date.slice(0,7)).find(c=>c.date===e.date);assert.ok(cell.count>=1);assert.equal(cell.inMonth,true);
    const av=X.hallAvailability(s,'V-LAWN',[e.date],'EVENING');assert.equal(av.free,false,'the lawn is part of the combined hall? no — only Sagar Hall is booked');
  });
  test('[HUI] calendar rows carry no money (DEC-061) and an enquiry soft hold shows as Enquiry hold',()=>{
    let s=seed();s=execute(s,'reception',{type:'HALL_ENQUIRY_ADD',name:'Deepa (fictional)',phone:'9000000014',eventType:'BIRTHDAY',dates:['2026-12-22'],slot:'MORNING',pax:50,venueId:'V-MOTI',source:'PHONE',followUp:s.date});
    s=execute(s,'manager',{type:'HALL_ENQUIRY_SOFT_HOLD',id:s.hallX.enquiries.at(-1).id,venueId:'V-MOTI',days:2});
    const g=X.hallDayGrid(s,'2026-12-22');assert.equal(g.find(r=>r.venue.id==='V-MOTI').slots[0].state,'HOLD');
    assert.ok(!/Paise|rate/i.test(JSON.stringify(g)),'no money fields in the day grid');
  });
  test('[HUI] print = screen: quotation document totals equal the editor totals; BEO has no prices for HK and the same bill for the desk',()=>{
    const [s0,qid]=quote(seed());const q=s0.hallX.quotations.find(o=>o.id===qid),v=q.versions[0];
    const doc=X.hallQuoteDoc(s0,q,v),screen=X.hallQuoteTotals(s0,v.lines,s0.date);
    assert.deepEqual([doc.totals.taxablePaise,doc.totals.cgstPaise,doc.totals.sgstPaise,doc.totals.totalPaise],[screen.taxablePaise,screen.cgstPaise,screen.sgstPaise,screen.totalPaise]);
    assert.equal(doc.lines.reduce((a,l)=>a+l.taxablePaise+l.cgstPaise+l.sgstPaise,0),doc.totals.totalPaise,'lines add to the grand total');
    assert.equal(doc.advance.reduce((a,r)=>a+r.amountPaise,0),doc.totals.totalPaise);
    const [s,,e]=accepted();const kitchen=X.hallBeo(s,e,false),desk=X.hallBeo(s,e,true);
    assert.equal(kitchen.prices,undefined);assert.ok(!/Paise|rate|₹/i.test(JSON.stringify(kitchen)));assert.equal(kitchen.guaranteedPax,150);assert.ok(kitchen.menu.length>=1);
    const pv=X.hallBillPreview(s,e,e.guaranteedPax,[]);assert.equal(desk.prices.totals.totalPaise,pv.totals.totalPaise);assert.equal(desk.prices.paid,2000000);
  });
  test('[HUI] invoice document = the posted invoice; receipt document carries no GST wording',()=>{
    let [s,,e]=accepted();s=at(s,e.date);s=execute(s,'reception',{type:'HALL_EVENT_CLOSE',eventId:e.id,actualPax:160,settle:{mode:'COMPANY',company:'CO-1'}});
    const ev=s.hallX.events.find(o=>o.id===e.id),m=X.hallInvoiceDoc(s,ev);assert.equal(m.totalPaise,ev.invoice.totalPaise);assert.equal(m.lines.reduce((a,l)=>a+l.taxablePaise+l.cgstPaise+l.sgstPaise,0),m.totalPaise);
    assert.match(m.settle,/Company credit/);const r=X.hallReceiptDoc(s,ev,ev.receipts[0]);assert.equal(r.kind,'ADVANCE');assert.equal(r.amountPaise,2000000);
    const rows=X.hallEventRows(s,'billed');assert.equal(rows.length,1);assert.equal(rows[0].totalPaise,m.totalPaise);
  });
  test('[HUI] hall cash in the drawer (was not done in H1): a cash advance adds, a UPI advance does not, a cash refund subtracts',()=>{
    let [s0,qid]=quote(seed(),{dates:['2027-01-20']});const base=M.drawer(s0);
    let s=execute(s0,'manager',{type:'QUO_ACCEPT',id:qid,version:1,advance:{amountPaise:2000000,method:'UPI',reference:'UPI-1'},key:'k1'});assert.equal(M.drawer(s),base);
    const e=s.hallX.events.at(-1);s=execute(s,'cashier',{type:'HALL_ADVANCE_RECEIVE',eventId:e.id,amountPaise:500000,method:'Cash'});assert.equal(M.drawer(s),base+500000,'cashier takes a cash advance');
    s=execute(s,'reception',{type:'HALL_EVENT_CANCEL',id:e.id,reason:'Customer changed plans'});const pend=s.hallX.events[0].cancellation.refundPaise;
    s=execute(s,'owner',{type:'HALL_REFUND',id:e.id,method:'Cash'});assert.equal(M.drawer(s),base+500000-pend);
  });
  test('[HUI] room block on acceptance: HALL_ROOM_BLOCK holds rooms like a group block; release and cancel give them back',()=>{
    let [s,,e]=accepted();const from=e.date,to='2026-12-06';
    const free=t=>s.rooms.filter(r=>r.type===t&&M.available(s,r.id,from,to)).length;const before=free('Deluxe');
    s=execute(s,'reception',{type:'HALL_ROOM_BLOCK',eventId:e.id,allot:{Deluxe:2}});const ev=s.hallX.events.find(o=>o.id===e.id);assert.ok(ev.blockId);
    assert.equal(free('Deluxe'),before-2,'two Deluxe rooms held');assert.throws(()=>execute(s,'reception',{type:'HALL_ROOM_BLOCK',eventId:e.id,allot:{Deluxe:1}}),/already has a room block/);
    const s2=execute(s,'reception',{type:'HALL_ROOM_BLOCK',eventId:e.id,release:true});assert.equal(free2(s2),before);function free2(z){return z.rooms.filter(r=>r.type==='Deluxe'&&M.available(z,r.id,from,to)).length;}
    const s3=execute(s,'reception',{type:'HALL_EVENT_CANCEL',id:e.id,reason:'Plans changed'});assert.equal(free2(s3),before,'cancelling the event releases the rooms');
    assert.throws(()=>execute(s,'cashier',{type:'HALL_ROOM_BLOCK',eventId:e.id,release:true}),/Access denied/);
  });
  test('[HUI] credit note: Owner only, whole invoice, CRN series; the voucher reverses the invoice and balances; once only; refused before billing',()=>{
    let [s,,e]=accepted();assert.throws(()=>execute(s,'owner',{type:'HALL_CREDIT_NOTE',id:e.id,reason:'Test'}),/billed event/);
    s=at(s,e.date);s=execute(s,'reception',{type:'HALL_EVENT_CLOSE',eventId:e.id,actualPax:150,settle:{mode:'COMPANY',company:'CO-1'}});
    assert.throws(()=>execute(s,'manager',{type:'HALL_CREDIT_NOTE',id:e.id,reason:'Wrong plates'}),/Access denied/);
    assert.throws(()=>execute(s,'owner',{type:'HALL_CREDIT_NOTE',id:e.id,reason:'x'}),/reason/);
    s=execute(s,'owner',{type:'HALL_CREDIT_NOTE',id:e.id,reason:'Wrong plates billed'});const ev=s.hallX.events.find(o=>o.id===e.id);
    assert.ok(ev.creditNote.no);assert.equal(ev.creditNote.totalPaise,ev.invoice.totalPaise);assert.throws(()=>execute(s,'owner',{type:'HALL_CREDIT_NOTE',id:e.id,reason:'Again'}),/already has a credit note/);
    const L=ledger(s,[e.date]);assert.equal(L['4400'],0,'revenue reversed');assert.equal(L['2110'],0);assert.equal(L['2120'],0);
    const cn=M.accLiveVouchers(s,e.date).find(v=>v.source.sub==='hall-credit-note');assert.ok(cn);const t=M.accVoucherTotals(cn);assert.equal(t.debit,t.credit);assert.deepEqual(cn.unmapped,[]);
  });
  test('[HUI] no hall command works for housekeeping / maintenance (DEC-061) and the Accountant only views',()=>{
    const [s,,e]=accepted();
    for(const u of ['housekeeper','maint','supervisor'])for(const cmd of [{type:'HALL_ADVANCE_RECEIVE',eventId:e.id,amountPaise:100,method:'Cash'},{type:'HALL_ENQUIRY_ADD',name:'X Y',phone:'9000000099',eventType:'OTHER',dates:[s.date],slot:'EVENING',pax:10,source:'PHONE',followUp:s.date},{type:'HALL_ROOM_BLOCK',eventId:e.id,release:true}])
      assert.throws(()=>execute(s,u,cmd),/Access denied/,u+' '+cmd.type);
    assert.throws(()=>execute(s,'accountant',{type:'HALL_ADVANCE_RECEIVE',eventId:e.id,amountPaise:100,method:'Cash'}),/Access denied/);
  });
}
