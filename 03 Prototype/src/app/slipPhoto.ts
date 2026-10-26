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

export type SlipEncoded={bytes:Uint8Array;width:number;height:number;quality:number};
/** Encodes the source scaled to width × height at a JPEG quality (0–1) and returns the file's bytes. */
export type SlipEncoder=(width:number,height:number,quality:number)=>Promise<Uint8Array>;

/** Target size for a long edge: never upscales; keeps the aspect ratio; at least 1 px each way. */
export function slipFit(width:number,height:number,edge:number):{width:number;height:number}{
  const long=Math.max(width,height);if(long<=edge)return {width:Math.max(1,Math.round(width)),height:Math.max(1,Math.round(height))};
  const k=edge/long;return {width:Math.max(1,Math.round(width*k)),height:Math.max(1,Math.round(height*k))};
}

/** The first (size, quality) whose JPEG is under the limit; throws a user-facing Error when none is. */
export async function slipCompressPlan(width:number,height:number,encode:SlipEncoder,max=SLIP_MAX_BYTES):Promise<SlipEncoded>{
  if(!(width>0&&height>0))throw new Error('The photo could not be read. Take it again.');
  let tried='';
  for(const edge of SLIP_EDGES){
    const size=slipFit(width,height,edge);const key=size.width+'x'+size.height;
    if(key===tried)continue; // a small photo gives the same size at every edge: one pass is enough
    tried=key;
    for(const quality of SLIP_QUALITIES){
      const bytes=await encode(size.width,size.height,quality);
      if(bytes.length>0&&bytes.length<=max)return {bytes,width:size.width,height:size.height,quality};
    }
  }
  throw new Error('The slip photo cannot be made smaller than 100 KB. Take it again, closer to the slip and in good light.');
}

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
 * Compresses a camera photo (from `<input type="file" accept="image/*" capture="environment">`) to a JPEG under 100 KB and returns it as a
 * data URL with its size. Throws an Error with a user-facing message when it cannot.
 */
export async function compressSlipPhoto(file:Blob):Promise<{dataUrl:string;bytes:number;width:number;height:number}>{
  if(!/^image\//.test(file.type||'image/'))throw new Error('Choose a photo of the deposit slip.');
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
    const r=await slipCompressPlan(src.width,src.height,encode);
    return {dataUrl:slipDataUrl(r.bytes),bytes:r.bytes.length,width:r.width,height:r.height};
  }finally{src.img.close?.();canvas.width=0;canvas.height=0;}
}
