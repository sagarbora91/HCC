// F7-R6 · rpt.acc.pl — profit and loss (catalogue §3.5, ACC-3 wired). Adapter over accPl: ICAI vertical, or the Tally-group view. Golden April 2026 profit before tax 1,40,400 rupees.
import {registerReport} from '../catalogue';
import {accPl,type ACCPlLine,type ACCPlGroup} from '../../streams/accounting-pl';
import {T,M,ACC_OWNER_ACCOUNTANT,stampRows,row} from './shared';

export const PL_COLS=[T('code','Code'),T('line','Line'),T('group','Group'),M('amount','Amount')];
const L=(l:ACCPlLine)=>row({code:l.code,line:l.name,group:l.group,amount:l.paise});
const head=(line:string)=>row({line},'subtotal');
const sub=(line:string,amount:number)=>row({line,amount},'subtotal');
export const pl=registerReport({
  id:'rpt.acc.pl',title:'Profit and loss',area:'acc',priority:'P2',status:'ACC-3',
  moneyFlag:true,moneyGate:ACC_OWNER_ACCOUNTANT,viewPerms:[ACC_OWNER_ACCOUNTANT],dateClass:'D-F',
  params:[{key:'from',label:'From',type:'range',dateClass:'D-F'},{key:'tallyView',label:'Tally-group view',type:'bool',default:'false'}],
  columns:PL_COLS,
  dateBasis:'Period vouchers of the range (the opening entry is never P&L). Income = credits less debits, expenses = debits less credits. Provisional while opening balances are pending or the range mixes locked and open days.',
  formats:['screen','xlsx','pdf','csv'],phone:'summary',
  select:(s,_uid,p)=>{
    const x=accPl(s,p.from!,p.to!);const stamp=stampRows(s,p.from,p.to,'line');
    const rows=[...stamp];
    if(p.tallyView===true){
      rows.push(...x.byTallyGroup.map(g=>row({code:g.group,line:g.name,group:'',amount:g.paise})));
    }else{
      rows.push(head('Revenue from operations'),...x.revenue.lines.map(L),sub('Total revenue',x.revenue.total),
        head('Other income'),...x.otherIncome.lines.map(L),sub('Total other income',x.otherIncome.total),
        head('Expenses'));
      for(const g of [x.expenses.consumption,x.expenses.employee,x.expenses.finance,x.expenses.depreciation,x.expenses.other] as ACCPlGroup[])
        rows.push(row({line:g.name},'subtotal'),...g.lines.map(L),sub('Total '+g.name.toLowerCase(),g.total));
      rows.push(sub('Total expenses',x.expenses.total));
    }
    rows.push(...(x.warnings??[]).map(w=>row({line:'Warning: '+w})),row({line:'Profit before tax',amount:x.profitBeforeTax},'total'));
    return {params:p,columns:PL_COLS,rows,about:{provisional:x.provisional||stamp.length>0}};
  },
});
