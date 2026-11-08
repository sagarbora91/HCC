// F8-RPT-P2-C · rpt.rest.sales-by-item — quantity and value per menu item over settled POS checks (value = line net, before tax; voided lines left out).
// Adapter over the POS orders. A Cashier sees only the bills they settled (own shift); the Restaurant role has no access (PERM-POS-004).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {posSlice,posActiveLines,posLineValue,posOutlet} from '../../streams/pos';
import {posItemCategoryName} from '../../streams/pos-master';
import {rptHolds} from '../mask';
import {col,rowOf,rangeParams} from '../money/common';
import {cap92,share1} from './shared';

const G='PERM-POS-004';
export const SALES_ITEM_COLS:RptColumn[]=[col('item','Item','text'),col('category','Category','text'),col('qty','Qty','int'),col('value','Value','money',true),col('share','Share','pct')];
export const salesByItem=registerReport({
  id:'rpt.rest.sales-by-item',title:'Sales by item',area:'rest',priority:'P2',status:'built',moneyFlag:true,moneyGate:G,viewPerms:[G],ownShift:true,dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'outlet',label:'Outlet',type:'text'}],columns:SALES_ITEM_COLS,
  dateBasis:'Settled checks by settlement date; value = quantity x price before tax (voided lines are left out); share = the item value in the report total. Categories by value, items by value inside a category.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,uid,p)=>{
    cap92(p);
    const all=rptHolds(s,uid,G,false),q=String(p.outlet??'').trim().toLowerCase(),P=posSlice(s);
    const byItem=new Map<string,{name:string;cat:string;qty:number;value:number}>();
    for(const o of P.orders){
      const st=o.settlement;if(o.status!=='Settled'||!st||st.date<p.from!||st.date>p.to!||(!all&&st.by!==uid))continue;
      if(q&&o.outlet.toLowerCase()!==q&&(posOutlet(s,o.outlet)?.name??'').toLowerCase()!==q)continue;
      for(const l of posActiveLines(o)){
        const m=P.menu.find(x=>x.id===l.item),cat=m?posItemCategoryName(s,m):'Other';
        const x=byItem.get(l.item)??{name:l.name,cat,qty:0,value:0};x.qty+=l.qty;x.value+=posLineValue(l);byItem.set(l.item,x);}}
    const items=[...byItem.values()],total=items.reduce((a,x)=>a+x.value,0);
    const cats=new Map<string,typeof items>();for(const x of items){const a=cats.get(x.cat)??[];a.push(x);cats.set(x.cat,a);}
    const catVal=(a:typeof items)=>a.reduce((n,x)=>n+x.value,0);
    const rows:RptRow[]=[];
    for(const [cat,list] of [...cats.entries()].sort((a,b)=>catVal(b[1])-catVal(a[1])||a[0].localeCompare(b[0]))){
      for(const x of [...list].sort((a,b)=>b.value-a.value||a.name.localeCompare(b.name)))rows.push(rowOf('row',{item:x.name,category:cat,qty:x.qty,value:x.value,share:share1(x.value,total)},cat));
      rows.push(rowOf('subtotal',{item:cat+' total',category:null,qty:list.reduce((n,x)=>n+x.qty,0),value:catVal(list),share:share1(catVal(list),total)},cat));}
    if(rows.length)rows.push(rowOf('total',{item:'Total',category:null,qty:items.reduce((n,x)=>n+x.qty,0),value:total,share:total>0?100:0}));
    return {params:p,columns:SALES_ITEM_COLS,rows};
  },
});
