// F12 MEAS · meas-study-a · rpt.study.rooms-and-rates (catalogue §3.10; selector roomsAndRates in domain/analytics/legacy-occupancy.ts).
import {registerStudy,FY_PARAM} from './shared';
import {roomsAndRates} from '../../analytics/legacy-occupancy';

export const study_roomsAndRates=registerStudy({id:'rpt.study.rooms-and-rates',title:'Rooms, rates and discounts',money:true,dateClass:'D-FY',params:[FY_PARAM],select:roomsAndRates});
