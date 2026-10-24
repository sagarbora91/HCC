// Generates the 1024 px app icon (maroon "H" on sand) and splash sources for @capacitor/assets using sharp.
import sharp from 'sharp';
import {mkdirSync} from 'node:fs';
mkdirSync(new URL('../assets/',import.meta.url),{recursive:true});
const H=(size,bg,fg,pad)=>`<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 1024 1024">${bg?`<rect width="1024" height="1024" fill="${bg}"/>`:''}
<g transform="translate(${pad} ${pad}) scale(${(1024-2*pad)/1024})"><rect x="212" y="180" width="130" height="664" rx="22" fill="${fg}"/><rect x="682" y="180" width="130" height="664" rx="22" fill="${fg}"/><rect x="212" y="445" width="600" height="134" rx="22" fill="${fg}"/></g></svg>`;
const out=(n)=>new URL('../assets/'+n,import.meta.url).pathname.replace(/^\/([A-Za-z]:)/,'$1').replaceAll('%20',' ');
await sharp(Buffer.from(H(1024,'#7A1F2B','#FAF6F0',90))).png().toFile(out('icon-only.png'));
await sharp(Buffer.from(H(1024,null,'#FAF6F0',250))).png().toFile(out('icon-foreground.png'));
await sharp(Buffer.from(`<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024"><rect width="1024" height="1024" fill="#7A1F2B"/></svg>`)).png().toFile(out('icon-background.png'));
const splash=(bg)=>`<svg xmlns="http://www.w3.org/2000/svg" width="2732" height="2732" viewBox="0 0 2732 2732"><rect width="2732" height="2732" fill="${bg}"/><g transform="translate(1066 900) scale(0.6)"><rect x="0" y="0" width="1024" height="1024" rx="200" fill="#7A1F2B"/><g transform="translate(90 90) scale(0.824)"><rect x="212" y="180" width="130" height="664" rx="22" fill="#FAF6F0"/><rect x="682" y="180" width="130" height="664" rx="22" fill="#FAF6F0"/><rect x="212" y="445" width="600" height="134" rx="22" fill="#FAF6F0"/></g></g><text x="1366" y="1900" text-anchor="middle" font-family="Arial,Helvetica,sans-serif" font-size="110" font-weight="700" fill="#7A1F2B">Hotel Control Centre</text></svg>`;
await sharp(Buffer.from(splash('#FAF6F0'))).png().toFile(out('splash.png'));
await sharp(Buffer.from(splash('#FAF6F0'))).png().toFile(out('splash-dark.png'));
console.log('assets written');
