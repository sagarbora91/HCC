// F7-R6 · rpt.acc.balance-sheet — owner's funds, liabilities and assets at a date (catalogue §3.5, ACC-3 wired). Adapter over accBalanceSheet.
// An HCC view, NOT the statutory statement. Golden 30 April 2026: both sides 79,27,900 rupees (rulebook §8.5).
import {registerReport} from '../catalogue';
import {accBalanceSheet,type ACCBsBlock} from '../../streams/accounting-pl';
import {accFyOfDate,accFyBounds} from '../../streams/accounting-year';
import {T,M,ACC_OWNER_ACCOUNTANT,stampRows,row} from './shared';

export const BS_COLS=[T('block','Block'),T('line','Line'),M('amount','Amount')];
const block=(b:ACCBsBlock)=>[...b.lines.map(l=>row({block:b.name,line:l.name,amount:l.paise})),row({block:b.name,line:'Total '+b.name.toLowerCase(),amount:b.total},'subtotal')];
export const balanceSheet=registerReport({
  id:'rpt.acc.balance-sheet',title:'Balance sheet',area:'acc',priority:'P3',status:'ACC-3',
  moneyFlag:true,moneyGate:ACC_OWNER_ACCOUNTANT,viewPerms:[ACC_OWNER_ACCOUNTANT],dateClass:'D-ASOF',
  params:[{key:'asOf',label:'As on',type:'date',dateClass:'D-ASOF'}],
  columns:BS_COLS,
  dateBasis:'Every voucher up to and including the date (opening entry and locked snapshots included). Owner\'s funds = capital + profit of the year to date - drawings. An HCC view, not the statutory statement.',
  formats:['screen','xlsx','pdf','csv'],phone:'summary',
  select:(s,_uid,p)=>{
    const b=accBalanceSheet(s,p.asOf as string);
    const fyFrom=accFyBounds(accFyOfDate(b.asOf)).from;const stamp=stampRows(s,fyFrom,b.asOf,'line');
    const rows=[...stamp,row({block:'Note',line:'An HCC view of the books — not the statutory statement'},'subtotal'),
      ...block(b.liabilities.ownersFunds),...block(b.liabilities.nonCurrent),...block(b.liabilities.current),row({block:'Liabilities',line:'Total liabilities',amount:b.liabilities.total},'subtotal'),
      ...block(b.assets.nonCurrent),...block(b.assets.current),row({block:'Assets',line:'Total assets',amount:b.assets.total},'subtotal'),
      row({block:'Check',line:'Suspense (9999)',amount:b.suspense}),row({block:'Check',line:'Opening-balance difference (1400)',amount:b.openingDifference}),
      row({block:b.balanced?'Balanced':'Not balanced',line:'Difference (assets - liabilities)',amount:b.assets.total-b.liabilities.total},'total')];
    return {params:p,columns:BS_COLS,rows,about:{provisional:b.provisional||stamp.length>0}};
  },
});
