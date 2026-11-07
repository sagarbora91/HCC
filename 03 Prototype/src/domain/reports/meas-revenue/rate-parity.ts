// F12-RPT-REVENUE · rpt.revenue.rate-parity — Rate shop and parity (catalogue §3.10, switch meas.rev.rate-shop). Direct = the live BAR of the room type; OTA =
// the lowest rate the connected channels publish for it (BAR plus the channel markup); competitors = the manual rate-shop entries (newest entry per
// competitor, night and type; the first six active competitors by name). Reads only.
import type {State} from '../../types';
import type {RptColumn,RptBoundParams} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {addDays} from '../../money';
import {HccError} from '../../errors';
import {yldNight,yldOcc,yldRoomTypes} from '../../streams/yield';
import {chnSlice} from '../../streams/channels';
import {mrParityRed,mrMedian,mrPositionPct} from './logic';
import {D,T,P,M,mrRows,mrRefType,mrIntParam,mrNightsGuard,CAPTURE_NOTE,type MrRow} from './shared';

const comp=(i:number)=>M('comp'+i,`Competitor ${i} rate`);
export const PARITY_COLS:RptColumn[]=[D('stayDate','Stay date'),M('direct','Direct rate'),M('ota','OTA rate'),comp(1),comp(2),comp(3),comp(4),comp(5),comp(6),M('median','Set median'),
  P('position','Our position'),T('exception','Parity exception')];
const PARITY_BASIS='Business date forward (up to 92 nights): direct = live BAR of the room type (largest type when none chosen); OTA = lowest published channel rate (BAR plus markup, connected channels with the type mapped and not stop-sold); competitor columns = the newest shop entry per night, first six active competitors by name; set median of the shopped rates (whole paise); position = our direct rate against the median in whole percent. Red when the OTA rate is below direct, or our rate is more than 15 % above the set median with forecast occupancy under 40 %.';
export function activeCompetitors(s:State){return (s.measX?.competitors??[]).filter(c=>c.active).sort((a,b)=>a.name.localeCompare(b.name)).slice(0,6);}
export function parityCompute(s:State,from:string,n:number,type:string):MrRow[]{
  const comps=activeCompetitors(s),shop=s.measX?.rateshop??[];
  const chans=chnSlice(s).channels.filter(c=>c.status==='Connected'&&(c.mapping[type]||'').trim()&&!c.stopSell.includes(type));
  const out:MrRow[]=[];
  for(let i=0;i<n;i++){
    const date=addDays(from,i),night=yldNight(s,type,date),occ=yldOcc(s,date);
    const direct=night.live||null;
    const ota=chans.length&&direct!==null?Math.min(...chans.map(c=>Math.round(night.live*(10000+c.markupBps)/10000))):null;
    const rates:(number|null)[]=comps.map(c=>{
      const e=shop.filter(x=>x.competitor===c.id&&x.roomType===type&&x.stayDate===date).sort((a,b)=>a.date<b.date?1:a.date>b.date?-1:a.id<b.id?1:-1)[0];
      return e?.ratePaise??null;
    });
    const med=mrMedian(rates.filter((x):x is number=>x!==null));
    const red=mrParityRed(direct,ota,med,Math.max(occ.onBooks,occ.forecast),occ.sellable);
    const why=[ota!==null&&direct!==null&&ota<direct?'OTA below direct':'',med!==null&&direct!==null&&direct*100>med*115?'Above set by more than 15 %':''].filter(Boolean).join(' · ');
    const cells:MrRow['cells']={stayDate:date,direct,ota,median:med,position:mrPositionPct(direct,med),exception:why};
    for(let k=1;k<=6;k++)cells['comp'+k]=rates[k-1]??null;
    out.push({cells,red});
  }
  return out;
}
export const rateParity=registerReport({
  id:'rpt.revenue.rate-parity',title:'Rate shop and parity',area:'revenue',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-FWD',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-FWD'},{key:'nights',label:'Nights (max 92)',type:'text',default:'30'},{key:'roomType',label:'Room type',type:'text'}],
  columns:PARITY_COLS,dateBasis:PARITY_BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s:State,_uid:string,p:RptBoundParams)=>{
    const n=mrIntParam('Nights',p.nights,30,1,92);mrNightsGuard(n,92);
    const types=yldRoomTypes(s),type=String(p.roomType??'')||mrRefType(s);
    if(!types.includes(type))throw new HccError('VALIDATION',`Room type: choose one of ${types.join(', ')}.`);
    const comps=activeCompetitors(s);
    const columns=PARITY_COLS.map(c=>{const m=/^comp(\d)$/.exec(c.key);const k=m?comps[+m[1]-1]:undefined;return k?{...c,label:`${k.name} rate`}:c;});
    const none=!comps.length&&!(s.measX?.rateshop?.length);
    const rows=none?[]:parityCompute(s,p.from!,n,type);
    return {params:p,columns,rows:mrRows(rows),about:{from:p.from,to:addDays(p.from!,n-1),...(none?{dateBasis:PARITY_BASIS+CAPTURE_NOTE('rate-shop entries (competitors)')}:{})}};
  },
});
registerMeasReport('rpt.revenue.rate-parity','meas.rev.rate-shop');
