// MA-1 · Rooms and room types masters — UI registration. The four masters (room types, rooms, amenities, occupancy charges) appear by
// themselves in Master configuration › Rooms and rate plans › Lists (MA-0 registry); this adds the Rooms tools above the list (range add,
// renumber, out of order; the generic Add and reorder are hidden for rooms) and the folio dialog 'rms-extra' (extra person / bed).
import type {FeatureRegistration} from '../../app/registry';
import {registerMasterTools} from '../masters/tools';
import {RoomTools} from './RoomTools';
import {ExtraModal,RmsExtraButton} from './ExtraModal';
import type {Booking} from '../../services/mock';

/** Booking dialog slot: "Extra person / bed" for open stays (billing holders; desktop dialog and phone sheet). */
function RmsBookingActions({b}:{b:Booking}){return b.status==='In house'||b.status==='Confirmed'?<RmsExtraButton id={b.id} className="btn"/>:null;}

// V14-7: rooms keep the floor / number order (read from s.rooms), so there are no arrows; the list says why. Paste rows: mstCanPaste (MST_IO_LOCAL).
registerMasterTools('rooms',{Tools:RoomTools,hideAdd:true,noReorder:true,noReorderNote:'Rooms are listed by floor and room number, so they have no manual order (no move arrows).'});

export const registration:FeatureRegistration={
  modals:{'rms-extra':ExtraModal},
  bookingActions:RmsBookingActions,
  mobile:{modals:{'rms-extra':ExtraModal}},
};
