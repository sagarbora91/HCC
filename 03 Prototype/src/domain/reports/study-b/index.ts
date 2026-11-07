// F12 MEAS · meas-study-b · registers the twelve ported study reports (catalogue 3.10). Each report wraps an analytics selector
// (domain/analytics/beyond-*.ts) over AnInput built from State; the server twin is Hcc.Reporting/Analytics + Reports/MeasStudyB.
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import type {RptColumn,RptParam,RptDateClass,RptRow} from '../types';
import type {AnalyticsSelector,AnInput} from '../../analytics/types';
import {anInputFromState,extrasFromState} from '../../analytics/beyond-lib';
import {legacyAnRows} from '../../analytics/legacy-input';
import {revenueKpi,channelEconomics,pricingShape} from '../../analytics/beyond-revenue';
import {guestRepeatValue,guestOriginPlan} from '../../analytics/beyond-guest';
import {housekeepingOperations,fnbHallsAncillary,complianceRisk} from '../../analytics/beyond-ops';
import {financeCost,corporateAccounts,forecastScenarios,ownerDashboard} from '../../analytics/beyond-finance';
import {registerMeasReport} from '../../streams/measure';

export const STUDY_B_BASIS='Ported from the management study workbooks over HCC data plus the imported legacy history. FY April-March; occupancy = distinct room-nights slept / (rooms in service x days); ADR = room charge before GST / charged nights (bill month); COVID window 1 Mar 2020 - 31 Mar 2021 excluded from normal years; room 211 = Suite. Legacy to 30 Oct 2026 / HCC live.';
const FY:RptParam={key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'};
const num=(key:string,label:string,def?:string):RptParam=>({key,label,type:'text',...(def!==undefined?{default:def}:{})});
type Spec={id:string;title:string;key:string;sel:AnalyticsSelector;money:boolean;prio?:'P1'|'P2';dc?:RptDateClass;params?:RptParam[];phone?:'yes'|'no'};
const EMPTY:AnInput={rooms:[],stays:[],bills:[],receipts:[],asOf:'2026-01-01',legacyUntil:'2026-10-30',covidWindow:{from:'2020-03-01',to:'2021-03-31'}};

export const STUDY_B_SPECS:Spec[]=[
  {id:'rpt.study.revenue-kpi',title:'Occupancy, ADR, RevPAR by month, FY, type, channel',key:'rm-kpi',sel:revenueKpi,money:true},
  {id:'rpt.study.channel-economics',title:'Channel economics and net ADR',key:'rm-channel',sel:channelEconomics,money:true,params:[FY,num('commissionPct','Commission % of OTA room revenue','15')]},
  {id:'rpt.study.pricing-shape',title:'Day-of-week, length-of-stay, segment mix, rate ladder, upgrades',key:'rm-pricing',sel:pricingShape,money:true},
  {id:'rpt.study.guest-repeat-value',title:'Repeat guests, frequency, lifetime value, repeat customer list',key:'guest-repeat',sel:guestRepeatValue,money:true},
  {id:'rpt.study.guest-origin-plan',title:'Guest origin, nationality, meal plan, company guests',key:'guest-origin',sel:guestOriginPlan,money:false},
  {id:'rpt.study.housekeeping-operations',title:'Turnaround, idle streaks, check-in/out vs standard, daily load',key:'hk-ops',sel:housekeepingOperations,money:false},
  {id:'rpt.study.fnb-halls-ancillary',title:'F&B on bills, breakfast covers, hall utilisation, ancillary',key:'fnb-halls',sel:fnbHallsAncillary,money:true},
  {id:'rpt.study.finance-cost',title:'USALI P&L, break-even, paid-outs, cash & bank, GST by slab',key:'finance',sel:financeCost,money:true,params:[FY,num('fixedPerMonth','Fixed cost per month (paise)','0'),num('variablePerRn','Variable cost per room-night (paise)','0')]},
  {id:'rpt.study.corporate-accounts',title:'Corporate accounts, win-back list, account base',key:'sales',sel:corporateAccounts,money:true},
  {id:'rpt.study.compliance-risk',title:'Capture rates, audit-trail counts, risk register, licence calendar',key:'compliance',sel:complianceRisk,money:false},
  {id:'rpt.study.forecast-scenarios',title:'Seasonality indices, forecast (three methods), scenarios, budget vs actual, comp-set index',key:'forecast',sel:forecastScenarios,money:true,
    params:[FY,num('budgetGrowthPct','Budget growth % over last year','10'),num('adrDeltaPct','Scenario ADR change %','5'),num('compOccPct','Comp-set occupancy %'),num('compAdr','Comp-set ADR (paise)')]},
  {id:'rpt.study.owner-dashboard',title:'Owner 12-KPI monthly dashboard',key:'dashboard',sel:ownerDashboard,money:true,prio:'P1',dc:'D-M',phone:'yes',params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'}]},
];

/** Runs a selector over a State (also used by the tests and the pack). */
export function studyBRun(s:State,spec:Pick<Spec,'sel'>,p:Record<string,unknown>){
  const live=anInputFromState(s),lg=legacyAnRows();
  // f12-int: legacy history (when a source is registered) joins the live rows, one series as on the server (SqlAnalyticsInputSource).
  const input=lg?{...live,rooms:[...live.rooms,...(lg.rooms??[]).filter(r=>!live.rooms.some(x=>x.id===r.id))],stays:[...lg.stays,...live.stays],bills:[...lg.bills,...live.bills],receipts:[...lg.receipts,...live.receipts]}:live;
  return spec.sel(input,{...p,extras:extrasFromState(s)});
}
const columnsOf=(sel:AnalyticsSelector):RptColumn[]=>sel(EMPTY,{}).columns;

for(const sp of STUDY_B_SPECS){
  registerReport({id:sp.id,title:sp.title,area:'study',priority:sp.prio??'P2',status:'MEAS',moneyFlag:sp.money,...(sp.money?{moneyGate:'PERM-RPT-002'}:{}),
    viewPerms:['PERM-RPT-001','PERM-RPT-002'],dateClass:sp.dc??'D-FY',params:sp.params??[FY],columns:columnsOf(sp.sel),dateBasis:STUDY_B_BASIS,formats:['screen','xlsx','pdf'],phone:sp.phone??'no',
    select:(s,_uid,p)=>{const r=studyBRun(s,sp,p as Record<string,unknown>);return {params:p,columns:r.columns,rows:r.rows as RptRow[],sections:r.sections,about:{legacy:r.about.legacy}};}});
  registerMeasReport(sp.id,'meas.study.'+sp.key);
}
