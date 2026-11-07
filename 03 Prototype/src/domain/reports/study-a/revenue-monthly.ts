// F12 MEAS · meas-study-a · rpt.study.revenue-monthly (catalogue §3.10; selector revenueMonthly in domain/analytics/legacy-revenue.ts).
import {registerStudy,FY_PARAM} from './shared';
import {revenueMonthly} from '../../analytics/legacy-revenue';

export const study_revenueMonthly=registerStudy({id:'rpt.study.revenue-monthly',title:'Monthly revenue and cash (FY history)',money:true,dateClass:'D-FY',params:[FY_PARAM],select:revenueMonthly});
