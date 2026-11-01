// CP-2 · text-size build transform. Every px font size in the stylesheet bundle becomes calc(<N>px * var(--hcc-text,1)),
// so the personal "Text size" preference (prefs.ts sets --hcc-text on <html>) reaches the ~1,200 px rules that a root
// font-size could never move. Pure functions, no DOM, used by build.mjs and by tests/stream-text-scale.mjs.
//
// Rules: only px font sizes are rewritten (font-size:<N>px, font-size: <N>px, <N>.<M>px, with or without !important, and the
// size token inside a `font:` shorthand). rem/em/%/pt/keywords, line-height, min-height, paddings, widths are never touched.
// Comments, strings and @media print blocks (including any @media query list that names print) are skipped, and the bundle
// ends with a print reset so non-print rules that also apply on paper print at 100 %.
import {readFile,readdir} from 'node:fs/promises';

export const TEXT_VAR='--hcc-text';
/** Appended once to the bundle: paper always prints at 100 %, whatever the on-screen preference (important beats the inline custom property). */
export const PRINT_RESET='@media print{:root{--hcc-text:1!important}}';

// A plain px number: 13, 12.5, .5
const NUM=/(\d*\.?\d+)/.source;
// font-size: [ws] N px   (the value must be a plain px length; calc()/max()/var() values never match, which also makes the transform idempotent)
const FONT_SIZE=new RegExp('^font-size(\\s*:\\s*)'+NUM+'px(?![\\w%.-])');
// font: [style/weight tokens] N px   (shorthand; only the size token is rewritten, line-height and family are kept)
const FONT_SHORTHAND=new RegExp('^font(\\s*:\\s*)((?:(?:italic|oblique|normal|bold|bolder|lighter|small-caps|\\d{3})\\s+)*)'+NUM+'px(?=[\\s/;}!]|$)');

const scaled=n=>`calc(${n}px * var(${TEXT_VAR},1))`;

/** True for an @media prelude that applies to paper: any comma-separated query naming `print` that is not `not print`. */
export function isPrintMedia(prelude){
  if(!/^@media\b/i.test(prelude))return false;
  return prelude.slice(6).split(',').some(q=>/\bprint\b/i.test(q)&&!/^\s*not\b/i.test(q));
}

/**
 * Rewrites px font sizes to calc(... * var(--hcc-text,1)). Print blocks, comments and strings are copied verbatim.
 * `found` (optional array) collects every plain size that was rewritten; run it on finished CSS to list leftovers.
 */
export function scaleFontSizes(css,found){
  let out='',i=0,prelude='',printDepth=0;
  const stack=[];                       // one entry per open block: true when that block is a print @media
  const n=css.length;
  while(i<n){
    const c=css[i];
    if(c==='/'&&css[i+1]==='*'){const e=css.indexOf('*/',i+2);const end=e<0?n:e+2;out+=css.slice(i,end);prelude+=css.slice(i,end);i=end;continue;}
    if(c==='"'||c==="'"){let j=i+1;while(j<n&&css[j]!==c){if(css[j]==='\\')j++;j++;}j=Math.min(j+1,n);out+=css.slice(i,j);prelude+=css.slice(i,j);i=j;continue;}
    if(c==='{'){const print=isPrintMedia(prelude.trim());stack.push(print);if(print)printDepth++;out+=c;prelude='';i++;continue;}
    if(c==='}'){if(stack.pop())printDepth--;out+=c;prelude='';i++;continue;}
    if(c===';'){out+=c;prelude='';i++;continue;}
    if(printDepth===0&&c==='f'&&(i===0||!/[-\w]/.test(css[i-1]))){
      const rest=css.slice(i,i+80);
      let m=FONT_SIZE.exec(rest);
      if(m){found?.push(m[0]);const t=`font-size${m[1]}${scaled(m[2])}`;out+=t;prelude+=t;i+=m[0].length;continue;}
      m=FONT_SHORTHAND.exec(rest);
      if(m){found?.push(m[0]);const t=`font${m[1]}${m[2]}${scaled(m[3])}`;out+=t;prelude+=t;i+=m[0].length;continue;}
    }
    out+=c;prelude+=c;i++;
  }
  return out;
}

/** scaleFontSizes + the print reset (added once). This is what the bundle uses. */
export function textScaleCss(css){
  const t=scaleFontSizes(css);
  return t.includes(PRINT_RESET)?t:`${t}\n${PRINT_RESET}`;
}

/** FIT (contract §3): z-fit.css loads after z-touch.css and before zz-print.css; every other file keeps its name order. */
export const styleOrder=files=>{const rest=files.filter(f=>f!=='z-fit.css').sort();if(!files.includes('z-fit.css'))return rest;
  const i=rest.findIndex(f=>f>'z-touch.css');rest.splice(i<0?rest.length:i,0,'z-fit.css');return rest;};
/** The stylesheet sources in bundle order: src/styles.css, then src/styles/*.css sorted by name (z-fit.css after z-touch.css, zz-print.css last). */
export async function readStyleSources(root='.'){
  const dir=`${root}/src/styles`;
  const extra=styleOrder((await readdir(dir)).filter(f=>f.endsWith('.css')));
  return [await readFile(`${root}/src/styles.css`,'utf8'),...await Promise.all(extra.map(f=>readFile(`${dir}/${f}`,'utf8')))];
}

/** The final bundle CSS: sources concatenated (as build.mjs always did), then transformed. */
export async function buildBundleCss(root='.'){return textScaleCss((await readStyleSources(root)).join('\n'));}

/** Plain px font sizes that survive outside print blocks/comments/strings (the dist/index.html guard). Empty = fully transformed. */
export function untransformedFontSizes(css){const found=[];scaleFontSizes(css,found);return found;}
