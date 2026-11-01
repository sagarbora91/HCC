// ACC-3 lane acc3-exports · plain "store" zip writer and reader — no package, no compression (contract §5: the bulk per-party export).
// Writing reuses the one preallocated-buffer writer of xlsx.ts (zipStored, CRC-32); this file adds text/byte entries, safe unique names,
// and a reader so a test (and the app) can open what it wrote. Entry names are UTF-8 (flag bit 11); modified time is fixed (1980-01-01), so
// the same inputs always give the same bytes.
import {crc32,zipStored} from './xlsx';

export const ZIP_MIME='application/zip';
export type ZipEntry={name:string;data:string|Uint8Array};
/** A name safe inside a zip and on Windows / Android: letters, digits, dot, dash, underscore, space; ≤ 120 chars; never empty. */
export const zipSafeName=(n:string)=>{const t=String(n??'').replace(/[\/:*?"<>|\u0000-\u001F]/g,'-').replace(/\s+/g,' ').trim().replace(/^\.+/,'').slice(0,120).trim();return t||'file';};
/** Writes the entries as a stored zip. Names are sanitised and made unique (`a.csv`, `a (2).csv`, …) case-insensitively. */
export function zipFiles(entries:ZipEntry[]):Uint8Array{
  const enc=new TextEncoder(),used:string[]=[];
  return zipStored(entries.map(e=>{
    let name=zipSafeName(e.name);const dot=name.lastIndexOf('.'),stem=dot>0?name.slice(0,dot):name,ext=dot>0?name.slice(dot):'';
    let n=name,i=2;while(used.indexOf(n.toLowerCase())>=0)n=`${stem} (${i++})${ext}`;
    used.push(n.toLowerCase());
    return {name:n,data:typeof e.data==='string'?enc.encode(e.data):e.data};
  }));
}
const rd16=(b:Uint8Array,p:number)=>b[p]|(b[p+1]<<8),rd32=(b:Uint8Array,p:number)=>(b[p]|(b[p+1]<<8)|(b[p+2]<<16)|(b[p+3]<<24))>>>0;
/** Reads a stored zip (method 0) through its central directory; throws on anything else or a CRC mismatch. */
export function unzipStored(bytes:Uint8Array):{name:string;data:Uint8Array}[]{
  let e=bytes.length-22;while(e>=0&&!(bytes[e]===0x50&&bytes[e+1]===0x4b&&bytes[e+2]===5&&bytes[e+3]===6))e--;
  if(e<0)throw new Error('Not a zip file.');
  const count=rd16(bytes,e+10);let p=rd32(bytes,e+16);const dec=new TextDecoder(),out:{name:string;data:Uint8Array}[]=[];
  for(let i=0;i<count;i++){
    if(rd32(bytes,p)!==0x02014b50)throw new Error('Broken zip directory.');
    const method=rd16(bytes,p+10),crc=rd32(bytes,p+16),size=rd32(bytes,p+24),nl=rd16(bytes,p+28),xl=rd16(bytes,p+30),cl=rd16(bytes,p+32),off=rd32(bytes,p+42);
    const name=dec.decode(bytes.subarray(p+46,p+46+nl));
    if(method!==0)throw new Error('Only stored zip entries are supported.');
    const ds=off+30+rd16(bytes,off+26)+rd16(bytes,off+28),data=bytes.slice(ds,ds+size);
    if(crc32(data)!==crc)throw new Error('Zip entry failed its CRC check: '+name);
    out.push({name,data});p+=46+nl+xl+cl;
  }
  return out;
}
