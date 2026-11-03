// F7-R5 · rpt.tax.documents-issued — Documents issued (GSTR-1 Table 13; catalogue A3, DEC-197). Adapter over gstr1Summary(...).documents:
// one row per series — the legacy Lodge series LGC first (plain numbers, before cut-over), then INV, RST, BOS, CRN. Cancelled = real cancelled numbers.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {gstr1Summary} from '../../gstr1';
import {taxLegacyBills} from './shared';

const T=(key:string,label:string):RptColumn=>({key,label,type:'text',money:false});
const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const DOC_COLS:RptColumn[]=[T('nature','Nature of document'),T('series','Series'),T('fromNo','From no'),T('toNo','To no'),I('total','Total'),I('cancelled','Cancelled'),I('net','Net issued')];
const NATURE:Record<string,string>={LGC:'Invoices for outward supply (legacy Lodge bills)',INV:'Invoices for outward supply',RST:'Invoices for outward supply (restaurant)',BOS:'Bills of supply',CRN:'Credit notes'};

export const documentsIssued=registerReport({
  id:'rpt.tax.documents-issued',title:'Documents issued (Table 13)',area:'tax',priority:'P1',status:'built',
  moneyFlag:false,viewPerms:['PERM-INV-001'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'}],
  columns:DOC_COLS,
  dateBasis:'Documents by invoice date; first and last number of each series in the month. Cancelled = numbers actually cancelled (a voided restaurant bill). Legacy Lodge bills (LGC) by bill date.',
  formats:['screen','xlsx','pdf','csv'],phone:'no',legacyIds:['A3'],
  select:(s,_uid,p)=>{
    const g=gstr1Summary(s,p.from!,p.to!,taxLegacyBills);
    const rows:RptRow[]=g.documents.map(d=>({kind:'row',cells:{nature:NATURE[d.docType]??d.docType,series:d.docType,fromNo:d.first,toNo:d.last,total:d.count,cancelled:d.cancelled,net:d.count-d.cancelled}}));
    const sum=(k:string)=>rows.reduce((a,x)=>a+(x.cells[k] as number),0);
    rows.push({kind:'total',cells:{nature:'Total',total:sum('total'),cancelled:sum('cancelled'),net:sum('net')}});
    return {params:p,columns:DOC_COLS,rows};
  },
});
