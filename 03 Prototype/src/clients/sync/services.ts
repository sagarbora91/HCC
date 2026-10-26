// W4-10 "services" slot: guest services (requests, complaints, lost & found), messaging (templates, consent, simulated outbox) and shift
// handover (checklists, desk notes, carry-forward) are three server modules behind one registry slot. The parts live in services/*.ts.
import type {SyncArea} from './types';
import {guestServices} from './services/guest';
import {messaging} from './services/messaging';
import {handover} from './services/handover';

export const services:SyncArea={
  id:'services',
  loaders:[...guestServices.loaders,...messaging.loaders,...handover.loaders],
  commands:[...guestServices.commands,...messaging.commands,...handover.commands],
  unmapped:{...guestServices.unmapped,...messaging.unmapped,...handover.unmapped},
};
