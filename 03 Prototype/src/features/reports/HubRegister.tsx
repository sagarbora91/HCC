// F7-R2 · UI registration of the Reports hub (page `report-hub`, phone version). Merged by app/registry.ts in its F7-R2 block.
import {LibraryBig} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {ReportsHub} from './HubPage';
import {ReportsHubMobile} from './mobileHub';
export const registration:FeatureRegistration={
  pages:{'report-hub':ReportsHub},
  // Coarse gate 'reports' hides it from Reception / Housekeeping / Maintenance; the report-level view rights (rule V) are applied by the engine.
  nav:[{group:'CLOSING & REPORTS',item:{id:'report-hub',label:'Report library',icon:LibraryBig,p:['reports','billing','reservations','checkin','housekeeping']},after:'reports'}],
  mobile:{pages:{'report-hub':ReportsHubMobile},modals:{}},
};
