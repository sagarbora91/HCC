// F12 MEAS · meas-study-a · rpt.study.fy-deep-dive (catalogue §3.10; selector fyDeepDive in domain/analytics/legacy-revenue.ts).
import {registerStudy,FY_PARAM} from './shared';
import {fyDeepDive} from '../../analytics/legacy-revenue';

export const study_fyDeepDive=registerStudy({id:'rpt.study.fy-deep-dive',title:'FY deep dive (price-volume, segments, top bills)',money:true,dateClass:'D-FY',params:[FY_PARAM,{key:'topN',label:'Top bills',type:'text',default:'10'}],select:fyDeepDive});
