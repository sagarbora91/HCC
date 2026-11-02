// LF-IMAGES (G1) generalised this file: compressPhoto(file, profile) with PHOTO_SLIP / PHOTO_DOC; the slip behaviour is byte-for-byte as before.
// Wave B1 (DEC-073): the bank-deposit slip photo is compressed ON THE DEVICE to under 100 KB before it is sent (the server refuses anything
// ≥ 102,400 bytes or not JPEG/PNG and never re-encodes). Long edge ≤ 1,280 px, JPEG quality stepped down; if the slip still does not fit,
// the long edge steps down (1,024, then 800 px) — and if nothing fits, the photo is refused (take it again, closer, in better light).
// The search is pure (`slipCompressPlan`) so it runs in node tests with a fake encoder; `compressSlipPhoto` is the browser / Android WebView
// adapter (createImageBitmap or <img>, a canvas, canvas.toBlob). No library, nothing leaves the device until the command is sent.

/** Under 100 KB: 100 × 1,024 = 102,400 bytes, so at most 102,399 (server CashOutRules.SlipMaxBytes). */
export const SLIP_MAX_BYTES=102399;
/** Long edges tried in order (px). */
export const SLIP_EDGES=[1280,1024,800];
/** JPEG qualities tried in order at each size. */
export const SLIP_QUALITIES=[0.82,0.72,0.62,0.52,0.42,0.32];

// ── LF-IMAGES (LF-CONTRACT §1.11, G1): one compressor for every photo. A profile = the byte limit, the long edges and the JPEG qualities tried. ──
export type PhotoProfile={maxBytes:number;edges:number[];qualities:number[];label:string};
/** The deposit slip, unchanged: under 100 KB (server CashOutRules.SlipMaxBytes). */
export const PHOTO_SLIP:PhotoProfile={maxBytes:102_399,edges:[1280,1024,800],qualities:[0.82,0.72,0.62,0.52,0.42,0.32],label:'deposit slip'};
/** G1: ID images, guest photos, expense bills and evidence images aim at 150 KB (server DocumentRules.MaxImageBytes = 153,600). */
export const PHOTO_DOC:PhotoProfile={maxBytes:153_600,edges:[1600,1280,1024,800],qualities:[0.85,0.78,0.7,0.6,0.5,0.4],label:'document'};

export type SlipEncoded={bytes:Uint8Array;width:number;height:number;quality:number};
/** Encodes the source scaled to width × height at a JPEG quality (0–1) and returns the file's bytes. */
export type SlipEncoder=(width:number,height:number,quality:number)=>Promise<Uint8Array>;

/** Target size for a long edge: never upscales; keeps the aspect ratio; at least 1 px each way. */
export function slipFit(width:number,height:number,edge:number):{width:number;height:number}{
  const long=Math.max(width,height);if(long<=edge)return {width:Math.max(1,Math.round(width)),height:Math.max(1,Math.round(height))};
  const k=edge/long;return {width:Math.max(1,Math.round(width*k)),height:Math.max(1,Math.round(height*k))};
}

/**
 * The first (size, quality) whose JPEG fits the profile's limit: the largest long edge that fits wins, and at each edge the highest quality that fits.
 * Throws a user-facing Error when nothing fits (take it again, closer and in good light).
 */
export async function photoCompressPlan(width:number,height:number,encode:SlipEncoder,profile:PhotoProfile):Promise<SlipEncoded>{
  if(!(width>0&&height>0))throw new Error('The photo could not be read. Take it again.');
  let tried='';
  for(const edge of profile.edges){
    const size=slipFit(width,height,edge);const key=size.width+'x'+size.height;
    if(key===tried)continue; // a small photo gives the same size at every edge: one pass is enough
    tried=key;
    for(const quality of profile.qualities){
      const bytes=await encode(size.width,size.height,quality);
      if(bytes.length>0&&bytes.length<=profile.maxBytes)return {bytes,width:size.width,height:size.height,quality};
    }
  }
  throw new Error(profile.label===PHOTO_SLIP.label?'The slip photo cannot be made smaller than 100 KB. Take it again, closer to the slip and in good light.'
    :`The ${profile.label} photo cannot be made smaller than ${Math.round(profile.maxBytes/1024)} KB. Take the photo again, closer and in good light.`);
}
/** The deposit slip's search (behaviour unchanged since Wave B1; `max` lets a test shrink the limit). */
export const slipCompressPlan=(width:number,height:number,encode:SlipEncoder,max=SLIP_MAX_BYTES):Promise<SlipEncoded>=>photoCompressPlan(width,height,encode,{...PHOTO_SLIP,maxBytes:max});

/** Bytes → data URL (base64) without spreading large arrays onto the stack. */
export function slipDataUrl(bytes:Uint8Array,type='image/jpeg'):string{
  let bin='';for(let i=0;i<bytes.length;i+=0x8000)bin+=String.fromCharCode(...bytes.subarray(i,i+0x8000));
  return `data:${type};base64,`+btoa(bin);
}

type Drawable={width:number;height:number;close?:()=>void};
async function decode(file:Blob):Promise<{img:CanvasImageSource&Drawable;width:number;height:number}>{
  const g=globalThis as {createImageBitmap?:(b:Blob,o?:object)=>Promise<ImageBitmap>};
  if(typeof g.createImageBitmap==='function'){
    try{const b=await g.createImageBitmap(file,{imageOrientation:'from-image'});return {img:b,width:b.width,height:b.height};}catch{/* older WebView: fall back to <img> */}
  }
  const url=URL.createObjectURL(file);
  try{
    const img=new Image();img.decoding='async';
    await new Promise<void>((ok,bad)=>{img.onload=()=>ok();img.onerror=()=>bad(new Error('The photo could not be read. Take it again.'));img.src=url;});
    return {img,width:img.naturalWidth,height:img.naturalHeight};
  }finally{setTimeout(()=>URL.revokeObjectURL(url),0);}
}

/**
 * Compresses a camera photo (from `<input type="file" accept="image/*" capture="environment">`) to a JPEG that fits the profile and returns it as a
 * data URL with its size. Throws an Error with a user-facing message when it cannot. Every image pick site calls this before the upload (G1).
 */
export async function compressPhoto(file:Blob,profile:PhotoProfile):Promise<{dataUrl:string;bytes:number;width:number;height:number}>{
  if(!/^image\//.test(file.type||'image/'))throw new Error(`Choose a photo of the ${profile.label}.`);
  const src=await decode(file);
  const canvas=document.createElement('canvas');const ctx=canvas.getContext('2d');
  if(!ctx)throw new Error('This device cannot prepare the photo. Use another device.');
  const encode:SlipEncoder=async(w,h,q)=>{
    canvas.width=w;canvas.height=h;ctx.fillStyle='#fff';ctx.fillRect(0,0,w,h);ctx.drawImage(src.img,0,0,w,h);
    const blob=await new Promise<Blob|null>(ok=>canvas.toBlob(ok,'image/jpeg',q));
    if(!blob)throw new Error('This device cannot prepare the photo. Use another device.');
    return new Uint8Array(await blob.arrayBuffer());
  };
  try{
    const r=await photoCompressPlan(src.width,src.height,encode,profile);
    return {dataUrl:slipDataUrl(r.bytes),bytes:r.bytes.length,width:r.width,height:r.height};
  }finally{src.img.close?.();canvas.width=0;canvas.height=0;}
}
/** The deposit slip photo (PHOTO_SLIP). */
export const compressSlipPhoto=(file:Blob)=>compressPhoto(file,PHOTO_SLIP);

// ── DEC-120 · expense bill photo (SCC expense.js readPhoto 456-473), through the same decode / canvas / data-URL pipeline as the slip ──
// SCC: a picked file above 4 MB is refused ("Photo too large (max 4MB)"); the photo is drawn at scale min(1, 1000 ÷ width) — width at most
// 1000 px, never enlarged, the canvas truncating the scaled size to whole pixels — and saved as JPEG quality 0.7. One pass, no size search.
/** SCC: the largest picked file (4 MB). */
export const BILL_MAX_INPUT_BYTES=4*1024*1024;
/** SCC: the widest saved photo (px) and its JPEG quality. */
export const BILL_WIDTH=1000;
export const BILL_QUALITY=0.7;
/** SCC's canvas size: scale = min(1, 1000 ÷ width); a canvas width / height set to a fraction keeps the whole pixels (at least 1 here). */
export function billFit(width:number,height:number):{width:number;height:number}{
  const sc=Math.min(1,BILL_WIDTH/width);return {width:Math.max(1,Math.trunc(width*sc)),height:Math.max(1,Math.trunc(height*sc))};
}
/** Why a picked file is refused before reading it ('' = accepted). */
export function billPickRefusal(file:{size:number;type?:string}):string{
  if(file.type&&!/^image\//.test(file.type))return 'Choose a photo of the bill.';
  return file.size>BILL_MAX_INPUT_BYTES?'Photo too large (max 4MB)':'';
}
/** Bill photo → JPEG data URL. LF-IMAGES (G1): the SCC one-pass 1,000 px / 0.7 became the shared PHOTO_DOC search (aims at 150 KB); SCC's 4 MB input refusal stays. */
export async function compressBillPhoto(file:Blob):Promise<{dataUrl:string;bytes:number;width:number;height:number}>{
  const why=billPickRefusal(file);if(why)throw new Error(why);
  return compressPhoto(file,PHOTO_DOC);
}
/** ID image, guest photo and evidence image: PHOTO_DOC before the upload (G1). */
export const compressDocPhoto=(file:Blob)=>compressPhoto(file,PHOTO_DOC);
