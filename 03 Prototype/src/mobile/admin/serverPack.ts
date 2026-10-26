// Phone Reports on the production build (Wave 4 gap G3): the pack is the server's report for the range (figures only).
import {useMemo} from 'react';
import type {State,ReportPack} from '../../services/mock';
import {useServerReport} from '../../features/reports/serverBits';
import {NO_FETCH} from '../../features/reports/asOf';
import {maRangeCheck,maReportPack,maScopedPack,maMaskReports,maLimit} from './logic';

export function useMaPack(s:State,uid:string,from:string,to:string){
  const ok=maRangeCheck(from,to,s.date,maLimit(s)).ok;
  const srv=process.env.HCC_BUILD==='production'?useServerReport(from,to,ok):NO_FETCH; // eslint-disable-line react-hooks/rules-of-hooks
  const mask=maMaskReports(s,uid);
  const pack:ReportPack|null=useMemo(()=>process.env.HCC_BUILD==='production'?(srv.data?maScopedPack(srv.data.pack,mask):null):maReportPack(s,uid,from,to),
    [srv.data,mask,s.bookings,s.expenses,s.rooms,s.posX,s.closings,s.closedDates,s.date,s.openingCash,s.shiftClose,from,to,uid]); // eslint-disable-line react-hooks/exhaustive-deps
  return {pack,srv};
}
