// F7-R6 · rpt.acc.ageing — money owed to the hotel or by the hotel, by party and age, bill by bill (catalogue §3.5, ACC-3 wired). Adapter over accAgeing.
// A party row (subtotal) is followed by its open bills; a party whose buckets do not tie to its ledger balance gets a note row in words.
import {registerReport} from '../catalogue';
import {accAgeing,accAgeingBucket,type ACCAgeingRow} from '../../streams/accounting-ageing';
import {accFyOfDate,accFyBounds} from '../../streams/accounting-year';
import {T,D,M,I,ACC_VIEW,stampRows,row} from './shared';

export const AG_COLS=[T('party','Party'),T('control','Control ledger'),T('billRef','Bill ref'),D('billDate','Bill date'),D('due','Due'),M('amount','Amount'),M('paid','Paid'),M('open','Open'),I('days','Days'),
  M('b0_30','0-30'),M('b31_60','31-60'),M('b61_90','61-90'),M('b90','90+')];
const B0={b0_30:0,b31_60:0,b61_90:0,b90:0};
export const ageing=registerReport({
  id:'rpt.acc.ageing',title:'Outstanding with ageing',area:'acc',priority:'P1',status:'ACC-3',
  moneyFlag:true,moneyGate:ACC_VIEW,viewPerms:[ACC_VIEW],dateClass:'D-ASOF',legacyIds:['A22','A28'],
  params:[{key:'asOf',label:'As on',type:'date',dateClass:'D-ASOF'},{key:'side',label:'Side',type:'enum',options:['receivable','payable'],default:'receivable'}],
  columns:AG_COLS,
  dateBasis:'Every voucher up to and including the date. Credits are allocated to bills (explicit vendor-payment allocations first, then oldest first); age = days since the bill date (no credit terms master).',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const a=accAgeing(s,p.asOf as string,(p.side??'receivable') as 'receivable'|'payable');
    const fy=accFyBounds(accFyOfDate(a.asOf));const stamp=stampRows(s,fy.from,a.asOf,'party');
    const rows=[...stamp];
    for(const r of a.rows as ACCAgeingRow[]){
      rows.push(row({party:r.name,control:r.control,amount:r.bills.reduce((x,b)=>x+b.amount,0),paid:r.bills.reduce((x,b)=>x+b.paid,0),open:r.open,b0_30:r.b0_30,b31_60:r.b31_60,b61_90:r.b61_90,b90:r.b90},'subtotal'));
      for(const b of r.bills)rows.push(row({party:r.name,control:r.control,billRef:b.ref,billDate:b.date,due:b.due,amount:b.amount,paid:b.paid,open:b.open,days:b.days,...B0,[accAgeingBucket(b.days)]:b.open}));
      if(r.flagged)rows.push(row({party:r.name,control:r.control,billRef:'Does not tie to the party ledger: '+(r.note??'')}));
    }
    rows.push(row({party:'Total',open:a.totals.open,b0_30:a.totals.b0_30,b31_60:a.totals.b31_60,b61_90:a.totals.b61_90,b90:a.totals.b90},'total'));
    return {params:p,columns:AG_COLS,rows,about:{provisional:stamp.length>0}};
  },
});
