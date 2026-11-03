// F6 H2 · hall quotations + T&C (contract §3, §8; spec §6–§8, §18, §21). Names prefixed [HQUO].
export function register({test,M,assert}){
  const at=(s,date)=>({...s,date,clock:date+'T10:00'});
  const enq=(s,o={})=>M.execute(s,'reception',{type:'HALL_ENQUIRY_ADD',name:'Kulkarni family (fictional)',phone:'9000000123',eventType:'RECEPTION',dates:['2026-12-05'],
    slot:'EVENING',pax:150,venueId:'V-SAGAR',source:'PHONE',followUp:s.date,...o});
  const lastEnq=s=>s.hallX.enquiries.at(-1).id,lastQ=s=>s.hallX.quotations.at(-1);
  /** Owner replaces the General + event-type template so quotations can be sent. */
  const realTnc=(s,eventType='GENERAL')=>M.execute(s,'owner',{type:'HALL_TNC_TEMPLATE_SAVE',eventType,clauses:[{heading:'Advance and payment',text:'25 % on booking, 50 % thirty days before.'},{heading:'Cancellation',text:'As per the slab table.'}]});
  const create=(s,u='reception',o={})=>M.execute(s,u,{type:'QUO_CREATE',enquiryId:lastEnq(s),menuId:'MP-SILVER-VEG',...o});
  const goldenLines=pct=>{const r=x=>x-Math.round(x*pct/100);return [
    {kind:'HALL',refId:'V-SAGAR',desc:'Sagar Hall · evening',qty:1,unit:'slot',ratePaise:r(1500000)},
    {kind:'MENU',refId:'MP-SILVER-VEG',desc:'Silver Veg per plate',qty:150,unit:'plate',ratePaise:r(65000)},
    {kind:'EXTRA',desc:'Stage decor',qty:1,unit:'lot',ratePaise:r(1000000),listRatePaise:1000000}];};

  test('[HQUO] seed T&C: General / Wedding / Corporate, ten placeholder clauses each; absent templates = seed',()=>{
    const x=M.hallSlice(M.seed()),t=M.hallTemplates(x);
    assert.deepEqual(t.map(t=>t.eventType),['GENERAL','WEDDING','CORPORATE']);
    for(const tpl of t){assert.equal(tpl.versions[0].clauses.length,10);assert.ok(tpl.versions[0].clauses.every(c=>c.text==='OWNER TO REPLACE'));}
    assert.equal(M.hallTemplateFor(x,'BIRTHDAY').id,'T-GEN','General where none');assert.equal(M.hallTemplateFor(x,'WEDDING').id,'T-WEDDING');
  });
  test('[HQUO] numbering QT/2627/0001 consecutive; restarts on 1 April (QT/2728/0001); a cancelled quotation keeps its number',()=>{
    let s=enq(M.seed());s=create(s);assert.equal(lastQ(s).number,'QT/2627/0001');assert.equal(lastQ(s).fy,'2627');
    s=M.execute(s,'reception',{type:'QUO_CANCEL',id:lastQ(s).id});
    s=create(s);assert.equal(lastQ(s).number,'QT/2627/0002','no reuse after cancel');
    s=at(s,'2027-03-31');s=enq(s,{dates:['2027-05-01'],followUp:'2027-03-31'});s=create(s);assert.equal(lastQ(s).number,'QT/2627/0003');
    s=at(s,'2027-04-01');s=enq(s,{dates:['2027-05-02'],followUp:'2027-04-01'});s=create(s);assert.equal(lastQ(s).number,'QT/2728/0001');
    s=enq(s,{dates:['2027-05-03'],followUp:'2027-04-01'});s=create(s);assert.equal(lastQ(s).number,'QT/2728/0002');
    assert.equal(M.hallQuoNext(s.hallX,'2027-04-02').number,'QT/2728/0003');
  });
  test('[HQUO] create: default lines from the hall slot + menu × guaranteed plates; Cashier / HK cannot quote; list rate comes from the master',()=>{
    let s=enq(M.seed());
    for(const u of ['cashier','housekeeper'])assert.throws(()=>create(s,u),/Access denied/);
    s=create(s);const v=M.hallLatest(lastQ(s));
    assert.deepEqual(v.lines.map(l=>[l.kind,l.qty,l.ratePaise,l.listRatePaise]),[['HALL',1,1500000,1500000],['MENU',150,65000,65000]]);
    assert.equal(v.status,'DRAFT');assert.equal(v.validUntil,M.addDays(s.date,15));assert.equal(v.tnc.hasPlaceholder,true);
    // a spoofed list rate on a master line is replaced by the master's
    s=M.execute(s,'manager',{type:'QUO_EDIT',id:lastQ(s).id,lines:[{kind:'HALL',refId:'V-SAGAR',desc:'Sagar',qty:1,unit:'slot',ratePaise:1500000,listRatePaise:1500000*2}]});
    assert.equal(M.hallLatest(lastQ(s)).lines[0].listRatePaise,1500000);
    assert.throws(()=>M.execute(s,'manager',{type:'QUO_EDIT',id:lastQ(s).id,lines:[{kind:'MENU',refId:'MP-NOPE',desc:'Nope menu',qty:1,ratePaise:1}]}),/menu package/);
  });
  test('[HQUO] discount limits: Reception 0 %, Manager 10 % passes / 11 % needs the Owner, Owner any; no self-approval',()=>{
    let s=realTnc(enq(M.seed()));
    s=create(s,'reception',{lines:goldenLines(1)});const r=lastQ(s).id;
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_SEND',id:r}),e=>e.code==='HCC_DISCOUNT_LIMIT');
    s=enq(s);s=create(s,'manager',{lines:goldenLines(10)});const m10=lastQ(s).id;s=M.execute(s,'manager',{type:'QUO_SEND',id:m10});
    assert.equal(M.hallLatest(lastQ(s)).status,'SENT');
    s=enq(s);s=create(s,'manager',{lines:goldenLines(11)});const m11=lastQ(s).id;
    assert.throws(()=>M.execute(s,'manager',{type:'QUO_SEND',id:m11}),e=>e.code==='HCC_DISCOUNT_LIMIT');
    assert.throws(()=>M.execute(s,'manager',{type:'QUO_DISCOUNT_APPROVE',id:m11}),/Access denied/);
    s=M.execute(s,'owner',{type:'QUO_DISCOUNT_APPROVE',id:m11});s=M.execute(s,'manager',{type:'QUO_SEND',id:m11});
    assert.equal(M.hallLatest(s.hallX.quotations.find(q=>q.id===m11)).status,'SENT');
    s=enq(s);s=create(s,'owner',{lines:goldenLines(40)});const o40=lastQ(s).id;
    assert.throws(()=>M.execute(s,'owner',{type:'QUO_DISCOUNT_APPROVE',id:o40}),/own quotation/);
    s=M.execute(s,'owner',{type:'QUO_SEND',id:o40});assert.equal(lastQ(s).versions[0].status,'SENT','Owner: any discount');
    // editing the lines clears an approval
    s=enq(s);s=create(s,'manager',{lines:goldenLines(11)});const e=lastQ(s).id;s=M.execute(s,'owner',{type:'QUO_DISCOUNT_APPROVE',id:e});
    s=M.execute(s,'manager',{type:'QUO_EDIT',id:e,lines:goldenLines(12)});assert.throws(()=>M.execute(s,'manager',{type:'QUO_SEND',id:e}),e=>e.code==='HCC_DISCOUNT_LIMIT');
  });
  test('[HQUO] placeholder guard: send refused HCC_TNC_PLACEHOLDER; Owner-only override with a reason, logged; once per quotation',()=>{
    let s=enq(M.seed());s=create(s);const id=lastQ(s).id;
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_SEND',id}),e=>e.code==='HCC_TNC_PLACEHOLDER');
    assert.throws(()=>M.execute(s,'manager',{type:'QUO_PLACEHOLDER_OVERRIDE',id,reason:'customer in a hurry'}),e=>e.code==='HCC_TNC_OWNER_ONLY');
    assert.throws(()=>M.execute(s,'owner',{type:'QUO_PLACEHOLDER_OVERRIDE',id,reason:'x'}),/reason/);
    s=M.execute(s,'owner',{type:'QUO_PLACEHOLDER_OVERRIDE',id,reason:'Paper T&C handed over by hand'});
    const o=M.hallLatest(lastQ(s)).placeholderOverride;assert.deepEqual([o.by,o.reason],['owner','Paper T&C handed over by hand']);
    assert.equal(M.hallQuotePrint(s,id).watermark,'DRAFT — NOT AN OFFER');
    s=M.execute(s,'reception',{type:'QUO_SEND',id});const v=M.hallLatest(lastQ(s));assert.equal(v.status,'SENT');assert.equal(v.tnc.hasPlaceholder,true);
    assert.equal(M.hallQuotePrint(s,id).watermark,undefined,'final after the override');
    s=M.execute(s,'reception',{type:'QUO_REVISE',id});assert.ok(M.hallLatest(lastQ(s)).placeholderOverride,'carried to v2 (once per quotation)');
    assert.throws(()=>M.execute(s,'owner',{type:'QUO_PLACEHOLDER_OVERRIDE',id,reason:'again please'}),/one override/);
  });
  test('[HQUO] T&C template: Owner only (Reception / Manager refused HCC_TNC_OWNER_ONLY); new version; a sent snapshot never changes',()=>{
    let s=realTnc(enq(M.seed()),'RECEPTION');
    for(const u of ['reception','manager'])assert.throws(()=>M.execute(s,u,{type:'HALL_TNC_TEMPLATE_SAVE',eventType:'GENERAL',clauses:[{heading:'H1',text:'abc'}]}),e=>e.code==='HCC_TNC_OWNER_ONLY');
    s=create(s);const id=lastQ(s).id;s=M.execute(s,'reception',{type:'QUO_SEND',id});
    const before=structuredClone(M.hallLatest(lastQ(s)).tnc);assert.equal(before.templateId,'T-RECEPTION');assert.equal(before.hasPlaceholder,false);
    s=M.execute(s,'owner',{type:'HALL_TNC_TEMPLATE_SAVE',eventType:'RECEPTION',clauses:[{heading:'Advance',text:'Changed wording after sending.'}]});
    const t=M.hallTemplates(s.hallX).find(t=>t.id==='T-RECEPTION');assert.deepEqual(t.versions.map(v=>v.v),[1,2]);
    assert.deepEqual(M.hallLatest(lastQ(s)).tnc,before,'snapshot unchanged');
    assert.deepEqual(M.hallQuotePrint(s,id).tnc.clauses,before.clauses);
    s=M.execute(s,'reception',{type:'QUO_REVISE',id});assert.equal(M.hallLatest(lastQ(s)).tnc.v,2,'a new version takes the new template');
    assert.equal(lastQ(s).versions[0].tnc.v,1);
    assert.throws(()=>M.execute(s,'owner',{type:'HALL_TNC_TEMPLATE_SAVE',eventType:'GENERAL',clauses:[]}),/at least one clause/);
  });
  test('[HQUO] revise keeps the number: v2 draft, v1 superseded and read-only; only the latest can be sent / accepted',()=>{
    let s=realTnc(enq(M.seed()));s=create(s);const id=lastQ(s).id,num=lastQ(s).number;s=M.execute(s,'reception',{type:'QUO_SEND',id});
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_EDIT',id,header:{pax:120,guaranteedPax:120}}),/Only a draft/);
    s=M.execute(s,'reception',{type:'QUO_REVISE',id});const q=lastQ(s);
    assert.equal(q.number,num);assert.equal(q.currentVersion,2);assert.deepEqual(q.versions.map(v=>v.status),['SUPERSEDED','DRAFT']);
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_SEND',id,version:1}),e=>e.code==='HCC_QUO_NOT_LATEST');
    s=M.execute(s,'manager',{type:'QUO_EDIT',id,lines:goldenLines(5)});s=M.execute(s,'manager',{type:'QUO_SEND',id});
    assert.equal(M.hallQuotePrint(s,id,1).watermark,'SUPERSEDED');assert.equal(M.hallQuotePrint(s,id).label,num+' v2');
    assert.equal(s.hallX.quotations.length,1);
  });
  test('[HQUO] expiry after 15 days (QUO_EXPIRE), reminder 2 days before; an expired one is revised into a new version',()=>{
    let s=realTnc(enq(M.seed()));s=create(s);const id=lastQ(s).id;s=M.execute(s,'reception',{type:'QUO_SEND',id});
    const vu=M.hallLatest(lastQ(s)).validUntil;assert.equal(vu,M.addDays(s.date,15));
    assert.equal(M.hallQuotesExpiring(at(s,M.addDays(vu,-2))).reminder.length,1);
    let t=M.execute(at(s,vu),'manager',{type:'QUO_EXPIRE'});assert.equal(M.hallLatest(lastQ(t)).status,'SENT','still valid on the day');
    t=M.execute(at(s,M.addDays(vu,1)),'manager',{type:'QUO_EXPIRE'});assert.equal(M.hallLatest(lastQ(t)).status,'EXPIRED');
    assert.throws(()=>M.execute(t,'reception',{type:'QUO_EXPIRE'}),/Access denied/);
    t=M.execute(t,'reception',{type:'QUO_REVISE',id});assert.deepEqual(lastQ(t).versions.map(v=>v.status),['EXPIRED','DRAFT']);
  });
  test('[HQUO] lost needs a listed reason (Other needs a note) and closes the enquiry; cancel only a draft',()=>{
    let s=realTnc(enq(M.seed()));s=create(s);const id=lastQ(s).id;s=M.execute(s,'reception',{type:'QUO_SEND',id});
    assert.equal(s.hallX.enquiries.at(-1).status,'QUOTED');
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_CANCEL',id}),/Only a draft/);
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_LOST',id}),/lost reason/);
    assert.throws(()=>M.execute(s,'reception',{type:'QUO_LOST',id,reasonId:'LR-OTHER'}),/needs a note/);
    s=M.execute(s,'reception',{type:'QUO_LOST',id,reasonId:'LR-PRICE'});
    assert.equal(M.hallLatest(lastQ(s)).status,'LOST');assert.deepEqual([s.hallX.enquiries.at(-1).status,s.hallX.enquiries.at(-1).lostReasonId],['LOST','LR-PRICE']);
  });
  test('[HQUO] golden: v1 → v2 5 % Manager → send → accept creates the event + 25/50/balance plan; print totals = screen totals',()=>{
    let s=realTnc(enq(M.seed()));s=create(s,'reception',{lines:goldenLines(0)});const id=lastQ(s).id;
    s=M.execute(s,'reception',{type:'QUO_SEND',id});s=M.execute(s,'manager',{type:'QUO_REVISE',id});s=M.execute(s,'manager',{type:'QUO_EDIT',id,lines:goldenLines(5)});
    s=M.execute(s,'manager',{type:'QUO_SEND',id});
    const p=M.hallQuotePrint(s,id),screen=M.hallPrice(s,M.hallLatest(lastQ(s)).lines,s.date).totals;
    assert.deepEqual(p.totals,screen);assert.equal(p.discountBps,500);
    assert.equal(p.totals.taxablePaise,1425000+9262500+950000);
    assert.equal(p.lines.reduce((a,l)=>a+l.cgstPaise+l.sgstPaise,0),p.totals.taxPaise,'GST per line');
    assert.equal(p.advance.reduce((a,r)=>a+r.amountPaise,0),p.totals.totalPaise);
    s=M.execute(s,'manager',{type:'QUO_ACCEPT',id,advance:{amountPaise:2000000,method:'UPI',reference:'UPI-778899'}});
    const e=s.hallX.events.at(-1);assert.equal(e.quotationId,id);assert.equal(e.version,2);assert.equal(e.status,'CONFIRMED');
    assert.deepEqual(e.advance.map(r=>r.label),['Advance on booking','Second advance','Balance']);
    assert.equal(e.advance.reduce((a,r)=>a+r.amountPaise,0),p.totals.totalPaise,'plan of the accepted grand total');
    assert.equal(e.advance[0].amountPaise,p.advance[0].amountPaise);assert.equal(e.advance[0].paidPaise,2000000);
    assert.deepEqual(lastQ(s).versions.map(v=>v.status),['SUPERSEDED','ACCEPTED']);assert.equal(s.hallX.enquiries.at(-1).status,'WON');
    assert.equal(M.hallQuoteRegister(s)[0].status,'ACCEPTED');
  });
  test('[HQUO] advance plan: Reception keeps the default; Manager sets 30 / balance; percentages must leave a balance',()=>{
    let s=realTnc(enq(M.seed()));const adv=[{label:'Booking',pctBps:3000,due:'ON_ACCEPT'},{label:'Balance',due:'EVENT_DAY'}];
    assert.throws(()=>create(s,'reception',{advance:adv}),/advance plan/);
    assert.throws(()=>create(s,'manager',{advance:[{pctBps:10000,due:'ON_ACCEPT'},{due:'EVENT_DAY'}]}),/percentage|balance/);
    s=create(s,'manager',{advance:adv});const id=lastQ(s).id,p=M.hallQuotePrint(s,id);
    assert.equal(p.advance[0].amountPaise,M.hallPct(p.totals.totalPaise,3000));assert.equal(p.advance[1].amountPaise,p.totals.totalPaise-p.advance[0].amountPaise);
  });
}
