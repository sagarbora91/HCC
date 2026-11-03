// F7-R6 · rpt.acc.day-book — every voucher entry of a range (catalogue §3.5). Adapter over accDayBook (ACC-1).
import {registerReport} from '../catalogue';
import {accDayBook} from '../../streams/accounting-reports';
import {T,D,M,ACC_VIEW,stampRows,row} from './shared';

export const DB_COLS=[D('date','Date'),T('voucher','Voucher'),T('type','Type'),T('ledger','Ledger'),M('debit','Debit'),M('credit','Credit'),T('narration','Narration'),T('source','Source')];
export const dayBook=registerReport({
  id:'rpt.acc.day-book',title:'Day book',area:'acc',priority:'P2',status:'built',
  moneyFlag:true,moneyGate:ACC_VIEW,viewPerms:[ACC_VIEW],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'range',dateClass:'D-B'}],
  columns:DB_COLS,
  dateBasis:'Every voucher dated in the range, in date and voucher-number order, one line per ledger entry (an opening voucher dated on From belongs to the opening, not the day book).',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const db=accDayBook(s,p.from!,p.to!);
    const stamp=stampRows(s,p.from,p.to,'voucher');
    const rows=[...stamp,
      ...db.rows.map(r=>row({date:r.date,voucher:r.no,type:r.type,ledger:`${r.account} ${r.ledger}`,debit:r.debit,credit:r.credit,narration:r.narration,source:r.source})),
      row({voucher:'Total',ledger:`${db.vouchers} voucher${db.vouchers===1?'':'s'}${db.balanced?'':' — debits and credits differ'}`,debit:db.debit,credit:db.credit},'total')];
    return {params:p,columns:DB_COLS,rows,about:{provisional:stamp.length>0}};
  },
});
