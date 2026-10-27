// SCC decisions wave (27 Oct 2026), compliance: DEC-125 (the compliance FY follows the business date on 1 April; a pick holds until the next
// 1 April) and DEC-133 (FSSAI and lodging house licence renewal cards from the firm profile's expiry dates, for the hotel in Latur, Maharashtra).
// Server twins: Hcc.Compliance.Tests (SccDecisionsTests, ComplianceSqlTests). Names prefixed [DEC-125] / [DEC-133].
import {readFileSync} from 'node:fs';

export function register({test,M,assert}){
  const {seed,execute,cmpSlice,cmpMonthPage,cmpActionCenter,cmpBadge,cmpCsv,cmpCsvRows,cmpPrintModel,cmpPayableFeed,cmpEvidenceZipPlan,cmpPackLines,cmpDueThisWeek,
    cmpQuarter,cmpRecord,cmpEffectiveFy,cmpFyStatus,cmpHasOverdueInMonth}=M;
  const at=(s,date)=>({...s,date});
  const fy=(s,date)=>cmpSlice(date?at(s,date):s).fyStart;
  const code=c=>e=>e.code===c;

  test('[DEC-125] the FY follows the business date on 1 April; a pick holds until the next 1 April (2026–27 in Oct 2026 stays; 1 Apr 2027 → 2027–28; 2026–27 picked on 2 Apr 2027 holds to 31 Mar 2028, then 2028–29)',()=>{
    const s0=seed();assert.equal(fy(s0),2026);assert.equal(s0.complianceX.fyChosenIn,undefined,'the demo has no pick: it follows the business date');
    const s=execute(s0,'owner',{type:'CMP_FY_SET',fy:2026});assert.equal(s.complianceX.fyChosenIn,2026,'the pick stores the business date’s FY');
    for(const d of ['2026-10-24','2027-01-15','2027-03-31'])assert.equal(fy(s,d),2026,d);
    assert.equal(fy(s,'2027-04-01'),2027,'1 Apr 2027 (business date) → FY 2027–28 by itself');
    const t=execute(at(s,'2027-04-02'),'accountant',{type:'CMP_FY_SET',fy:2026});assert.equal(t.complianceX.fyChosenIn,2027);
    for(const d of ['2027-04-02','2027-12-31','2028-03-31'])assert.equal(fy(t,d),2026,d);
    assert.equal(fy(t,'2028-04-01'),2028,'the pick lapses on the next 1 April → FY 2028–29');
    assert.deepEqual(cmpFyStatus(at(t,'2027-06-01')),{fy:2026,businessFy:2027,pickEnds:'2028-03-31',next:2028});
    assert.deepEqual(cmpFyStatus(at(t,'2028-04-01')),{fy:2028,businessFy:2028,pickEnds:null,next:null});
    // A command after the pick lapsed writes the slice back unchanged in meaning: effective FY stored, no pick in force.
    const w=execute(at(t,'2028-04-01'),'accountant',{type:'CMP_NA',itemId:'ap1'});
    assert.deepEqual([w.complianceX.fyStart,w.complianceX.fyChosenIn,cmpRecord(cmpSlice(w),2028,'ap1').na],[2028,null,true]);assert.equal(fy(w,'2029-04-01'),2029);
    // Without a pick: the business date's FY, clamped to SCC's list (2026–27 … 2032–33). Never the device clock.
    for(const [d,y] of [['2025-06-01',2026],['2026-03-31',2026],['2026-04-01',2026],['2029-04-01',2029],['2033-03-31',2032],['2033-04-01',2032],['2040-01-01',2032]])assert.equal(fy(s0,d),y,d);
    // A pick made before April 2026 (business FY 2025–26, shown as 2026–27) holds only until 31 Mar 2026; after the list, until the next 1 April too.
    const e=execute(at(s0,'2026-02-10'),'owner',{type:'CMP_FY_SET',fy:2028});assert.deepEqual([fy(e),fy(e,'2026-03-31'),fy(e,'2026-04-01')],[2028,2028,2026]);
    const z=execute(at(s0,'2034-05-01'),'owner',{type:'CMP_FY_SET',fy:2030});assert.deepEqual([fy(z),fy(z,'2035-03-31'),fy(z,'2035-04-01')],[2030,2030,2032]);
    // The pure rule (same as the server's SccTaxRules.EffectiveFy).
    assert.deepEqual([cmpEffectiveFy(2029,null,'2026-10-24'),cmpEffectiveFy(2029,2026,'2026-10-24'),cmpEffectiveFy(2029,2026,'2027-04-01'),cmpEffectiveFy(2040,2026,'2026-10-24')],[2026,2029,2027,2032]);
  });

  test('[DEC-125] every reader uses the effective FY: month page, overdue dots, dashboard, Action Center, badge, “Due this week”, CSV, print, payable feed, evidence ZIP, CA pack, commands',()=>{
    const s=at(seed(),'2027-04-01');// no pick → FY 2027–28
    assert.deepEqual([cmpMonthPage(s,'April').fy,cmpQuarter(s,'Q1').fy],[2027,2027]);
    assert.deepEqual(M.CMP_MONTHS.map(m=>cmpHasOverdueInMonth(s,m)),M.CMP_MONTHS.map(m=>cmpHasOverdueInMonth(s,m,2027)));
    const ac=cmpActionCenter(s);assert.deepEqual(ac,cmpActionCenter(s,2027));assert.notDeepEqual(ac.map(r=>r.dueIso),cmpActionCenter(s,2026).map(r=>r.dueIso));
    assert.ok(ac.length>0&&ac.every(r=>r.dueIso>='2027-04-01'),'FY 2027–28 cards (nothing filed yet), due from 1 Apr 2027');
    assert.equal(cmpBadge(s,'owner'),ac.length);assert.deepEqual(cmpDueThisWeek(s).map(r=>r.itemId),ac.filter(r=>r.days<=7).map(r=>r.itemId));
    assert.equal(cmpCsv(s).fileName,'Compliance_HCC_Demo_Residency_FY_2027_28.csv');assert.deepEqual(cmpCsvRows(s),cmpCsvRows(s,2027));
    assert.equal(cmpPrintModel(s,'April').fyLabel,'FY 2027–28');assert.deepEqual(cmpPayableFeed(s),cmpPayableFeed(s,2027));
    assert.match(cmpEvidenceZipPlan(s).fileName,/_2027_28\.zip$/);assert.match(cmpPackLines(s)[0],/\(FY 2027–28\)/);
    const d=execute(s,'accountant',{type:'CMP_DONE',itemId:'ap1'});assert.equal(cmpRecord(cmpSlice(d),2027,'ap1').done,true,'a command without an FY records in the effective FY');
    assert.deepEqual(cmpRecord(cmpSlice(d),2026,'ap1'),cmpRecord(cmpSlice(seed()),2026,'ap1'),'FY 2026–27 untouched');
    // A pick of 2026–27 made on 2 Apr 2027 keeps every reader on 2026–27 until 31 Mar 2028.
    const p=execute(at(seed(),'2027-04-02'),'owner',{type:'CMP_FY_SET',fy:2026});
    assert.deepEqual([cmpMonthPage(p,'April').fy,cmpCsv(p).fileName,cmpPrintModel(p,'May').fyLabel],[2026,'Compliance_HCC_Demo_Residency_FY_2026_27.csv','FY 2026–27']);
    assert.deepEqual(cmpActionCenter(p),cmpActionCenter(p,2026));
  });

  test('[DEC-125] a save from before DEC-125: a recorded FY pick counts as made in the current business-date FY (no jump on restore, idempotent); no pick → follows the business date',()=>{
    const old=structuredClone(execute(seed(),'owner',{type:'CMP_FY_SET',fy:2028}));delete old.complianceX.fyChosenIn;// the pre-DEC-125 shape
    assert.equal(cmpSlice(old).fyStart,2026,'unrestored, the missing field reads as no pick');
    const r=M.restoreState(JSON.stringify(old)).state;
    assert.deepEqual([r.complianceX.fyChosenIn,fy(r),fy(r,'2027-03-31'),fy(r,'2027-04-01')],[2026,2028,2028,2027]);
    assert.deepEqual(M.restoreState(JSON.stringify(r)).state.complianceX,r.complianceX,'idempotent');
    const plain=M.restoreState(JSON.stringify(seed())).state;
    assert.equal('fyChosenIn' in plain.complianceX,false,'no FY pick in the audit: nothing stored, the calendar follows the business date');
    assert.deepEqual([fy(plain),fy(plain,'2027-04-01')],[2026,2027]);
  });

  // ── DEC-133 ──
  const prof=(s,over,uid='owner')=>{const p=cmpSlice(s).profile;return execute(s,uid,{type:'CMP_PROFILE_SET',name:p.name,pan:p.pan,gstin:p.gstin,entity:p.entity,gstType:p.gstType,
    hasEmployees:p.hasEmployees,qrmp:p.qrmp,notes:p.notes,...over});};
  const lic=(s,month,y)=>cmpMonthPage(s,month,'all',y).all.filter(v=>v.card._licence);

  test('[DEC-133] FSSAI and lodging licence cards: only while the expiry date is set, on the FY page of the expiry month, due on it, risk High, SCC’s control layer; the demo’s are in FY 2027–28',()=>{
    const s=seed(),p=cmpSlice(s).profile;
    assert.deepEqual([p.fssaiNo,p.fssaiExpiry,p.lodgingNo,p.lodgingExpiry],['DEMO-FSSAI-0001','2027-05-31','DEMO-LODGING-0001','2027-12-31']);
    assert.ok(M.CMP_MONTHS.every(m=>lic(s,m).length===0),'no licence card in FY 2026–27: the demo calendar there stays SCC’s own');
    const [f]=lic(s,'May',2027),[l]=lic(s,'December',2027);
    assert.deepEqual([f.card.id,f.card.name,f.due,f.card.due,f.card.cat,f.control.risk,f.control.buffer,f.control.internalDate,f.control.responsible,f.control.reviewer],
      ['lic_fssai','FSSAI Licence – Renewal','2027-05-31','31 May','lab','High',5,'2027-05-26','Owner / General Manager','Owner']);
    assert.equal(f.form,'FSSAI licence renewal (FoSCoS portal) · Licence no. DEMO-FSSAI-0001');assert.match(f.penaltyText,/CA to confirm/);assert.match(f.whatText,/CA to confirm/);
    assert.deepEqual([l.card.id,l.due,l.control.risk],['lic_lodging','2027-12-31','High']);assert.match(l.card.form,/local police \/ municipal corporation/);
    assert.deepEqual(M.CMP_MONTHS.flatMap(m=>lic(s,m,2027).map(v=>m+':'+v.card.id)),['May:lic_fssai','December:lic_lodging']);
    for(const y of [2028,2029])assert.ok(M.CMP_MONTHS.every(m=>lic(s,m,y).length===0),'only the FY of the expiry date');
    // No employees: the licences stay (business-level, like SCC's Shops & Establishment renewal).
    const n=prof(s,{hasEmployees:false});assert.equal(lic(n,'May',2027).length,1);
  });

  test('[DEC-133] an expiry inside FY 2026–27 puts the card in the Action Center, badge, CSV, print and dashboard; done / NA / evidence / archive work as for every card',()=>{
    let s=prof(seed(),{fssaiExpiry:'2026-11-15'});// 22 days after the business date (24 Oct 2026)
    assert.match(s.audit[0].detail,/· FSSAI licence expires 2026-11-15 · lodging licence expires 2027-12-31$/);
    const [v]=lic(s,'November');assert.deepEqual([v.card.id,v.due,v.days,v.tag.text,v.control.internalDate,v.control.escalation.label],['lic_fssai','2026-11-15',22,'22d left','2026-11-10','Monitor']);
    const ac=cmpActionCenter(s);assert.ok(ac.some(r=>r.itemId==='lic_fssai'&&r.days===22&&r.month==='November'));assert.equal(cmpBadge(s,'accountant'),cmpBadge(seed(),'accountant')+1);
    const row=cmpCsvRows(s).find(r=>r[4]==='FSSAI Licence – Renewal');assert.deepEqual([row[2],row[3],row[6],row[7],row[9],row[10],row[12]],['November','15 Nov','Labour Law','On licence expiry','High','Pending','22']);
    assert.ok(cmpPrintModel(s,'November').rows.some(r=>r.name==='FSSAI Licence – Renewal'&&r.status==='Pending'));
    assert.ok(cmpQuarter(s,'Q3').focus.some(i=>i.card.id==='lic_fssai'),'High risk: in the quarter’s focus list');
    // Done (any FY other than the expiry's is refused), evidence, reopen, not applicable.
    assert.throws(()=>execute(s,'accountant',{type:'CMP_DONE',fy:2027,itemId:'lic_fssai'}),code('NOT_FOUND'));
    s=execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'lic_fssai',name:'ack.pdf',dataUrl:'data:application/pdf;base64,JVBERg=='});
    s=execute(s,'accountant',{type:'CMP_DONE',itemId:'lic_fssai',filedOn:'2026-11-01',evidenceRef:'FOSCOS-ACK-DEMO'});
    assert.deepEqual([cmpRecord(cmpSlice(s),2026,'lic_fssai').dueDate,lic(s,'November')[0].isDone,lic(s,'November')[0].files],['2026-11-15',true,1]);
    assert.ok(!cmpActionCenter(s).some(r=>r.itemId==='lic_fssai'));assert.match(s.audit[0].detail,/^FSSAI Licence – Renewal · FY 2026–27 · filed on 2026-11-01/);
    s=execute(s,'owner',{type:'CMP_REOPEN',fy:2026,itemId:'lic_fssai',reason:'Wrong acknowledgement'});
    s=execute(s,'accountant',{type:'CMP_NA',itemId:'lic_fssai'});assert.equal(lic(s,'November')[0].isNA,true);
    // Archive (key = the name up to the en dash, listed with SCC's built-in keys) hides it everywhere; restore brings it back.
    assert.deepEqual(M.cmpLicenceObligations().map(o=>[o.key,o.cat]),[['FSSAI Licence','lab'],['Lodging House Licence','lab']]);
    let a=execute(prof(seed(),{fssaiExpiry:'2026-11-15'}),'owner',{type:'CMP_ARCHIVE_KEY',obligationKey:'FSSAI Licence'});
    assert.deepEqual([lic(a,'November').length,cmpActionCenter(a).some(r=>r.itemId==='lic_fssai'),cmpCsvRows(a).some(r=>r[4]==='FSSAI Licence – Renewal')],[0,false,false]);
    a=execute(a,'owner',{type:'CMP_ARCHIVE_KEY',obligationKey:'FSSAI Licence'});assert.equal(lic(a,'November').length,1);
    assert.throws(()=>execute(a,'accountant',{type:'CMP_ARCHIVE_KEY',obligationKey:'FSSAI Licence'}),/permission|Owner|denied/i);
  });

  test('[DEC-133] firm profile licence fields: optional, Owner only, expiry a valid date, number ≤ 60 characters; left out = kept, empty = cleared (the card goes)',()=>{
    const s=seed();
    assert.throws(()=>prof(s,{fssaiExpiry:'2026-02-30'}),/FSSAI licence expiry must be a valid date/);
    assert.throws(()=>prof(s,{lodgingNo:'X'.repeat(61)}),/limited to 60/);
    assert.throws(()=>prof(s,{fssaiExpiry:'2026-11-15'},'accountant'),/permission|Owner|denied/i);
    const kept=execute(s,'owner',{type:'CMP_PROFILE_SET',name:'HCC Demo Residency',gstType:'regular'});
    assert.deepEqual([cmpSlice(kept).profile.fssaiNo,cmpSlice(kept).profile.lodgingExpiry],['DEMO-FSSAI-0001','2027-12-31'],'a command without the fields keeps them');
    const cleared=prof(s,{fssaiNo:'',fssaiExpiry:''});
    assert.deepEqual(['fssaiNo' in cmpSlice(cleared).profile,'fssaiExpiry' in cmpSlice(cleared).profile,lic(cleared,'May',2027).length,lic(cleared,'December',2027).length],[false,false,0,1]);
    assert.equal(M.cmpFindCard(cmpSlice(cleared),'lic_fssai'),undefined);
  });

  test('[DEC-133] the licence texts are identical to the server’s LicenceCards.json; lead time and penalty amounts are left to the CA (no ₹ figures)',()=>{
    const server=JSON.parse(readFileSync(new URL('../../08 Production/src/Hcc.Domain/Compliance/LicenceCards.json',import.meta.url),'utf8'));
    assert.equal(JSON.stringify(M.CMP_LICENCE_DEFS),JSON.stringify(server));
    for(const d of M.CMP_LICENCE_DEFS){assert.match(d.penalty,/CA to confirm/);assert.match(d.what,/CA to confirm/);assert.doesNotMatch(d.who+d.what+d.why+d.penalty,/₹|Rs\.?\s?\d/);
      assert.equal(M.cmpRenderFYText(d.what+d.who+d.why+d.penalty,2030),d.what+d.who+d.why+d.penalty,'no FY text for SCC’s rebasing to touch');}
  });
}
