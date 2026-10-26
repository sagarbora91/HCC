// CP-5 · Themes: Light (Maroon & Sand) · Dark · High contrast, on the CP-3 colour tokens. Pure functions, no DOM.
//
// How the layers fit (src/styles/themes.css is GENERATED from this file: `node tools/themes.mjs --write`; a test fails when it is stale):
//   1. src/styles/0-tokens.css   every --c-<hex> with its light value (CP-3, one sheet).
//   2. ROLES (below)             semantic roles with a light, dark and high-contrast value: surfaces, text, lines, primary family,
//                                status families (bg / border / text, plus the solid "fill" used behind white text), room-status
//                                tints, dots and bars, focus ring. Light value of a role == the hex it replaces, so Light is unchanged.
//   3. ALIASES                   `--c-<hex>: var(--role)` for every hex that belongs to a role, in a theme-independent :root block.
//   4. Generated overrides       every other --c-<hex> gets a dark and a high-contrast value from its usage class (text / surface /
//                                line / fill / alpha) with the hue kept (OKLCH), and text lifted until it reaches AA (dark) / AAA (hc)
//                                against every surface of that theme.
//   5. Role rewrites             applyRoles() in tools/cssTokens.mjs: solid fills behind white text use --*-fill, white text uses
//                                --on-primary (a fill and its text then stay readable in every theme).
// Dark and high-contrast blocks sit inside @media screen, so printing always uses the light palette (printed documents unchanged).

import {readFile} from 'node:fs/promises';
import {readFileSync,writeFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';

export const THEMES=['light','dark','hc'];
export const THEMES_FILE='src/styles/themes.css';

// ── colour maths (sRGB <-> OKLab/OKLCH, WCAG contrast) ──
const clamp=(x,a,b)=>Math.min(b,Math.max(a,x));
export function hexRgb(hex){const h=hex.replace('#','');return [0,2,4].map(i=>parseInt(h.slice(i,i+2),16)/255);}
export function rgbHex(rgb){return '#'+rgb.map(v=>Math.round(clamp(v,0,1)*255).toString(16).padStart(2,'0')).join('');}
const toLin=v=>v<=0.04045?v/12.92:((v+0.055)/1.055)**2.4;
const toGam=v=>v<=0.0031308?12.92*v:1.055*v**(1/2.4)-0.055;
/** WCAG relative luminance of a 6-digit hex (alpha digits ignored). */
export function luminance(hex){const [r,g,b]=hexRgb(hex).map(v=>v<=0.03928?v/12.92:((v+0.055)/1.055)**2.4);return 0.2126*r+0.7152*g+0.0722*b;}
export function contrast(a,b){const x=luminance(a),y=luminance(b);return (Math.max(x,y)+0.05)/(Math.min(x,y)+0.05);}
export function oklch(hex){
  const [r,g,b]=hexRgb(hex).map(toLin);
  const l=Math.cbrt(0.4122214708*r+0.5363325363*g+0.0514459929*b),m=Math.cbrt(0.2119034982*r+0.6806995451*g+0.1073969566*b),s=Math.cbrt(0.0883024619*r+0.2817188376*g+0.6299787005*b);
  const L=0.2104542553*l+0.7936177850*m-0.0040720468*s,A=1.9779984951*l-2.4285922050*m+0.4505937099*s,B=0.0259040371*l+0.7827717662*m-0.8086757660*s;
  return [L,Math.hypot(A,B),Math.atan2(B,A)];
}
function oklabRgb(L,A,B){
  const l=(L+0.3963377774*A+0.2158037573*B)**3,m=(L-0.1055613458*A-0.0638541728*B)**3,s=(L-0.0894841775*A-1.2914855480*B)**3;
  return [4.0767416621*l-3.3077115913*m+0.2309699292*s,-1.2684380046*l+2.6097574011*m-0.3413193965*s,-0.0041960863*l-0.7034186147*m+1.7076147010*s];
}
/** OKLCH -> hex, chroma reduced until the colour fits sRGB (hue and lightness kept). */
export function fromOklch(L,C,h){
  L=clamp(L,0,1);
  for(let c=C;c>=0;c-=0.002){const rgb=oklabRgb(L,c*Math.cos(h),c*Math.sin(h));if(rgb.every(v=>v>=-1e-4&&v<=1+1e-4))return rgbHex(rgb.map(toGam));}
  return rgbHex(oklabRgb(L,0,0).map(toGam));
}

// ── Roles: [name, light, dark, hc, what it is] ── (light value == the hex it stands for; the 9 styles.css :root variables included)
export const ROLES=[
  // surfaces
  ['--bg','#faf6f0','#16110f','#ffffff','page canvas'],
  ['--surface','#ffffff','#201916','#ffffff','cards, inputs, sidebar, sheets'],
  ['--surface-2','#fffcf8','#241c19','#ffffff','raised / alternate rows'],
  ['--sand-2','#f6efe6','#2a211c','#ffffff','sand panels, table heads'],
  ['--sand-3','#faf3e6','#2c231d','#ffffff','sand note panels'],
  ['--sand-4','#efe4d6','#3a2e27','#e6e6e6','sand dividers and wells'],
  // text
  ['--ink','#2b1d16','#f2e9e1','#120a06','body text'],
  ['--muted','#6b5547','#c9b8aa','#33241b','secondary text'],
  ['--ink-2','#4a372c','#e0d2c6','#22160f','strong secondary text'],
  ['--ink-3','#5c4636','#d5c4b6','#2b1d14','labels, status-less captions'],
  // lines
  ['--line','#e6d9c8','#40342c','#5c4c42','hairlines, card borders'],
  ['--line-strong','#cdb9a3','#5e4d41','#2b1d16','control borders'],
  // primary (maroon) family
  ['--primary','#7a1f2b','#f0a3ae','#5e1620','brand maroon: text, borders, focus'],
  ['--primary-dark','#5e1620','#f7c4cb','#4a0f18','headings and strong brand text'],
  ['--primary-tint','#f5e4e6','#3c2026','#ffffff','selected / active background (hc: no tints)'],
  ['--primary-fill','#7a1f2b','#ae4155','#5e1620','solid maroon behind white text (buttons, app bar)'],
  ['--primary-fill-dark','#5e1620','#8a2c3d','#3f0c14','hover / pressed solid maroon'],
  ['--on-primary','#ffffff','#ffffff','#ffffff','text and icons on a solid fill'],
  ['--on-primary-soft','#f5e4e6','#f8dfe3','#ffffff','secondary text on a solid fill'],
  ['--accent','#9e4a55','#b8525f','#6e1f2b','rose accent: tab underline, progress, blocked dot'],
  ['--green','#7a1f2b','#f0a3ae','#5e1620','legacy alias of --primary (styles.css)'],
  ['--orange','#b5541e','#e08a5a','#8a3a0e','orange accent (borders)'],
  // status families: text / bg / border / fill
  ['--success-text','#2e6b34','#93d19c','#1d4d22','success text'],
  ['--success-bg','#eaf4e4','#1c2e1f','#ffffff','success tint'],
  ['--success-border','#bfd9b0','#3d6142','#1d4d22','success border'],
  ['--success-fill','#2e6b34','#2f7a3a','#1d5524','success solid (white text, bars)'],
  ['--danger-text','#9a3b2a','#f4a593','#6e1f12','danger text'],
  ['--danger-bg','#fbe9e4','#3a201b','#ffffff','danger tint'],
  ['--danger-border','#f0cfc6','#6e3a30','#6e1f12','danger border'],
  ['--danger-fill','#9a3b2a','#a8432f','#7a2414','danger solid'],
  ['--warn-text','#8a4b0f','#f0c27a','#5a3200','warning text'],
  ['--warn-bg','#fbf0dc','#35281a','#ffffff','warning tint'],
  ['--warn-border','#efd9ad','#6b5230','#5a3200','warning border'],
  ['--warn-fill','#8a5a10','#8f6012','#5e3a00','warning solid'],
  ['--info-text','#1f5f99','#9cc7ef','#0f3d66','information text'],
  ['--info-bg','#eef4f7','#1b2a35','#ffffff','information tint'],
  ['--info-border','#c9dcea','#3a5568','#0f3d66','information border'],
  ['--info-fill','#4c7084','#4c7084','#1f4a63','information solid'],
  // room status (always shown with its text label — CLAUDE.md theme rule)
  ['--st-ready','#3b7530','#2f7a3a','#1d5524','ready dot / stripe'],
  ['--st-occupied','#93b0c2','#7f9db0','#3f6580','occupied dot'],
  ['--st-occupied-bar','#a6bccb','#7d99ad','#3f6580','occupied stripe'],
  ['--st-occupied-bg','#eff3f5','#1e2a31','#ffffff','occupied tint'],
  ['--st-occupied-border','#dce6ec','#3b4d58','#1f3d52','occupied border'],
  ['--st-occupied-text','#527086','#a9c3d6','#1f3d52','occupied text'],
  ['--st-dirty','#d5b77e','#c4a463','#7a5c18','dirty dot'],
  ['--st-dirty-bar','#d8bc8a','#b89a62','#7a5c18','dirty stripe'],
  ['--st-dirty-bg','#fbf4e6','#322a1b','#ffffff','dirty tint'],
  ['--st-dirty-border','#eee4cc','#5a4a2c','#4d3d12','dirty border'],
  ['--st-dirty-text','#816b41','#dcc48f','#4d3d12','dirty text'],
  ['--st-cleaning','#b4a2ca','#a593c0','#5e4a85','cleaning dot'],
  ['--st-cleaning-bar','#b6a4ca','#9d8bb8','#5e4a85','cleaning stripe'],
  ['--st-cleaning-bg','#f4f0f7','#2a2433','#ffffff','cleaning tint'],
  ['--st-cleaning-border','#e4ddea','#4c4259','#3f2d5c','cleaning border'],
  ['--st-cleaning-text','#786291','#c9b8e3','#3f2d5c','cleaning text'],
  ['--st-inspection','#c7aa81','#b39670','#6e5530','inspection dot'],
  ['--st-inspection-bar','#c7a478','#ad9168','#6e5530','inspection stripe'],
  ['--st-inspection-bg','#f6f0e8','#2f281f','#ffffff','inspection tint'],
  ['--st-inspection-border','#eaddca','#554838','#4a3a22','inspection border'],
  ['--st-inspection-text','#7f6848','#d9c3a1','#4a3a22','inspection text'],
  // focus
  ['--focus-ring','#7a1f2b','#f0a3ae','#000000','keyboard focus outline'],
];
/** Role values that are another role in every theme (no hex of their own). */
export const ROLE_REFS=[['--st-blocked','--accent'],['--chip-bg','--sand-2'],['--chip-text','--ink-2'],['--chip-on-bg','--primary-tint'],['--chip-on-text','--primary']];
/** The 9 variables styles.css :root defines (light values stay literal there). */
export const STYLES_ROOT=['--primary','--primary-dark','--primary-tint','--green','--ink','--muted','--line','--bg','--sand-2','--orange'];
/** Roles applyRoles() introduces into the stylesheets (see tools/cssTokens.mjs). */
export const REWRITE_ROLES=['--primary-fill','--primary-fill-dark','--success-fill','--danger-fill','--warn-fill','--info-fill','--on-primary','--on-primary-soft'];
const roleTable=new Map(ROLES.map(([n,l,d,h,what])=>[n,{light:l,dark:d,hc:h,what}]));
export const roleValue=(name,theme)=>roleTable.get(name)?.[theme];
/** role -> light hex (resolves the role rewrites back to the original colours). */
export const ROLE_LIGHT=new Map(ROLES.map(([n,l])=>[n,l]));
/** Hexes that ARE a role: --c-<hex>: var(--role). Every other --c- token gets generated dark / hc values. */
export const ALIASES=new Map([
  ['ffffff','--surface'],['fffcf8','--surface-2'],['faf3e6','--sand-3'],['efe4d6','--sand-4'],
  ['4a372c','--ink-2'],['5c4636','--ink-3'],['cdb9a3','--line-strong'],['9e4a55','--accent'],
  ['2e6b34','--success-text'],['eaf4e4','--success-bg'],['bfd9b0','--success-border'],
  ['9a3b2a','--danger-text'],['fbe9e4','--danger-bg'],['f0cfc6','--danger-border'],
  ['8a4b0f','--warn-text'],['fbf0dc','--warn-bg'],['efd9ad','--warn-border'],
  ['1f5f99','--info-text'],['eef4f7','--info-bg'],['c9dcea','--info-border'],
  ['3b7530','--st-ready'],
  ['93b0c2','--st-occupied'],['a6bccb','--st-occupied-bar'],['eff3f5','--st-occupied-bg'],['dce6ec','--st-occupied-border'],['527086','--st-occupied-text'],
  ['d5b77e','--st-dirty'],['d8bc8a','--st-dirty-bar'],['fbf4e6','--st-dirty-bg'],['eee4cc','--st-dirty-border'],['816b41','--st-dirty-text'],
  ['b4a2ca','--st-cleaning'],['b6a4ca','--st-cleaning-bar'],['f4f0f7','--st-cleaning-bg'],['e4ddea','--st-cleaning-border'],['786291','--st-cleaning-text'],
  ['c7aa81','--st-inspection'],['c7a478','--st-inspection-bar'],['f6f0e8','--st-inspection-bg'],['eaddca','--st-inspection-border'],['7f6848','--st-inspection-text'],
]);
/** Status bar / <meta theme-color> per theme: the colour of the phone app bar (--primary-fill) with light icons. */
export const THEME_CHROME={light:roleValue('--primary-fill','light'),dark:roleValue('--primary-fill','dark'),hc:roleValue('--primary-fill','hc')};

// ── usage classification ──
const TEXT_PROPS=/^(color|fill|stroke|caret-color|-webkit-text-fill-color|text-decoration-color)$/;
/** --c-token -> Set of properties it is used in (custom-property definitions ignored). */
export function tokenUsage(css){
  const use=new Map();
  const clean=css.replace(/\/\*[\s\S]*?\*\//g,'');
  for(const m of clean.matchAll(/(?:^|[;{\s])([a-z-]+)\s*:([^;{}]*var\(--c-[^;{}]*)/g)){
    for(const v of m[2].matchAll(/var\((--c-[0-9a-f]+)\)/g)){if(!use.has(v[1]))use.set(v[1],new Set());use.get(v[1]).add(m[1]);}
  }
  return use;
}
/** 'alpha' | 'alpha-surface' | 'text' | 'surface' | 'line' | 'fill' */
export function classify(hex,props){
  const h=hex.replace('#','');
  const ps=[...(props||[])];
  // a mostly opaque white used as a background is a translucent surface (it must turn dark with the surfaces)
  if(h.length===8)return h.startsWith('ffffff')&&parseInt(h.slice(6),16)>=0x80&&ps.some(p=>/^background/.test(p))?'alpha-surface':'alpha';
  if(ps.some(p=>TEXT_PROPS.test(p)))return 'text';
  const [L]=oklch('#'+h);
  const bg=ps.some(p=>/^background|box-shadow/.test(p));
  if(L>=0.88&&bg)return 'surface';
  if(L>=0.7&&!bg)return 'line';
  if(L>=0.88)return 'line';
  return 'fill';
}
const surfacesOf=theme=>ROLES.filter(([n])=>/^--(bg|surface|surface-2|sand-[234]|primary-tint|success-bg|danger-bg|warn-bg|info-bg|st-[a-z]+-bg)$/.test(n)).map(r=>roleValue(r[0],theme));
/** Moves `hex` along OKLCH lightness (hue kept) until it has at least `min` contrast with every colour in `against`. */
function reach(hex,against,min,dir){
  let [L,C,h]=oklch(hex);let out=fromOklch(L,C,h);
  for(let i=0;i<120&&against.some(s=>contrast(out,s)<min);i++){L=clamp(L+dir*0.005,0,1);out=fromOklch(L,C,h);}
  return out;
}
/** Generated dark-theme value of a non-role token. */
export function darkValue(hex,cls,surfaces=surfacesOf('dark')){
  const h=hex.replace('#','');
  if(cls==='alpha-surface')return roleValue('--surface','dark')+h.slice(6);
  if(cls==='alpha'){const rgb=h.slice(0,6),a=h.slice(6);return oklch('#'+rgb)[0]<0.5&&rgb!=='5e1620'?'#000000'+a:'#'+h;}
  const [L,C,hu]=oklch('#'+h);
  if(cls==='text')return reach(fromOklch(Math.max(0.78,0.99-0.4*L),C,hu),surfaces,4.6,+1);
  if(cls==='surface')return fromOklch(0.255+(1-L)*0.55,Math.min(C,0.045),hu);
  if(cls==='line')return fromOklch(0.36+(1-L)*0.6,C*0.8,hu);
  return fromOklch(L<0.6?clamp(L,0.42,0.56):L*0.85,C,hu); // fill: dots, bars, solid backgrounds (white text stays readable)
}
/** Generated high-contrast value (light based: white surfaces, near-black hue-kept text, dark lines). */
export function hcValue(hex,cls,surfaces=surfacesOf('hc')){
  const h=hex.replace('#','');
  if(cls==='alpha-surface')return '#ffffff';
  if(cls==='alpha'){const rgb=h.slice(0,6),a=h.slice(6);return oklch('#'+rgb)[0]<0.5&&rgb!=='5e1620'?'#000000'+a:'#'+h;}
  const [L,C,hu]=oklch('#'+h);
  if(cls==='text')return reach(fromOklch(Math.min(L,0.36),C,hu),surfaces,7.2,-1);
  if(cls==='surface')return '#ffffff';
  if(cls==='line')return fromOklch(0.42,Math.min(C,0.04),hu);
  return fromOklch(L<0.6?Math.min(L,0.42):Math.min(L,0.6),C,hu);
}

// ── sheet generation ──
const STYLE_DIR='src/styles';
/** The stylesheets that USE tokens (themes.css itself excluded): src/styles.css + src/styles/*.css. */
export async function usageSources(root='.'){
  const {readdir}=await import('node:fs/promises');
  const files=(await readdir(`${root}/${STYLE_DIR}`)).filter(f=>f.endsWith('.css')&&f!=='themes.css'&&!f.startsWith('0-tokens')).sort();
  return [await readFile(`${root}/src/styles.css`,'utf8'),...await Promise.all(files.map(f=>readFile(`${root}/${STYLE_DIR}/${f}`,'utf8')))].join('\n');
}
/** {dark, hc}: Map --c-token -> value for every token that is not a role alias; plus the class of each. */
export function generatedValues(tokens,usage){
  const dark=new Map(),hc=new Map(),cls=new Map();
  for(const [name,hex] of tokens){
    const key=name.slice(4);if(ALIASES.has(key))continue;
    const c=classify(hex,usage.get(name));cls.set(name,c);
    dark.set(name,darkValue(hex,c));hc.set(name,hcValue(hex,c));
  }
  return {dark,hc,cls};
}
const block=(sel,decls)=>`${sel}{\n${decls.map(d=>d+';').join('\n')}\n}`;
/** Theme-specific rules beyond variables (named colours, hc borders and focus). */
const EXTRA_RULES=`/* named colours in the stylesheets that a dark surface must replace */
[data-theme="dark"] .small-stats>div{background:var(--surface)}
/* High contrast: 2 px borders on controls, a stronger focus ring, no translucent hover tints */
[data-theme="hc"] :is(input,select,textarea,.btn,.pf-seg,.pf-quick,.m-chips button,.mo-seg button,.display-toggle button){border-width:2px}
[data-theme="hc"] :is(button,a,input,select,textarea,[tabindex]):focus-visible{outline:4px solid var(--focus-ring)!important;outline-offset:2px!important;box-shadow:0 0 0 6px #ffffff!important}
[data-theme="hc"] .card,[data-theme="hc"] .m-card,[data-theme="hc"] .pf-m-card{border-color:var(--line-strong)}`;
/** Appearance & display: the theme preview strip (each card carries its own data-theme and uses that theme's roles). */
const PREVIEW_RULES="/* CP-5 · Theme choice and live preview strip. Each preview card carries its own data-theme, so it uses the role variables of that\n   theme (themes.css) whatever the page shows; status chips keep their text labels. */\n.pf-seg-theme button svg{flex-shrink:0}\n.pf-theme-strip{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;min-width:0}\n.pf-theme-card{display:flex;flex-direction:column;align-items:stretch;gap:6px;text-align:left;min-width:0;padding:10px 12px;border-radius:10px;cursor:pointer;\n  background:var(--surface);color:var(--ink);border:1px solid var(--line-strong);font:inherit}\n.pf-theme-card.on{outline:3px solid var(--primary);outline-offset:2px}\n.pf-theme-card:focus-visible{outline:3px solid var(--focus-ring);outline-offset:2px}\n.pf-tp-head{display:flex;justify-content:space-between;align-items:baseline;gap:6px;min-width:0}\n.pf-tp-head b{font-size:14px;color:var(--ink)}\n.pf-tp-head small{font-size:11px;color:var(--muted);overflow-wrap:anywhere}\n.pf-tp-muted{font-size:12px;color:var(--muted)}\n.pf-tp-chips{display:flex;flex-wrap:wrap;gap:4px}\n.pf-tp-chips i{font-style:normal;font-size:11px;font-weight:600;border-radius:999px;padding:2px 7px;border:1px solid}\n.pf-tp-ready{background:var(--success-bg);color:var(--success-text);border-color:var(--success-border)}\n.pf-tp-dirty{background:var(--st-dirty-bg);color:var(--st-dirty-text);border-color:var(--st-dirty-border)}\n.pf-tp-occ{background:var(--st-occupied-bg);color:var(--st-occupied-text);border-color:var(--st-occupied-border)}\n.pf-tp-btn{align-self:flex-start;font-size:12px;font-weight:600;border-radius:7px;padding:4px 10px;background:var(--primary-fill);color:var(--on-primary)}\n.pf-m-page .pf-theme-strip{gap:8px}\n.pf-m-page .pf-theme-card{padding:8px;min-height:48px}\n.pf-m-page .pf-tp-head{flex-direction:column;gap:2px}\n.pf-m-page .pf-tp-head b{font-size:14px}\n.pf-m-page .pf-tp-muted{display:none}\n.pf-m-page .pf-seg-theme button{flex:1 1 45%}";
export function themesCss(tokens,usage){
  const {dark,hc}=generatedValues(tokens,usage);
  const roleDecls=t=>[...ROLES.map(([n])=>`${n}:${roleValue(n,t)}`),...ROLE_REFS.map(([n,r])=>`${n}:var(${r})`)];
  const sorted=m=>[...m].sort((a,b)=>a[0]<b[0]?-1:1).map(([k,v])=>`${k}:${v}`);
  const aliasDecls=[...ALIASES].sort((a,b)=>a[0]<b[0]?-1:1).filter(([k])=>tokens.has('--c-'+k)).map(([k,r])=>`--c-${k}:var(${r})`);
  return [
    '/* CP-5 themes: Light (Maroon & Sand) · Dark · High contrast. GENERATED by tools/themes.mjs (node tools/themes.mjs --write); do not edit by hand. */',
    '/* <html data-theme="light|dark|hc"> is set by src/app/prefs.ts (Follow device resolves prefers-color-scheme). Status colours always come with a text label. */',
    '/* 1. Roles, light values (== the hexes they replace; styles.css :root keeps its literal copies of the first nine) */',
    block(':root,[data-theme="light"]',['color-scheme:light',...roleDecls('light')]),
    '/* 2. Role aliases: the tokens that belong to a role follow it in every theme */',
    block(':root',aliasDecls),
    '/* 3. Dark and high contrast: screen only, so printing always uses the light palette */',
    '@media screen{',
    block('[data-theme="dark"]',['color-scheme:dark',...roleDecls('dark'),...sorted(dark)]),
    block('[data-theme="hc"]',['color-scheme:light',...roleDecls('hc'),...sorted(hc)]),
    EXTRA_RULES,
    '}',
    '@media print{:root{color-scheme:light}}',
    PREVIEW_RULES,
    '',
  ].join('\n');
}

// ── resolution (tests): every variable to a hex per theme ──
/** Map var-name -> raw value for a theme (styles.css :root, token sheet, roles, aliases, generated overrides). */
export function themeVars(theme,{tokens,usage,stylesRoot}){
  const m=new Map([...stylesRoot,...tokens]);
  for(const [n] of ROLES)m.set(n,roleValue(n,'light'));
  for(const [n,r] of ROLE_REFS)m.set(n,`var(${r})`);
  for(const [k,r] of ALIASES)if(tokens.has('--c-'+k))m.set('--c-'+k,`var(${r})`);
  if(theme!=='light'){
    for(const [n] of ROLES)m.set(n,roleValue(n,theme));
    const g=generatedValues(tokens,usage);for(const [k,v] of g[theme])m.set(k,v);
  }
  return m;
}
/** Replace var(--x) chains in a value with their values from `vars` (unknown variables are left as they are). */
export function resolveVars(value,vars){
  let out=value;
  for(let i=0;i<8&&/var\(--/.test(out);i++)out=out.replace(/var\((--[\w-]+)(?:,[^()]*)?\)/g,(m,n)=>vars.has(n)?vars.get(n):m);
  return out;
}
/** styles.css :root custom properties. */
export function stylesRootVars(stylesCss){
  const root=/:root\{([^}]*)\}/.exec(stylesCss)[1];
  return new Map([...root.matchAll(/(--[\w-]+):([^;]+)/g)].map(m=>[m[1],m[2].trim()]));
}

// ── CLI ──
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1]&&!process.argv.includes('--audit')){
  const {parseTokenSheet,TOKEN_SHEET}=await import('./cssTokens.mjs');
  const tokens=parseTokenSheet(readFileSync(TOKEN_SHEET,'utf8'));
  const css=themesCss(tokens,tokenUsage(await usageSources('.')));
  if(process.argv.includes('--write')){writeFileSync(THEMES_FILE,css);console.log(`Wrote ${THEMES_FILE} (${css.length} bytes)`);}
  else if(process.argv.includes('--check')){const cur=readFileSync(THEMES_FILE,'utf8');if(cur!==css){console.error(`${THEMES_FILE} is stale: run node tools/themes.mjs --write`);process.exit(1);}console.log('themes.css up to date');}
  else process.stdout.write(css);
}

// ── contrast audit (tests/stream-themes.mjs and `node tools/themes.mjs --audit`) ──
const strip=s=>s.replace(/\/\*[\s\S]*?\*\//g,'');
const HEX6=/^#[0-9a-f]{6}$/i;
/** Rules of `css` that set both a text colour and a background, resolved with `vars`:
 *  {pairs:[{sel,fg,bg,ratio}], unresolved:[{sel,fg,bg}]} (unresolved = translucent, gradient, keyword or unknown variable). Print blocks skipped. */
export function contrastAudit(css,vars){
  const pairs=[],unresolved=[];
  const src=strip(css).replace(/@media\s+print\s*\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}/g,'');
  let idx=-1;
  for(const m of src.matchAll(/([^{}]+)\{([^{}]*)\}/g)){
    idx++;
    const fg=/(?:^|;)\s*color\s*:\s*([^;!]+)/.exec(m[2]),bg=/(?:^|;)\s*background(?:-color)?\s*:\s*([^;!]+)/.exec(m[2]);
    if(!fg||!bg)continue;
    const sel=m[1].trim().replace(/\s+/g,' ');
    const f=resolveVars(fg[1].trim(),vars).toLowerCase(),b=resolveVars(bg[1].trim(),vars).toLowerCase();
    if(HEX6.test(f)&&HEX6.test(b))pairs.push({idx,sel,fg:f,bg:b,ratio:Math.round(contrast(f,b)*100)/100});
    else unresolved.push({idx,sel,fg:f,bg:b});
  }
  return {pairs,unresolved};
}
/** Every text-class colour against every surface of the theme (inherited text sits on these): [{text,surface,ratio}] below `min`. */
export function textOnSurfaces(theme,vars,min){
  const texts=[...vars].filter(([k])=>/^--(ink|muted|ink-2|ink-3|primary|primary-dark|[a-z]+-text|st-[a-z]+-text)$/.test(k)).map(([k])=>k);
  const surf=[...vars].filter(([k])=>/^--(bg|surface|surface-2|sand-[234]|primary-tint|[a-z]+-bg|st-[a-z]+-bg)$/.test(k)).map(([k])=>k);
  const low=[];
  for(const t of texts)for(const s of surf){const a=resolveVars(`var(${t})`,vars),b=resolveVars(`var(${s})`,vars);if(HEX6.test(a)&&HEX6.test(b)&&contrast(a,b)<min)low.push({text:t,surface:s,ratio:Math.round(contrast(a,b)*100)/100});}
  return low;
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1]&&process.argv.includes('--audit')){
  const {parseTokenSheet,TOKEN_SHEET}=await import('./cssTokens.mjs');
  const tokens=parseTokenSheet(readFileSync(TOKEN_SHEET,'utf8')),src=await usageSources('.'),usage=tokenUsage(src);
  const stylesRoot=stylesRootVars(readFileSync('src/styles.css','utf8'));
  for(const t of THEMES){
    const vars=themeVars(t,{tokens,usage,stylesRoot}),{pairs,unresolved}=contrastAudit(src,vars),min=t==='hc'?7:4.5;
    const lightLow=new Set(contrastAudit(src,themeVars('light',{tokens,usage,stylesRoot})).pairs.filter(p=>p.ratio<4.5).map(p=>p.idx));
    const low=pairs.filter(p=>p.ratio<min&&!lightLow.has(p.idx));
    console.log('  light-theme pairs below 4.5 (not text, kept as designed):',[...lightLow].length);
    console.log(`${t}: ${pairs.length} pairs, ${unresolved.length} unresolved, ${low.length} below ${min}`);
    for(const p of low)console.log('  ',p.ratio,p.fg,p.bg,p.sel.slice(0,90));
    for(const p of textOnSurfaces(t,vars,min))console.log('   role',p.text,'on',p.surface,p.ratio);
  }
}
