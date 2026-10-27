// Module 17 · HR & payroll — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
// 'staff'  = HR desk. Nav permission 'reports' (Owner, Manager, Cashier, Accountant); content is gated by role inside:
//            managers (approve) see people/roster/attendance/leave without salaries; Owner/Accountant see payroll; others see My HR.
// 'my-hr'  = self-service (own roster, check-in, leave, payslips). Nav permission 'housekeeping' (Owner, Manager, Supervisor,
//            Housekeeper, Maintenance) — the most widely held single permission among roster staff.
import {IdCard,CalendarClock} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {HrDesk,HrDeskMobile} from './HrDesk';
import {MyHr,MyHrMobile} from './MyHr';
import {HRStaffModal,HRRosterModal,HRAttModal,HRAdvanceModal,HRRatesModal,HRPayslipModal,HRUnlockModal,HRFnfModal,HRDeptModal,
  HRAttCorrectModal,HRRequestDecideModal,HRHolidayModal} from './modals';
import {HRLeaveModal,HRLeaveDecideModal,HRLeavePolicyModal,HRLeaveDayModal,HRLeaveDetailModal,HRLeaveApprovalsModal,HRAgendaModal,HRLeaveEntModal,HRLeaveBlackoutModal,
  HRLeavePrintModal,HRLeaveImportModal,HRLeaveClearModal} from './leaveModals';
export const registration:FeatureRegistration={
  pages:{'staff':HrDesk,'my-hr':()=><MyHr/>},
  nav:[
    {group:'ADMINISTRATION',item:{id:'staff',label:'Staff & payroll',icon:IdCard,p:'reports'}},
    // 'billing' adds the Restaurant role (DEC-079 D1, POS operate only): every other billing holder already reaches it through another permission.
    {group:'OPERATIONS',item:{id:'my-hr',label:'My shifts & pay',icon:CalendarClock,p:['housekeeping','reservations','payments','reports','admin','billing']}},
  ],
  modals:{'hr-staff':HRStaffModal,'hr-roster':HRRosterModal,'hr-att':HRAttModal,'hr-leave':HRLeaveModal,'hr-leave-decide':HRLeaveDecideModal,
    'hr-advance':HRAdvanceModal,'hr-rates':HRRatesModal,'hr-payslip':HRPayslipModal,'hr-leave-policy':HRLeavePolicyModal,'hr-unlock':HRUnlockModal,'hr-fnf':HRFnfModal,
    // Wave B item B4: DEC-075 past-attendance correction, the Owner's decision on a correction or bank-detail change (DEC-084), DEC-083 holidays.
    'hr-att-correct':HRAttCorrectModal,'hr-request-decide':HRRequestDecideModal,'hr-holiday':HRHolidayModal,
    // SCC leave port (DEC-116 / DEC-119): day view, leave detail, approvals, agenda, settings, ⚙ override, blackout, print, import, clear.
    'hr-leave-day':HRLeaveDayModal,'hr-leave-detail':HRLeaveDetailModal,'hr-leave-approvals':HRLeaveApprovalsModal,'hr-agenda':HRAgendaModal,'hr-leave-ent':HRLeaveEntModal,
    'hr-leave-blackout':HRLeaveBlackoutModal,'hr-leave-print':HRLeavePrintModal,'hr-leave-import':HRLeaveImportModal,'hr-leave-clear':HRLeaveClearModal,
    ...(process.env.HCC_BUILD==='production'?{'hr-dept':HRDeptModal}:{})},   // N3: department editor (server build only)
  wideModals:['hr-payslip','hr-rates','hr-fnf','hr-leave-day','hr-leave-approvals','hr-leave-print','hr-leave-policy'],
  mobile:{pages:{'staff':HrDeskMobile,'my-hr':MyHrMobile},modals:{}},
};
