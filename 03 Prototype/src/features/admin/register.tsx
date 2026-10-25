// Stream E · Access & configuration — UI registration. Pages/modals registered here are merged by app/registry.ts.
// A page or modal with an existing ID replaces the built-in one (only replace IDs your stream owns).
import {ClipboardCheck} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {AccessControl} from './AccessControl';
import {HotelSettings} from './HotelSettings';
import {ApprovalsAudit} from './ApprovalsAudit';
import {UserModal,SettingModal,TariffModal} from './modals';
import {CfgSettingSheet,CfgImportSheet} from './config/SettingSheet';
export const registration:FeatureRegistration={
  pages:{access:AccessControl,settings:HotelSettings,approvals:ApprovalsAudit},
  // Coarse gate 'reports' (Owner, Manager, Accountant; Cashier sees own requests). Audit content is further gated by PERM-ADM-004.
  nav:[{group:'ADMINISTRATION',item:{id:'approvals',label:'Approvals & audit',icon:ClipboardCheck,p:'reports'},after:'access'}],
  modals:{'admin-user':UserModal,'admin-setting':SettingModal,'admin-tariff':TariffModal,'cfg-setting':CfgSettingSheet,'cfg-import':CfgImportSheet},
};
