// F12 MEAS · meas-study-a · rpt.study.zero-rate-complimentary (catalogue §3.10; selector zeroRateComplimentary in domain/analytics/legacy-zero.ts).
import {registerStudy,FY_PARAM} from './shared';
import {zeroRateComplimentary} from '../../analytics/legacy-zero';

export const study_zeroRateComplimentary=registerStudy({id:'rpt.study.zero-rate-complimentary',title:'Zero-rate and complimentary stays',money:true,dateClass:'D-FY',params:[FY_PARAM],select:zeroRateComplimentary});
