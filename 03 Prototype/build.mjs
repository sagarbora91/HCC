import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {prodTextPlugin} from './build-prod-text.mjs'; // production-only wording clean-up (N7)
import {resolve} from 'node:path';
import {buildBundleCss} from './tools/cssTextScale.mjs'; // CP-2: every px font-size → calc(<N>px * var(--hcc-text,1)); print untouched
import {moHeadTags} from './src/mobile/ops/manifest.mjs'; // MB-10 manifest + icons (inline data URIs)
import {addCspMeta} from './tools/csp.mjs'; // A10-05: Content-Security-Policy <meta> (inline script + style by SHA-256) for the production build
// Build flavour (compile-time constant process.env.HCC_BUILD): "prototype" (default; mock adapter, demo controls) or
// "production" (--production: HTTP adapter; demo toolbar, scenario switcher, simulated clock and Reset are compiled out).
const production=process.argv.includes('--production');
const flavour={'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':production?'"production"':'"prototype"'};
// Scenario worker (audit P-01): bundled on its own and inlined into the app as a string, started from a Blob URL at runtime.
const worker=await build({entryPoints:['src/app/scenarioWorker.ts'],bundle:true,write:false,format:'iife',target:['es2020'],minify:true,define:flavour});
const result=await build({entryPoints:['src/main.tsx'],bundle:true,write:false,format:'iife',target:['es2020'],minify:true,plugins:production?[prodTextPlugin(resolve('src'))]:[],define:{...flavour,__HCC_SCENARIO_WORKER__:JSON.stringify(worker.outputFiles[0].text)}});
const css=await buildBundleCss('.'); // src/styles.css + every src/styles/*.css (readdir, sorted; FIT: z-fit.css right after z-touch.css, zz-print.css last — tools/cssTextScale.mjs styleOrder) concatenated, then the text-size transform (tools/cssTextScale.mjs)
const js=result.outputFiles[0].text.replace(/<\/script/gi,'<\\/script');
// A10-05: the browser hashes an inline block after turning CR LF / CR into LF, so the production CSS (CRLF source files) is normalised first.
const style=production?css.replace(/\r\n?/g,'\n'):css;
const page=`<!doctype html><html lang="en"><head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1.0,viewport-fit=cover,interactive-widget=resizes-content"><meta name="theme-color" content="#7a1f2b">${moHeadTags()}<title>${production?'Hotel Control Centre':'Hotel Control Centre · Interactive Prototype'}</title><style>${style}</style></head><body><div id="root"></div><script>${js}</script></body></html>`;
// A10-05: the production page carries its own CSP (the APK serves it with no header); the demo build stays without one.
const html=production?addCspMeta(page):page;
if(production){await mkdir('dist/production',{recursive:true});await writeFile('dist/production/index.html',html);console.log(`Built dist/production/index.html (${Math.round(html.length/1024)} KB) — production flavour (HTTP adapter, no demo controls, CSP meta).`);process.exit(0);}
await mkdir('dist',{recursive:true});await writeFile('dist/index.html',html);await writeFile('Hotel Control Centre.html',html);
console.log(`Built dist/index.html (${Math.round(html.length/1024)} KB). Serve with: npm start   (tablets on Wi-Fi: npm start -- --lan)`);
