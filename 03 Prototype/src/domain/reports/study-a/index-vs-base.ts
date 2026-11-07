// F12 MEAS · meas-study-a · rpt.study.index-vs-base (catalogue §3.10; selector indexVsBase in domain/analytics/legacy-revenue.ts).
import {registerStudy,FY_PARAM} from './shared';
import {indexVsBase} from '../../analytics/legacy-revenue';

export const study_indexVsBase=registerStudy({id:'rpt.study.index-vs-base',title:'Index vs a base year',money:true,dateClass:'D-FY',params:[FY_PARAM,{key:'baseFy',label:'Base year',type:'text',default:'2018-19'}],select:indexVsBase});
