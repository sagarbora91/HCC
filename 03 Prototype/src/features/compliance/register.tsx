// Wave 0.5 · W05-C Compliance calendar — UI registration (desktop page, nav, modals, phone page). Merged by app/registry.ts.
import {CalendarCheck} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {CompliancePage,ComplianceMobile} from './CompliancePage';
import {CmpFileModal,CmpNaModal,CmpReopenModal,CmpPackModal,CmpManageModal,CmpObligationModal} from './modals';
// Visible to Owner and Accountant only (DEC-055). Nav can only test permissions, so the page and every command re-check the role (cmpHasAccess).
export const registration:FeatureRegistration={
  pages:{'compliance':CompliancePage},
  nav:[{group:'CLOSING & REPORTS',item:{id:'compliance',label:'Compliance calendar',icon:CalendarCheck,p:'reports'}}],
  modals:{
    'cmp-file':CmpFileModal,           // id = obligation ID, room = period
    'cmp-na':CmpNaModal,               // id = obligation ID, room = period
    'cmp-reopen':CmpReopenModal,       // id = obligation ID, room = period
    'cmp-pack':CmpPackModal,
    'cmp-manage':CmpManageModal,
    'cmp-obligation':CmpObligationModal, // id = obligation ID (absent = new)
  },
  wideModals:['cmp-manage','cmp-pack'],
  mobile:{pages:{'compliance':ComplianceMobile},modals:{}},
};
