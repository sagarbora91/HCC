// F8-RPT-P2-C · rpt.acc.asset-register — Fixed-asset register (partial -> built on the engine). Adapter over accAssetRegister (no new figures).
// Depreciation and written-down value per asset are NOT here: HCC keeps no per-asset rate table (DEC-138/140: the CA passes depreciation in Tally); see F8-RPT-P2-C-DONE.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {accAssetRegister} from '../../streams/accounting-assets';
import {col,rowOf} from '../money/common';

export const ASSET_COLS:RptColumn[]=[col('no','Asset no','text'),col('name','Name','text'),col('ledger','Ledger','text'),col('vendor','Vendor','text'),col('bought','Bought','date'),
  col('cost','Cost','money',true),col('itc','ITC taken','money',true),col('putToUse','Put to use','date'),col('disposal','Disposal','text')];
export const assetRegister=registerReport({
  id:'rpt.acc.asset-register',title:'Fixed-asset register',area:'acc',priority:'P2',status:'built',moneyFlag:true,moneyGate:'PERM-ACC-001',viewPerms:['PERM-EXP-004','PERM-ACC-001'],dateClass:'D-ASOF',
  params:[{key:'asOf',label:'As on',type:'date',dateClass:'D-ASOF'}],columns:ASSET_COLS,
  dateBasis:'Assets bought on or before the date, by asset no. Cost is the bill less the ITC taken and net of posted purchase returns; put-to-use and disposal count only when they fall on or before the date. Depreciation is passed by the CA in Tally (DEC-138).',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_u,p)=>{
    const asOf=p.asOf!;
    const list=accAssetRegister(s).filter(a=>a.date<=asOf).sort((a,b)=>a.no<b.no?-1:a.no>b.no?1:0);
    const rows:RptRow[]=list.map(a=>{
      const gone=a.disposed&&a.disposed.date<=asOf?a.disposed:undefined;
      return rowOf('row',{no:a.no,name:a.name,ledger:a.ledgerName,vendor:a.vendor||null,bought:a.date,cost:a.cost,itc:a.itc,putToUse:a.putToUse&&a.putToUse<=asOf?a.putToUse:null,
        disposal:gone?`Disposed ${gone.date}${gone.voucher?' · '+gone.voucher:''}`:null});});
    if(rows.length)rows.push(rowOf('total',{no:'Total',name:null,ledger:null,vendor:null,bought:null,cost:list.reduce((a,x)=>a+x.cost,0),itc:list.reduce((a,x)=>a+x.itc,0),putToUse:null,disposal:null}));
    return {params:p,columns:ASSET_COLS,rows};
  },
});
