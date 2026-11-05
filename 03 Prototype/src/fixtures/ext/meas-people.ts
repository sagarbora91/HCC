// F12-CAP-PEOPLE · fictional demo rows for the people captures (training, one exit, pulse answers). Not part of the seed: tests and the demo
// loader apply them with measPeopleDemo(s). Staff ids are taken from the state (first active staff), never hard-coded.
import type {State} from '../../domain/types';
import type {MeasTraining,MeasPulse} from '../../domain/streams/measure-types';
import {addDays} from '../../domain/money';

export function measPeopleDemoTraining(s:State,staffIds:string[]):MeasTraining[]{
  const [a,b]=[staffIds[0]??'',staffIds[1]??staffIds[0]??''];
  return [
    {id:'TRN-D1',staff:a,course:'Fire safety (fictional)',date:addDays(s.date,-200),hours:4,certificateNo:'FS-1001',validTo:addDays(s.date,20),mandatory:true},
    {id:'TRN-D2',staff:b,course:'Food hygiene (fictional)',date:addDays(s.date,-90),hours:6,certificateNo:'FH-2002',validTo:addDays(s.date,270),mandatory:true},
    {id:'TRN-D3',staff:a,course:'Guest service basics',date:addDays(s.date,-30),hours:3},
  ];
}
export function measPeopleDemoPulse(deptIds:string[]):MeasPulse[]{
  const d=deptIds[0]??'';
  return [{id:'PLS-D1',quarter:'2026-27-Q2',dept:d,workload:3,respect:4,tools:3,payFairness:2,recommend:6},{id:'PLS-D2',quarter:'2026-27-Q2',dept:d,workload:4,respect:4,tools:4,payFairness:3,recommend:8}];
}
