// Module 24–25 · Notifications & document capture — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {MessageSquare,ScanLine} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {MsgMessagesPage,MsgMessagesMobile} from './Messages';
import {MsgDocumentsPage,MsgDocumentsMobile} from './Documents';
import {MsgComposeModal,MsgMessageModal,MsgTemplateModal,MsgConsentModal,MsgDocModal} from './modals';
export const registration:FeatureRegistration={
  pages:{'messages':MsgMessagesPage,'documents':MsgDocumentsPage},
  nav:[
    {group:'FRONT DESK',item:{id:'messages',label:'Messages',icon:MessageSquare,p:'reservations'}},
    {group:'FRONT DESK',item:{id:'documents',label:'Document capture',icon:ScanLine,p:'checkin'}}
  ],
  modals:{
    'msg-compose':MsgComposeModal,   // id = booking ID (optional)
    'msg-message':MsgMessageModal,   // id = message ID
    'msg-template':MsgTemplateModal, // id = template ID
    'msg-consent':MsgConsentModal,   // id = guest key (G-… or B:<booking>)
    'msg-doc':MsgDocModal,           // id = capture ID
  },
  wideModals:['msg-doc'],
  mobile:{pages:{'messages':MsgMessagesMobile,'documents':MsgDocumentsMobile},modals:{}},
};
