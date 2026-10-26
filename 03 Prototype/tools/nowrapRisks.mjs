// CP-2 · browser-free layout risk list for the text-size transform. Text grows with --hcc-text (up to 1.3) while widths,
// heights and paddings stay in px, so a rule that pins the box (px width / max-width / flex-basis, or px height with
// overflow hidden) AND forbids wrapping (white-space:nowrap) AND sets a px font size can clip or overflow at 130 %.
// Usage: node tools/nowrapRisks.mjs        (prints the list; exit 0)
import {readStyleSources} from './cssTextScale.mjs';
import {isPrintMedia} from './cssTextScale.mjs';
import {pathToFileURL} from 'node:url';

/** Flat list of {selector, body, media} for every style rule outside @media print (comments stripped, nested at-rules flattened). */
export function styleRules(css){
  const text=css.replace(/\/\*[\s\S]*?\*\//g,'');
  const rules=[];const stack=[];let prelude='',i=0,body='',bodyOpen=false;
  while(i<text.length){
    const c=text[i];
    if(c==='{'){
      const p=prelude.trim();
      if(p.startsWith('@')&&!/^@(font-face|page|keyframes|-webkit-keyframes)/.test(p)){stack.push({at:p});prelude='';}
      else{stack.push({rule:p});bodyOpen=true;body='';prelude='';}
    }else if(c==='}'){
      const top=stack.pop();
      if(top&&top.rule!==undefined){
        const media=stack.map(s=>s.at).filter(Boolean);
        if(!media.some(isPrintMedia))rules.push({selector:top.rule,body,media:media.join(' ')});
        bodyOpen=false;body='';
      }
      prelude='';
    }else if(bodyOpen)body+=c;
    else prelude+=c;
    i++;
  }
  return rules;
}

const decl=(body,prop)=>{const m=new RegExp('(?:^|;)\\s*'+prop+'\\s*:\\s*([^;]+)','i').exec(body);return m?m[1].trim():null;};
const px=v=>v&&/^[\d.]+px\b/.test(v);

/** Rules that pin their box in px, forbid wrapping and carry a px font size. */
export function nowrapRisks(css){
  const out=[];
  for(const r of styleRules(css)){
    const ws=decl(r.body,'white-space');
    if(!ws||!/nowrap/.test(ws))continue;
    const font=decl(r.body,'font-size')||(/(?:^|;)\s*font\s*:[^;]*px/.test(r.body)?'font shorthand':null);
    const widths=['width','max-width','flex-basis'].map(p=>[p,decl(r.body,p)]).filter(([,v])=>px(v));
    const clipped=px(decl(r.body,'height'))&&/hidden|clip/.test(decl(r.body,'overflow')||'');
    const ellipsis=/ellipsis/.test(decl(r.body,'text-overflow')||'');
    if(!font&&!widths.length)continue;
    if(!widths.length&&!clipped)continue;
    out.push({selector:r.selector,media:r.media,font,widths:widths.map(([p,v])=>p+':'+v).join(' '),clipped:!!clipped,ellipsis});
  }
  return out;
}

/** Rules with a fixed px height (not min-height) and a px font size: the text box cannot grow (avatars, badges, steppers). */
export function fixedHeightText(css){
  return styleRules(css).filter(r=>px(decl(r.body,'height'))&&/px/.test(decl(r.body,'font-size')||'')).map(r=>({selector:r.selector,height:decl(r.body,'height'),font:decl(r.body,'font-size')}));
}
/** Every other nowrap rule: text that cannot wrap grows sideways inside a parent that may not (list only). */
export function otherNowrap(css){
  const fixed=new Set(nowrapRisks(css).map(r=>r.selector));
  return styleRules(css).filter(r=>/nowrap/.test(decl(r.body,'white-space')||'')&&!fixed.has(r.selector)).map(r=>r.selector);
}

if(import.meta.url===pathToFileURL(process.argv[1]||'').href){
  const css=(await readStyleSources('.')).join('\n');
  const risks=nowrapRisks(css);
  console.log(`A. ${risks.length} fixed-box nowrap rules (px width/max-width, or px height + overflow hidden):`);
  for(const r of risks)console.log(`- ${r.selector.slice(0,110)}  [${r.widths||'no px width'}${r.clipped?' + px height & overflow hidden':''}; font ${r.font||'-'}${r.ellipsis?'; ellipsis (degrades gracefully)':''}${r.media?'; in '+r.media:''}]`);
  const fh=fixedHeightText(css);console.log(`B. ${fh.length} fixed px height + px font (text box cannot grow):`);
  for(const r of fh)console.log(`- ${r.selector.slice(0,90)}  [height ${r.height}; font ${r.font}]`);
  const on=otherNowrap(css);console.log(`C. ${on.length} other nowrap rules (grow sideways; check inside narrow flex/grid parents at 360 px):`);
  for(const sel of on)console.log(`- ${sel.slice(0,110)}`);
}
