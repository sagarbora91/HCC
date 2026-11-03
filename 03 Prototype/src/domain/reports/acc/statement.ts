// F7-R6 · rpt.acc.statement — ledger or party statement (catalogue §3.5). Adapter over accStatement (ACC-1). Balance is signed (Dr positive, Cr negative) as the screen's running balance.
import {registerReport} from '../catalogue';
import {accStatement,accDrCr} from '../../streams/accounting-reports';
import {T,D,M,ACC_VIEW,stampRows,row} from './shared';

export const ST_COLS=[D('date','Date'),T('voucher','Voucher'),T('type','Type'),T('narration','Narration'),T('counter','Counter ledger'),M('debit','Debit'),M('credit','Credit'),M('balance','Balance (Dr / Cr)')];
const side=(n:number)=>{const x=accDrCr(n);return x.side?' — '+x.side:'';};
export const statement=registerReport({
  id:'rpt.acc.statement',title:'Ledger or party statement',area:'acc',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:ACC_VIEW,viewPerms:[ACC_VIEW],dateClass:'D-F',legacyIds:['A27'],
  params:[{key:'account',label:'Ledger or party',type:'party',required:true},{key:'from',label:'From',type:'range',dateClass:'D-F'}],
  columns:ST_COLS,
  dateBasis:'Vouchers of the ledger (a control ledger includes its parties) in the range, oldest first; opening = every voucher before From. Balance: Dr positive, Cr negative.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const st=accStatement(s,String(p.account),p.from!,p.to!);
    const stamp=stampRows(s,p.from,p.to,'voucher');
    const rows=[...stamp,
      row({narration:`Opening balance · ${st.name}${side(st.opening)}`,balance:st.opening},'subtotal'),
      ...st.rows.map(r=>row({date:r.date,voucher:r.no,type:r.type,narration:r.narration,counter:r.counter,debit:r.debit,credit:r.credit,balance:r.running})),
      row({narration:`Closing balance${side(st.closing)}`,debit:st.debit,credit:st.credit,balance:st.closing},'total')];
    return {params:p,columns:ST_COLS,rows,about:{provisional:stamp.length>0}};
  },
});
