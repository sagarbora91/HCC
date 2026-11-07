// F12 MEAS · rpt.acc.asset-life — Asset register, FF&E reserve and renovation cycle (catalogue §3.10; switch meas.hk.assets). Money flag y.
// Data: maintenance assets (installed date), the fixed-asset register (cost = the written-down value HCC keeps, DEC-138 / DEC-140), the captured
// expected life / condition grade / replacement cost (s.measX.assetLife, meas-cap-hk), maintenance plans, room revenue of the FY and ffe.reservePct.
// Money columns (removed, not blanked, without the gate; Housekeeping / Maintenance never hold it, DEC-061): WDV, Replacement cost, FF&E reserve accrued, Capex spent.
// Red row: an asset past its expected life with no maintenance plan; the month row when capex spent is above the reserve accrued.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {accAssetRegister,accFyEnd} from '../../streams/accounting-assets';
import {mntSlice} from '../../streams/maintenance';
import {T,D,M,mkRow,captureNote,monthsBetween,addMonths,ageText} from './shared';

export const ASSET_LIFE_COLS=[T('asset','Asset'),T('age','Age'),T('expectedLife','Expected life'),T('grade','Condition grade'),D('replacementDue','Replacement due'),M('wdv','WDV'),M('replacementCost','Replacement cost'),
  M('reserveAccrued','FF&E reserve accrued'),M('capexSpent','Capex spent'),T('refurbishment','Refurbishment due (category)')];
export const FFE_DEFAULT_PCT=3;
/** Past life = the as-of date is after the replacement date (the day itself is not past). */
export const assetPastLife=(replacementDue:string|undefined,asOf:string)=>!!replacementDue&&asOf>replacementDue;
export const ffePct=(s:State)=>{const v=s.configX?.values?.['ffe.reservePct'];return typeof v==='number'?v:FFE_DEFAULT_PCT;};
/** Room revenue (before GST, paise) of the booking lines dated inside [from, to]. */
export const roomRevenueBetween=(s:State,from:string,to:string)=>s.bookings.reduce((a,b)=>a+b.lines.filter(l=>l.kind==='room'&&l.date>=from&&l.date<=to).reduce((t,l)=>t+l.amount,0),0);

const BASIS='As of one date. Age from the installed date (or the put-to-use date for register assets); expected life, grade and replacement cost as captured. Replacement due = installed date + expected life. WDV = the register cost HCC holds (the CA passes depreciation in Tally). Month-end row: FF&E reserve accrued = ffe.reservePct of the FY room revenue (before GST) to the date; capex spent = register purchases of the FY. Refurbishment due is named when the replacement date falls within 12 months. Red: past expected life with no maintenance plan; capex spent above the reserve accrued.';

export const assetLife=registerReport({
  id:'rpt.acc.asset-life',title:'Asset register, FF&E reserve and renovation cycle',area:'acc',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-ACC-001',viewPerms:['PERM-ACC-001'],deskPerms:['PERM-HK-001','PERM-MNT-001'],dateClass:'D-ASOF',
  params:[{key:'asOf',label:'As of',type:'date',dateClass:'D-ASOF'},{key:'kind',label:'Asset kind',type:'text'}],
  columns:ASSET_LIFE_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf'],phone:'no',
  select:(s,_uid,p)=>{
    const life=s.measX?.assetLife??{};
    if(!Object.keys(life).length)return {params:p,columns:ASSET_LIFE_COLS,rows:[],about:{dateBasis:captureNote(BASIS,'asset life fields: expected life, condition grade and replacement cost (meas-cap-hk)')}};
    const asOf=(p.asOf??p.to) as string,x=mntSlice(s);
    const planned=new Set(x.plans.filter(pl=>pl.active).map(pl=>pl.assetId));
    type A={id:string;name:string;kind:string;start?:string;wdv:number|null;room:boolean};
    const assets:A[]=[
      ...x.assets.filter(a=>a.status!=='Retired').map(a=>({id:a.id,name:a.name,kind:a.kind as string,start:a.installedOn,wdv:null,room:!!a.room})),
      ...accAssetRegister(s).filter(a=>a.status!=='Disposed'&&a.date<=asOf).map(a=>({id:a.id,name:`${a.no} ${a.name}`,kind:a.ledgerName,start:a.putToUse??a.date,wdv:a.cost,room:false})),
    ].filter(a=>!p.kind||a.kind===p.kind).sort((a,b)=>a.kind.localeCompare(b.kind)||a.name.localeCompare(b.name));
    const rows:RptRow[]=assets.map(a=>{
      const l=life[a.id],age=a.start?monthsBetween(a.start,asOf):null;
      const due=a.start&&l?.expectedLifeMonths?addMonths(a.start,l.expectedLifeMonths):undefined;
      const past=assetPastLife(due,asOf)&&!planned.has(a.id);
      const soon=!!due&&due<=addMonths(asOf,12);
      return mkRow({asset:a.name,age:age===null?null:ageText(age),expectedLife:l?.expectedLifeMonths?ageText(l.expectedLifeMonths):null,grade:l?.conditionGrade??null,replacementDue:due??null,
        wdv:a.wdv,replacementCost:l?.replacementCostPaise??null,reserveAccrued:null,capexSpent:null,refurbishment:soon?(a.room?'Room refurbishment':'Plant and equipment'):null},past);
    });
    const fyEnd=accFyEnd(asOf),fyStart=`${+fyEnd.slice(0,4)-1}-04-01`;
    const accrued=Math.round(roomRevenueBetween(s,fyStart,asOf)*ffePct(s)/100);
    const capex=accAssetRegister(s).filter(a=>a.status!=='Disposed'&&a.date>=fyStart&&a.date<=asOf).reduce((t,a)=>t+a.cost,0);
    rows.push({kind:'total',...(capex>accrued?{group:'red'}:{}),cells:{asset:`FY ${fyStart.slice(0,4)}-${fyEnd.slice(2,4)} to date`,age:null,expectedLife:null,grade:null,replacementDue:null,wdv:null,replacementCost:null,reserveAccrued:accrued,capexSpent:capex,refurbishment:null}});
    return {params:p,columns:ASSET_LIFE_COLS,rows};
  },
});
