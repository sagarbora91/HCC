// Module 17 · HR & payroll — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
// 'staff'  = HR desk. Nav permission 'reports' (Owner, Manager, Cashier, Accountant); content is gated by role inside:
//            managers (approve) see people/roster/attendance/leave without salaries; Owner/Accountant see payroll; others see My HR.
// 'my-hr'  = self-service (own roster, check-in, leave, payslips). Nav permission 'housekeeping' (Owner, Manager, Supervisor,
//            Housekeeper, Maintenance) — the most widely held single permission among roster staff.
import {IdCard,CalendarClock} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {HrDesk,HrDeskMobile} from './HrDesk';
import {MyHr,MyHrMobile} from './MyHr';
import {HRStaffModal,HRRosterModal,HRAttModal,HRLeaveModal,HRLeaveDecideModal,HRAdvanceModal,HRRatesModal,HRPayslipModal} from './modals';
export const registration:FeatureRegistration={
  pages:{'staff':HrDesk,'my-hr':()=><MyHr/>},
  nav:[
    {group:'ADMINISTRATION',item:{id:'staff',label:'Staff & payroll',icon:IdCard,p:'reports'}},
    {group:'OPERATIONS',item:{id:'my-hr',label:'My shifts & pay',icon:CalendarClock,p:['housekeeping','reservations','payments','reports','admin']}},
  ],
  modals:{'hr-staff':HRStaffModal,'hr-roster':HRRosterModal,'hr-att':HRAttModal,'hr-leave':HRLeaveModal,'hr-leave-decide':HRLeaveDecideModal,
    'hr-advance':HRAdvanceModal,'hr-rates':HRRatesModal,'hr-payslip':HRPayslipModal},
  wideModals:['hr-payslip'],
  mobile:{pages:{'staff':HrDeskMobile,'my-hr':MyHrMobile},modals:{}},
};
