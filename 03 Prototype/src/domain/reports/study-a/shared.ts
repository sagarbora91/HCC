// F12 MEAS · meas-study-a · registration helper for the nine legacy-study reports. Each report wraps one analytics selector (domain/analytics/legacy-*.ts)
// over AnInput built from State + the lgc history. Money columns follow the selector's column flags; the engine removes them without PERM-RPT-002 (rule M).
import type {RptDefinition,RptParam,RptFormat} from '../types';
import {registerReport} from '../catalogue';
import type {AnalyticsSelector,AnInput} from '../../analytics/types';
import {buildAnInput} from '../../analytics/legacy-input';

const EMPTY:AnInput={rooms:[],stays:[],bills:[],receipts:[],asOf:'2026-01-01',legacyUntil:'2026-10-30',covidWindow:{from:'2020-03-01',to:'2021-03-31'}};
export const FY_PARAM:RptParam={key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'};
const FORMATS:RptFormat[]=['screen','xlsx','pdf'];
export const STUDY_DATE_BASIS='Nights from the arrival date (nights x rooms, same-day stays count 1); revenue by bill month (billed = total incl. GST); receipts by receipt date. Legacy to 30 Oct 2026 / HCC live.';

export function registerStudy(o:{id:string;title:string;money:boolean;dateClass:'D-FY'|'D-F'|'D-ASOF';params:RptParam[];select:AnalyticsSelector;legacyIds?:string[]}){
  const first=o.select(EMPTY,{});
  const def:RptDefinition={id:o.id,title:o.title,area:'study',priority:'P2',status:'MEAS',moneyFlag:o.money,...(o.money?{moneyGate:'PERM-RPT-002'}:{}),
    viewPerms:o.money?['PERM-RPT-001']:['PERM-RPT-002','PERM-RPT-001'],dateClass:o.dateClass,params:o.params,columns:first.columns,dateBasis:STUDY_DATE_BASIS,formats:FORMATS,phone:'no',
    ...(o.legacyIds?{legacyIds:o.legacyIds}:{}),
    select:(s,_uid,p)=>{const r=o.select(buildAnInput(s),p);return {params:p,columns:r.columns,rows:r.rows,sections:r.sections,about:{legacy:true}};}};
  return registerReport(def);
}
