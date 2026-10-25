// Wave 0.5 · W05-C Compliance calendar — fixture extensions (fictional data only). Due rules from SCC tax module (see domain/streams/compliance.ts).
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import type {CMPObligation,CMPFiling,CMPState} from '../../domain/streams/compliance';
import {CMP_DEFAULT_SETTINGS,cmpPeriods,cmpDueDate} from '../../domain/streams/compliance';
import {addDays} from '../../domain/money';

const START='2000-01-01';
export function cmpFixtureObligations():CMPObligation[]{
  const o=(id:string,kind:CMPObligation['kind'],name:string,form:string,freq:CMPObligation['freq'],rule:CMPObligation['rules'][0]['rule'],owner:string,from:string,note?:string):CMPObligation=>
    ({id,kind,name,form,freq,rules:[{from:START,rule}],owner,from,active:true,...(note?{note}:{}),updatedBy:'owner',updatedAt:'2026-04-01T09:00'});
  return [
    o('CMPO-TDS','TDS','TDS deposit','Challan 281 · TIN-NSDL','Monthly',{type:'day',day:7,special:{month:3,day:30}},'Accountant','2026-04','Salary, rent and professional-fee deductions. March deductions have an extended date (SCC).'),
    o('CMPO-G1','GSTR-1','GSTR-1 · outward supplies','GST portal','Monthly',{type:'day',day:11},'Accountant','2026-04','Monthly filer assumed (not QRMP).'),
    o('CMPO-G3B','GSTR-3B','GSTR-3B · summary return and tax','GST portal','Monthly',{type:'day',day:20},'Accountant','2026-04'),
    o('CMPO-G9','GSTR-9','GSTR-9 · annual return','GST portal','Annual',{type:'fixed',month:12,day:31},'Owner','2025-26','Filed with the CA after the audit of the year.'),
    o('CMPO-EPF','EPF','EPF contribution','EPFO unified portal · ECR','Monthly',{type:'day',day:15},'Accountant','2026-04'),
    o('CMPO-ESIC','ESIC','ESIC contribution','ESIC portal · challan','Monthly',{type:'day',day:15},'Accountant','2026-04'),
    o('CMPO-PT','PT','Professional Tax (Maharashtra)','State PT portal · Form III-B','Monthly',{type:'month-end'},'Accountant','2026-04'),
  ];
}
export function cmpFixtureFilings(s:State,obligations:CMPObligation[]):CMPFiling[]{
  const out:CMPFiling[]=[];
  // Left open on purpose: GSTR-1 for September (overdue), PT for September (due within the week) and GSTR-9 for FY 2025-26.
  const open=new Set(['CMPO-G1:2026-09','CMPO-PT:2026-09','CMPO-G9:2025-26']);
  for(const o of obligations)for(const p of cmpPeriods(o,s.date)){
    if(open.has(`${o.id}:${p}`))continue;
    const due=cmpDueDate(o,p),at=addDays(due,-1)+'T11:00';
    if(o.id==='CMPO-ESIC'&&(p==='2026-04'||p==='2026-05')){out.push({obligation:o.id,period:p,status:'Not applicable',by:'accountant',at,reason:'Staff count below the ESIC threshold until June (illustrative)'});continue;}
    out.push({obligation:o.id,period:p,status:'Filed',by:'accountant',at,reference:`${o.kind}-${p.replace('-','')}-${String(out.length*7919%9000+1000)}`});
  }
  return out;
}
export function extendBase(s:State):void{
  const obligations=cmpFixtureObligations();
  const x:CMPState={obligations,filings:cmpFixtureFilings(s,obligations),packs:[],settings:{...CMP_DEFAULT_SETTINGS}};
  s.complianceX=x;
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];
