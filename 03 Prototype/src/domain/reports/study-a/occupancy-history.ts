// F12 MEAS · meas-study-a · rpt.study.occupancy-history (catalogue §3.10; selector occupancyHistory in domain/analytics/legacy-occupancy.ts).
import {registerStudy,FY_PARAM} from './shared';
import {occupancyHistory} from '../../analytics/legacy-occupancy';

export const study_occupancyHistory=registerStudy({id:'rpt.study.occupancy-history',title:'Occupancy history (three denominators)',money:false,dateClass:'D-FY',params:[FY_PARAM],select:occupancyHistory});
