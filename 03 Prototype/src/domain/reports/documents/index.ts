// F7-R7 · the document registry: every legal / Lodge-look document of batch 3, built by a pure builder from State (model.ts). `docView` says whether the caller
// may print it at all (the print log checks it: rule V.4); the builders throw FORBIDDEN / NOT_FOUND themselves. Ids never change.
import type {State} from '../../types';
import {HccError} from '../../errors';
import {registrationCardLayout,dayEndLayout} from '../guests/layouts';
import {type DocModel,docResult,docHtml,canAny,docMoneyRole,holds} from './model';
import {folioDocument,creditNoteDocument,receiptDocument,quotationDocument} from './folio';
import {payslipDocument} from './payslip';
import {kotDocument,outletBillDocument} from './pos';
import {rateCardDocument} from './ratecard';
import {handoverPackDocument} from './handover';
import {hovHasAccess} from '../../streams/handover';
import {trcCan} from '../../streams/tariffcard';
import {hrSlice,hrCanPay,hrStaffOfUser} from '../../streams/staff';

export {docResult,docHtml,folioDocument,creditNoteDocument,receiptDocument,quotationDocument,payslipDocument,kotDocument,outletBillDocument,rateCardDocument,handoverPackDocument};
export type {DocModel};
export type DocParams=Record<string,string|undefined>;
export type DocDef={id:string;title:string;/** Params the builder needs (all strings). */params:string[];money:boolean;view:(s:State,uid:string)=>boolean;build:(s:State,uid:string,p:DocParams)=>DocModel};
const need=(p:DocParams,k:string)=>{const v=p[k];if(!v)throw new HccError('VALIDATION',`Missing ${k}.`);return v;};
const lodge=(l:ReturnType<typeof registrationCardLayout>,id:string,title:string,number:string,s:State,money:boolean):DocModel=>({id,title,number,property:s.settings.hotel,pairs:[],tables:[],notes:[],sign:[],money,basis:'Business date.',lodge:l});

export const DOCUMENTS:DocDef[]=[
  {id:'doc.registration-card',title:'Guest registration card',params:['booking'],money:false,view:(s,uid)=>holds(s,uid,'PERM-STY-001')||holds(s,uid,'PERM-RPT-001'),
    build:(s,uid,p)=>lodge(registrationCardLayout(s,uid,need(p,'booking')),'doc.registration-card','Guest registration card',need(p,'booking'),s,false)},
  {id:'doc.day-end',title:'Day end report',params:['date'],money:true,view:(s,uid)=>holds(s,uid,'PERM-RPT-001'),
    build:(s,uid,p)=>lodge(dayEndLayout(s,uid,p.date??s.date),'doc.day-end','Day end report',p.date??s.date,s,holds(s,uid,'PERM-RPT-002'))},
  {id:'doc.folio',title:'Guest folio',params:['booking'],money:true,view:(s,uid)=>canAny(s,uid,['billing','payments']),build:(s,uid,p)=>folioDocument(s,uid,need(p,'booking'),'folio')},
  {id:'doc.invoice',title:'Tax invoice',params:['booking'],money:true,view:(s,uid)=>canAny(s,uid,['billing','payments']),build:(s,uid,p)=>folioDocument(s,uid,need(p,'booking'),'invoice')},
  {id:'doc.credit-note',title:'Credit note',params:['booking','creditNote'],money:true,view:(s,uid)=>canAny(s,uid,['billing','payments']),build:(s,uid,p)=>creditNoteDocument(s,uid,need(p,'booking'),need(p,'creditNote'))},
  {id:'doc.receipt',title:'Receipt',params:['booking','receipt'],money:true,view:(s,uid)=>canAny(s,uid,['billing','payments']),build:(s,uid,p)=>receiptDocument(s,uid,need(p,'booking'),need(p,'receipt'))},
  {id:'doc.quotation',title:'Quotation',params:['booking'],money:true,view:(s,uid)=>canAny(s,uid,['reservations','billing']),build:(s,uid,p)=>quotationDocument(s,uid,need(p,'booking'))},
  {id:'doc.rate-card',title:'Tariff card',params:['version?'],money:false,view:(s,uid)=>trcCan(s,uid,'view'),build:(s,uid,p)=>rateCardDocument(s,uid,p.version)},
  {id:'rpt.people.payslip',title:'Payslip',params:['run','staff'],money:true,
    view:(s,uid)=>hrCanPay(s,uid)||(!!hrStaffOfUser(s,uid)&&hrSlice(s).runs.some(r=>r.status==='Locked')),build:(s,uid,p)=>payslipDocument(s,uid,need(p,'run'),need(p,'staff'))},
  {id:'doc.kot',title:'Kitchen order ticket',params:['order','kot'],money:false,view:(s,uid)=>{const u=s.users.find(x=>x.id===uid);return !!u&&(canAny(s,uid,['billing','payments'])||u.role==='Restaurant');},build:(s,uid,p)=>kotDocument(s,uid,need(p,'order'),need(p,'kot'))},
  {id:'doc.outlet-bill',title:'Outlet bill',params:['order'],money:true,view:(s,uid)=>canAny(s,uid,['billing','payments']),build:(s,uid,p)=>outletBillDocument(s,uid,need(p,'order'))},
  {id:'doc.handover-pack',title:'Shift hand-over pack',params:['date?'],money:true,view:(s,uid)=>hovHasAccess(s,uid),build:(s,uid,p)=>handoverPackDocument(s,uid,p.date)},
];
export const docDef=(id:string)=>DOCUMENTS.find(d=>d.id===id);
/** Documents the caller may print (the Documents list in the hub). */
export const docCatalogue=(s:State,uid:string)=>DOCUMENTS.filter(d=>d.view(s,uid)&&(!d.money||docMoneyRole(s,uid)||d.id==='rpt.people.payslip'));
export function buildDocument(s:State,uid:string,id:string,p:DocParams={}):DocModel{
  const d=docDef(id);if(!d)throw new HccError('NOT_FOUND',`No document ${id}.`);
  if(!d.view(s,uid))throw new HccError('FORBIDDEN',`You cannot print ${d.title}.`);
  return d.build(s,uid,p);
}
