import {build} from 'esbuild';
import {readFile,readdir,mkdir,writeFile} from 'node:fs/promises';
import {moHeadTags} from './src/mobile/ops/manifest.mjs'; // MB-10 manifest + icons (inline data URIs)
// Build flavour (compile-time constant process.env.HCC_BUILD): "prototype" (default; mock adapter, demo controls) or
// "production" (--production: HTTP adapter; demo toolbar, scenario switcher, simulated clock and Reset are compiled out).
const production=process.argv.includes('--production');
const result=await build({entryPoints:['src/main.tsx'],bundle:true,write:false,format:'iife',target:['es2020'],minify:true,define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':production?'"production"':'"prototype"'}});
const extra=(await readdir('src/styles')).filter(f=>f.endsWith('.css')).sort();
const css=[await readFile('src/styles.css','utf8'),...await Promise.all(extra.map(f=>readFile('src/styles/'+f,'utf8')))].join('\n');
const js=result.outputFiles[0].text.replace(/<\/script/gi,'<\\/script');
const html=`<!doctype html><html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1.0,viewport-fit=cover,interactive-widget=resizes-content"><meta name="theme-color" content="#7a1f2b">${moHeadTags()}<title>${production?'Hotel Control Centre':'Hotel Control Centre · Interactive Prototype'}</title><style>${css}</style></head><body><div id="root"></div><script>${js}</script></body></html>`;
if(production){await mkdir('dist/production',{recursive:true});await writeFile('dist/production/index.html',html);console.log(`Built dist/production/index.html (${Math.round(html.length/1024)} KB) — production flavour (HTTP adapter, no demo controls).`);process.exit(0);}
await mkdir('dist',{recursive:true});await writeFile('dist/index.html',html);await writeFile('Hotel Control Centre.html',html);
console.log(`Built dist/index.html (${Math.round(html.length/1024)} KB). Serve with: npm start   (tablets on Wi-Fi: npm start -- --lan)`);
