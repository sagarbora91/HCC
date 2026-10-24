// Stream D · Housekeeping & rooms — UI registration. Pages/modals registered here are merged by app/registry.ts.
// A page or modal with an existing ID replaces the built-in one (only replace IDs your stream owns).
// Modals: 'hk-reject' (id = task ID), 'room-block' (id = room ID), 'room-repair' (id = room ID).
import {DatabaseBackup,Map} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {Backup} from './Backup';
import {Roadmap} from './Roadmap';
import {RejectModal,BlockModal,RepairModal} from './modals';
export const registration:FeatureRegistration={
  pages:{backup:Backup,roadmap:Roadmap},
  nav:[
    {group:'ADMINISTRATION',item:{id:'backup',label:'Backup & recovery',icon:DatabaseBackup,p:'admin'}}, // PERM-ADM-005
    {group:'ADMINISTRATION',item:{id:'roadmap',label:'Roadmap',icon:Map,p:'reports'}},
  ],
  modals:{'hk-reject':RejectModal,'room-block':BlockModal,'room-repair':RepairModal},
};
