// F7-R5 · rpt.tax.invoice-register — Invoice register (catalogue A45, C1.BillSummary, C4.SalesBill, C4.LodgingBillReceipt). Tax invoices and credit notes of a
// range with the tariff / other / discount split, GST, net bill and the payment split of the guest folio, restaurant bill or counter bill.
// Round-off is always 0: HCC bills carry exact paise (no rounding). Credit notes are negative rows. Sorted by series (INV, RST, BOS, CRN) then number.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {HccError} from '../../errors';
import {nights} from '../../money';
import {taxEntries,entryFigures,SERIES_ORDER,isRoomSac} from './shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const INV_REG_COLS:RptColumn[]=[T('invoiceNo','Invoice no'),{key:'date',label:'Date',type:'date',money:false},T('party','Guest or company','guestName'),T('room','Room'),
  M('tariff','Tariff'),M('other','Food, laundry, other'),M('discount','Discount'),M('gst','GST'),M('roundOff','Round-off'),M('net','Net bill'),M('cash','Cash'),M('cardUpi','Card, UPI, cheque'),M('credit','Credit'),T('status','Status')];
const STATUS:Record<string,string>={issued:'Issued',cancelled:'Cancelled',corrected:'Corrected'};

export const invoiceRegister=registerReport({
  id:'rpt.tax.invoice-register',title:'Invoice register',area:'tax',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:'PERM-ACC-013',viewPerms:['PERM-ACC-013'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},
    {key:'series',label:'Series',type:'enum',options:['all','INV','RST','BOS','CRN'],default:'all'},
    {key:'billType',label:'Bill type',type:'enum',options:['all','room','counter','credit note'],default:'all'}],
  columns:INV_REG_COLS,
  dateBasis:'Invoices and credit notes by document date (a stay invoice is dated its departure or its correction), range up to 92 days. Tariff = room-SAC charges; discount = negative lines; credit notes are negative rows. Payment split: guest folio receipts on the current invoice, restaurant and counter settlement.',
  formats:['screen','xlsx','pdf'],phone:'no',legacyIds:['A45','C1.BillSummary','C1.BillSummerShort','C4.SalesBill','C4.LodgingBillReceipt'],
  select:(s,_uid,p)=>{
    if(nights(p.from!,p.to!)+1>92)throw new HccError('VALIDATION','The range is longer than 92 days. Narrow the range.');
    const want=(e:ReturnType<typeof taxEntries>[number])=>(p.series==='all'||!p.series||e.series===p.series)&&
      (!p.billType||p.billType==='all'||(p.billType==='credit note'?e.kind==='note':p.billType==='room'?e.kind==='invoice'&&e.series==='INV'&&!!e.room:e.kind==='invoice'&&(e.series==='RST'||(e.series==='INV'&&!e.room))));
    const list=taxEntries(s,p.from!,p.to!).filter(want).sort((a,b)=>SERIES_ORDER[a.series]!-SERIES_ORDER[b.series]!||a.no.localeCompare(b.no));
    const acc={tariff:0,other:0,discount:0,gst:0,roundOff:0,net:0,cash:0,cardUpi:0,credit:0};
    const rows:RptRow[]=list.map(e=>{
      const f=entryFigures(e);const sgn=e.kind==='note'?-1:1;
      const tariff=f.lines.filter(l=>isRoomSac(l.sac)&&sgn*l.taxable>0).reduce((a,l)=>a+l.taxable,0);
      const other=f.lines.filter(l=>!isRoomSac(l.sac)&&sgn*l.taxable>0).reduce((a,l)=>a+l.taxable,0);
      const discount=f.lines.filter(l=>sgn*l.taxable<0).reduce((a,l)=>a+l.taxable,0);
      const c={tariff,other,discount,gst:f.gst,roundOff:0,net:f.total,cash:e.pay.cash,cardUpi:e.pay.cardUpi,credit:e.pay.credit};
      for(const k of Object.keys(acc) as (keyof typeof acc)[])acc[k]+=c[k];
      return {kind:'row',cells:{invoiceNo:e.no,date:e.date,party:e.party,room:e.room,...c,status:STATUS[e.status]!}};
    });
    rows.push({kind:'total',cells:{invoiceNo:'Total',...acc}});
    return {params:p,columns:INV_REG_COLS,rows};
  },
});
