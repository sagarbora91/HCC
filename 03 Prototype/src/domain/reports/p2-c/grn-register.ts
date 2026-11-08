// F8-RPT-P2-C · rpt.stock.grn-register — goods received: one row per GRN line (GRN no, date, supplier, item, qty, rate, amount, bill no), totals per supplier.
// Adapter over the inventory GRN records (INVGrn). Rate and amount need PERM-STK-004 (the report's gate), so they are removed without it (rule M).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {invSlice,invItem} from '../../streams/inventory';
import {col,rowOf,rangeParams} from '../money/common';
import {cap92} from './shared';

const G='PERM-STK-004';
export const GRN_COLS:RptColumn[]=[col('grn','GRN no','text'),col('date','Date','date'),col('supplier','Supplier','text'),col('item','Item','text'),col('qty','Qty','int'),
  col('rate','Rate','money',true),col('amount','Amount','money',true),col('bill','Bill no','text')];
export const GRN_SUPPLIER_COLS:RptColumn[]=[col('supplier','Supplier','text'),col('grns','GRNs','int'),col('qty','Qty','int'),col('amount','Amount','money',true)];
export const grnRegister=registerReport({
  id:'rpt.stock.grn-register',title:'Goods received (GRN) register',area:'stock',priority:'P2',status:'built',moneyFlag:true,moneyGate:G,viewPerms:[G],dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'supplier',label:'Supplier',type:'text'}],columns:GRN_COLS,
  dateBasis:'Goods received notes by the business date they were recorded, oldest first; amount = quantity x unit cost on the receipt. The second block totals each supplier.',
  formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s,_u,p)=>{
    cap92(p);
    const q=String(p.supplier??'').trim().toLowerCase();
    const grns=invSlice(s).grns.filter(g=>g.date>=p.from!&&g.date<=p.to!&&(!q||g.supplier.toLowerCase().includes(q))).sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.id<b.id?-1:a.id>b.id?1:0);
    const rows:RptRow[]=[];
    for(const g of grns)for(const l of g.lines)rows.push(rowOf('row',{grn:g.id,date:g.date,supplier:g.supplier,item:invItem(s,l.item)?.name??l.item,qty:l.qty,rate:l.cost,amount:l.qty*l.cost,bill:g.ref||null},g.id));
    const by=new Map<string,{grns:number;qty:number;amount:number}>();
    for(const g of grns){const x=by.get(g.supplier)??{grns:0,qty:0,amount:0};x.grns++;for(const l of g.lines){x.qty+=l.qty;x.amount+=l.qty*l.cost;}by.set(g.supplier,x);}
    const sup:RptRow[]=[...by.entries()].sort((a,b)=>a[0].localeCompare(b[0])).map(([k,v])=>rowOf('row',{supplier:k,grns:v.grns,qty:v.qty,amount:v.amount}));
    if(sup.length)sup.push(rowOf('total',{supplier:'Total',grns:grns.length,qty:sup.reduce((a,r)=>a+Number(r.cells.qty),0),amount:sup.reduce((a,r)=>a+Number(r.cells.amount),0)}));
    if(rows.length)rows.push(rowOf('total',{grn:'Total',date:null,supplier:null,item:null,qty:rows.reduce((a,r)=>a+Number(r.cells.qty),0),rate:null,amount:rows.reduce((a,r)=>a+Number(r.cells.amount),0),bill:null}));
    return {params:p,columns:GRN_COLS,rows,sections:[{key:'lines',title:'Receipts',columns:GRN_COLS,rows},{key:'suppliers',title:'Totals per supplier',columns:GRN_SUPPLIER_COLS,rows:sup}]};
  },
});
