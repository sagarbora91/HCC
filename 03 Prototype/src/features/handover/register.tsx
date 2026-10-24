// Module 19 · Checklists & shift handover — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {ClipboardList} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {HandoverPage} from './HandoverPage';
import {HandoverMobile} from './HandoverMobile';
import {HovItemModal,HovNoteModal,HovAckModal,HovTemplateModal,HovReopenModal} from './modals';
// Single nav entry 'handover' with any-of permissions ['housekeeping','payments']: floor staff and front desk/cashier reach the same page; content is filtered by role.
export const registration:FeatureRegistration={
  pages:{'handover':HandoverPage},
  nav:[
    // One entry for every checklist role: floor staff via housekeeping, front desk/cashier via payments (any-of).
    {group:'OPERATIONS',item:{id:'handover',label:'Checklists & handover',icon:ClipboardList,p:['housekeeping','payments']}},
  ],
  modals:{
    'hov-item':HovItemModal,         // id = template ID, room = item ID
    'hov-note':HovNoteModal,         // id = desk (optional)
    'hov-ack':HovAckModal,           // id = note ID
    'hov-template':HovTemplateModal, // id = template ID (absent = new)
    'hov-reopen':HovReopenModal,     // id = template ID, room = business date
  },
  wideModals:['hov-template','hov-note'],
  mobile:{pages:{'handover':HandoverMobile},modals:{}},
};
