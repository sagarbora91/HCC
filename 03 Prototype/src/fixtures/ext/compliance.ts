// Compliance calendar fixture (fictional data only) — SCC tax module in full (DEC-116).
// The W05-C demo filings (TDS, GSTR-1, GSTR-3B, EPF, ESIC, PT for Apr–Sep 2026 and GSTR-9 for FY 2025-26) are kept in their old shape and
// mapped onto SCC's cards by cmpFromLegacy — the same path an old save takes — so the demo proves nothing recorded is lost. Every other SCC card
// of FY 2026–27 due before the business date is filed the day before (QRMP cards: not applicable for a monthly filer), except GSTR-1 for
// September (overdue). PT for September and GSTR-9 stay open as before. Two filings carry no evidence (the completeness meter shows them).
import {dobEvidenceRef,dobParseDataUrl} from '../../domain/streams/docobjects';
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import type {CMPLegacyObligation,CMPLegacyFiling,CMPState} from '../../domain/streams/compliance';
import {CMP_DEFAULT_SETTINGS,CMP_MONTHS,cmpFromLegacy,cmpLegacyCard,cmpFindCard,cmpParseDue,cmpMonthCards,cmpGstFilingMode,cmpRecord} from '../../domain/streams/compliance';
import {addDays} from '../../domain/money';

const START='2000-01-01';
/** The W05-C obligations as they were stored before the SCC port (old shape). */
export function cmpFixtureLegacyObligations():CMPLegacyObligation[]{
  const o=(id:string,kind:string,name:string,form:string,freq:'Monthly'|'Annual',rule:CMPLegacyObligation['rules'][0]['rule'],owner:string,from:string,note?:string):CMPLegacyObligation=>
    ({id,kind,name,form,freq,rules:[{from:START,rule}],owner,from,active:true,...(note?{note}:{})});
  return [
    o('CMPO-TDS','TDS','TDS deposit','Challan 281 · TIN-NSDL','Monthly',{type:'day',day:7,special:{month:3,day:30}},'Accountant','2026-04'),
    o('CMPO-G1','GSTR-1','GSTR-1 · outward supplies','GST portal','Monthly',{type:'day',day:11},'Accountant','2026-04'),
    o('CMPO-G3B','GSTR-3B','GSTR-3B · summary return and tax','GST portal','Monthly',{type:'day',day:20},'Accountant','2026-04'),
    o('CMPO-G9','GSTR-9','GSTR-9 · annual return','GST portal','Annual',{type:'fixed',month:12,day:31},'Owner','2025-26'),
    o('CMPO-EPF','EPF','EPF contribution','EPFO unified portal · ECR','Monthly',{type:'day',day:15},'Accountant','2026-04'),
    o('CMPO-ESIC','ESIC','ESIC contribution','ESIC portal · challan','Monthly',{type:'day',day:15},'Accountant','2026-04'),
    o('CMPO-PT','PT','Professional Tax (Maharashtra)','State PT portal · Form III-B','Monthly',{type:'month-end'},'Accountant','2026-04'),
  ];
}
/** The W05-C demo filings: Apr–Sep 2026 (monthly) and FY 2025-26 (GSTR-9), filed the day before the due date with a reference. */
export function cmpFixtureLegacyFilings(obligations:CMPLegacyObligation[]):CMPLegacyFiling[]{
  const out:CMPLegacyFiling[]=[];
  const open=new Set(['CMPO-G1:2026-09','CMPO-PT:2026-09','CMPO-G9:2025-26']);
  for(const o of obligations){
    const periods=o.freq==='Annual'?['2025-26']:['2026-04','2026-05','2026-06','2026-07','2026-08','2026-09'];
    for(const p of periods){
      if(open.has(`${o.id}:${p}`))continue;
      const w=cmpLegacyCard(o.kind,p)!,card=cmpFindCard({custom:[]},w.itemId)!,at=addDays(cmpParseDue(card.due,w.fy)!,-1)+'T11:00';
      if(o.id==='CMPO-ESIC'&&(p==='2026-04'||p==='2026-05')){out.push({obligation:o.id,period:p,status:'Not applicable',by:'accountant',at,reason:'Staff count below the ESIC threshold until June (illustrative)'});continue;}
      out.push({obligation:o.id,period:p,status:'Filed',by:'accountant',at,reference:`${o.kind}-${p.replace('-','')}-${String(out.length*7919%9000+1000)}`});
    }
  }
  return out;
}
/** SCC cards left open on purpose (besides the legacy ones above). */
const OPEN=new Set(['oc4']);
/** Filed without an evidence reference (Evidence Pack Completeness shows them as missing). */
const NO_EVIDENCE=new Set(['jn4','jl11']);
export function cmpFixtureState(s:State):CMPState{
  const obligations=cmpFixtureLegacyObligations();
  const x=cmpFromLegacy({obligations,filings:cmpFixtureLegacyFilings(obligations),packs:[],settings:{...CMP_DEFAULT_SETTINGS}},s);
  // DEC-133: fictional licence numbers and expiry dates in FY 2027–28 (the FY 2026–27 demo calendar stays SCC's own); the renewal cards show on
  // the May and December pages of FY 2027–28 — the year the calendar moves to by itself on 1 April 2027 (DEC-125).
  x.profile={name:s.settings.hotel,pan:'ABCDE1234F',gstin:'27ABCDE1234F1Z5',entity:'partnership',gstType:'regular',hasEmployees:true,qrmp:false,
    notes:'Fictional demo firm profile (one per property).',fssaiNo:'DEMO-FSSAI-0001',fssaiExpiry:'2027-05-31',lodgingNo:'DEMO-LODGING-0001',lodgingExpiry:'2027-12-31',
    updatedBy:'owner',updatedAt:'2026-04-01T09:00'};
  const fy=2026,b=(x.records[String(fy)]??={});
  for(const m of CMP_MONTHS)for(const c of cmpMonthCards(x,m)){
    const due=cmpParseDue(c.due,fy);if(!due||due>=s.date||OPEN.has(c.id)||cmpRecord(x,fy,c.id))continue;
    if(cmpGstFilingMode(c)==='qrmp'){b[c.id]={na:true,notes:'Monthly filer — the QRMP quarterly return does not apply.',naBy:'accountant',naAt:due+'T10:00'};continue;}
    const filedOn=addDays(due,-1);
    b[c.id]={done:true,filedOn,notes:'',evidenceRef:NO_EVIDENCE.has(c.id)?'':`ACK-${c.id.toUpperCase()}-${filedOn.replace(/-/g,'')}`,reviewerNote:'Owner',dueDate:due,by:'accountant',at:filedOn+'T11:00'};
  }
  return x;
}
/** One small fictional evidence file on TDS deposit — September deductions (the October page). */
const DEMO_FILE='data:text/plain;base64,Q2hhbGxhbiAyODEg4oCUIFREUyBkZXBvc2l0LCBTZXB0ZW1iZXIgMjAyNiBkZWR1Y3Rpb25zIChmaWN0aW9uYWwgZGVtbyBldmlkZW5jZSk=';
export function extendBase(s:State):void{
  const x=cmpFixtureState(s);
  x.files=[{id:'CMPF-DEMO-1',fy:2026,itemId:'oc1',name:'challan-281-sep-2026.txt',size:80,type:'text/plain',addedBy:'accountant',addedAt:'2026-10-06T11:05',objectRef:dobEvidenceRef('CMPF-DEMO-1')}];
  s.complianceX=x;s.docObjectsX={...(s.docObjectsX??{}),[dobEvidenceRef('CMPF-DEMO-1')]:{ref:dobEvidenceRef('CMPF-DEMO-1'),kind:'compliance-evidence',owner:'CMPF-DEMO-1',contentType:'text/plain',bytes:dobParseDataUrl(DEMO_FILE)?.bytes??0,dataUrl:DEMO_FILE,at:s.clock,by:'accountant'}}; // LF-docdb: the demo's evidence content lives in the documents mirror (SCHEMA 13)
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];
