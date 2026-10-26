// CP-3 · colour-token transform. Replaces every hex colour literal in a stylesheet with a CSS variable so that dark and
// high-contrast themes can later be added as [data-theme] blocks. Pure functions, no DOM, used by the CP-3 migration scripts
// and by tests/stream-css-tokens-{a,b,c}.mjs.
//
// tokenizeColours(css) -> {css, tokens}
//   * Only hex literals (#abc, #abcd, #aabbcc, #aabbccdd) in declaration VALUES are rewritten. Anything inside comments, strings
//     and url(...) is skipped, selectors (#id) are never touched, and custom-property DEFINITIONS inside a :root block are skipped.
//   * A literal becomes var(--c-<key>); <key> is the lowercase 6-digit hex (3-digit #abc expands to aabbcc); 4- and 8-digit hex
//     expand to 8 digits and keep the alpha (#5e162050 -> --c-5e162050, #fffa -> --c-ffffffaa).
//   * A 6-digit hex equal to one of the semantic :root values of styles.css (SEMANTIC below) uses that variable instead.
//   * rgb()/rgba()/hsl()/hsla() literals and named colours (white, black, transparent, currentColor, inherit ...) stay as they are.
//   * Deterministic: same input -> same output; idempotent (an already tokenised file is returned unchanged).
// resolveTokens(css, tokens) -> css : the inverse; every var(--name) found in `tokens` becomes its value again.
// canonHex(css) : lowercases hex literals and expands #abc/#abcd, so original and resolved CSS compare equal.

/** Semantic variables of the styles.css :root block (hex -> variable). */
export const SEMANTIC=new Map([
  ['7a1f2b','--primary'],['5e1620','--primary-dark'],['f5e4e6','--primary-tint'],['2b1d16','--ink'],['6b5547','--muted'],
  ['e6d9c8','--line'],['faf6f0','--bg'],['f6efe6','--sand-2'],['b5541e','--orange'],
]);
/** The same mapping as variable -> '#hex', for resolveTokens. */
export const SEMANTIC_TOKENS=new Map([...SEMANTIC].map(([hex,name])=>[name,'#'+hex]));

export const TOKEN_PREFIX='--c-';
const GROUP_AT=/^@(media|supports|layer|container|document|keyframes|-webkit-keyframes)\b/i;
const HEX=/^#([0-9a-fA-F]{3,8})(?![\w-])/;

/** Normalise a hex digit string to its token key: 3 -> 6 digits, 4 -> 8 digits, 6/8 unchanged, lowercase. null if not a colour length. */
export function hexKey(d){
  d=d.toLowerCase();
  if(d.length===3||d.length===4)d=[...d].map(c=>c+c).join('');
  return d.length===6||d.length===8?d:null;
}
/** Variable name for a hex digit string (semantic variable when the 6-digit value is a styles.css :root colour). */
export function varName(d){
  const k=hexKey(d);
  if(!k)return null;
  return k.length===6&&SEMANTIC.has(k)?SEMANTIC.get(k):TOKEN_PREFIX+k;
}

export function tokenizeColours(css){
  const tokens=new Map();
  let out='',i=0,segStart=0,inValue=false,skipValue=false,paren=0;
  const stack=[]; // 'group' | 'decl' | 'root'
  const n=css.length;
  const top=()=>stack.length?stack[stack.length-1]:'group';
  while(i<n){
    const c=css[i];
    if(c==='/'&&css[i+1]==='*'){let e=css.indexOf('*/',i+2);e=e<0?n:e+2;out+=css.slice(i,e);i=e;continue;}
    if(c==='"'||c==="'"){let j=i+1;while(j<n&&css[j]!==c){if(css[j]==='\\')j++;j++;}j=Math.min(j+1,n);out+=css.slice(i,j);i=j;continue;}
    if((c==='u'||c==='U')&&/^url\(/i.test(css.slice(i,i+4))&&(i===0||!/[\w-]/.test(css[i-1]))){
      let j=i+4;
      while(j<n&&css[j]!==')'){if(css[j]==='"'||css[j]==="'"){const q=css[j];j++;while(j<n&&css[j]!==q){if(css[j]==='\\')j++;j++;}}j++;}
      j=Math.min(j+1,n);out+=css.slice(i,j);i=j;continue;
    }
    if(c==='('){paren++;}
    else if(c===')'){if(paren>0)paren--;}
    else if(c==='{'){
      const prelude=css.slice(segStart,i).replace(/\/\*[\s\S]*?\*\//g,'').trim();
      stack.push(GROUP_AT.test(prelude)?'group':(/^:root$/i.test(prelude)?'root':'decl'));
      segStart=i+1;inValue=false;skipValue=false;paren=0;
    }else if(c==='}'){stack.pop();segStart=i+1;inValue=false;skipValue=false;paren=0;}
    else if(c===';'&&paren===0){segStart=i+1;inValue=false;skipValue=false;}
    else if(c===':'&&!inValue&&paren===0&&(top()==='decl'||top()==='root')){
      inValue=true;skipValue=top()==='root'&&css.slice(segStart,i).trim().startsWith('--');
    }else if(c==='#'&&inValue&&!skipValue&&(i===0||!/[\w&-]/.test(css[i-1]))){
      const m=HEX.exec(css.slice(i,i+10));
      if(m){
        const key=hexKey(m[1]);
        if(key){
          const name=varName(m[1]);
          if(!SEMANTIC_TOKENS.has(name))tokens.set(name,'#'+key);
          out+='var('+name+')';i+=m[0].length;continue;
        }
      }
    }
    out+=c;i++;
  }
  return {css:out,tokens};
}

export function resolveTokens(css,tokens){
  const all=tokens instanceof Map?tokens:new Map(Object.entries(tokens));
  return css.replace(/var\((--[\w-]+)\)/g,(m,name)=>{
    if(all.has(name))return all.get(name);
    if(SEMANTIC_TOKENS.has(name))return SEMANTIC_TOKENS.get(name);
    return m;
  });
}

/** Lowercase every hex literal and expand #abc -> #aabbcc, #abcd -> #aabbccdd (selectors untouched is not guaranteed: use on declaration-only comparisons). */
export function canonHex(css){
  return css.replace(/#([0-9a-fA-F]{3,8})(?![\w-])/g,(m,d)=>{const k=hexKey(d);return k?'#'+k:m;});
}

/** Render a token sheet: one :root block, keys sorted, one declaration per line. */
export function tokenSheet(tokens,label){
  const names=[...tokens.keys()].sort();
  return `/* ${label} */\n:root{\n${names.map(k=>`  ${k}:${tokens.get(k)};`).join('\n')}\n}\n`;
}
