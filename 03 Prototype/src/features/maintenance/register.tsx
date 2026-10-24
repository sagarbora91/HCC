// Module 15 · Maintenance — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {Wrench} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {MaintenancePage,MntReportsTab} from './Maintenance';
import {MaintenanceMobile} from './MaintenanceMobile';
import {MntWoModal,MntNewModal,MntHoldModal,MntPartModal,MntDoneModal,MntAssetModal,MntPlanModal,MntVendorModal,MntAmcModal,MntReceiveModal} from './modals';
export const registration:FeatureRegistration={
  pages:{'maintenance':MaintenancePage},
  nav:[
    {group:'OPERATIONS',item:{id:'maintenance',label:'Maintenance',icon:Wrench,p:'housekeeping'}}
  ],
  modals:{'mnt-wo':MntWoModal,'mnt-new':MntNewModal,'mnt-hold':MntHoldModal,'mnt-part':MntPartModal,'mnt-done':MntDoneModal,
    'mnt-asset':MntAssetModal,'mnt-plan':MntPlanModal,'mnt-vendor':MntVendorModal,'mnt-amc':MntAmcModal,'mnt-receive':MntReceiveModal,
    /** Printable report in a dialog (phones print dialogs/sheets, not the page shell). */
    'mnt-print':MntReportsTab},
  wideModals:['mnt-wo','mnt-print'],
  /** Maintenance technicians land on their own work orders. */
  landing:{Maintenance:'maintenance'},
  mobile:{pages:{'maintenance':MaintenanceMobile},modals:{}},
};
