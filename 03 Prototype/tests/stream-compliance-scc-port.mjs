// SCC tax port (DEC-116): HCC's compliance calendar computes exactly what SCC's tax module computes. Oracle = SCC's OWN functions, copied verbatim
// from SCC tax/index.html into tests/_scc-tax-oracle.mjs, and the golden table tests/_scc-tax-golden.json that SCC's own code produced on SCC's own
// catalogue (both written by tools/scc-tax-catalogue.mjs). Names prefixed [SCC-TAX].
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {sccTaxOracle} from './_scc-tax-oracle.mjs';

const G=JSON.parse(readFileSync(new URL('./_scc-tax-golden.json',import.meta.url),'utf8'));
const sha=s=>createHash('sha256').update(s).digest('hex');
const iso=d=>d?d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0'):null;

export function register({test,M,assert}){
  const D=M.CMP_SCC_D;
  const cards=()=>M.CMP_MONTHS.flatMap(m=>D[m].items.map(c=>({month:m,card:c})));
  /** SCC STATE for an HCC compliance slice (one firm = the property's profile). */
  const sccState=(x)=>({firms:x.profile?[{id:'f',name:x.profile.name,type:x.profile.gstType,hasEmployees:x.profile.hasEmployees,qrmp:x.profile.qrmp,pan:x.profile.pan,gstin:x.profile.gstin}]:[],
    activeFirmId:x.profile?'f':null,fyStartYear:x.fyStart,compliance:{f:JSON.parse(JSON.stringify(x.records))},custom:x.custom.map(c=>({...c,firmId:null})),archived:[...x.archived],archivedCats:[...x.archivedCats]});
  const oracle=(x,today,feed)=>sccTaxOracle({STATE:sccState(x),today,D,feed});
  const slice=(over={})=>({...M.cmpEmpty(),...over});

  test('[SCC-TAX] the catalogue is SCC’s verbatim: same sha256 as SCC’s D, 113 cards, 30 obligation keys; the server copy is identical',()=>{
    assert.equal(sha(JSON.stringify(D)),G.sha256);assert.equal(cards().length,113);assert.equal(G.cards,113);
    assert.equal(M.cmpBuiltInObligations().length,30);assert.equal(G.keys,30);
    assert.deepEqual(Object.keys(D),[...M.CMP_MONTHS]);
    const server=JSON.parse(readFileSync(new URL('../../08 Production/src/Hcc.Domain/Compliance/SccTaxCatalogue.json',import.meta.url),'utf8'));
    assert.equal(JSON.stringify(server),JSON.stringify(D));});

  test('[SCC-TAX] all 113 due dates for FY 2026–27 … 2032–33 equal SCC’s parseDueDate (golden + verbatim oracle)',()=>{
    let n=0;
    for(let fy=2026;fy<=2032;fy++){
      const o=sccTaxOracle({STATE:{firms:[],fyStartYear:fy,compliance:{},custom:[],archived:[],archivedCats:[]},today:'2026-10-24',D});
      const rows=G.fy[fy];assert.equal(rows.length,113);
      cards().forEach(({month,card},i)=>{const [gm,gid,gdue,giso]=rows[i];
        assert.deepEqual([month,card.id,card.due],[gm,gid,gdue]);
        assert.equal(M.cmpParseDue(card.due,fy),giso,`${fy} ${card.id}`);assert.equal(iso(o.parseDueDate(card.due,month)),giso);n++;});
    }
    assert.equal(n,791);
    // spot checks quoted in the gap report: March TDS 30 Apr, GSTR-4 on the April page due 30 Jun, PT "Last day of Feb" in a leap year, GSTR-9 31 Dec
    assert.equal(M.cmpParseDue('30 Apr',2026),'2026-04-30');assert.equal(M.cmpParseDue('30 Jun',2026),'2026-06-30');
    assert.equal(M.cmpParseDue('Last day of Feb',2027),'2028-02-29');assert.equal(M.cmpParseDue('By end of Feb',2026),'2027-02-28');assert.equal(M.cmpParseDue('31 Dec',2026),'2026-12-31');
    assert.equal(M.cmpParseDue('someday',2026),null);assert.equal(M.cmpParseDue('',2026),null);});

  test('[SCC-TAX] per card: obligation key, payroll item, risk, cash flow, responsible, reviewer, T-buffer, ≈₹ field and GST filing mode equal SCC',()=>{
    const byId=new Map(cards().map(c=>[c.card.id,c]));
    for(const [id,key,payroll,risk,cash,resp,rev,buffer,field,mode] of G.cardRules){
      const {card}=byId.get(id),p=M.cmpControlProfile(card,2026,'2026-10-24');
      assert.deepEqual([M.cmpObligationKey(card),M.cmpIsPayrollItem(card)?1:0,p.risk,p.cashFlow,p.responsible,p.reviewer,p.buffer,M.cmpTaxPayableField(card.name),M.cmpGstFilingMode(card)],
        [key,payroll,risk,cash,resp,rev,buffer,field,mode],id);
    }
    // the name-substring risk classes SCC uses (owner question 7)
    const risk=id=>M.cmpControlProfile(byId.get(id).card,2026,'2026-10-24').risk;
    assert.deepEqual(['jl3','oc3','mr4','ap4','sp3'].map(risk),['Low','Low','Low','Low','Low']);
    assert.deepEqual(['fb3','mr3'].map(risk),['High','High']);assert.deepEqual(['ap2','dc5','dc6'].map(risk),['Medium','Medium','Medium']);});

  test('[SCC-TAX] on 24 Oct 2026 (FY 2026–27): days left, the days tag, the internal deadline and the escalation of every card equal SCC',()=>{
    const byId=new Map(cards().map(c=>[c.card.id,c]));
    const o=sccTaxOracle({STATE:{firms:[],fyStartYear:2026,compliance:{},custom:[],archived:[],archivedCats:[]},today:'2026-10-24',D});
    for(const [id,days,tag,internal,esc] of G.control.rows){
      const {card,month}=byId.get(id),d=M.cmpDaysLeft(card.due,2026,'2026-10-24'),p=M.cmpControlProfile(card,2026,'2026-10-24');
      assert.deepEqual([d,M.cmpDaysTag(d,false)?.text,p.internalDate,p.escalation.label],[days,tag,internal,esc],id);
      const op=o.getControlProfile(card,month);assert.equal(iso(op.internalDate),p.internalDate);assert.equal(op.escalation.note,p.escalation.note);
      assert.deepEqual(op.checklist,p.checklist);assert.deepEqual(op.workflow,p.workflow);assert.deepEqual(op.evidence,p.evidence);
    }});

  test('[SCC-TAX] days tag bands (overdue / due today / ≤ 7 / ≤ 15 / later / filed) equal SCC daysLeftTag',()=>{
    const o=sccTaxOracle({STATE:{firms:[],fyStartYear:2026,compliance:{},custom:[],archived:[],archivedCats:[]},today:'2026-10-24',D});
    for(let d=-40;d<=40;d++)assert.equal(M.cmpDaysTag(d,false).text,o.stripHtml(o.daysLeftTag(d,false)),String(d));
    assert.equal(M.cmpDaysTag(5,true).text,o.stripHtml(o.daysLeftTag(5,true)));assert.equal(M.cmpDaysTag(null,false),null);assert.equal(o.daysLeftTag(null,false),'');
    assert.deepEqual([7,8,15,16].map(d=>M.cmpDaysTag(d,false).cls),['urgent-days','soon','soon','ok']);});

  test('[SCC-TAX] applicability: GST type × has employees (and no firm) shows exactly SCC’s cards; QRMP is advisory only',()=>{
    assert.equal(M.cmpVisibleItems(slice(),cards().map(c=>c.card)).map(c=>c.id).join(' '),G.visible.none);
    for(const type of ['regular','composition','both','unregistered'])for(const emp of [true,false]){
      const x=slice({profile:{name:'F',pan:'',gstin:'',entity:'proprietorship',gstType:type,hasEmployees:emp,qrmp:false,notes:''}});
      assert.equal(M.cmpVisibleItems(x,cards().map(c=>c.card)).map(c=>c.id).join(' '),G.visible[`${type}|${emp?'emp':'noemp'}`],type+emp);
      for(const qrmp of [true,false])assert.equal(M.cmpVisibleItems({...x,profile:{...x.profile,qrmp}},cards().map(c=>c.card)).length,M.cmpVisibleItems(x,cards().map(c=>c.card)).length);
    }
    const n=k=>G.visible[k].split(' ').length;
    assert.deepEqual([n('regular|emp'),n('composition|emp'),n('both|emp'),n('unregistered|emp'),n('regular|noemp'),n('both|noemp')],[108,80,113,75,68,73]);});

  test('[SCC-TAX] QRMP advisory texts per card (regular / both × QRMP on / off) equal SCC',()=>{
    for(const type of ['regular','both'])for(const qrmp of [true,false]){
      const prof={gstType:type,qrmp},got=cards().map(({card})=>[card.id,M.cmpQrmpAdvisory(card,prof)?.text??'']).filter(r=>r[1]);
      assert.deepEqual(got,G.qrmp[`${type}|${qrmp?'qrmp':'monthly'}`],type+qrmp);}
    assert.equal(M.cmpQrmpAdvisory(cards()[2].card,undefined),null);});

  test('[SCC-TAX] FY text rebasing and the Income-tax Act 2025 renames of every text equal SCC (FY 2026–27 … 2029–30)',()=>{
    for(const fy of [2026,2027,2028]){
      const rendered=M.CMP_MONTHS.map(m=>[M.cmpRenderFYText(D[m].note,fy),...D[m].items.map(i=>['name','form','who','what','why','penalty'].map(k=>M.cmpRenderFYText(i[k],fy)))]);
      assert.equal(sha(rendered.flat(2).join('')),G.text[fy].sha256,String(fy));
      const t=G.text[fy];assert.equal(M.cmpRenderFYText(D.April.note,fy),t.aprilNote);assert.equal(M.cmpRenderFYText('January – March 2025',fy),t.q4);
      for(const id of ['ap4','my2','jn3','dc5'])assert.equal(M.cmpRenderFYText(cards().find(c=>c.card.id===id).card.name,fy),t[id]);
    }
    const o=sccTaxOracle({STATE:{firms:[],fyStartYear:2029,compliance:{},custom:[],archived:[],archivedCats:[]},today:'2026-10-24',D});
    for(const m of M.CMP_MONTHS){assert.equal(M.cmpRenderFYText(D[m].note,2029),o.renderFYText(D[m].note));for(const i of D[m].items)for(const k of ['name','form','who','what','why','penalty'])assert.equal(M.cmpRenderFYText(i[k],2029),o.renderFYText(i[k]));}
    // the renames quoted in the gap report (owner question 4: they also hit prior-year returns and the April note itself)
    assert.equal(G.text[2026].my2,'TDS Return – Q4 (January–March) | Form 138 & 26Q'); // SCC renames only "Form 26Q": the bare 26Q stays (owner question 4)assert.equal(G.text[2026].jn3,'Form 130 – Salary TDS Certificate (FY 2025–26)');
    assert.equal(G.text[2026].dc5,'GSTR-9 – Annual GST Return (FY 2025–26)');assert.match(G.text[2026].aprilNote,/\(replacing Tax Year \/ Tax Year\)/);
    for(const s of ['Form 16A and Form 16','Section 200 / u/s 192 / Sec. 192','194I, 194J, u/s 234B, u/s 234C, u/s 234E','80C and 80D','u/s 201(1A) and u/s 271H and u/s 40(a)(ia)','Income Tax Act 1961','Form 24Q, 26Q, Form 26AS','₹1,92,000 on 15 Mar'])
      assert.equal(M.cmpRenderNewActForms(s),o.renderNewActForms(s),s);});

  test('[SCC-TAX] custom obligations expand as SCC: due day 0 = last day, 1–31 in the ticked month (31 Apr rolls over to 1 May), never urgent',()=>{
    const custom=[{id:'cust-1',cat:'lab',name:'PF inspection <b>&</b> “TDS” check',freq:'Monthly',dueDay:31,months:['April','May','February'],form:'Portal',who:'',what:'',why:'',penalty:'',responsible:'',reviewer:'',risk:'',buffer:null,checklist:[],workflow:[],evidence:[],urgent:false},
      {id:'cust-2',cat:'it',name:'Board minutes',freq:'Annual',dueDay:0,months:['February'],form:'',who:'',what:'',why:'',penalty:'',responsible:'CA',reviewer:'Owner',risk:'Low',buffer:12,checklist:['a'],workflow:[],evidence:['b'],urgent:false}];
    for(const fy of [2026,2027]){
      const x=slice({fyStart:fy,custom}),o=oracle(x,'2026-10-24');
      for(const m of M.CMP_MONTHS){const h=M.cmpExpandCustom(x,m),s=o.expandCustom(m);assert.deepEqual(h.map(c=>[c.id,c._gid,c.due,c.cat,c.urgent]),s.map(c=>[c.id,c._gid,c.due,c.cat,c.urgent]),m);
        h.forEach((c,i)=>{assert.equal(M.cmpParseDue(c.due,fy),iso(o.parseDueDate(s[i].due,m)));const hp=M.cmpControlProfile(c,fy,'2026-10-24'),sp=o.getControlProfile(s[i],m);
          assert.deepEqual([hp.risk,hp.responsible,hp.reviewer,hp.buffer,iso(sp.internalDate)],[sp.risk,o.stripHtml(sp.responsible),o.stripHtml(sp.reviewer),sp.buffer,hp.internalDate]);});}
    }
    const x=slice({custom});
    assert.equal(M.cmpParseDue(M.cmpExpandCustom(x,'April')[0].due,2026),'2026-05-01');assert.equal(M.cmpParseDue(M.cmpExpandCustom(x,'February')[0].due,2026),'2027-03-03');
    assert.equal(M.cmpParseDue(M.cmpExpandCustom(x,'February')[1].due,2027),'2028-02-29');assert.equal(M.cmpObligationKey(M.cmpExpandCustom(x,'May')[0]),'cust-1');});

  test('[SCC-TAX] archive keys as SCC: "EPF Contribution" leaves April’s "EPF Contribution Deposit" card; LWF has two keys; a category hides built-in + custom',()=>{
    const all=cards().map(c=>c.card),prof={name:'F',pan:'',gstin:'',entity:'proprietorship',gstType:'both',hasEmployees:true,qrmp:false,notes:''};
    const x=slice({profile:prof,archived:['EPF Contribution','Labour Welfare Fund (LWF)']}),o=oracle(x,'2026-10-24');
    const vis=M.cmpVisibleItems(x,all).map(c=>c.id);assert.deepEqual(vis,o.getVisibleItems(all).map(c=>c.id));
    assert.ok(vis.includes('ap5')&&!vis.includes('my5')&&!vis.includes('jl11')&&vis.includes('jn_10'));
    const keys=M.cmpBuiltInObligations().map(o=>o.key);for(const k of ['EPF Contribution Deposit','EPF Contribution','ESIC Contribution Deposit','ESIC Contribution','Labour Welfare Fund (LWF)','Labour Welfare Fund'])assert.ok(keys.includes(k),k);
    const y=slice({profile:prof,archivedCats:['lab'],custom:[{id:'cust-9',cat:'lab',name:'X',freq:'Monthly',dueDay:5,months:[...M.CMP_MONTHS],form:'',who:'',what:'',why:'',penalty:'',responsible:'',reviewer:'',risk:'',buffer:null,checklist:[],workflow:[],evidence:[],urgent:false}]});
    for(const m of M.CMP_MONTHS){const items=M.cmpMonthItems(y,m);assert.deepEqual(M.cmpVisibleItems(y,items).map(c=>c.id),oracle(y,'2026-10-24').getVisibleItems(oracle(y,'2026-10-24').getMonthItems(m)).map(c=>c.id));
      assert.ok(M.cmpVisibleItems(y,items).every(c=>c.cat!=='lab'));}});

  test('[SCC-TAX] the demo calendar equals SCC page by page: visible cards, done / NA, overdue dots, Action Center (30 days), dashboard quarters, CSV register',()=>{
    const s=M.seed(),x=M.cmpSlice(s),o=oracle(x,s.date);
    for(const m of M.CMP_MONTHS){
      const p=M.cmpMonthPage(s,m),sv=o.getVisibleItems(o.getMonthItems(m));
      assert.deepEqual(p.all.map(v=>v.card.id),sv.map(i=>i.id),m);
      assert.deepEqual(p.all.map(v=>[v.isNA,v.isDone]),sv.map(i=>{const na=o.isItemNA(i.id);return [na,!na&&!!o.getItemRecord(i.id)?.done];}),m);
      const active=sv.filter(i=>!o.isItemNA(i.id));
      assert.deepEqual([p.stats.total,p.stats.completed,p.stats.pending,p.stats.highRisk,p.stats.na,p.stats.overdue],
        [active.length,active.filter(i=>o.isItemDone(i.id)).length,active.filter(i=>!o.isItemDone(i.id)).length,active.filter(i=>o.getControlProfile(i,m).risk==='High').length,
         sv.length-active.length,active.filter(i=>!o.isItemDone(i.id)&&(o.getDaysLeft(i.due,m)??999)<0).length],m);
      assert.equal(M.cmpHasOverdueInMonth(s,m),o.hasOverdueInMonth(m),m);
    }
    assert.deepEqual(M.cmpActionCenter(s).map(r=>[r.itemId,r.month,r.days]),o.collectOutstanding().map(r=>[r.itemId,r.month,r.days]));
    for(const q of ['Q1','Q2','Q3','Q4']){const h=M.cmpQuarter(s,q),d=o.dashboard(q);
      assert.deepEqual(h.quarters.map(z=>[z.key,z.total,z.score]),d.quarterData.map(z=>[z.key,z.total,z.score]));
      assert.deepEqual([h.q.total,h.q.done,h.q.pending,h.q.overdue,h.q.highRisk,h.q.score],[d.cq.total,d.cq.done,d.cq.pending,d.cq.overdue,d.cq.highRisk,d.cq.score],q);
      assert.deepEqual(h.cash,d.cashExposure);assert.deepEqual(h.ownerLoad,Object.entries(d.ownerLoad));assert.deepEqual(h.monthly,d.monthlyRows);
      assert.deepEqual(h.focus.map(i=>i.card.id),d.focusItems.map(i=>i.id),q);}
    const c=o.csv();assert.deepEqual(M.cmpCsvRows(s),c.rows);
    for(const r of c.rows)for(const v of r)assert.equal(M.cmpCsvCell(v),c.cell(v));
    for(const v of ['=1+1',' -3','@x','+9','\t=cmd','"quoted"','a,b','plain'])assert.equal(M.cmpCsvCell(v),c.cell(v),v);});

  test('[SCC-TAX] ≈₹ chips read the month before the page, the Statutory Payable panel the current month (else the newest) — as SCC',()=>{
    const feedScc={'2026-08':{pf:24326.05,esic:5181,pt:1800,gstEstimate:0},'2026-09':{pf:24000,esic:5000,pt:1700,gstEstimate:91234.6},'2026-10':{pf:0,esic:0,pt:0,gstEstimate:500},_note:'x'};
    const feed={note:'x',months:Object.fromEntries(Object.entries(feedScc).filter(([k])=>k!=='_note').map(([k,v])=>[k,{pf:Math.round(v.pf*100),esic:v.esic*100,pt:v.pt*100,gstEstimate:Math.round(v.gstEstimate*100),sources:[],at:'2026-10-24'}]))};
    const x=slice(),o=sccTaxOracle({STATE:{...sccState(x)},today:'2026-10-24',D,feed:feedScc});
    for(const {month,card} of cards())assert.equal(M.cmpPayableAmount(feed,card.name,month,2026),o.taxPayableAmount(card.name,month),card.id);
    assert.equal(M.cmpPayableAmount(feed,'EPF Contribution – August Wages','September',2026),24326);assert.equal(M.cmpPayableAmount(feed,'GSTR-3B – September (Monthly Filers)','October',2026),91235);
    assert.equal(M.cmpPayableAmount(feed,'GSTR-3B – Q2 (Jul–Sep) | QRMP Quarterly Filers','October',2026),null);
    const sp=M.cmpStatutoryPayable(feed,'2026-10-24'),os=o.getStatutoryPayable();assert.deepEqual([sp.ym,sp.isCurrent],[os.ym,os.isCurrent]);
    const late=sccTaxOracle({STATE:{...sccState(x)},today:'2026-12-02',D,feed:feedScc}).getStatutoryPayable(),hl=M.cmpStatutoryPayable(feed,'2026-12-02');
    assert.deepEqual([hl.ym,hl.isCurrent],[late.ym,late.isCurrent]);assert.deepEqual([hl.ym,hl.isCurrent],['2026-10',false]);
    assert.equal(M.cmpPayableStrip(feed,'2026-10-24','October').total,500);assert.equal(M.cmpPayableStrip(feed,'2026-10-24','September'),null);});

  test('[SCC-TAX] evidence ZIP folders and file names follow SCC (Month_Category_Name__itemId, de-duplicated names, index.csv)',()=>{
    let s=M.seed();const o=oracle(M.cmpSlice(s),s.date);
    for(const n of [1,2])s=M.execute(s,'accountant',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'oc1',name:'challan-281-sep-2026.txt',dataUrl:'data:text/plain;base64,QQ=='});
    const plan=M.cmpEvidenceZipPlan(s),folder=o.sanitize('October_Income Tax_TDS Deposit – September Deductions').slice(0,60)+'__'+o.sanitize('oc1').slice(0,24);
    assert.deepEqual(plan.entries.map(e=>e.path),[folder+'/challan-281-sep-2026.txt',folder+'/challan-281-sep-2026 (2).txt',folder+'/challan-281-sep-2026 (3).txt']);
    assert.equal(plan.fileName,'Evidence_HCC_Demo_Residency_2026_27.zip');
    assert.match(plan.index,/^﻿Obligation,Original filename,Stored path,Added-on\r\nTDS Deposit – September Deductions,challan-281-sep-2026\.txt,/);});
}
