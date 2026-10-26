// Cashier area (W4 fix F3, D4): the "Open cashier shift" control the prototype never had.
// The prototype opens a shift only as a side effect of NEXT_DAY (DEC-035). On a new installation the till-cash import leaves the first
// day's shift closed (a counted opening: the float), and no day close has happened yet, so the Cashier screen had no way to open one.
// SHIFT_OPEN (production only; sent from OpenShiftPanel in cashier-ui.tsx) → OpenShift {tillId, openingFloatPaise:null}: the server carries
// the float from the last counted close (the imported opening count); nothing is typed here. Same command the day close sends after a run.
import type {SyncArea} from './types';
import {CASH_SLICES} from './money';
import {HccClientError} from '../errors';

/** PERM id the server's OpenShift requires (BillingPermissions.ShiftCount). */
const SHIFT_PERM='PERM-SHF-001';

export const cashier:SyncArea={
  id:'cashier',
  loaders:[],
  commands:[{type:'SHIFT_OPEN',reload:CASH_SLICES,
    precheck:(_cmd,s,me)=>!me.permIds.includes(SHIFT_PERM)?'Opening a cash shift needs the cashier permission.':s.shiftOpen?'A shift is already open.':undefined,
    async send(c){
      const till=c.ids.server('till','desk');
      if(till===undefined)throw new HccClientError('INVALID_TRANSITION','No cash till is set up yet. The owner imports the till-cash file (deployment/import-templates, README step 8), then refresh.');
      const r=await c.api.command('OpenShift',{tillId:till,openingFloatPaise:null});
      return {result:r};}}],
  unmapped:{},
};
