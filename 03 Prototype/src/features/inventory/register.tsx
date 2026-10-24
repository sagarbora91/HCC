// Module 16 · Linen, laundry & stock — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {Package} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {InventoryPage} from './InventoryPage';
import {InventoryMobile} from './InventoryMobile';
import {InvGrnModal,InvMoveModal,InvLaundrySendModal,InvLaundryReceiveModal,InvLossModal,InvCountModal,InvCountReviewModal,InvItemModal,InvPrintModal} from './modals';
export const registration:FeatureRegistration={
  pages:{'inventory':InventoryPage},
  nav:[
    {group:'OPERATIONS',item:{id:'inventory',label:'Linen & stock',icon:Package,p:'housekeeping'}}
  ],
  modals:{
    'inv-grn':InvGrnModal,                       // id = item to pre-fill, room = quantity
    'inv-move':InvMoveModal,                     // id = item, room = 'issue'|'return'|'use'|'change'
    'inv-laundry-send':InvLaundrySendModal,
    'inv-laundry-receive':InvLaundryReceiveModal,// id = batch
    'inv-loss':InvLossModal,                     // id = item, room = location
    'inv-count':InvCountModal,                   // room = location
    'inv-count-review':InvCountReviewModal,      // id = count
    'inv-item':InvItemModal,                     // id = item
    'inv-print':InvPrintModal,                   // id = 'stock'|'consumption', room = 'from|to'
  },
  wideModals:['inv-grn','inv-laundry-receive','inv-count','inv-count-review','inv-print'],
  mobile:{pages:{'inventory':InventoryMobile},modals:{}},
};
