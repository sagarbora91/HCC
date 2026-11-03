// F6 H1 · hall booking domain (contract §2–§4, §8; spec §4, §5, §10, §11). Names prefixed [HALL].
import {hallFixtures} from './_hall.mjs';
export function register({test,M,assert}){
  const {quote,at}=hallFixtures(M);
  const enq=(s,o={})=>M.execute(s,'reception',{type:'HALL_ENQUIRY_ADD',name:'Deshmukh (fictional)',phone:'9000000456',eventType:'BIRTHDAY',dates:['2026-11-20'],slot:'EVENING',pax:80,source:'WALKIN',followUp:'2026-10-26',...o});
  test('[HALL] seed: 2 halls + 1 lawn (+ the combined Sagar Hall + Lawn), 3 menus on POS items, 6 lost reasons, A26 settings; absent slice = seed, BASE untouched',()=>{
    const s=M.seed(),x=M.hallSlice(s);assert.equal(s.hallX,undefined);
    assert.deepEqual(x.venues.map(v=>[v.id,v.kind,!!v.combinedOf]),[['V-SAGAR','HALL',false],['V-MOTI','HALL',false],['V-LAWN','LAWN',false],['V-SAGAR-LAWN','HALL',true]]);
    assert.equal(x.lostReasons.length,6);assert.equal(x.lostReasons.find(r=>r.id==='LR-OTHER').needsNote,true);
    assert.deepEqual([x.settings.validityDays,x.settings.advanceDefault,x.settings.secondDueDays,x.settings.discountLimitBps.MANAGER,x.settings.refundManagerLimitPaise,x.settings.enquiryAutoLostDays],[15,[2500,5000,'BALANCE'],30,1000,5000000,30]);
    const menu=new Set(M.posSlice(s).menu.map(m=>m.id));for(const m of x.menus)assert.ok(m.posItemIds.every(i=>menu.has(i)),m.name);
    assert.equal(M.hallCapacity(x,'V-SAGAR-LAWN','ROUND'),450,'combined = sum of the parts');
  });
  test('[HALL] halls master: Owner only, versioned, combined hall must join existing halls; menus Owner or Manager with POS item ids that exist',()=>{
    let s=M.seed();const v={name:'Terrace Deck',kind:'TERRACE',capacity:{FLOATING:60},slots:[{slot:'EVENING',from:'18:00',to:'23:00',ratePaise:600000}]};
    for(const u of ['manager','reception','housekeeper'])assert.throws(()=>M.execute(s,u,{type:'HALL_VENUE_UPSERT',venue:v}),/Access denied/);
    s=M.execute(s,'owner',{type:'HALL_VENUE_UPSERT',venue:v});const t=s.hallX.venues.at(-1);assert.equal(t.version,1);
    s=M.execute(s,'owner',{type:'HALL_VENUE_UPSERT',venue:{...t,slots:[{slot:'EVENING',from:'18:00',to:'23:00',ratePaise:700000}]},version:1});assert.equal(s.hallX.venues.at(-1).version,2);
    assert.throws(()=>M.execute(s,'owner',{type:'HALL_VENUE_UPSERT',venue:{...t},version:1}),e=>e.code==='STALE_VERSION');
    assert.throws(()=>M.execute(s,'owner',{type:'HALL_VENUE_UPSERT',venue:{...v,name:'X combo',combinedOf:['V-SAGAR','V-NOPE']}}),/combined/);
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_MENU_UPSERT',menu:{name:'Snacks',perPlatePaise:20000,posItemIds:['m-tea']}}),/Access denied/);
    assert.throws(()=>M.execute(s,'manager',{type:'HALL_MENU_UPSERT',menu:{name:'Snacks',perPlatePaise:20000,posItemIds:['m-tea','m-nope']}}),/POS menu master: m-nope/);
    s=M.execute(s,'manager',{type:'HALL_MENU_UPSERT',menu:{name:'Snacks',perPlatePaise:20000,posItemIds:['m-tea','m-poha'],veg:true}});assert.equal(s.hallX.menus.at(-1).name,'Snacks');
  });
  test('[HALL] enquiry: Open → Quoted → Lost needs a listed reason (Other needs a note); reopen; Cashier / HK cannot add',()=>{
    let s=enq(M.seed());const id=s.hallX.enquiries.at(-1).id;assert.equal(s.hallX.enquiries.at(-1).status,'OPEN');
    for(const u of ['cashier','housekeeper','accountant'])assert.throws(()=>M.execute(M.seed(),u,{type:'HALL_ENQUIRY_ADD',name:'X'}),/Access denied/);
    assert.throws(()=>enq(M.seed(),{dates:['2026-10-01']}),/from today/);assert.throws(()=>enq(M.seed(),{phone:'12'}),/phone/);
    s=M.execute(s,'reception',{type:'HALL_ENQUIRY_UPDATE',id,status:'QUOTED',followUp:'2026-10-28'});assert.equal(s.hallX.enquiries.at(-1).status,'QUOTED');
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_ENQUIRY_LOST',id}),/lost reason/);
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_ENQUIRY_LOST',id,reasonId:'LR-OTHER'}),/needs a note/);
    s=M.execute(s,'reception',{type:'HALL_ENQUIRY_LOST',id,reasonId:'LR-PRICE'});assert.deepEqual([s.hallX.enquiries.at(-1).status,s.hallX.enquiries.at(-1).lostReasonId],['LOST','LR-PRICE']);
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_ENQUIRY_UPDATE',id,pax:90}),/read-only/);
    s=M.execute(s,'reception',{type:'HALL_ENQUIRY_UPDATE',id,status:'OPEN'});assert.equal(s.hallX.enquiries.at(-1).lostReasonId,undefined);
  });
  test('[HALL] enquiry auto-lost 30 days after the follow-up (night run), warned 3 days earlier; follow-ups today listed',()=>{
    let s=enq(M.seed(),{followUp:'2026-10-24'});s=enq(s,{followUp:'2026-11-10',name:'Later (fictional)'});
    assert.equal(M.hallEnquiriesDue(s).today.length,1);
    let t=at(s,'2026-11-21');assert.equal(M.hallEnquiriesDue(t).warn.length,1);assert.equal(M.hallEnquiriesDue(t).lost.length,0);
    t=at(s,'2026-11-23');assert.equal(M.hallEnquiriesDue(t).lost.length,1);
    assert.throws(()=>M.execute(t,'reception',{type:'HALL_NIGHT_RUN'}),/Access denied/);
    t=M.execute(t,'manager',{type:'HALL_NIGHT_RUN'});const [a,b]=t.hallX.enquiries;
    assert.deepEqual([a.status,a.lostReasonId,b.status],['LOST','LR-NORESPONSE','OPEN']);
  });
  test('[HALL] soft hold: Manager only, 1–3 days, warns on another soft hold, refused on a hard booking; shows on the calendar without money',()=>{
    let s=enq(M.seed());s=enq(s,{name:'Second (fictional)'});const [a,b]=s.hallX.enquiries.map(e=>e.id);
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_ENQUIRY_SOFT_HOLD',id:a,venueId:'V-MOTI'}),/Access denied/);
    assert.throws(()=>M.execute(s,'manager',{type:'HALL_ENQUIRY_SOFT_HOLD',id:a,venueId:'V-MOTI',days:4}),/1–3 days/);
    s=M.execute(s,'manager',{type:'HALL_ENQUIRY_SOFT_HOLD',id:a,venueId:'V-MOTI',days:3});
    const r=M.execute(s,'manager',{type:'HALL_ENQUIRY_SOFT_HOLD',id:b,venueId:'V-MOTI'});assert.match(r.audit[0].detail,/also soft-held/);
    const cal=M.hallCalendar(s,'2026-11-01','2026-11-30');assert.deepEqual(cal.map(c=>[c.date,c.venueId,c.kind,c.status]),[['2026-11-20','V-MOTI','SOFT_HOLD','Enquiry hold']]);
    assert.ok(!JSON.stringify(cal).includes('Paise'));
  });
  test('[HALL] accept: event created, hall hard-held, enquiry Won; a second booking of the same hall / slot is refused HCC_HALL_CLASH (also via the combined hall); other slot OK',()=>{
    let [s,q1,e1]=quote(M.seed());s=M.execute(s,'reception',{type:'QUO_ACCEPT',id:q1});
    const ev=s.hallX.events.at(-1);assert.deepEqual([ev.status,ev.venueIds,ev.date,ev.slot,ev.guaranteedPax],['CONFIRMED',['V-SAGAR'],'2026-12-05','EVENING',150]);
    assert.equal(s.hallX.enquiries.find(e=>e.id===e1).status,'WON');assert.equal(s.hallX.quotations.find(q=>q.id===q1).versions[0].status,'ACCEPTED');
    let [t,q2]=quote(s);assert.throws(()=>M.execute(t,'reception',{type:'QUO_ACCEPT',id:q2}),e=>e.code==='HCC_HALL_CLASH');
    [t,q2]=quote(s,{venueId:'V-SAGAR-LAWN'});assert.throws(()=>M.execute(t,'reception',{type:'QUO_ACCEPT',id:q2}),e=>e.code==='HCC_HALL_CLASH','combined blocks its parts');
    [t,q2]=quote(s,{slot:'FULL_DAY'});assert.throws(()=>M.execute(t,'reception',{type:'QUO_ACCEPT',id:q2}),e=>e.code==='HCC_HALL_CLASH','full day overlaps evening');
    [t,q2]=quote(s,{slot:'MORNING'});t=M.execute(t,'reception',{type:'QUO_ACCEPT',id:q2});assert.equal(t.hallX.events.length,2);
    let [u,q3]=quote(M.seed(),{venueId:'V-SAGAR-LAWN',pax:300});u=M.execute(u,'manager',{type:'QUO_ACCEPT',id:q3});
    let [w,q4]=quote(u,{venueId:'V-LAWN',pax:200});assert.throws(()=>M.execute(w,'reception',{type:'QUO_ACCEPT',id:q4}),e=>e.code==='HCC_HALL_CLASH','a part is blocked by the combined booking');
    [w,q4]=quote(u,{venueId:'V-MOTI',pax:50});w=M.execute(w,'reception',{type:'QUO_ACCEPT',id:q4});assert.equal(w.hallX.events.length,2,'Moti Hall is free');
  });
  test('[HALL] accept guards: only the latest SENT version; expired → HCC_QUO_EXPIRED unless a Manager gives a reason; capacity → HCC_HALL_CAPACITY (Manager override)',()=>{
    const v=(n,status)=>({v:n,status,header:{eventType:'RECEPTION',dates:['2026-12-05'],slot:'EVENING',venueId:'V-SAGAR',setup:'ROUND',pax:150,guaranteedPax:150},lines:[],advance:[],tnc:{templateId:'T',v:1,clauses:[],hasPlaceholder:false},validUntil:'2026-11-08',createdBy:'manager',createdAt:'2026-10-24T10:00'});
    let [s,q]=quote(M.seed(),{versions:[v(1,'SUPERSEDED'),v(2,'SENT')]});
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_ACCEPT',id:q,version:1}),e=>e.code==='HCC_QUO_NOT_LATEST');
    let [d,qd]=quote(M.seed(),{status:'DRAFT'});assert.throws(()=>M.execute(d,'reception',{type:'QUO_ACCEPT',id:qd}),/sent quotation/);
    const late=at(s,'2026-11-09');assert.throws(()=>M.execute(late,'reception',{type:'QUO_ACCEPT',id:q}),e=>e.code==='HCC_QUO_EXPIRED');
    assert.throws(()=>M.execute(late,'manager',{type:'QUO_ACCEPT',id:q}),e=>e.code==='HCC_QUO_EXPIRED');
    assert.equal(M.execute(late,'manager',{type:'QUO_ACCEPT',id:q,reason:'Customer confirmed by phone on time'}).hallX.events.length,1);
    let [c,qc]=quote(M.seed(),{pax:180});assert.throws(()=>M.execute(c,'reception',{type:'QUO_ACCEPT',id:qc}),e=>e.code==='HCC_HALL_CAPACITY');
    assert.equal(M.execute(c,'manager',{type:'QUO_ACCEPT',id:qc,capacityReason:'Extra tables on the stage side'}).hallX.events.length,1);
  });
  test('[HALL] expiry reminder 2 days before for SENT quotations; expired after the validity; cancelled event frees the calendar',()=>{
    let [s]=quote(M.seed());assert.equal(M.hallQuotesExpiring(s).reminder.length,0);
    assert.equal(M.hallQuotesExpiring(at(s,'2026-11-06')).reminder.length,1);assert.equal(M.hallQuotesExpiring(at(s,'2026-11-09')).expired.length,1);
    assert.equal(M.hallSlotsOverlap('MORNING','EVENING'),false);assert.equal(M.hallSlotsOverlap('FULL_DAY','AFTERNOON'),true);
    assert.deepEqual(M.hallVenueFootprint(M.hallSlice(s),'V-LAWN').sort(),['V-LAWN','V-SAGAR-LAWN']);
  });
}
