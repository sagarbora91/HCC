// Compliance calendar = SCC tax module in full (DEC-116) — commands, HCC safeguards, mapping of the W05-C filings, payable from HCC data.
// SCC's own computations are proven against SCC's code in stream-compliance-scc-port.mjs. Names prefixed [CMP].
export function register({test,M,assert}){
  const {seed,execute,cmpSlice,cmpActionCenter,cmpDueThisWeek,cmpBadge,cmpHasAccess,cmpCanManage,cmpMonthPage,cmpRecord,cmpView,cmpFindCard,cmpSettingDefs,cmpPackLines,
    aiAnswer,aiMatchIntent,aiAccess,aiSuggestions}=M;
  const code=c=>e=>e.code===c;
  const rec=(s,id,fy=2026)=>cmpRecord(cmpSlice(s),fy,id);
  const view=(s,id,fy=2026)=>{const x=cmpSlice(s),c=cmpFindCard(x,id);return cmpView(x,c,M.cmpItemMonth(x,id),fy,s.date);};
  const legacyOb=(id,kind,name,freq,rule,active=true)=>({id,kind,name,form:'Portal',freq,rules:[{from:'2000-01-01',rule}],owner:'Accountant',from:'2026-04',active});

  test('[CMP] fixture: one firm profile (regular, monthly, has employees), FY 2026–27, 34 W05-C filings carried onto SCC cards, demo figures unchanged',()=>{
    const s=seed(),x=cmpSlice(s);
    assert.deepEqual([x.profile.name,x.profile.gstType,x.profile.qrmp,x.profile.hasEmployees,x.fyStart],['HCC Demo Residency','regular',false,true,2026]);
    assert.equal(Object.values(x.records['2026']).filter(r=>r.legacy).length,34);assert.equal(Object.keys(x.records['2026']).length,55);
    assert.equal(rec(s,'oc1').evidenceRef.startsWith('TDS-202609-'),true);assert.equal(rec(s,'oc1').filedOn,'2026-10-06');
    assert.deepEqual([rec(s,'my6').na,rec(s,'my6').notes],[true,'Staff count below the ESIC threshold until June (illustrative)']);
    for(const id of ['oc4','oc11','dc5'])assert.equal(rec(s,id),undefined,id);// GSTR-1 Sep, PT Sep, GSTR-9 FY 2025-26 stay open (as before)
    assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);});

  test('[CMP] October page on 24 Oct 2026: 8 cards (2 QRMP NA), 4 done, 4 pending, 1 overdue (GSTR-1 September); only October has the overdue dot',()=>{
    const s=seed(),p=cmpMonthPage(s,'October');
    assert.deepEqual(p.stats,{total:8,completed:4,pending:4,highRisk:6,na:2,overdue:1,evidence:'4/4'});
    assert.deepEqual(M.CMP_MONTHS.filter(m=>M.cmpHasOverdueInMonth(s,m)),['October']);
    assert.equal(cmpMonthPage(s,'October','na').list.length,2);assert.equal(cmpMonthPage(s,'October','gst').list.length,4);
    assert.equal(view(s,'oc4').tag.text,'⚠ 13d overdue');assert.equal(view(s,'oc11').tag.text,'⚡ 7d left');assert.equal(view(s,'oc1').tag.text,'✓ Filed');});

  test('[CMP] Action Center = overdue + due within 30 days of the selected FY, soonest first; badge for Owner and Accountant only',()=>{
    const s=seed(),ac=cmpActionCenter(s);
    assert.deepEqual(ac.map(r=>r.itemId+':'+r.days),['oc4:-13','oc2:7','oc3:7','oc11:7','nv1:14','nv3:18','nv7:18','nv2:22','nv5:22','nv6:22','nv4:27']);
    assert.equal(cmpBadge(s,'owner'),11);assert.equal(cmpBadge(s,'accountant'),11);for(const u of ['manager','reception','cashier'])assert.equal(cmpBadge(s,u),0);
    assert.equal(M.cmpBadgeText(120),'99+');assert.equal(M.cmpBadgeText(7),'7');
    // HCC extra kept: the "Due this week" card (dueSoonDays 7) = the Action Center rows inside 7 days
    assert.deepEqual(cmpDueThisWeek(s).map(r=>r.itemId),['oc4','oc2','oc3','oc11']);
    const w=execute(s,'owner',{type:'CMP_SETTINGS_SET',dueSoonDays:3});assert.deepEqual(cmpDueThisWeek(w).map(r=>r.itemId),['oc4']);
    // SCC: no firm selected → no actions
    const n=structuredClone(s);delete n.complianceX.profile;assert.equal(cmpActionCenter(n).length,0);});

  test('[CMP] access: Owner and Accountant only (DEC-055); the Owner manages (profile, archive, custom, reopen, settings)',()=>{
    const s=seed();for(const u of ['owner','accountant'])assert.equal(cmpHasAccess(s,u),true);
    for(const u of ['manager','cashier','reception','supervisor','housekeeper','maint','techadmin'])assert.equal(cmpHasAccess(s,u),false);
    assert.equal(cmpCanManage(s,'owner'),true);assert.equal(cmpCanManage(s,'accountant'),false);
    assert.throws(()=>execute(s,'manager',{type:'CMP_DONE',fy:2026,itemId:'oc4'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'cashier',{type:'CMP_CA_PACK'}),code('FORBIDDEN'));
    for(const cmd of [{type:'CMP_PROFILE_SET',name:'X'},{type:'CMP_ARCHIVE_KEY',obligationKey:'GSTR-1'},{type:'CMP_ARCHIVE_CAT',cat:'gst'},{type:'CMP_CUSTOM_SAVE',name:'X',months:['April']},
      {type:'CMP_REOPEN',fy:2026,itemId:'oc1',reason:'Wrong date'},{type:'CMP_SETTINGS_SET',dueSoonDays:5}])
      assert.throws(()=>execute(s,'accountant',cmd),code('FORBIDDEN'),cmd.type);
    assert.ok(execute(s,'accountant',{type:'CMP_FY_SET',fy:2027}));});

  test('[CMP] mark done as SCC: every field optional, filed-on any date (default the business date); late days noted; idempotent; NA is read-only',()=>{
    let s=seed();s=execute(s,'accountant',{type:'CMP_DONE',fy:2026,itemId:'oc4',key:'d1'});
    const r=rec(s,'oc4');assert.deepEqual([r.done,r.filedOn,r.notes,r.evidenceRef,r.reviewerNote,r.dueDate,r.by],[true,s.date,'','','','2026-10-11','accountant']);
    assert.match(s.audit[0].detail,/filed on 2026-10-24 · 13 day\(s\) after the due date/);
    assert.deepEqual(execute(s,'accountant',{type:'CMP_DONE',fy:2026,itemId:'oc4',key:'d1'}),s);
    assert.throws(()=>execute(s,'accountant',{type:'CMP_DONE',fy:2026,itemId:'oc4'}),code('INVALID_TRANSITION'));// HCC: the Owner reopens first
    s=execute(s,'owner',{type:'CMP_DONE',fy:2026,itemId:'nv1',filedOn:'2026-09-01',notes:'Ack 991',evidenceRef:'',reviewerNote:'Owner'});// a future card, an earlier date: SCC accepts both
    assert.deepEqual([rec(s,'nv1').filedOn,rec(s,'nv1').notes,rec(s,'nv1').reviewerNote],['2026-09-01','Ack 991','Owner']);
    assert.throws(()=>execute(s,'owner',{type:'CMP_DONE',fy:2026,itemId:'nv3',filedOn:'2026-02-30'}),/Filed on/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_DONE',fy:2026,itemId:'zz9'}),code('NOT_FOUND'));
    assert.throws(()=>execute(s,'owner',{type:'CMP_DONE',fy:2026,itemId:'oc6'}),/Not Applicable/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_DONE',fy:2033,itemId:'oc4'}),/financial year/);
    const n=structuredClone(seed());delete n.complianceX.profile;assert.throws(()=>execute(n,'owner',{type:'CMP_DONE',fy:2026,itemId:'oc4'}),/firm profile/);
    assert.equal(cmpActionCenter(s).some(r=>r.itemId==='oc4'),false);});

  test('[CMP] not applicable as SCC: no reason; additive over a done record (NA wins); the Owner removes NA and the done record is back',()=>{
    let s=seed();s=execute(s,'accountant',{type:'CMP_NA',fy:2026,itemId:'oc11'});assert.equal(rec(s,'oc11').na,true);assert.equal(cmpMonthPage(s,'October').stats.na,3);
    s=execute(s,'accountant',{type:'CMP_NA',fy:2026,itemId:'oc1'});const v=view(s,'oc1');assert.deepEqual([v.isNA,v.isDone,rec(s,'oc1').done],[true,false,true]);
    assert.throws(()=>execute(s,'accountant',{type:'CMP_NA',fy:2026,itemId:'oc1'}),code('INVALID_TRANSITION'));
    s=execute(s,'owner',{type:'CMP_REOPEN',fy:2026,itemId:'oc1',reason:'Applies after all'});assert.deepEqual([rec(s,'oc1').na,rec(s,'oc1').done],[undefined,true]);
    s=execute(s,'owner',{type:'CMP_REOPEN',fy:2026,itemId:'oc11',reason:'Applies after all'});assert.equal(rec(s,'oc11'),undefined);});

  test('[CMP] reopen (SCC undo) is Owner-only with a reason; the removed record is kept (HCC safeguard)',()=>{
    let s=seed();assert.throws(()=>execute(s,'accountant',{type:'CMP_REOPEN',fy:2026,itemId:'oc1',reason:'Wrong reference'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'CMP_REOPEN',fy:2026,itemId:'oc1',reason:'x'}),/reason/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_REOPEN',fy:2026,itemId:'oc4',reason:'Not filed yet'}),code('INVALID_TRANSITION'));
    const before=rec(s,'oc1');s=execute(s,'owner',{type:'CMP_REOPEN',fy:2026,itemId:'oc1',reason:'Wrong reference'});
    assert.equal(rec(s,'oc1'),undefined);assert.match(s.audit[0].detail,/reopened \(was filed on 2026-10-06 · evidence TDS-202609-/);
    const r=cmpSlice(s).reopened[0];assert.deepEqual([r.fy,r.itemId,r.part,r.record,r.reopenedBy,r.reopenReason],[2026,'oc1','done',before,'owner','Wrong reference']);
    assert.equal(cmpSlice(seed()).reopened,undefined);});

  test('[CMP] FY selector 2026–27 … 2032–33; records are per FY; DEC-125: on 1 April the calendar follows the business date (SCC kept the pick until changed)',()=>{
    let s=seed();assert.throws(()=>execute(s,'owner',{type:'CMP_FY_SET',fy:2025}),/financial year/);assert.throws(()=>execute(s,'owner',{type:'CMP_FY_SET',fy:2033}),/financial year/);
    s=execute(s,'accountant',{type:'CMP_FY_SET',fy:2027});assert.equal(cmpSlice(s).fyStart,2027);assert.equal(cmpActionCenter(s).length,0);
    assert.equal(cmpMonthPage(s,'October').stats.completed,0);
    s=execute(s,'accountant',{type:'CMP_DONE',itemId:'ap1'});assert.equal(rec(s,'ap1',2027).done,true);assert.equal(rec(s,'ap1',2026).done,true);
    const later={...s,date:'2027-05-02'};assert.equal(cmpSlice(later).fyStart,2027);assert.equal(cmpSlice({...seed(),date:'2027-05-02'}).fyStart,2027);});

  test('[CMP] firm profile as SCC: name required; PAN / GSTIN upper-cased, no format check; QRMP only for regular / both; the type and employees decide the cards',()=>{
    let s=seed();assert.throws(()=>execute(s,'owner',{type:'CMP_PROFILE_SET',name:' '}),/firm name/);
    s=execute(s,'owner',{type:'CMP_PROFILE_SET',name:'Hotel X',pan:'abcde1234f',gstin:'27abc',entity:'company',gstType:'composition',hasEmployees:false,qrmp:true,notes:'n'});
    const p=cmpSlice(s).profile;assert.deepEqual([p.pan,p.gstin,p.gstType,p.qrmp,p.hasEmployees,p.entity],['ABCDE1234F','27ABC','composition',false,false,'company']);
    const apr=cmpMonthPage(s,'April').all.map(v=>v.card.id);assert.deepEqual(apr,['ap1','ap_comp1','ap4']);// composition: CMP-08 + GSTR-4; no staff: no EPF / ESIC / PT
    assert.ok(cmpMonthPage(s,'December').all.some(v=>v.card.id==='dc_shops'));// business-level labour cards stay
    s=execute(s,'owner',{type:'CMP_PROFILE_SET',name:'Hotel X',gstType:'both',qrmp:true});assert.equal(cmpSlice(s).profile.qrmp,true);assert.equal(cmpMonthPage(s,'April').all.length,8);
    assert.equal(view(s,'ap2').card&&M.cmpQrmpAdvisory(view(s,'ap2').card,cmpSlice(s).profile),null);// "both" shows every card without the amber note
    s=execute(s,'owner',{type:'CMP_PROFILE_SET',name:'Hotel X',gstType:'unregistered',qrmp:true});assert.equal(cmpSlice(s).profile.qrmp,false);
    assert.ok(cmpMonthPage(s,'July').all.every(v=>v.card.cat==='it'||v.card.cat==='lab'));});

  test('[CMP] archive an obligation key or a whole category (Owner); everything hides it — calendar, Action Center, CSV, print',()=>{
    let s=seed();assert.throws(()=>execute(s,'owner',{type:'CMP_ARCHIVE_KEY',obligationKey:'Nope'}),code('NOT_FOUND'));
    s=execute(s,'owner',{type:'CMP_ARCHIVE_KEY',obligationKey:'GSTR-1'});
    assert.equal(cmpActionCenter(s).some(r=>r.itemId==='oc4'),false);assert.equal(M.cmpCsvRows(s).some(r=>r[4].startsWith('GSTR-1')),false);
    assert.equal(M.cmpPrintModel(s,'October').rows.some(r=>r.name.startsWith('GSTR-1')),false);
    s=execute(s,'owner',{type:'CMP_ARCHIVE_KEY',obligationKey:'GSTR-1'});assert.equal(cmpActionCenter(s).some(r=>r.itemId==='oc4'),true);
    s=execute(s,'owner',{type:'CMP_ARCHIVE_CAT',cat:'lab'});assert.ok(cmpMonthPage(s,'October').all.every(v=>v.card.cat!=='lab'));
    assert.throws(()=>execute(s,'owner',{type:'CMP_ARCHIVE_CAT',cat:'tax'}),code('NOT_FOUND'));});

  test('[CMP] custom obligations as SCC: name + month required, due day 0–31 in the ticked month (29–31 roll-over flagged), edits retroactive, no cap, delete purges files',()=>{
    let s=seed();assert.throws(()=>execute(s,'owner',{type:'CMP_CUSTOM_SAVE',name:'',months:['April']}),/obligation name/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_CUSTOM_SAVE',name:'Fire NOC',months:[]}),/at least one/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_CUSTOM_SAVE',name:'Fire NOC',months:['April'],dueDay:32}),/Due day/);
    s=execute(s,'owner',{type:'CMP_CUSTOM_SAVE',name:'Fire NOC renewal',cat:'lab',freq:'Annual',dueDay:31,months:['November','April'],form:'Fire dept',buffer:45,risk:'High',checklist:'Inspect\n\nPay fee'});
    assert.match(s.audit[0].detail,/rolls over into the next month/);
    const c=cmpSlice(s).custom.at(-1);assert.deepEqual([c.months,c.buffer,c.checklist,c.urgent],[['April','November'],30,['Inspect','Pay fee'],false]);
    const v=view(s,c.id+'-Nov');assert.deepEqual([v.due,v.control.risk,v.control.buffer,v.control.internalDate,v.card.urgent],['2026-12-01','High',30,'2026-11-01',false]);
    s=execute(s,'owner',{type:'CMP_CUSTOM_SAVE',id:c.id,name:'Fire NOC renewal',cat:'lab',freq:'Annual',dueDay:0,months:['November']});
    assert.equal(view(s,c.id+'-Nov').due,'2026-11-30');assert.equal(cmpFindCard(cmpSlice(s),c.id+'-Apr'),undefined);
    for(let n=0;n<31;n++)s=execute(s,'owner',{type:'CMP_CUSTOM_SAVE',name:'Same name',months:['May'],dueDay:n});assert.equal(cmpSlice(s).custom.length,32);// no cap, no duplicate-name check
    s=execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',itemId:c.id+'-Nov',name:'noc.pdf',dataUrl:'data:application/pdf;base64,JVBERg=='});
    const fid=cmpSlice(s).files.at(-1).id;assert.ok(s.complianceFilesX[fid]);
    s=execute(s,'owner',{type:'CMP_CUSTOM_DELETE',id:c.id});assert.equal(cmpSlice(s).custom.some(o=>o.id===c.id),false);assert.equal(cmpSlice(s).files.some(f=>f.id===fid),false);
    assert.equal(s.complianceFilesX[fid],undefined);assert.match(s.audit[0].detail,/1 evidence file\(s\) removed/);});

  test('[CMP] evidence files (5 MB each): attach and remove; a file or an evidence reference completes the evidence pack',()=>{
    let s=seed();assert.deepEqual([M.cmpEvidenceCompleteness(s,'fy').doneSatisfied,M.cmpEvidenceCompleteness(s,'fy').doneTotal,M.cmpEvidenceCompleteness(s,'fy').missing.map(m=>m.id)],[47,49,['jn4','jl11']]);
    s=execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'jn4',name:'form16a.pdf',dataUrl:'data:application/pdf;base64,JVBERi0xLjQ='});
    const f=cmpSlice(s).files.at(-1);assert.deepEqual([f.itemId,f.name,f.size,f.type],['jn4','form16a.pdf',8,'application/pdf']);
    assert.deepEqual(M.cmpEvidenceCompleteness(s,'fy').missing.map(m=>m.id),['jl11']);assert.equal(M.cmpEvidenceCompleteness(s,'June').doneSatisfied,8);
    assert.throws(()=>execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'jn4',name:'big.bin',dataUrl:'data:application/octet-stream;base64,'+'A'.repeat(7*1024*1024)}),/5 MB/);
    assert.throws(()=>execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'jn4',name:'x',dataUrl:'not a file'}),/Choose a file/);
    s=execute(s,'accountant',{type:'CMP_EVIDENCE_DELETE',id:f.id});assert.equal(cmpSlice(s).files.some(e=>e.id===f.id),false);assert.equal(s.complianceFilesX[f.id],undefined);});

  test('[CMP] ≈₹ payable from HCC data: EPF / ESIC = employee + employer, PT, from the LOCKED pay run of the month; GST = output tax of issued invoices',()=>{
    const s=seed(),run=M.hrSlice(s).runs.find(r=>r.month==='2026-08'&&r.status==='Locked'),sum=f=>run.lines.reduce((a,l)=>a+f(l),0);
    const feed=M.cmpPayableFeed(s),aug=feed.months['2026-08'];
    assert.deepEqual([aug.pf,aug.esic,aug.pt],[sum(l=>l.pf+l.pfEr),sum(l=>l.esic+l.esicEr),sum(l=>l.pt)]);assert.deepEqual([aug.pf,aug.esic,aug.pt],[2432605,518100,180000]);
    assert.deepEqual(aug.sources,['Payroll Aug 2026 (locked run)']);
    assert.equal(M.cmpPayableAmount(feed,'EPF Contribution – August Wages','September',2026),24326);// the September page reads August (SCC P−1)
    assert.equal(M.cmpPayableAmount(feed,'EPF Contribution – September Wages','October',2026),null);// no locked September run yet
    const p=M.cmpPayablePanel(feed,s.date);assert.deepEqual([p.chip,p.pf,p.esic,p.pt,p.gst,p.total],['Aug 2026 (latest available)',24326,5181,1800,0,31307]);
    assert.equal(M.cmpPayableStrip(feed,s.date,'October'),null);// SCC: the strip only for a genuine current-month entry
    // GST: the GSTR-1 working summary's SAC total of the month (CGST + SGST + IGST, net of credit notes)
    const g=M.gstr1Summary(s,'2026-10-01','2026-10-31'),gst=g.sac.reduce((a,r)=>a+r.cgst+r.sgst+r.igst,0);assert.equal(feed.months['2026-10']?.gstEstimate??0,gst);
    const unlocked=structuredClone(s);unlocked.staffX.runs.find(r=>r.month==='2026-08').status='Draft';assert.equal(M.cmpPayableFeed(unlocked).months['2026-08'],undefined);});

  test('[CMP] CSV register: SCC’s 13 columns, file Compliance_<firm>_<FY>.csv, formula guard (overdue days get a leading apostrophe, as SCC)',()=>{
    const r=M.cmpCsv(seed());assert.equal(r.fileName,'Compliance_HCC_Demo_Residency_FY_2026_27.csv');assert.equal(r.rows,108);
    const lines=r.csv.split('\r\n');assert.equal(lines[0],'Firm,FY,Month,Due,Obligation,Form/Portal,Category,Frequency,Responsible,Risk,Status,Filed On,Days Left');
    assert.match(lines[1],/^HCC Demo Residency,FY 2026–27,April,30 Apr,TDS Deposit – March Deductions,.*,Done,2026-04-29,'-177$/);
    const n=structuredClone(seed());delete n.complianceX.profile;assert.equal(M.cmpCsv(n),null);});

  test('[CMP] month print (A4 landscape) and the simulated CA pack',()=>{
    const s=seed(),p=M.cmpPrintModel(s,'October');
    assert.deepEqual([p.title,p.obCount,p.doneCount,p.naCount,p.footer],['Compliance — October — HCC Demo Residency',10,4,2,'10 obligations · 4 done · 4 pending · 2 N/A']);
    assert.deepEqual(p.rows.find(r=>r.name.startsWith('GSTR-1 – Q2')),{due:'13 Oct',name:'GSTR-1 – Q2 (Jul–Sep) | QRMP Quarterly Filers',form:p.rows.find(r=>r.name.startsWith('GSTR-1 – Q2')).form,freq:'Quarterly (Q2)',responsible:'GST Executive',status:'N/A'});
    let t=execute(s,'accountant',{type:'CMP_CA_PACK',key:'p1'});const pack=cmpSlice(t).packs[0];
    assert.equal(pack.counts.overdue,1);assert.match(pack.lines[0],/^Position as on 2026-10-24 \(FY 2026–27\): 49 done, 53 pending \(1 overdue\), 6 not applicable\.$/);
    assert.ok(pack.lines.some(l=>l.startsWith('OPEN · GSTR-1 – September (Monthly Filers) · October · due 2026-10-11 · 13d overdue')));assert.match(t.audit[0].detail,/simulated/);
    assert.deepEqual(execute(t,'accountant',{type:'CMP_CA_PACK',key:'p1'}),t);for(let n=0;n<25;n++)t=execute(t,'owner',{type:'CMP_CA_PACK',key:'pp'+n});assert.equal(cmpSlice(t).packs.length,20);
    assert.ok(cmpPackLines(s).length>=12);});

  test('[CMP] HCC-only business rules are gone: no holiday / Sunday shift, no 1–28 limit, reference and NA reason optional',()=>{
    let s=seed();assert.equal(view(s,'jn8').due,'2026-06-15');// EPF May wages: 15 Jun, whatever the holiday calendar says
    assert.ok(M.hrHolidayDates(s).has('2026-08-15'));assert.equal(view(s,'ag5').due,'2026-08-15');// 15 Aug is a paid holiday in the demo: the date stays (SCC never shifts)
    s=execute(s,'owner',{type:'CMP_CUSTOM_SAVE',name:'Day 30',months:['June'],dueDay:30});assert.equal(view(s,cmpSlice(s).custom.at(-1).id+'-Jun').due,'2026-06-30');
    s=execute(s,'accountant',{type:'CMP_DONE',fy:2026,itemId:'oc4',evidenceRef:''});s=execute(s,'accountant',{type:'CMP_NA',fy:2026,itemId:'oc2'});assert.equal(rec(s,'oc2').notes,undefined);});

  test('[CMP] the W05-C calendar maps onto SCC cards (gap report table); an old save is converted on restore, idempotently, nothing lost',()=>{
    assert.deepEqual(['2026-03','2026-04','2026-09','2026-12','2027-02'].map(p=>M.cmpLegacyCard('TDS',p)),[{fy:2026,itemId:'ap1'},{fy:2026,itemId:'my1'},{fy:2026,itemId:'oc1'},{fy:2026,itemId:'jn_1'},{fy:2026,itemId:'mr1'}]);
    assert.deepEqual(M.CMP_MONTHS.map((_,i)=>M.cmpLegacyCard('PT',['2026-03','2026-04','2026-05','2026-06','2026-07','2026-08','2026-09','2026-10','2026-11','2026-12','2027-01','2027-02'][i]).itemId),
      ['ap7','my8','jn10','jl12','ag7','sp8','oc11','nv8','dc9','jn_12','fb8','mr10']);
    assert.deepEqual(M.cmpLegacyCard('GSTR-9','2025-26'),{fy:2026,itemId:'dc5'});assert.equal(M.cmpLegacyCard('XYZ','2026-04'),undefined);
    const old={obligations:[legacyOb('A','EPF','EPF contribution','Monthly',{type:'day',day:15},false),legacyOb('B','PT','PT',  'Monthly',{type:'month-end'}),
        legacyOb('C','PT','PT — annex (fictional)','Monthly',{type:'day',day:10}),legacyOb('D','GSTR-9','GSTR-9','Annual',{type:'fixed',month:12,day:31})],
      filings:[{obligation:'A',period:'2026-05',status:'Filed',by:'accountant',at:'2026-06-14T11:00',reference:'EPF-1'},{obligation:'B',period:'2026-06',status:'Not applicable',by:'accountant',at:'2026-07-30T10:00',reason:'No staff in June'},
        {obligation:'C',period:'2026-06',status:'Filed',by:'owner',at:'2026-07-09T10:00',reference:'PTA-7'},{obligation:'D',period:'2025-26',status:'Filed',by:'owner',at:'2026-12-30T10:00',reference:'G9'}],
      packs:[],settings:{caName:'Rao & Co (fictional)',caEmail:'ca@example.test',dueSoonDays:5},
      reopened:[{obligation:'B',period:'2026-05',status:'Filed',by:'accountant',at:'2026-06-29T10:00',reference:'PT-5',reopenedBy:'owner',reopenedAt:'2026-07-01T09:00',reopenReason:'Wrong ref'}]};
    const save=structuredClone(seed());save.complianceX=structuredClone(old);
    const r=M.restoreState(JSON.stringify(save)),x=r.state.complianceX;
    assert.equal(r.reset,false);assert.ok(x.records&&!x.obligations);
    assert.deepEqual([x.records['2026'].jn8.done,x.records['2026'].jn8.evidenceRef,x.records['2026'].jn8.filedOn],[true,'EPF-1','2026-06-14']);
    assert.deepEqual([x.records['2026'].jl12.na,x.records['2026'].jl12.notes],[true,'No staff in June']);
    assert.deepEqual(x.archived,['EPF Contribution Deposit','EPF Contribution']);
    assert.deepEqual([x.custom.length,x.custom[0].id,x.custom[0].dueDay,x.custom[0].cat],[1,'cust-C',10,'lab']);assert.equal(x.records['2026']['cust-C-Jul'].evidenceRef,'PTA-7');
    assert.equal(x.records['2026'].dc5.evidenceRef,'G9');assert.equal(x.settings.dueSoonDays,5);
    assert.deepEqual([x.reopened[0].itemId,x.reopened[0].part,x.reopened[0].record.evidenceRef],['jn10','done','PT-5']);
    assert.equal(x.profile.gstType,'regular');
    const again=M.restoreState(JSON.stringify(r.state)).state.complianceX;assert.deepEqual(again,x);// idempotent
    assert.equal(M.cmpSlice({...seed(),complianceX:old}).records['2026'].jn8.done,true);});

  test('[CMP] Master configuration hub: CA, window (editable) and the SCC profile / catalogue (shown, edited in the calendar)',()=>{
    const d=cmpSettingDefs(seed());assert.equal(d.length,6);assert.ok(d.every(x=>x.category==='Compliance obligations'&&x.help));
    assert.equal(d.find(x=>x.key==='cmp.profile.gstType').value,'Regular Dealer');assert.equal(d.find(x=>x.key==='cmp.profile.hasEmployees').value,'Yes');
    assert.match(d.find(x=>x.key==='cmp.catalogue').value,/^113 cards · 30 obligations · 0 custom · 0 archived$/);});

  test('[CMP] assistant: "what is due" lists the Action Center rows of the week for Owner / Accountant, refused for others',()=>{
    const s=seed();assert.equal(aiMatchIntent('What compliance filings are due?'),'compliance-due');assert.equal(aiMatchIntent('is GST filed'),'compliance-due');
    const a=aiAnswer(s,'accountant','What compliance is due this week?');assert.equal(a.intent,'compliance-due');assert.equal(a.figures[0].value,'1');assert.equal(a.figures[2].value,'11');
    assert.equal(a.items.length,4);assert.ok(a.items[0].startsWith('GSTR-1 – September (Monthly Filers) · October · due 2026-10-11 · 13d overdue'));assert.ok(a.links.some(l=>l.page==='compliance'));
    assert.equal(aiAnswer(s,'manager','What compliance filings are due?').restricted,true);
    assert.equal(aiAccess(s,'owner').compliance,true);assert.equal(aiAccess(s,'reception').compliance,false);
    assert.ok(aiSuggestions(s,'owner').some(t=>t.intent==='compliance-due'));assert.ok(!aiSuggestions(s,'manager').some(t=>t.intent==='compliance-due'));});

  test('[CMP] existing assistant intents unchanged by the compliance keywords',()=>{
    for(const [q,i] of [['Who has the biggest balances due','balances'],['Collections vs outstanding today','collections'],['What approvals are pending?','approvals'],['How is the cash drawer and any variance?','cash'],
      ['Which balances are due this week?','collections'],['What payments are due this week?','collections'],['Are there expenses due this week?','collections'],['Which expenses were filed today?','expenses-week'],['What is due today?','collections']])
      assert.equal(aiMatchIntent(q),i,q);
    for(const q of ['Is GSTR-3B filed?','When is TDS due?','PF and ESIC due this week','Is professional tax paid?','Which returns are due?','Any filing due this week?','Is PT due?'])assert.equal(aiMatchIntent(q),'compliance-due',q);});
}
