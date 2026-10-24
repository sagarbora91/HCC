// MB-08 closing, reports, admin, config, audit, backup, roadmap (M17–M23) — phone screens. Registers mobile.pages / mobile.modals keyed by the desktop page / modal ID they replace on phones.
import type {FeatureRegistration} from '../../app/registry';
import {MaApprovals} from './Approvals';
import {MaClosing} from './Closing';
import {MaReports} from './Reports';
import {MaAccess} from './Access';
import {MaSettings} from './Settings';
import {MaBackup,MaRoadmap} from './Backup';
import {MaRejectSheet,MaAuditFilterSheet,MaRangeSheet,MaDrillSheet,MaDictionarySheet} from './sheets';
export const registration:FeatureRegistration={mobile:{
  pages:{approvals:MaApprovals,closing:MaClosing,reports:MaReports,access:MaAccess,settings:MaSettings,backup:MaBackup,roadmap:MaRoadmap},
  // Phone-only sheets. They are not desktop modals, so they live under mobile.modals with the ma- prefix.
  modals:{'ma-reject':MaRejectSheet,'ma-audit-filter':MaAuditFilterSheet,'ma-range':MaRangeSheet,'ma-drill':MaDrillSheet,'ma-dictionary':MaDictionarySheet},
}};
