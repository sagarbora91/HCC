// LF-docdb (LF-CONTRACT §1.10, §1.11, DEC-200): the prototype's mirror of the separate documents database. Every image / file of the demo lives in ONE
// slice, `s.docObjectsX`, keyed by object ref (the server's HccDocuments `dob.object`); records carry only the ref (`Expense.billPhotoRef`,
// `CMPFile.objectRef`, `MSGDoc.objectRef`). The slice is its own IndexedDB record (storage.ts saves one record per slice), so a save that changed no
// image does not rewrite them. Pure functions; handlers assign the returned slice (copy-on-write) exactly as they did for `complianceFilesX`.
import type {State,DOBKind,DOBObject,DOBState} from '../types';

/** G1 (contract §1.11): the on-device compressor aims at 150 KB, the server and the mock refuse above it: 150 × 1,024 = 153,600. */
export const DOC_MAX_BYTES=153600;
/** The deposit slip stays under 100 KB (server CashOutRules.SlipMaxBytes). */
export const SLIP_MAX_BYTES_DOB=102399;
/** Compliance evidence: images 150 KB like every photo, PDFs (challans) keep the old 5 MB (OQ-1). */
export const EVIDENCE_PDF_MAX_BYTES=5*1024*1024;
/** Legacy images (LG importer, read from files, never re-encoded). */
export const LEGACY_MAX_BYTES=8*1024*1024;
/** The server's refusal text (DocumentRules / ExpenseScc / compliance), word for word. */
export const DOC_CAP_MESSAGE='Take the photo again, closer and in good light.';

/** Largest object per kind for a content type ('' = any). */
export function dobCap(kind:DOBKind,contentType=''):number{
  if(kind==='banking-slip')return SLIP_MAX_BYTES_DOB;
  if(kind==='legacy-image')return LEGACY_MAX_BYTES;
  if(kind==='compliance-evidence')return contentType==='application/pdf'?EVIDENCE_PDF_MAX_BYTES:/^image\//.test(contentType)||!contentType?DOC_MAX_BYTES:EVIDENCE_PDF_MAX_BYTES;
  return DOC_MAX_BYTES;
}

const DATA_URL=/^data:([\w.+-]+\/[\w.+-]+)?(?:;[\w.+-]+=[^;,]*)*;base64,/;
/** Content type and decoded size of a base64 data URL (undefined when it is not one). */
export function dobParseDataUrl(dataUrl:string):{contentType:string;bytes:number}|undefined{
  const m=DATA_URL.exec(dataUrl);if(!m)return undefined;
  const b64=dataUrl.slice(m[0].length),pad=b64.endsWith('==')?2:b64.endsWith('=')?1:0;
  return {contentType:m[1]||'application/octet-stream',bytes:Math.max(0,Math.floor(b64.length*3/4)-pad)};
}

/** Why `dataUrl` cannot be stored as `kind` ('' = it can). Same words as the server. */
export function dobRefusal(kind:DOBKind,dataUrl:string):string{
  const p=dobParseDataUrl(dataUrl);
  if(!p||p.bytes<1)return 'Choose a file to attach.';
  return p.bytes>dobCap(kind,p.contentType)?DOC_CAP_MESSAGE:'';
}

export type DobPutArgs={ref:string;kind:DOBKind;owner:string;dataUrl:string;at:string;by:string};
/**
 * Writes one object and returns the new slice (the caller assigns it: `s.docObjectsX=dobPut(s,…)`). Throws an Error with the server's message
 * when the object is empty or above the kind's cap (§1.11). A put never replaces another ref's content.
 */
export function dobPut(s:Pick<State,'docObjectsX'>,a:DobPutArgs):DOBState{
  const why=dobRefusal(a.kind,a.dataUrl);if(why)throw new Error(why);
  const p=dobParseDataUrl(a.dataUrl)!;
  const o:DOBObject={ref:a.ref,kind:a.kind,owner:a.owner,contentType:p.contentType,bytes:p.bytes,dataUrl:a.dataUrl,at:a.at,by:a.by};
  return {...(s.docObjectsX??{}),[a.ref]:o};
}
export const dobGet=(s:Pick<State,'docObjectsX'>,ref?:string):DOBObject|undefined=>ref?s.docObjectsX?.[ref]:undefined;
/** The data URL behind a ref, or the legacy inline value of a pre-v13 record. */
export const dobUrl=(s:Pick<State,'docObjectsX'>,ref?:string,legacy?:string):string|undefined=>dobGet(s,ref)?.dataUrl??legacy;
/** Removes one object (release on the server); returns the new slice. */
export function dobDelete(s:Pick<State,'docObjectsX'>,ref?:string):DOBState{
  const next={...(s.docObjectsX??{})};if(ref)delete next[ref];return next;
}

export type DobSize={total:number;count:number;byKind:Partial<Record<DOBKind,{count:number;bytes:number}>>};
/** Bytes and counts per kind (the demo System health "Databases" row of the documents database). */
export function dobSize(s:Pick<State,'docObjectsX'>):DobSize{
  const out:DobSize={total:0,count:0,byKind:{}};
  for(const o of Object.values(s.docObjectsX??{})){
    out.total+=o.bytes;out.count++;
    const k=out.byKind[o.kind]??={count:0,bytes:0};k.count++;k.bytes+=o.bytes;
  }
  return out;
}

/** The demo's reference check (what the server's check-documents reports): refs held by records that have no object, and objects nobody refers to. */
export function dobRefCheck(s:State):{dangling:number;orphans:number}{
  const refs=new Set<string>();
  for(const e of s.expenses)if(e.billPhotoRef)refs.add(e.billPhotoRef);
  for(const f of s.complianceX?.files??[])if(f.objectRef)refs.add(f.objectRef);
  for(const d of s.messagingX?.docs??[])if(d.objectRef)refs.add(d.objectRef);
  const have=new Set(Object.keys(s.docObjectsX??{}));
  for(const g of s.guests??[])for(let q=0;q<=g.party.length;q++){const r=dobPhotoRef(g.id,q);if(have.has(r))refs.add(r);} // B5: a person's photo is referenced by its owner (by convention, no field)
  let dangling=0,orphans=0;
  for(const r of refs)if(!have.has(r))dangling++;
  for(const r of have)if(!refs.has(r))orphans++;
  return {dangling,orphans};
}

/** Ref of the object that holds a compliance evidence file's content (v12 → v13 move keeps the old file id inside it, so a re-run finds it again). */
export const dobEvidenceRef=(fileId:string)=>'DOB-'+fileId;
/** Ref of an expense's bill photo after the v12 → v13 move. */
export const dobBillRef=(expenseId:string)=>'DOB-'+expenseId+'-bill';

/**
 * The v12 → v13 move (contract §1.10), idempotent and shape-detected: every `Expense.billPhoto` data URL and every `complianceFilesX` entry becomes an
 * object in `docObjectsX`, the record keeps only its ref, and the old fields are deleted. Photos above the new cap are moved as they are (they were
 * accepted when taken; only new puts are refused). Returns true when anything moved, so the first save writes the new slice.
 */
export function dobMigrateV12(s:State):boolean{
  let changed=false;
  let slice:DOBState|undefined;
  const put=(ref:string,kind:DOBKind,owner:string,dataUrl:string,at:string,by:string)=>{
    const p=dobParseDataUrl(dataUrl);if(!p)return false;
    slice??={...(s.docObjectsX??{})};
    slice[ref]={ref,kind,owner,contentType:p.contentType,bytes:p.bytes,dataUrl,at,by};return true;
  };
  for(const e of s.expenses)if(e.billPhoto){
    const ref=dobBillRef(e.id);
    if(put(ref,'expense-bill',e.id,e.billPhoto,e.createdAt??s.clock,e.actor??'')){e.billPhotoRef=ref;changed=true;}
    delete e.billPhoto;changed=true;
  }
  const files=s.complianceFilesX;
  if(files){
    const meta=new Map((s.complianceX?.files??[]).map(f=>[f.id,f]));
    for(const [id,dataUrl] of Object.entries(files)){
      const f=meta.get(id);
      if(f&&put(dobEvidenceRef(id),'compliance-evidence',id,dataUrl,f.addedAt,f.addedBy))f.objectRef=dobEvidenceRef(id);
    }
    delete s.complianceFilesX;changed=true;
  }
  if(slice)s.docObjectsX=slice;
  return changed;
}

/** LF-images B5 (DEC-201): the photo of a guest (seq 0 / absent) or of an accompanying guest (seq 1-6) is one object per person; replacing a photo reuses the ref. */
export const dobPhotoRef=(guestId:string,seq=0)=>'DOB-PHOTO-'+guestId+'-'+seq;
export const DOB_MAX_PARTY=6;
