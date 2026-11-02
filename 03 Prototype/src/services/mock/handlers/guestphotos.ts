// LF-images B5 (LF-CONTRACT §10.1, DEC-201): a photo for the registered guest and for each accompanying guest. The full ID number of every party member is
// already stored like the primary's (DEC-193, GUEST_SAVE party[n].idNumber); this adds the optional per-person photo, kept in the documents mirror (docObjectsX,
// kind guest-photo), compressed on the device before it gets here (compressDocPhoto, at most 153,600 bytes). Commands: GUEST_PHOTO, GUEST_PHOTO_REMOVE.
// The photo is never required (DEC-193), needs the check-in permission (prototype stand-in for PERM-GST-003 / PERM-DOC-004) and never appears in audit text.
import type {Handler,Kit} from '../commands';
import {dobPut,dobDelete,dobPhotoRef,DOB_MAX_PARTY} from '../../../domain/streams/docobjects';

function target(k:Kit){
  const g=k.s.guests?.find(x=>x.id===k.cmd.id);k.assert(g,'Guest not found.','NOT_FOUND');
  const seq=k.cmd.seq===undefined||k.cmd.seq===null||k.cmd.seq===''?0:Number(k.cmd.seq);
  k.assert(Number.isInteger(seq)&&seq>=0&&seq<=DOB_MAX_PARTY,'Select a party member.');
  const who=seq===0?g!.name:g!.party[seq-1]?.name;
  k.assert(who,'Party member not found.','NOT_FOUND');
  return {g:g!,seq,who:String(who)};
}
export const handlers:Record<string,Handler>={
  GUEST_PHOTO:k=>{
    k.req('checkin');const {g,seq,who}=target(k);
    let next;try{next=dobPut(k.s,{ref:dobPhotoRef(g.id,seq),kind:'guest-photo',owner:g.id+(seq?'#'+seq:''),dataUrl:String(k.cmd.dataUrl??''),at:k.s.clock,by:k.uid});}
    catch(x){return k.fail('VALIDATION',x instanceof Error?x.message:'The photo could not be stored.');}
    k.s.docObjectsX=next;
    return `${g.id} · photo saved for ${seq?'accompanying guest '+seq:'the registered guest'} (${who})`;
  },
  GUEST_PHOTO_REMOVE:k=>{
    k.req('checkin');const {g,seq}=target(k);
    k.assert(k.s.docObjectsX?.[dobPhotoRef(g.id,seq)],'There is no photo to remove.','NOT_FOUND');
    k.s.docObjectsX=dobDelete(k.s,dobPhotoRef(g.id,seq));
    return `${g.id} · photo removed for ${seq?'accompanying guest '+seq:'the registered guest'}`;
  },
};
