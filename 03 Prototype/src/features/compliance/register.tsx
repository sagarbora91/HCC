// Compliance calendar (SCC tax module in full, DEC-116) — UI registration (desktop page, nav, modals, phone page). Merged by app/registry.ts.
import {CalendarCheck} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {CompliancePage,ComplianceMobile} from './CompliancePage';
import {CmpActionModal,CmpProfileModal,CmpReopenModal,CmpManageModal,CmpCustomModal,CmpPrintModal,CmpPackModal} from './modals';
import {CmpGstr1Modal,CmpGstr1Mobile} from './Gstr1';
// Visible to Owner and Accountant only (DEC-055). Nav can only test permissions, so the page and every command re-check the role (cmpHasAccess).
export const registration:FeatureRegistration={
  pages:{'compliance':CompliancePage},
  nav:[{group:'CLOSING & REPORTS',item:{id:'compliance',label:'Compliance calendar',icon:CalendarCheck,p:'reports'}}],
  modals:{
    'cmp-action':CmpActionModal,       // SCC Action Center (30 days)
    'cmp-profile':CmpProfileModal,     // firm profile (one per property)
    'cmp-reopen':CmpReopenModal,       // id = item ID, room = FY start year (Owner)
    'cmp-manage':CmpManageModal,       // categories / custom / built-in archive + HCC CA settings
    'cmp-custom':CmpCustomModal,       // id = custom obligation ID (absent = new)
    'cmp-print':CmpPrintModal,         // id = month (A4 landscape)
    'cmp-pack':CmpPackModal,
    'cmp-gstr1':CmpGstr1Modal,         // DEC-072 GSTR-1 working summary (rooms INV + restaurant RST + credit notes)
  },
  wideModals:['cmp-action','cmp-manage','cmp-custom','cmp-print','cmp-pack','cmp-gstr1'],
  mobile:{pages:{'compliance':ComplianceMobile},modals:{'cmp-gstr1':CmpGstr1Mobile}},
};
