// F7-R6 · rpt.acc.trial-balance — six-column trial balance (catalogue §3.5). Adapter over accTrialBalance6 (ACC-1) — same figures as the Accounting screen / CSV.
import {registerReport} from '../catalogue';
import {accTrialBalance6} from '../../streams/accounting-reports';
import {T,M,ACC_VIEW,stampRows,row} from './shared';

export const TB_COLS=[T('code','Code'),T('ledger','Ledger'),T('group','Group'),M('openDr','Opening Dr'),M('openCr','Opening Cr'),M('perDr','Period Dr'),M('perCr','Period Cr'),M('closeDr','Closing Dr'),M('closeCr','Closing Cr')];
export const trialBalance=registerReport({
  id:'rpt.acc.trial-balance',title:'Trial balance (six columns)',area:'acc',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:ACC_VIEW,viewPerms:[ACC_VIEW],dateClass:'D-F',
  params:[{key:'from',label:'From',type:'range',dateClass:'D-F'},{key:'view',label:'View',type:'enum',options:['control ledgers','with parties'],default:'control ledgers'}],
  columns:TB_COLS,
  dateBasis:'Every voucher up to the To date; opening = vouchers before From (the opening entry included); opening and closing netted per ledger, period columns gross. Locked days read their snapshot.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const tb=accTrialBalance6(s,p.from!,p.to!,p.view==='with parties');
    const stamp=stampRows(s,p.from,p.to,'code');
    const rows=[...stamp,
      ...(tb.openingPending?[row({code:'',ledger:'Opening balances pending — no approved opening entry with a zero difference yet'},'subtotal')]:[]),
      ...tb.rows.map(r=>row({code:r.account,ledger:r.name,group:r.group,openDr:r.openingDebit,openCr:r.openingCredit,perDr:r.debit,perCr:r.credit,closeDr:r.closingDebit,closeCr:r.closingCredit})),
      row({code:'Total',ledger:tb.balanced?'Debits equal credits in every column':'Debits and credits differ',group:'',openDr:tb.totals.openingDebit,openCr:tb.totals.openingCredit,perDr:tb.totals.debit,perCr:tb.totals.credit,closeDr:tb.totals.closingDebit,closeCr:tb.totals.closingCredit},'total')];
    return {params:p,columns:TB_COLS,rows,about:{provisional:tb.openingPending||stamp.length>0}};
  },
});
