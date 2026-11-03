// F7-R6 · rpt.acc.cash-ratio — share of receipts and payments made in cash, flagged above 5 % (catalogue §3.5, ACC-3 wired). Adapter over accCashRatio.
import {registerReport} from '../catalogue';
import {accCashRatio} from '../../streams/accounting-pl';
import {accFyBounds} from '../../streams/accounting-year';
import {T,M,P,B,ACC_OWNER_ACCOUNTANT,stampRows,row} from './shared';

export const CR_COLS=[T('measure','Measure'),M('cash','Cash'),M('total','Total'),P('share','Share'),B('flagged','Flagged')];
/** Basis points → percent with one decimal, in integer arithmetic (same on the server). */
export const bpsToPct=(bps:number)=>Math.floor((bps+5)/10)/10;
export const cashRatio=registerReport({
  id:'rpt.acc.cash-ratio',title:'Cash ratio',area:'acc',priority:'P2',status:'ACC-3',
  moneyFlag:true,moneyGate:ACC_OWNER_ACCOUNTANT,viewPerms:[ACC_OWNER_ACCOUNTANT],dateClass:'D-FY',
  params:[{key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'}],
  columns:CR_COLS,
  dateBasis:'Receipt and Payment vouchers of the financial year: the money side on cash and bank ledgers; cash = 1000 and 1005. Flagged above 5 %.',
  formats:['screen','xlsx','pdf'],phone:'summary',
  select:(s,_uid,p)=>{
    const c=accCashRatio(s,p.fy as string);const b=accFyBounds(c.fy);const stamp=stampRows(s,b.from,b.to,'measure');
    const rows=[...stamp,
      row({measure:'Receipts',cash:c.cashReceipts,total:c.totalReceipts,share:bpsToPct(c.receiptsPct),flagged:c.receiptsPct>500?1:0}),
      row({measure:'Payments',cash:c.cashPayments,total:c.totalPayments,share:bpsToPct(c.paymentsPct),flagged:c.paymentsPct>500?1:0})];
    return {params:p,columns:CR_COLS,rows,about:{provisional:stamp.length>0}};
  },
});
