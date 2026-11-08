// F8-RPT-P2-B · rpt.money.bank-book: statement of one bank ledger with running balance — a fixed view of rpt.acc.statement for the Bank Accounts group
// (ACC-1 accStatement; no figure of its own). A ledger outside Bank Accounts is refused. Server twin: Hcc.Reporting/Reports/P2B/BankBookReport.cs.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {HccError} from '../../errors';
import {accStatement,accDrCr} from '../../streams/accounting-reports';
import {accLedger,accGroupOf} from '../../streams/accounting-chart';
import {T,D,M,ACC_VIEW,stampRows,row} from '../acc/shared';

export const BB_COLS=[D('date','Date'),T('voucher','Voucher'),T('narration','Narration'),T('counter','Counter ledger'),M('debit','Debit'),M('credit','Credit'),M('balance','Balance (Dr / Cr)')];
const side=(n:number)=>{const x=accDrCr(n);return x.side?' — '+x.side:'';};
/** True when the ledger sits in Bank Accounts (or a group below it). */
export function isBankLedger(s:State,account:string):boolean{
  const l=accLedger(s,account);if(!l)return false;
  let g=accGroupOf(s,l.group);
  for(let i=0;g&&i<32;i++){if(g.id==='bank')return true;g=g.parent?accGroupOf(s,g.parent):undefined;}
  return false;
}
export const bankBook=registerReport({
  id:'rpt.money.bank-book',title:'Bank book',area:'money',priority:'P2',status:'built',moneyFlag:true,moneyGate:ACC_VIEW,viewPerms:[ACC_VIEW],dateClass:'D-F',legacyIds:['A17'],
  params:[{key:'account',label:'Bank ledger',type:'party',required:true},{key:'from',label:'From',type:'range',dateClass:'D-F'}],columns:BB_COLS,
  dateBasis:'Vouchers of one bank ledger (Bank Accounts group) in the range, oldest first; opening = every voucher before From. Balance: Dr positive, Cr negative.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_u,p)=>{
    const acc=String(p.account);
    if(!isBankLedger(s,acc))throw new HccError('VALIDATION','Choose a ledger in the Bank Accounts group.');
    const st=accStatement(s,acc,p.from!,p.to!);
    const stamp=stampRows(s,p.from,p.to,'voucher');
    const rows=[...stamp,row({narration:`Opening balance · ${st.name}${side(st.opening)}`,balance:st.opening},'subtotal'),
      ...st.rows.map(r=>row({date:r.date,voucher:r.no,narration:r.narration,counter:r.counter,debit:r.debit,credit:r.credit,balance:r.running})),
      row({narration:`Closing balance${side(st.closing)}`,debit:st.debit,credit:st.credit,balance:st.closing},'total')];
    return {params:p,columns:BB_COLS,rows,about:{provisional:stamp.length>0}};
  },
});
