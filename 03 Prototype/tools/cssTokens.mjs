// CP-3 · colour-token migration helpers. Pure functions, no DOM.
//   tokenizeColours(css) → {css, tokens, left}   every hex colour literal in a declaration value becomes var(--c-<key>) (or a semantic
//                                                variable when the value equals one of styles.css :root's semantic colours).
//   resolveTokens(css, tokens) → css             the inverse: every var(--c-…) / semantic var(--…) found in `tokens` (or SEMANTIC) is
//                                                replaced by its hex value again.
// Skipped on purpose: comments, strings, url(...), custom-property definitions (`--x:#…`), selectors (`#id`), and any rgb()/rgba()/hsl()/hsla()
// literal or named colour (white, black, transparent, currentColor, inherit), which stay as written (counted in `left`).
// <key> = lowercase hex digits; 3-digit and 4-digit shorthands are expanded (#abc → aabbcc); 8-digit hex keeps its alpha digits.

/** styles.css :root semantic colours (hex digits → variable name). */
export const SEMANTIC_VALUES=new Map([
  ['7a1f2b','--primary'],['5e1620','--primary-dark'],['f5e4e6','--primary-tint'],['2b1d16','--ink'],['6b5547','--muted'],
  ['e6d9c8','--line'],['faf6f0','--bg'],['f6efe6','--sand-2'],['b5541e','--orange']]);
/** name → #hex for the semantic variables above. */
export const SEMANTIC=new Map([...SEMANTIC_VALUES].map(([hex,name])=>[name,'#'+hex]));

const HEX=/#([0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{4}|[0-9a-fA-F]{3})(?![0-9a-zA-Z_-])/y;
const FUNC_LEFT=/^(rgba?|hsla?)\(/i;
const NAMED_LEFT=/^(white|black|transparent|currentcolor|inherit)(?![\w-])/i;
const byName=(a,b)=>a[0]<b[0]?-1:a[0]>b[0]?1:0;

/** Lowercase key for a hex literal body: 3/4-digit shorthand expanded. */
export function hexKey(body){
  let h=body.toLowerCase();
  if(h.length===3||h.length===4)h=[...h].map(c=>c+c).join('');
  return h;
}

/** Name of the variable that stands for #<key>. */
export function tokenName(key){return SEMANTIC_VALUES.get(key)||`--c-${key}`;}

/** Index just past the quoted string whose opening quote is at css[i]. */
function endString(css,i){const q=css[i];let j=i+1;while(j<css.length&&css[j]!==q){if(css[j]==='\\')j++;j++;}return Math.min(j+1,css.length);}
/** Index just past the `)` that closes the url( at i. */
function endUrl(css,i){let j=css.indexOf('(',i)+1;while(j<css.length){const c=css[j];if(c==='"'||c==="'"){j=endString(css,j);continue;}if(c===')')return j+1;j++;}return css.length;}
const isUrlAt=(css,i)=>(css[i]==='u'||css[i]==='U')&&/^url\(/i.test(css.slice(i,i+4))&&!/[\w-]/.test(css[i-1]||'');

/**
 * Walks `css`, replacing every hex colour literal in a declaration value with onHex(body); everything else is copied verbatim.
 * `left` (optional) is incremented for rgb()/hsl() literals and named colours that stay as written.
 */
function walk(css,onHex,left){
  let out='',i=0;const n=css.length;let inCustom=false,inDecl=false;
  while(i<n){
    const c=css[i];
    if(c==='/'&&css[i+1]==='*'){const e=css.indexOf('*/',i+2);const end=e<0?n:e+2;out+=css.slice(i,end);i=end;continue;}
    if(c==='"'||c==="'"){const end=endString(css,i);out+=css.slice(i,end);i=end;continue;}
    if(isUrlAt(css,i)){const end=endUrl(css,i);out+=css.slice(i,end);i=end;continue;}
    if(c==='{'||c==='}'||c===';'){inCustom=false;inDecl=false;out+=c;i++;continue;}
    if(c===':'&&!inDecl){
      // the identifier just before the colon is the property name; custom properties (--x) are definitions and never rewritten
      const m=/(--[\w-]*|[\w-]+)\s*$/.exec(out);
      inCustom=!!(m&&m[1].startsWith('--'));
      inDecl=true;out+=c;i++;continue;}
    if(c==='#'&&inDecl&&!inCustom){
      HEX.lastIndex=i;const m=HEX.exec(css);
      if(m){
        // a `{` before the next `;`/`}` means we are inside a selector (e.g. `a:hover #abc{…}`), not a value
        const rest=css.slice(i+m[0].length);const stop=rest.search(/[{;}]/);
        if(!(stop>=0&&rest[stop]==='{')){out+=onHex(m[1]);i+=m[0].length;continue;}
      }
    }
    if(inDecl&&!inCustom&&left&&!/[\w-]/.test(css[i-1]||'')){
      const tail=css.slice(i,i+14);
      if(FUNC_LEFT.test(tail))left.fn++;else if(NAMED_LEFT.test(tail))left.named++;
    }
    out+=c;i++;
  }
  return out;
}

/** → {css, tokens: Map<name, #hex>, left:{fn,named}}. `tokens` holds every variable that was introduced (semantic ones included). */
export function tokenizeColours(css){
  const tokens=new Map();const left={fn:0,named:0};
  const out=walk(css,body=>{const key=hexKey(body);const name=tokenName(key);tokens.set(name,'#'+key);return `var(${name})`;},left);
  return {css:out,tokens:new Map([...tokens].sort(byName)),left};
}

/** Inverse of tokenizeColours: var(--name) → its value for every name in `tokens` or the semantic set. Unknown variables are untouched. */
export function resolveTokens(css,tokens){
  const all=new Map([...SEMANTIC,...(tokens||[])]);
  let out='',i=0;const n=css.length;
  while(i<n){
    const c=css[i];
    if(c==='/'&&css[i+1]==='*'){const e=css.indexOf('*/',i+2);const end=e<0?n:e+2;out+=css.slice(i,end);i=end;continue;}
    if(c==='"'||c==="'"){const end=endString(css,i);out+=css.slice(i,end);i=end;continue;}
    if(isUrlAt(css,i)){const end=endUrl(css,i);out+=css.slice(i,end);i=end;continue;}
    if(c==='v'&&css.startsWith('var(--',i)){
      const m=/^var\((--[\w-]+)\)/.exec(css.slice(i,i+80));
      if(m&&all.has(m[1])){out+=all.get(m[1]);i+=m[0].length;continue;}
    }
    out+=c;i++;
  }
  return out;
}

/** The `:root{…}` token sheet text for a set of tokens (semantic variables live in styles.css and are not repeated). */
export function tokenSheet(label,tokens){
  const lines=[...tokens].filter(([k])=>k.startsWith('--c-')).sort(byName).map(([k,v])=>`${k}:${v};`);
  return `/* ${label} colour tokens — light theme values; the lead merges 0-tokens-{a,b,c}.css into styles.css */\n:root{\n${lines.join('\n')}\n}\n`;
}
