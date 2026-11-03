// F7-R5 · rpt.tax.bill-corrections — Bill corrections (catalogue A4, LF A2). Bills corrected after check-out: old and new invoice, amounts (incl. GST),
// GST of each, who and why. Issued corrections only (a pending / rejected request changed no invoice). Replaces the old Bill Updation / Bill Finalize reports.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {HccError} from '../../errors';
import {nights} from '../../money';
import {blcState} from '../../streams/bill-correction';
import {gstLineTax} from '../../gst';
import {userLabel} from '../guests/shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const BILL_CORR_COLS:RptColumn[]=[T('correctedAt','Corrected at'),T('oldNo','Old invoice no'),T('newNo','New invoice no'),T('guest','Guest','guestName'),T('room','Room'),
  M('oldAmount','Old amount'),M('newAmount','New amount'),M('difference','Difference'),M('oldGst','Old GST'),M('newGst','New GST'),T('reason','Reason'),T('correctedBy','Corrected by'),T('approvedBy','Approved by')];

export const billCorrections=registerReport({
  id:'rpt.tax.bill-corrections',title:'Bill corrections',area:'tax',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'correctedBy',label:'Corrected by',type:'user'}],
  columns:BILL_CORR_COLS,
  dateBasis:'Issued corrections by the date the Owner approved them (range up to 92 days). Amounts include GST; old GST = GST of the credited invoice, new GST = GST of the re-issued invoice.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',legacyIds:['A4'],
  select:(s,_uid,p)=>{
    if(nights(p.from!,p.to!)+1>92)throw new HccError('VALIDATION','The range is longer than 92 days. Narrow the range.');
    const list=blcState(s).corrections.filter(c=>c.status==='Issued'&&c.decidedAt&&c.decidedAt.slice(0,10)>=p.from!&&c.decidedAt.slice(0,10)<=p.to!&&(!p.correctedBy||c.requestedBy===p.correctedBy))
      .sort((a,b)=>a.decidedAt!.localeCompare(b.decidedAt!)||a.id.localeCompare(b.id));
    const rows:RptRow[]=list.map(c=>{
      const b=s.bookings.find(x=>x.id===c.booking);
      const gst=(pred:(id:string)=>boolean)=>(b?.lines??[]).filter(l=>pred(l.id)).reduce((a,l)=>a+gstLineTax(l.amount,l.taxBps),0);
      const oldGst=-gst(id=>id.startsWith(c.id+'/cn')),newGst=gst(id=>id.startsWith(c.id+'/n'));
      return {kind:'row',cells:{correctedAt:c.decidedAt!.replace('T',' ').slice(0,16),oldNo:c.oldInvoiceNo,newNo:c.newInvoiceNo??'',guest:b?.guest??'',room:b?.room??'',
        oldAmount:c.oldTotal,newAmount:c.newTotal,difference:c.newTotal-c.oldTotal,oldGst,newGst,reason:c.reason,correctedBy:userLabel(s,c.requestedBy),approvedBy:userLabel(s,c.approvedBy)}};
    });
    const sum=(k:string)=>rows.reduce((a,x)=>a+(x.cells[k] as number),0);
    rows.push({kind:'total',cells:{correctedAt:`Total · ${rows.length} correction${rows.length===1?'':'s'}`,oldAmount:sum('oldAmount'),newAmount:sum('newAmount'),difference:sum('difference'),oldGst:sum('oldGst'),newGst:sum('newGst')}});
    return {params:p,columns:BILL_CORR_COLS,rows};
  },
});
