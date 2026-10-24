// MB-10 web app manifest + icons, generated at build time and inlined as data URIs (single self-contained HTML).
// Icon: maroon (#7a1f2b) square with a white "H". PNGs are encoded here with node:zlib (no npm packages).
import {deflateSync} from 'node:zlib';
export const MO_THEME='#7a1f2b',MO_BACKGROUND='#faf6f0';
const CRC=new Int32Array(256).map((_,n)=>{let c=n;for(let k=0;k<8;k++)c=c&1?0xedb88320^(c>>>1):c>>>1;return c;});
function crc32(buf){let c=-1;for(const b of buf)c=CRC[(c^b)&255]^(c>>>8);return (c^-1)>>>0;}
function chunk(type,data){const len=Buffer.alloc(4);len.writeUInt32BE(data.length);const td=Buffer.concat([Buffer.from(type,'ascii'),data]);const crc=Buffer.alloc(4);crc.writeUInt32BE(crc32(td));return Buffer.concat([len,td,crc]);}
/** True where the white "H" is drawn (unit coordinates 0..1; kept inside the maskable safe zone). */
export function moInH(x,y){const inY=y>=0.28&&y<0.72;return inY&&((x>=0.30&&x<0.42)||(x>=0.58&&x<0.70)||(x>=0.30&&x<0.70&&y>=0.455&&y<0.545));}
/** RGB PNG of the icon at size×size pixels. */
export function moIconPng(size){
  const row=size*3+1,raw=Buffer.alloc(row*size);
  for(let y=0;y<size;y++){raw[y*row]=0;for(let x=0;x<size;x++){const o=y*row+1+x*3,w=moInH((x+.5)/size,(y+.5)/size);raw[o]=w?255:0x7a;raw[o+1]=w?255:0x1f;raw[o+2]=w?255:0x2b;}}
  const ihdr=Buffer.alloc(13);ihdr.writeUInt32BE(size,0);ihdr.writeUInt32BE(size,4);ihdr[8]=8;ihdr[9]=2;ihdr[10]=0;ihdr[11]=0;ihdr[12]=0;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',ihdr),chunk('IDAT',deflateSync(raw,{level:9})),chunk('IEND',Buffer.alloc(0))]);
}
export const moIconSvg=`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect width="100" height="100" fill="${MO_THEME}"/><path fill="#fff" d="M30 28h12v17.5h16V28h12v44H58V54.5H42V72H30z"/></svg>`;
const png=size=>'data:image/png;base64,'+moIconPng(size).toString('base64');
/** Manifest object. start_url/scope are omitted on purpose: with a data-URI manifest they default to the page URL. */
export function moManifest(){
  return {name:'Hotel Control Centre',short_name:'HCC',description:'Hotel operations prototype — fictional demo data',display:'standalone',orientation:'any',
    theme_color:MO_THEME,background_color:MO_BACKGROUND,
    icons:[{src:png(192),sizes:'192x192',type:'image/png',purpose:'any maskable'},{src:png(512),sizes:'512x512',type:'image/png',purpose:'any maskable'},
      {src:'data:image/svg+xml,'+encodeURIComponent(moIconSvg),sizes:'any',type:'image/svg+xml'}]};
}
/** <head> tags: manifest, favicon, apple-touch-icon and standalone hints. */
export function moHeadTags(){
  const manifest='data:application/manifest+json,'+encodeURIComponent(JSON.stringify(moManifest()));
  return `<link rel="manifest" href="${manifest}"><link rel="icon" type="image/svg+xml" href="data:image/svg+xml,${encodeURIComponent(moIconSvg)}"><link rel="apple-touch-icon" sizes="180x180" href="${png(180)}"><meta name="mobile-web-app-capable" content="yes"><meta name="apple-mobile-web-app-capable" content="yes"><meta name="apple-mobile-web-app-title" content="HCC"><meta name="application-name" content="HCC">`;
}
