// LF-ALERTS · UI registration: page 'desk-alerts' (Front Desk › Reminders & wake-ups), its phone page, and the 'rmd-edit' / 'shortcuts' dialogs.
import {BellRing} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {DeskAlertsPage} from './DeskAlertsPage';
import {RmdEditModal,ShortcutsModal} from './modals';
import {MfDeskAlerts} from './Mobile';

export const registration:FeatureRegistration={
  pages:{'desk-alerts':DeskAlertsPage},
  nav:[{group:'FRONT DESK',item:{id:'desk-alerts',label:'Reminders & wake-ups',icon:BellRing,p:['checkin','expenses','approve']},after:'keys'}],
  modals:{'rmd-edit':RmdEditModal,shortcuts:ShortcutsModal},
  wideModals:['shortcuts'],
  mobile:{pages:{'desk-alerts':MfDeskAlerts}},
};
