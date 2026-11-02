// LF-IMAGES B5 · party photos in the production sync layer (LF-CONTRACT §10.1, DEC-193 / DEC-201).
//   server    POST /commands/CaptureIdDocument {guestId, docKind:'photo', contentType, imageBase64, partySeq?} (no OCR needed for a photo; stored sealed in HccDocuments),
//             GET /documents/id-captures?guestId= (masked metadata, PERM-DOC-004), POST /commands/ViewIdCaptureImage {captureId} (audited read of one image)
//   commands  GUEST_PHOTO → CaptureIdDocument (seq 0 = the registered guest, 1-6 = accompanying guest)
//   GUEST_PHOTO_REMOVE → RemoveGuestPhoto {captureId, reason} (soft: status Rejected + audit GUEST_PHOTO_REMOVE; the sealed image stays until the retention purge)
// The picture itself is never put in the runtime state: PartyPhotosServer (guestphoto-ui.tsx) holds it in component state only.
import {HccClientError} from '../errors';
import type {HttpTransport} from '../http';
import {serverId} from './map';
import type {SyncArea} from './types';

type SrvCapture={captureId:number;guestId:number;docKind:string;partySeq:number|null;capturedUtc:string;imageAvailable:boolean;status:string};
/** The newest photo capture per person (seq 0 = the registered guest). */
export function photosBySeq(list:SrvCapture[]):Map<number,SrvCapture>{
  const m=new Map<number,SrvCapture>();
  for(const c of list){if(c.docKind!=='photo'||!c.imageAvailable||c.status==='Rejected'||c.status==='Purged')continue;const q=c.partySeq??0,o=m.get(q);if(!o||c.capturedUtc>o.capturedUtc||(c.capturedUtc===o.capturedUtc&&c.captureId>o.captureId))m.set(q,c);}
  return m;
}
export async function listPartyPhotos(t:Pick<HttpTransport,'get'>,guestId:string|number):Promise<Map<number,SrvCapture>>{
  return photosBySeq(await t.get<SrvCapture[]>('/documents/id-captures',{guestId:Number(guestId)}));
}
/** One audited image read; the result is a data URL for an <img> and must be dropped by the caller when the screen closes. */
export async function viewPartyPhoto(t:Pick<HttpTransport,'command'>,captureId:number):Promise<string>{
  const key=`photoview-${captureId}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2,10)}`;
  const r=await t.command<{contentType:string;imageBase64:string}>('ViewIdCaptureImage',{captureId},{idempotencyKey:key});
  return `data:${r.contentType};base64,${r.imageBase64}`;
}
/** data:image/jpeg;base64,… → {contentType, base64}. */
export function splitDataUrl(u:unknown):{contentType:string;base64:string}{
  const m=/^data:(image\/[a-z0-9.+-]+);base64,([A-Za-z0-9+/=]+)$/i.exec(String(u??''));
  if(!m)throw new HccClientError('VALIDATION','The photo could not be read. Take it again.');
  return {contentType:m[1].toLowerCase(),base64:m[2]};
}

export const guestphoto:SyncArea={
  id:'guestphoto',
  loaders:[],
  commands:[
    {type:'GUEST_PHOTO',reload:[],async send(c){const k=c.cmd;const {contentType,base64}=splitDataUrl(k.dataUrl);
      const seq=k.seq===undefined||k.seq===null||k.seq===''?0:Number(k.seq);
      await c.api.command('CaptureIdDocument',{guestId:serverId(k.id,'Guest'),docKind:'photo',contentType,imageBase64:base64,partySeq:seq>0?seq:undefined});}},
    {type:'GUEST_PHOTO_REMOVE',reload:[],async send(c){const k=c.cmd;const seq=k.seq===undefined||k.seq===null||k.seq===''?0:Number(k.seq);
      const cur=(await listPartyPhotos(c.api,serverId(k.id,'Guest'))).get(seq);
      if(!cur)throw new HccClientError('VALIDATION','There is no photo to remove.');
      await c.api.command('RemoveGuestPhoto',{captureId:cur.captureId,reason:String(k.reason??'').trim()||'Removed from the guest profile'});}},
  ],
};
