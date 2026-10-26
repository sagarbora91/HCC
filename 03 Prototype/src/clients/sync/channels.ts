// W4-11 area "channels": yield & tariffs, OTA channels, booking-website queue, ID document capture (see channels/yield.ts, channels/ota.ts).
import type {SyncArea} from './types';
import {yieldLoaders,yieldCommands} from './channels/yield';
import {otaLoaders,otaCommands} from './channels/ota';

export const channels:SyncArea={
  id:'channels',
  loaders:[...yieldLoaders,...otaLoaders],
  commands:[...yieldCommands,...otaCommands],
  unmapped:{
    CHN_WEB_REQUEST:'public website form preview: the real guest submission is the anonymous POST /public/booking/requests (rate-limited, no staff session); staff only confirm/decline',
    MSG_DOC_EXTRACT:'ID capture on the server needs the real image of a guest document (CaptureIdDocument: guestId + image); the prototype extracts from fictional samples — needs a capture screen (and messagingX.docs belongs to W4-10)',
    MSG_DOC_CONFIRM:'maps to ConfirmIdCapture once captures exist (captureId + expectedVersion); depends on MSG_DOC_EXTRACT and the docs slice owner',
    MSG_DOC_DISCARD:'maps to RejectIdCapture once captures exist; same dependency',
    GUEST_ID_VIEW:'RevealGuestId returns the number in the command result but the screen reads it from the guest record (guestRevealId); needs a shared hook — left to the guests area (W4-1)',
  },
};
