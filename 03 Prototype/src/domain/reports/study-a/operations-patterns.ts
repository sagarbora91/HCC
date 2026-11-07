// F12 MEAS · meas-study-a · rpt.study.operations-patterns (catalogue §3.10; selector operationsPatterns in domain/analytics/legacy-occupancy.ts).
import {registerStudy,FY_PARAM} from './shared';
import {operationsPatterns} from '../../analytics/legacy-occupancy';

export const study_operationsPatterns=registerStudy({id:'rpt.study.operations-patterns',title:'Operations patterns',money:false,dateClass:'D-FY',params:[FY_PARAM],select:operationsPatterns});
