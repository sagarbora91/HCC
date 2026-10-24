// Copies the built single-file prototype into www/ for Capacitor. Refuses to copy a build that is older than the sources.
import {mkdirSync,copyFileSync,statSync,readdirSync,existsSync} from 'node:fs';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {applyPrinterPatch} from './patch-printer.mjs';
const here=dirname(fileURLToPath(import.meta.url));
const proto=join(here,'..','..','03 Prototype');
const built=join(proto,'dist','index.html');
if(!existsSync(built)){console.error('Missing '+built+'. Run "npm run build" in 03 Prototype first.');process.exit(1);}
const newest=(dir)=>readdirSync(dir,{withFileTypes:true}).reduce((m,e)=>{const p=join(dir,e.name);return Math.max(m,e.isDirectory()?newest(p):statSync(p).mtimeMs);},0);
const srcTime=Math.max(newest(join(proto,'src')),statSync(join(proto,'build.mjs')).mtimeMs);
if(statSync(built).mtimeMs<srcTime){console.error('dist/index.html is older than the sources in 03 Prototype\\src. Run "npm run build" first.');process.exit(1);}
applyPrinterPatch();
mkdirSync(join(here,'..','www'),{recursive:true});
copyFileSync(built,join(here,'..','www','index.html'));
console.log('Copied dist/index.html ('+Math.round(statSync(built).size/1024)+' KB) to www/index.html');
