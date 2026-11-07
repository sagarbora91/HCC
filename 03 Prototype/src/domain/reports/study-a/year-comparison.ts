// F12 MEAS · meas-study-a · rpt.study.year-comparison (catalogue §3.10; selector yearComparison in domain/analytics/legacy-revenue.ts).
import {registerStudy,FY_PARAM} from './shared';
import {yearComparison} from '../../analytics/legacy-revenue';

export const study_yearComparison=registerStudy({id:'rpt.study.year-comparison',title:'Year comparison',money:true,dateClass:'D-F',params:[{key:'from',label:'From',type:'date',dateClass:'D-F'},{key:'to',label:'To',type:'date',dateClass:'D-F'}],select:yearComparison});
